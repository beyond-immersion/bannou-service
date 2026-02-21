using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.ObjectModel;

namespace BeyondImmersion.BannouService.Resource.Tests;

/// <summary>
/// Unit tests for ResourceService reference counting, cleanup, and compression logic.
/// Tests verify atomic reference tracking, grace period management, cleanup policies,
/// and centralized compression/decompression operations.
/// </summary>
public class ResourceServiceTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IServiceNavigator> _mockNavigator;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<ResourceService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly ResourceServiceConfiguration _configuration;
    private readonly Mock<ICacheableStateStore<ResourceReferenceEntry>> _mockRefStore;
    private readonly Mock<IStateStore<GracePeriodRecord>> _mockGraceStore;
    private readonly Mock<IStateStore<CleanupCallbackDefinition>> _mockCleanupStore;
    private readonly Mock<ICacheableStateStore<string>> _mockCallbackIndexStore;

    // Compression-specific mocks
    private readonly Mock<IStateStore<CompressCallbackDefinition>> _mockCompressStore;
    private readonly Mock<IStateStore<ResourceArchiveModel>> _mockArchiveStore;
    private readonly Mock<ICacheableStateStore<string>> _mockCompressIndexStore;

    // Snapshot-specific mocks
    private readonly Mock<IStateStore<ResourceSnapshotModel>> _mockSnapshotStore;

    // Capture containers for verifying side effects
    private readonly List<(string Key, ResourceReferenceEntry Entry)> _capturedSetAdds = new();
    private readonly List<string> _capturedSetDeletes = new();
    private readonly List<(string Key, GracePeriodRecord Record)> _capturedGraceSaves = new();
    private readonly List<string> _capturedGraceDeletes = new();
    private readonly List<(string Topic, object Event)> _capturedPublishedEvents = new();

    // Compression-specific captures
    private readonly List<(string Key, CompressCallbackDefinition Callback)> _capturedCompressCallbackSaves = new();
    private readonly List<(string Key, ResourceArchiveModel Archive)> _capturedArchiveSaves = new();

    // Snapshot-specific captures
    private readonly List<(string Key, ResourceSnapshotModel Snapshot, StateOptions? Options)> _capturedSnapshotSaves = new();
    private readonly Dictionary<string, ResourceSnapshotModel> _simulatedSnapshots = new();

    // Simulated set state for testing
    private readonly Dictionary<string, HashSet<ResourceReferenceEntry>> _simulatedSets = new();

    // Simulated compression callback index state
    private readonly Dictionary<string, HashSet<string>> _simulatedCompressIndex = new();

    public ResourceServiceTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockNavigator = new Mock<IServiceNavigator>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<ResourceService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        _mockRefStore = new Mock<ICacheableStateStore<ResourceReferenceEntry>>();
        _mockGraceStore = new Mock<IStateStore<GracePeriodRecord>>();
        _mockCleanupStore = new Mock<IStateStore<CleanupCallbackDefinition>>();
        _mockCallbackIndexStore = new Mock<ICacheableStateStore<string>>();

        // Compression-specific mocks
        _mockCompressStore = new Mock<IStateStore<CompressCallbackDefinition>>();
        _mockArchiveStore = new Mock<IStateStore<ResourceArchiveModel>>();
        _mockCompressIndexStore = new Mock<ICacheableStateStore<string>>();

        // Snapshot-specific mocks
        _mockSnapshotStore = new Mock<IStateStore<ResourceSnapshotModel>>();

        _configuration = new ResourceServiceConfiguration
        {
            DefaultGracePeriodSeconds = 3600, // 1 hour for faster tests
            CleanupCallbackTimeoutSeconds = 30,
            CleanupLockExpirySeconds = 300,
            DefaultCleanupPolicy = CleanupPolicy.BEST_EFFORT,
            DefaultCompressionPolicy = CompressionPolicy.ALL_REQUIRED,
            CompressionCallbackTimeoutSeconds = 60,
            CompressionLockExpirySeconds = 600,
            // Snapshot configuration for tests
            SnapshotDefaultTtlSeconds = 3600, // 1 hour default
            SnapshotMinTtlSeconds = 60,       // 1 minute min
            SnapshotMaxTtlSeconds = 86400     // 24 hours max
        };

        SetupMocks();
    }

    private void SetupMocks()
    {
        // Setup state store factory
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<ResourceReferenceEntry>(StateStoreDefinitions.ResourceRefcounts))
            .Returns(_mockRefStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CleanupCallbackDefinition>(StateStoreDefinitions.ResourceCleanup))
            .Returns(_mockCleanupStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GracePeriodRecord>(StateStoreDefinitions.ResourceGrace))
            .Returns(_mockGraceStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<string>(StateStoreDefinitions.ResourceCleanup))
            .Returns(_mockCallbackIndexStore.Object);

        // Setup AddToSetAsync with capture and simulated state
        _mockRefStore
            .Setup(s => s.AddToSetAsync(It.IsAny<string>(), It.IsAny<ResourceReferenceEntry>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ResourceReferenceEntry, StateOptions?, CancellationToken>((key, entry, _, _) =>
            {
                _capturedSetAdds.Add((key, entry));
                if (!_simulatedSets.ContainsKey(key))
                    _simulatedSets[key] = new HashSet<ResourceReferenceEntry>();
                _simulatedSets[key].Add(entry);
            })
            .ReturnsAsync((string key, ResourceReferenceEntry entry, StateOptions? _, CancellationToken _) =>
            {
                // Return true if newly added (simulated)
                return true;
            });

        // Setup RemoveFromSetAsync with simulated state
        _mockRefStore
            .Setup(s => s.RemoveFromSetAsync(It.IsAny<string>(), It.IsAny<ResourceReferenceEntry>(), It.IsAny<CancellationToken>()))
            .Callback<string, ResourceReferenceEntry, CancellationToken>((key, entry, _) =>
            {
                if (_simulatedSets.ContainsKey(key))
                    _simulatedSets[key].Remove(entry);
            })
            .ReturnsAsync(true);

        // Setup SetCountAsync based on simulated state
        _mockRefStore
            .Setup(s => s.SetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                _simulatedSets.ContainsKey(key) ? _simulatedSets[key].Count : 0);

        // Setup GetSetAsync based on simulated state
        _mockRefStore
            .Setup(s => s.GetSetAsync<ResourceReferenceEntry>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                _simulatedSets.ContainsKey(key) ? _simulatedSets[key].ToList() : new List<ResourceReferenceEntry>());

        // Setup DeleteSetAsync with capture
        _mockRefStore
            .Setup(s => s.DeleteSetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) =>
            {
                _capturedSetDeletes.Add(key);
                _simulatedSets.Remove(key);
            })
            .ReturnsAsync(true);

        // Setup grace store with capture
        _mockGraceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GracePeriodRecord>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GracePeriodRecord, StateOptions?, CancellationToken>((key, record, _, _) =>
                _capturedGraceSaves.Add((key, record)))
            .ReturnsAsync("mock-etag");

        _mockGraceStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => _capturedGraceDeletes.Add(key))
            .ReturnsAsync(true);

        // Setup message bus with capture
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((topic, evt, _) =>
                _capturedPublishedEvents.Add((topic, evt)))
            .ReturnsAsync(true);

        _mockMessageBus
            .Setup(m => m.TryPublishErrorAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Setup callback index store (empty by default)
        _mockCallbackIndexStore
            .Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _mockCallbackIndexStore
            .Setup(s => s.AddToSetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Setup cleanup store (defaults to not found for new callbacks)
        _mockCleanupStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CleanupCallbackDefinition?)null);

        _mockCleanupStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CleanupCallbackDefinition>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("mock-etag");

        // Setup compression stores
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CompressCallbackDefinition>(StateStoreDefinitions.ResourceCompress))
            .Returns(_mockCompressStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ResourceArchiveModel>(StateStoreDefinitions.ResourceArchives))
            .Returns(_mockArchiveStore.Object);

        // Setup compress store (defaults to not found for new callbacks)
        _mockCompressStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CompressCallbackDefinition?)null);

        _mockCompressStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CompressCallbackDefinition>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CompressCallbackDefinition, StateOptions?, CancellationToken>((key, callback, _, _) =>
                _capturedCompressCallbackSaves.Add((key, callback)))
            .ReturnsAsync("mock-etag");

        // Setup archive store (defaults to not found)
        _mockArchiveStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceArchiveModel?)null);

        _mockArchiveStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ResourceArchiveModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ResourceArchiveModel, StateOptions?, CancellationToken>((key, archive, _, _) =>
                _capturedArchiveSaves.Add((key, archive)))
            .ReturnsAsync("mock-etag");

        // Setup compress index store (used for callback indexes)
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<string>(StateStoreDefinitions.ResourceCompress))
            .Returns(_mockCompressIndexStore.Object);

        _mockCompressIndexStore
            .Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                _simulatedCompressIndex.ContainsKey(key)
                    ? _simulatedCompressIndex[key].ToList()
                    : new List<string>());

        _mockCompressIndexStore
            .Setup(s => s.AddToSetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, StateOptions?, CancellationToken>((key, value, _, _) =>
            {
                if (!_simulatedCompressIndex.ContainsKey(key))
                    _simulatedCompressIndex[key] = new HashSet<string>();
                _simulatedCompressIndex[key].Add(value);
            })
            .ReturnsAsync(true);

        // Setup snapshot store
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ResourceSnapshotModel>(StateStoreDefinitions.ResourceSnapshots))
            .Returns(_mockSnapshotStore.Object);

        _mockSnapshotStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                _simulatedSnapshots.TryGetValue(key, out var snapshot) ? snapshot : null);

        _mockSnapshotStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ResourceSnapshotModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ResourceSnapshotModel, StateOptions?, CancellationToken>((key, snapshot, options, _) =>
            {
                _capturedSnapshotSaves.Add((key, snapshot, options));
                _simulatedSnapshots[key] = snapshot;
            })
            .ReturnsAsync("mock-etag");
    }

    private ResourceService CreateService(IEnumerable<ISeededResourceProvider>? seededProviders = null)
    {
        return new ResourceService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockNavigator.Object,
            _mockLockProvider.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object,
            seededProviders ?? Array.Empty<ISeededResourceProvider>());
    }

    private void ClearCaptures()
    {
        _capturedSetAdds.Clear();
        _capturedSetDeletes.Clear();
        _capturedGraceSaves.Clear();
        _capturedGraceDeletes.Clear();
        _capturedPublishedEvents.Clear();
        _simulatedSets.Clear();
        _capturedCompressCallbackSaves.Clear();
        _capturedArchiveSaves.Clear();
        _simulatedCompressIndex.Clear();
        _capturedSnapshotSaves.Clear();
        _simulatedSnapshots.Clear();
    }

    #region Constructor Validation

    [Fact]
    public void ResourceService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<ResourceService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void ResourceServiceConfiguration_CanBeInstantiated()
    {
        var config = new ResourceServiceConfiguration();
        Assert.NotNull(config);
    }

    [Fact]
    public void ResourceServiceConfiguration_HasExpectedDefaults()
    {
        var config = new ResourceServiceConfiguration();

        // Grace period and cleanup defaults
        Assert.Equal(604800, config.DefaultGracePeriodSeconds); // 7 days
        Assert.Equal(30, config.CleanupCallbackTimeoutSeconds);
        Assert.Equal(300, config.CleanupLockExpirySeconds);
        Assert.Equal(CleanupPolicy.BEST_EFFORT, config.DefaultCleanupPolicy);

        // Compression defaults
        Assert.Equal(CompressionPolicy.ALL_REQUIRED, config.DefaultCompressionPolicy);
        Assert.Equal(60, config.CompressionCallbackTimeoutSeconds);
        Assert.Equal(600, config.CompressionLockExpirySeconds);

        // Snapshot defaults
        Assert.Equal(3600, config.SnapshotDefaultTtlSeconds);  // 1 hour
        Assert.Equal(60, config.SnapshotMinTtlSeconds);        // 1 minute
        Assert.Equal(86400, config.SnapshotMaxTtlSeconds);     // 24 hours
    }

    #endregion

    #region Reference Registration Tests

    [Fact]
    public async Task RegisterReferenceAsync_NewReference_IncrementsCount()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();
        var sourceId = Guid.NewGuid().ToString();

        var request = new RegisterReferenceRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            SourceType = "actor",
            SourceId = sourceId
        };

        // Act
        var (status, response) = await service.RegisterReferenceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.NewRefCount);
        Assert.False(response.AlreadyRegistered);

        // Verify side effects via capture
        Assert.Single(_capturedSetAdds);
        var (key, entry) = _capturedSetAdds[0];
        Assert.Equal($"character:{resourceId}:sources", key);
        Assert.Equal("actor", entry.SourceType);
        Assert.Equal(sourceId, entry.SourceId);
    }

    [Fact]
    public async Task RegisterReferenceAsync_MultipleReferences_IncrementsCountCorrectly()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Act - register 3 different references
        for (int i = 0; i < 3; i++)
        {
            var request = new RegisterReferenceRequest
            {
                ResourceType = "character",
                ResourceId = resourceId,
                SourceType = "actor",
                SourceId = Guid.NewGuid().ToString()
            };
            await service.RegisterReferenceAsync(request, CancellationToken.None);
        }

        // Assert - verify 3 entries were added
        Assert.Equal(3, _capturedSetAdds.Count);
        Assert.Equal(3, _simulatedSets[$"character:{resourceId}:sources"].Count);
    }

    [Fact]
    public async Task RegisterReferenceAsync_ClearsGracePeriod_WhenReferenceAdded()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        var request = new RegisterReferenceRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            SourceType = "actor",
            SourceId = Guid.NewGuid().ToString()
        };

        // Act
        await service.RegisterReferenceAsync(request, CancellationToken.None);

        // Assert - grace period should be cleared
        Assert.Single(_capturedGraceDeletes);
        Assert.Equal($"character:{resourceId}:grace", _capturedGraceDeletes[0]);
    }

    #endregion

    #region Reference Unregistration Tests

    [Fact]
    public async Task UnregisterReferenceAsync_ExistingReference_DecrementsCount()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();
        var sourceId = Guid.NewGuid().ToString();

        // Pre-populate with 2 references
        var setKey = $"character:{resourceId}:sources";
        _simulatedSets[setKey] = new HashSet<ResourceReferenceEntry>
        {
            new() { SourceType = "actor", SourceId = sourceId, RegisteredAt = DateTimeOffset.UtcNow },
            new() { SourceType = "scene", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow }
        };

        var request = new UnregisterReferenceRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            SourceType = "actor",
            SourceId = sourceId
        };

        // Act
        var (status, response) = await service.UnregisterReferenceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.NewRefCount); // 2 - 1 = 1
        Assert.True(response.WasRegistered);
        Assert.Null(response.GracePeriodStartedAt); // Not zero yet
    }

    [Fact]
    public async Task UnregisterReferenceAsync_LastReference_StartsGracePeriod()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();
        var sourceId = Guid.NewGuid().ToString();

        // Pre-populate with 1 reference
        var setKey = $"character:{resourceId}:sources";
        _simulatedSets[setKey] = new HashSet<ResourceReferenceEntry>
        {
            new() { SourceType = "actor", SourceId = sourceId, RegisteredAt = DateTimeOffset.UtcNow }
        };

        var request = new UnregisterReferenceRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            SourceType = "actor",
            SourceId = sourceId
        };

        // Act
        var (status, response) = await service.UnregisterReferenceAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.NewRefCount);
        Assert.True(response.WasRegistered);
        Assert.NotNull(response.GracePeriodStartedAt);

        // Verify grace period record was saved
        Assert.Single(_capturedGraceSaves);
        var (graceKey, graceRecord) = _capturedGraceSaves[0];
        Assert.Equal($"character:{resourceId}:grace", graceKey);
        Assert.Equal("character", graceRecord.ResourceType);
        Assert.Equal(resourceId, graceRecord.ResourceId);
    }

    [Fact]
    public async Task UnregisterReferenceAsync_LastReference_PublishesGracePeriodEvent()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();
        var sourceId = Guid.NewGuid().ToString();

        // Pre-populate with 1 reference
        var setKey = $"character:{resourceId}:sources";
        _simulatedSets[setKey] = new HashSet<ResourceReferenceEntry>
        {
            new() { SourceType = "actor", SourceId = sourceId, RegisteredAt = DateTimeOffset.UtcNow }
        };

        var request = new UnregisterReferenceRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            SourceType = "actor",
            SourceId = sourceId
        };

        // Act
        await service.UnregisterReferenceAsync(request, CancellationToken.None);

        // Assert - verify grace period event was published
        Assert.Single(_capturedPublishedEvents);
        var (topic, evt) = _capturedPublishedEvents[0];
        Assert.Equal("resource.grace-period.started", topic);

        var graceEvent = Assert.IsType<ResourceGracePeriodStartedEvent>(evt);
        Assert.Equal("character", graceEvent.ResourceType);
        Assert.Equal(resourceId, graceEvent.ResourceId);
    }

    #endregion

    #region Check References Tests

    [Fact]
    public async Task CheckReferencesAsync_WithReferences_ReturnsNotEligibleForCleanup()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Pre-populate with references
        var setKey = $"character:{resourceId}:sources";
        _simulatedSets[setKey] = new HashSet<ResourceReferenceEntry>
        {
            new() { SourceType = "actor", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow }
        };

        var request = new CheckReferencesRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.CheckReferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.RefCount);
        Assert.False(response.IsCleanupEligible);
        Assert.NotNull(response.Sources);
        Assert.Single(response.Sources);
    }

    [Fact]
    public async Task CheckReferencesAsync_ZeroRefWithGracePeriodNotPassed_ReturnsNotEligible()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup grace period that started recently (not yet passed)
        var graceRecord = new GracePeriodRecord
        {
            ResourceType = "character",
            ResourceId = resourceId,
            ZeroTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5) // 5 minutes ago
        };

        _mockGraceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graceRecord);

        var request = new CheckReferencesRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.CheckReferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.RefCount);
        Assert.False(response.IsCleanupEligible); // Grace period not passed
        Assert.NotNull(response.GracePeriodEndsAt);
        Assert.NotNull(response.LastZeroTimestamp);
    }

    [Fact]
    public async Task CheckReferencesAsync_ZeroRefWithGracePeriodPassed_ReturnsEligible()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup grace period that started 2 hours ago (config is 1 hour)
        var graceRecord = new GracePeriodRecord
        {
            ResourceType = "character",
            ResourceId = resourceId,
            ZeroTimestamp = DateTimeOffset.UtcNow.AddHours(-2)
        };

        _mockGraceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graceRecord);

        var request = new CheckReferencesRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.CheckReferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.RefCount);
        Assert.True(response.IsCleanupEligible);
        Assert.Null(response.GracePeriodEndsAt); // Cleared when eligible
    }

    #endregion

    #region Execute Cleanup Tests

    [Fact]
    public async Task ExecuteCleanupAsync_WithActiveReferences_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Pre-populate with references
        var setKey = $"character:{resourceId}:sources";
        _simulatedSets[setKey] = new HashSet<ResourceReferenceEntry>
        {
            new() { SourceType = "actor", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow }
        };

        var request = new ExecuteCleanupRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.ExecuteCleanupAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Contains("active reference", response.AbortReason);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_GracePeriodNotPassed_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup grace period that started recently
        var graceRecord = new GracePeriodRecord
        {
            ResourceType = "character",
            ResourceId = resourceId,
            ZeroTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        _mockGraceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graceRecord);

        var request = new ExecuteCleanupRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.ExecuteCleanupAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Contains("Grace period not yet passed", response.AbortReason);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_LockAcquisitionFails_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup eligible for cleanup
        var graceRecord = new GracePeriodRecord
        {
            ResourceType = "character",
            ResourceId = resourceId,
            ZeroTimestamp = DateTimeOffset.UtcNow.AddHours(-2)
        };

        _mockGraceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graceRecord);

        // Setup lock to fail
        var failedLockResponse = new Mock<ILockResponse>();
        failedLockResponse.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLockResponse.Object);

        var request = new ExecuteCleanupRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.ExecuteCleanupAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Contains("lock", response.AbortReason?.ToLower());
    }

    [Fact]
    public async Task ExecuteCleanupAsync_Eligible_SucceedsAndCleansUp()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup eligible for cleanup (grace period passed)
        var graceRecord = new GracePeriodRecord
        {
            ResourceType = "character",
            ResourceId = resourceId,
            ZeroTimestamp = DateTimeOffset.UtcNow.AddHours(-2)
        };

        _mockGraceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graceRecord);

        // Setup successful lock acquisition
        var successLockResponse = new Mock<ILockResponse>();
        successLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLockResponse.Object);

        var request = new ExecuteCleanupRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.ExecuteCleanupAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Null(response.AbortReason);

        // Verify cleanup actions via capture
        Assert.Contains($"character:{resourceId}:grace", _capturedGraceDeletes);
        Assert.Contains($"character:{resourceId}:sources", _capturedSetDeletes);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_GracePeriodSecondsZero_SkipsGracePeriodCheck()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup grace period that started recently (would normally block)
        var graceRecord = new GracePeriodRecord
        {
            ResourceType = "character",
            ResourceId = resourceId,
            ZeroTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        _mockGraceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graceRecord);

        // Setup successful lock
        var successLockResponse = new Mock<ILockResponse>();
        successLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLockResponse.Object);

        var request = new ExecuteCleanupRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            GracePeriodSeconds = 0 // Skip grace period
        };

        // Act
        var (status, response) = await service.ExecuteCleanupAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
    }

    #endregion

    #region Cleanup Callback Definition Tests

    [Fact]
    public async Task DefineCleanupCallbackAsync_NewCallback_RegistersSuccessfully()
    {
        // Arrange
        var service = CreateService();

        var request = new DefineCleanupRequest
        {
            ResourceType = "character",
            SourceType = "actor",
            ServiceName = "actor",
            CallbackEndpoint = "/actor/cleanup-by-character",
            PayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}",
            Description = "Clean up actors when character is deleted"
        };

        // Act
        var (status, response) = await service.DefineCleanupCallbackAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Registered);
        Assert.False(response.PreviouslyDefined);

        // Verify callback was saved
        _mockCleanupStore.Verify(s => s.SaveAsync(
            "callback:character:actor",
            It.Is<CleanupCallbackDefinition>(c =>
                c.ResourceType == "character" &&
                c.SourceType == "actor" &&
                c.ServiceName == "actor" &&
                c.CallbackEndpoint == "/actor/cleanup-by-character"),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DefineCleanupCallbackAsync_ExistingCallback_UpdatesAndReportsPreviouslyDefined()
    {
        // Arrange
        var service = CreateService();

        // Setup existing callback
        _mockCleanupStore
            .Setup(s => s.GetAsync("callback:character:actor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleanupCallbackDefinition
            {
                ResourceType = "character",
                SourceType = "actor",
                ServiceName = "actor",
                CallbackEndpoint = "/actor/old-endpoint",
                PayloadTemplate = "{}"
            });

        var request = new DefineCleanupRequest
        {
            ResourceType = "character",
            SourceType = "actor",
            ServiceName = "actor",
            CallbackEndpoint = "/actor/cleanup-by-character",
            PayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}"
        };

        // Act
        var (status, response) = await service.DefineCleanupCallbackAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Registered);
        Assert.True(response.PreviouslyDefined);
    }

    #endregion

    #region List References Tests

    [Fact]
    public async Task ListReferencesAsync_ReturnsAllReferences()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Pre-populate with multiple references
        var setKey = $"character:{resourceId}:sources";
        _simulatedSets[setKey] = new HashSet<ResourceReferenceEntry>
        {
            new() { SourceType = "actor", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow },
            new() { SourceType = "scene", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow },
            new() { SourceType = "encounter", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow }
        };

        var request = new ListReferencesRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.ListReferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.TotalCount);
        Assert.Equal(3, response.References.Count);
    }

    [Fact]
    public async Task ListReferencesAsync_WithFilter_ReturnsFilteredReferences()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Pre-populate with mixed references
        var setKey = $"character:{resourceId}:sources";
        _simulatedSets[setKey] = new HashSet<ResourceReferenceEntry>
        {
            new() { SourceType = "actor", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow },
            new() { SourceType = "actor", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow },
            new() { SourceType = "scene", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow }
        };

        var request = new ListReferencesRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            FilterSourceType = "actor"
        };

        // Act
        var (status, response) = await service.ListReferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount);
        Assert.All(response.References, r => Assert.Equal("actor", r.SourceType));
    }

    [Fact]
    public async Task ListReferencesAsync_WithLimit_RespectsLimit()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Pre-populate with many references
        var setKey = $"character:{resourceId}:sources";
        _simulatedSets[setKey] = new HashSet<ResourceReferenceEntry>();
        for (int i = 0; i < 10; i++)
        {
            _simulatedSets[setKey].Add(new ResourceReferenceEntry
            {
                SourceType = "actor",
                SourceId = Guid.NewGuid().ToString(),
                RegisteredAt = DateTimeOffset.UtcNow
            });
        }

        var request = new ListReferencesRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            Limit = 5
        };

        // Act
        var (status, response) = await service.ListReferencesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(10, response.TotalCount);
        Assert.Equal(5, response.References.Count);
    }

    #endregion

    #region OnDeleteAction Tests

    [Fact]
    public async Task DefineCleanupCallbackAsync_WithOnDeleteAction_StoresCorrectly()
    {
        // Arrange
        var service = CreateService();

        var request = new DefineCleanupRequest
        {
            ResourceType = "character",
            SourceType = "scene",
            OnDeleteAction = OnDeleteAction.RESTRICT,
            ServiceName = "scene",
            CallbackEndpoint = "/scene/cleanup-by-character",
            PayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}",
            Description = "Block character deletion if scenes exist"
        };

        // Act
        var (status, response) = await service.DefineCleanupCallbackAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Registered);

        // Verify OnDeleteAction was stored
        _mockCleanupStore.Verify(s => s.SaveAsync(
            "callback:character:scene",
            It.Is<CleanupCallbackDefinition>(c =>
                c.OnDeleteAction == OnDeleteAction.RESTRICT),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DefineCleanupCallbackAsync_WithoutOnDeleteAction_DefaultsToCascade()
    {
        // Arrange
        var service = CreateService();

        var request = new DefineCleanupRequest
        {
            ResourceType = "character",
            SourceType = "actor",
            ServiceName = "actor",
            CallbackEndpoint = "/actor/cleanup-by-character",
            PayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}"
        };

        // Act
        var (status, response) = await service.DefineCleanupCallbackAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify default OnDeleteAction is CASCADE
        _mockCleanupStore.Verify(s => s.SaveAsync(
            "callback:character:actor",
            It.Is<CleanupCallbackDefinition>(c =>
                c.OnDeleteAction == OnDeleteAction.CASCADE),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_WithRestrictCallback_AndActiveReferences_BlocksDeletion()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Pre-populate with active scene reference (RESTRICT type)
        var setKey = $"character:{resourceId}:sources";
        _simulatedSets[setKey] = new HashSet<ResourceReferenceEntry>
        {
            new() { SourceType = "scene", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow }
        };

        // Setup grace period passed
        var graceRecord = new GracePeriodRecord
        {
            ResourceType = "character",
            ResourceId = resourceId,
            ZeroTimestamp = DateTimeOffset.UtcNow.AddHours(-2)
        };

        _mockGraceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graceRecord);

        // Setup RESTRICT callback for scene source type
        _mockCallbackIndexStore
            .Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "scene" });

        _mockCleanupStore
            .Setup(s => s.GetAsync("callback:character:scene", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleanupCallbackDefinition
            {
                ResourceType = "character",
                SourceType = "scene",
                OnDeleteAction = OnDeleteAction.RESTRICT,
                ServiceName = "scene",
                CallbackEndpoint = "/scene/cleanup-by-character",
                PayloadTemplate = "{}"
            });

        // Setup successful lock
        var successLockResponse = new Mock<ILockResponse>();
        successLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLockResponse.Object);

        var request = new ExecuteCleanupRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            GracePeriodSeconds = 0 // Skip grace period check to get to RESTRICT logic
        };

        // Act
        var (status, response) = await service.ExecuteCleanupAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Contains("RESTRICT", response.AbortReason);
        Assert.Contains("scene", response.AbortReason);

        // Verify no callbacks were executed (blocked before execution)
        _mockNavigator.Verify(n => n.ExecutePreboundApiBatchAsync(
            It.IsAny<IEnumerable<PreboundApiDefinition>>(),
            It.IsAny<Dictionary<string, object?>>(),
            It.IsAny<BatchExecutionMode>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_WithRestrictCallback_NoActiveRestrictedReferences_Proceeds()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // No active references of RESTRICT type
        var setKey = $"character:{resourceId}:sources";
        _simulatedSets[setKey] = new HashSet<ResourceReferenceEntry>(); // Empty - no references

        // Setup grace period passed
        var graceRecord = new GracePeriodRecord
        {
            ResourceType = "character",
            ResourceId = resourceId,
            ZeroTimestamp = DateTimeOffset.UtcNow.AddHours(-2)
        };

        _mockGraceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graceRecord);

        // Setup RESTRICT callback for scene
        _mockCallbackIndexStore
            .Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "scene" });

        _mockCleanupStore
            .Setup(s => s.GetAsync("callback:character:scene", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleanupCallbackDefinition
            {
                ResourceType = "character",
                SourceType = "scene",
                OnDeleteAction = OnDeleteAction.RESTRICT,
                ServiceName = "scene",
                CallbackEndpoint = "/scene/cleanup-by-character",
                PayloadTemplate = "{}"
            });

        // Setup successful lock
        var successLockResponse = new Mock<ILockResponse>();
        successLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLockResponse.Object);

        var request = new ExecuteCleanupRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.ExecuteCleanupAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);

        // RESTRICT callbacks are not executed - verify no callbacks ran
        // (RESTRICT type is filtered out from executableCallbacks)
        _mockNavigator.Verify(n => n.ExecutePreboundApiBatchAsync(
            It.IsAny<IEnumerable<PreboundApiDefinition>>(),
            It.IsAny<Dictionary<string, object?>>(),
            It.IsAny<BatchExecutionMode>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_WithMixedPolicies_OnlyExecutesCascadeAndDetach()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // No active references
        var setKey = $"character:{resourceId}:sources";
        _simulatedSets[setKey] = new HashSet<ResourceReferenceEntry>();

        // Setup grace period passed
        var graceRecord = new GracePeriodRecord
        {
            ResourceType = "character",
            ResourceId = resourceId,
            ZeroTimestamp = DateTimeOffset.UtcNow.AddHours(-2)
        };

        _mockGraceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(graceRecord);

        // Setup mixed callbacks: CASCADE, RESTRICT, DETACH
        _mockCallbackIndexStore
            .Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "actor", "scene", "encounter" });

        _mockCleanupStore
            .Setup(s => s.GetAsync("callback:character:actor", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleanupCallbackDefinition
            {
                ResourceType = "character",
                SourceType = "actor",
                OnDeleteAction = OnDeleteAction.CASCADE,
                ServiceName = "actor",
                CallbackEndpoint = "/actor/cleanup-by-character",
                PayloadTemplate = "{}"
            });

        _mockCleanupStore
            .Setup(s => s.GetAsync("callback:character:scene", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleanupCallbackDefinition
            {
                ResourceType = "character",
                SourceType = "scene",
                OnDeleteAction = OnDeleteAction.RESTRICT,
                ServiceName = "scene",
                CallbackEndpoint = "/scene/cleanup-by-character",
                PayloadTemplate = "{}"
            });

        _mockCleanupStore
            .Setup(s => s.GetAsync("callback:character:encounter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleanupCallbackDefinition
            {
                ResourceType = "character",
                SourceType = "encounter",
                OnDeleteAction = OnDeleteAction.DETACH,
                ServiceName = "encounter",
                CallbackEndpoint = "/character-encounter/detach-by-character",
                PayloadTemplate = "{}"
            });

        // Setup successful lock
        var successLockResponse = new Mock<ILockResponse>();
        successLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLockResponse.Object);

        // Setup navigator to return successful batch results
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiBatchAsync(
                It.IsAny<IEnumerable<PreboundApiDefinition>>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<BatchExecutionMode>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<PreboundApiDefinition> apis, IReadOnlyDictionary<string, object?> ctx, BatchExecutionMode mode, CancellationToken ct) =>
            {
                var results = new List<PreboundApiResult>();
                foreach (var api in apis)
                {
                    results.Add(new PreboundApiResult
                    {
                        Api = api,
                        SubstitutedPayload = "{}",
                        SubstitutionSucceeded = true,
                        Result = new RawApiResult
                        {
                            StatusCode = 200,
                            ResponseBody = "{}",
                            Duration = TimeSpan.FromMilliseconds(50)
                        }
                    });
                }
                return results;
            });

        var request = new ExecuteCleanupRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.ExecuteCleanupAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);

        // Verify only CASCADE and DETACH callbacks were executed (2 out of 3)
        _mockNavigator.Verify(n => n.ExecutePreboundApiBatchAsync(
            It.Is<IEnumerable<PreboundApiDefinition>>(apis =>
                apis.Count() == 2 &&
                apis.Any(a => a.ServiceName == "actor") &&
                apis.Any(a => a.ServiceName == "encounter") &&
                !apis.Any(a => a.ServiceName == "scene")),
            It.IsAny<Dictionary<string, object?>>(),
            It.IsAny<BatchExecutionMode>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Compression Callback Definition Tests

    [Fact]
    public async Task DefineCompressCallbackAsync_NewCallback_RegistersSuccessfully()
    {
        // Arrange
        var service = CreateService();

        var request = new DefineCompressCallbackRequest
        {
            ResourceType = "character",
            SourceType = "character-personality",
            ServiceName = "character-personality",
            CompressEndpoint = "/character-personality/get-compress-data",
            CompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}",
            DecompressEndpoint = "/character-personality/restore-from-archive",
            DecompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\", \"data\": \"{{data}}\"}",
            Priority = 10,
            Description = "Personality traits and combat preferences"
        };

        // Act
        var (status, response) = await service.DefineCompressCallbackAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Registered);
        Assert.False(response.PreviouslyDefined);

        // Verify callback was saved
        Assert.Single(_capturedCompressCallbackSaves);
        var (key, callback) = _capturedCompressCallbackSaves[0];
        Assert.Equal("compress-callback:character:character-personality", key);
        Assert.Equal("character", callback.ResourceType);
        Assert.Equal("character-personality", callback.SourceType);
        Assert.Equal(10, callback.Priority);
    }

    [Fact]
    public async Task DefineCompressCallbackAsync_ExistingCallback_UpdatesAndReportsPreviouslyDefined()
    {
        // Arrange
        var service = CreateService();

        // Setup existing callback
        _mockCompressStore
            .Setup(s => s.GetAsync("compress-callback:character:character-personality", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompressCallbackDefinition
            {
                ResourceType = "character",
                SourceType = "character-personality",
                ServiceName = "character-personality",
                CompressEndpoint = "/old-endpoint",
                CompressPayloadTemplate = "{}"
            });

        var request = new DefineCompressCallbackRequest
        {
            ResourceType = "character",
            SourceType = "character-personality",
            ServiceName = "character-personality",
            CompressEndpoint = "/character-personality/get-compress-data",
            CompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}"
        };

        // Act
        var (status, response) = await service.DefineCompressCallbackAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Registered);
        Assert.True(response.PreviouslyDefined);
    }

    [Fact]
    public async Task DefineCompressCallbackAsync_DefaultServiceName_UsesSourceType()
    {
        // Arrange
        var service = CreateService();

        var request = new DefineCompressCallbackRequest
        {
            ResourceType = "character",
            SourceType = "character-personality",
            // ServiceName intentionally not set
            CompressEndpoint = "/character-personality/get-compress-data",
            CompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}"
        };

        // Act
        var (status, response) = await service.DefineCompressCallbackAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.Single(_capturedCompressCallbackSaves);
        var (_, callback) = _capturedCompressCallbackSaves[0];
        Assert.Equal("character-personality", callback.ServiceName);
    }

    #endregion

    #region Execute Compression Tests

    [Fact]
    public async Task ExecuteCompressAsync_NoCallbacks_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        var request = new ExecuteCompressRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.ExecuteCompressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Contains("No compression callbacks registered", response.AbortReason);
        Assert.Empty(response.CallbackResults);
    }

    [Fact]
    public async Task ExecuteCompressAsync_DryRun_DoesNotExecuteCallbacks()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0),
            CreateCompressCallback("character-personality", priority: 10)
        });

        var request = new ExecuteCompressRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            DryRun = true
        };

        // Act
        var (status, response) = await service.ExecuteCompressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.True(response.DryRun);
        Assert.Null(response.ArchiveId);
        Assert.Equal(2, response.CallbackResults.Count);

        // Verify no actual API calls were made
        _mockNavigator.Verify(n => n.ExecutePreboundApiAsync(
            It.IsAny<PreboundApiDefinition>(),
            It.IsAny<IReadOnlyDictionary<string, object?>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCompressAsync_LockContention_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0)
        });

        // Setup lock to fail
        var failedLockResponse = new Mock<ILockResponse>();
        failedLockResponse.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLockResponse.Object);

        var request = new ExecuteCompressRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.ExecuteCompressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Contains("lock", response.AbortReason?.ToLower());
    }

    [Fact]
    public async Task ExecuteCompressAsync_AllCallbacksSucceed_CreatesArchive()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0),
            CreateCompressCallback("character-personality", priority: 10)
        });

        // Setup successful lock
        SetupSuccessfulLock();

        // Setup successful API calls
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreboundApiResult
            {
                SubstitutionSucceeded = true,
                Result = new RawApiResult
                {
                    StatusCode = 200,
                    ResponseBody = "{\"characterId\": \"" + resourceId + "\", \"data\": \"test\"}",
                    Duration = TimeSpan.FromMilliseconds(50)
                }
            });

        var request = new ExecuteCompressRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            DeleteSourceData = false
        };

        // Act
        var (status, response) = await service.ExecuteCompressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.False(response.DryRun);
        Assert.NotNull(response.ArchiveId);
        Assert.Equal(2, response.CallbackResults.Count);
        Assert.All(response.CallbackResults, r => Assert.True(r.Success));
        Assert.False(response.SourceDataDeleted);

        // Verify archive was saved
        Assert.Single(_capturedArchiveSaves);
        var (archiveKey, archive) = _capturedArchiveSaves[0];
        Assert.Contains(resourceId.ToString(), archiveKey);
        Assert.Equal(resourceId, archive.ResourceId);
        Assert.Equal(1, archive.Version);
        Assert.Equal(2, archive.Entries.Count);

        // Verify compressed event was published
        Assert.Contains(_capturedPublishedEvents, e => e.Topic == "resource.compressed");
    }

    [Fact]
    public async Task ExecuteCompressAsync_CallbackFails_AllRequired_Aborts()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0),
            CreateCompressCallback("character-personality", priority: 10)
        });

        // Setup successful lock
        SetupSuccessfulLock();

        // Setup first callback to succeed, second to fail
        var callCount = 0;
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new PreboundApiResult
                    {
                        SubstitutionSucceeded = true,
                        Result = new RawApiResult { StatusCode = 200, ResponseBody = "{}", Duration = TimeSpan.FromMilliseconds(50) }
                    };
                }
                return new PreboundApiResult
                {
                    SubstitutionSucceeded = true,
                    Result = new RawApiResult { StatusCode = 500, ErrorMessage = "Service unavailable", Duration = TimeSpan.FromMilliseconds(50) }
                };
            });

        var request = new ExecuteCompressRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            CompressionPolicy = CompressionPolicy.ALL_REQUIRED
        };

        // Act
        var (status, response) = await service.ExecuteCompressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Contains("ALL_REQUIRED", response.AbortReason);
        Assert.Null(response.ArchiveId);

        // Verify failure event was published
        Assert.Contains(_capturedPublishedEvents, e => e.Topic == "resource.compress.callback-failed");

        // Verify no archive was saved
        Assert.Empty(_capturedArchiveSaves);
    }

    [Fact]
    public async Task ExecuteCompressAsync_CallbackFails_BestEffort_Continues()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0),
            CreateCompressCallback("character-personality", priority: 10)
        });

        // Setup successful lock
        SetupSuccessfulLock();

        // Setup first callback to succeed, second to fail
        var callCount = 0;
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new PreboundApiResult
                    {
                        SubstitutionSucceeded = true,
                        Result = new RawApiResult { StatusCode = 200, ResponseBody = "{\"data\": \"test\"}", Duration = TimeSpan.FromMilliseconds(50) }
                    };
                }
                return new PreboundApiResult
                {
                    SubstitutionSucceeded = true,
                    Result = new RawApiResult { StatusCode = 404, ErrorMessage = "Not found", Duration = TimeSpan.FromMilliseconds(50) }
                };
            });

        var request = new ExecuteCompressRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            CompressionPolicy = CompressionPolicy.BEST_EFFORT
        };

        // Act
        var (status, response) = await service.ExecuteCompressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotNull(response.ArchiveId);

        // Verify partial results
        Assert.Equal(2, response.CallbackResults.Count);
        Assert.Single(response.CallbackResults, r => r.Success);
        Assert.Single(response.CallbackResults, r => !r.Success);

        // Verify archive was saved with partial data
        Assert.Single(_capturedArchiveSaves);
        var (_, archive) = _capturedArchiveSaves[0];
        Assert.Single(archive.Entries);
    }

    [Fact]
    public async Task ExecuteCompressAsync_MultipleCallbacks_ExecutesInPriorityOrder()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();
        var executionOrder = new List<string>();

        // Setup callbacks with different priorities
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-history", priority: 50),
            CreateCompressCallback("character-personality", priority: 10),
            CreateCompressCallback("character-encounter", priority: 100)
        });

        // Setup successful lock
        SetupSuccessfulLock();

        // Mock navigator to capture execution order
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Callback<PreboundApiDefinition, IReadOnlyDictionary<string, object?>, CancellationToken>((api, _, _) =>
            {
                executionOrder.Add(api.ServiceName);
            })
            .ReturnsAsync(new PreboundApiResult
            {
                SubstitutionSucceeded = true,
                Result = new RawApiResult { StatusCode = 200, ResponseBody = "{}", Duration = TimeSpan.FromMilliseconds(50) }
            });

        var request = new ExecuteCompressRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        await service.ExecuteCompressAsync(request, CancellationToken.None);

        // Assert - verify priority order: 10 -> 50 -> 100
        Assert.Equal(3, executionOrder.Count);
        Assert.Equal("character-personality", executionOrder[0]);  // priority 10
        Assert.Equal("character-history", executionOrder[1]);      // priority 50
        Assert.Equal("character-encounter", executionOrder[2]);    // priority 100
    }

    [Fact]
    public async Task ExecuteCompressAsync_ExistingArchive_IncrementsVersion()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0)
        });

        // Setup successful lock
        SetupSuccessfulLock();

        // Setup existing archive with version 2
        _mockArchiveStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResourceArchiveModel
            {
                ArchiveId = Guid.NewGuid(),
                ResourceType = "character",
                ResourceId = resourceId,
                Version = 2,
                Entries = new List<ArchiveEntryModel>(),
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
            });

        // Setup successful API call
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreboundApiResult
            {
                SubstitutionSucceeded = true,
                Result = new RawApiResult { StatusCode = 200, ResponseBody = "{}", Duration = TimeSpan.FromMilliseconds(50) }
            });

        var request = new ExecuteCompressRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.ExecuteCompressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);

        // Verify archive was saved with incremented version
        Assert.Single(_capturedArchiveSaves);
        var (_, archive) = _capturedArchiveSaves[0];
        Assert.Equal(3, archive.Version); // 2 + 1 = 3
    }

    [Fact]
    public async Task ExecuteCompressAsync_PublishesCompressedEvent()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0)
        });

        // Setup successful lock
        SetupSuccessfulLock();

        // Setup successful API call
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreboundApiResult
            {
                SubstitutionSucceeded = true,
                Result = new RawApiResult { StatusCode = 200, ResponseBody = "{}", Duration = TimeSpan.FromMilliseconds(50) }
            });

        var request = new ExecuteCompressRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            DeleteSourceData = false
        };

        // Act
        await service.ExecuteCompressAsync(request, CancellationToken.None);

        // Assert - verify compressed event was published
        var compressedEvent = _capturedPublishedEvents.FirstOrDefault(e => e.Topic == "resource.compressed");
        Assert.NotNull(compressedEvent.Event);
        var typedEvent = Assert.IsType<ResourceCompressedEvent>(compressedEvent.Event);
        Assert.Equal("character", typedEvent.ResourceType);
        Assert.Equal(resourceId, typedEvent.ResourceId);
        Assert.False(typedEvent.SourceDataDeleted);
    }

    #endregion

    #region Decompression Tests

    [Fact]
    public async Task ExecuteDecompressAsync_NoArchive_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        var request = new ExecuteDecompressRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.ExecuteDecompressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Contains("No archive found", response.AbortReason);
    }

    [Fact]
    public async Task ExecuteDecompressAsync_ArchiveIdMismatch_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();
        var existingArchiveId = Guid.NewGuid();
        var requestedArchiveId = Guid.NewGuid();

        // Setup existing archive
        _mockArchiveStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResourceArchiveModel
            {
                ArchiveId = existingArchiveId,
                ResourceType = "character",
                ResourceId = resourceId,
                Version = 1,
                Entries = new List<ArchiveEntryModel>(),
                CreatedAt = DateTimeOffset.UtcNow
            });

        var request = new ExecuteDecompressRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            ArchiveId = requestedArchiveId
        };

        // Act
        var (status, response) = await service.ExecuteDecompressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Contains(requestedArchiveId.ToString(), response.AbortReason);
    }

    #endregion

    #region Get Archive Tests

    [Fact]
    public async Task GetArchiveAsync_Exists_ReturnsArchive()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();
        var archiveId = Guid.NewGuid();

        // Setup existing archive
        _mockArchiveStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResourceArchiveModel
            {
                ArchiveId = archiveId,
                ResourceType = "character",
                ResourceId = resourceId,
                Version = 1,
                Entries = new List<ArchiveEntryModel>
                {
                    new()
                    {
                        SourceType = "character-base",
                        ServiceName = "character",
                        Data = "SGVsbG8=", // Base64 encoded
                        CompressedAt = DateTimeOffset.UtcNow
                    }
                },
                CreatedAt = DateTimeOffset.UtcNow
            });

        var request = new GetArchiveRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.GetArchiveAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Found);
        Assert.NotNull(response.Archive);
        Assert.Equal(archiveId, response.Archive.ArchiveId);
        Assert.Single(response.Archive.Entries);
    }

    [Fact]
    public async Task GetArchiveAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        var request = new GetArchiveRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.GetArchiveAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Found);
        Assert.Null(response.Archive);
    }

    #endregion

    #region List Compress Callbacks Tests

    [Fact]
    public async Task ListCompressCallbacksAsync_FilterByType_ReturnsFiltered()
    {
        // Arrange
        var service = CreateService();

        // Setup callbacks for multiple resource types
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0),
            CreateCompressCallback("character-personality", priority: 10)
        });

        SetupCompressCallbacksForResourceType("realm", new[]
        {
            CreateCompressCallback("realm-history", priority: 0)
        });

        var request = new ListCompressCallbacksRequest
        {
            ResourceType = "character"
        };

        // Act
        var (status, response) = await service.ListCompressCallbacksAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount);
        Assert.All(response.Callbacks, c => Assert.Equal("character", c.ResourceType));
    }

    [Fact]
    public async Task ListCompressCallbacksAsync_NoFilter_ReturnsAll()
    {
        // Arrange
        var service = CreateService();

        // Setup callbacks for multiple resource types
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0)
        });

        SetupCompressCallbacksForResourceType("realm", new[]
        {
            CreateCompressCallback("realm-history", priority: 0)
        });

        // Add master index
        _simulatedCompressIndex["compress-callback-resource-types"] = new HashSet<string> { "character", "realm" };

        var request = new ListCompressCallbacksRequest();

        // Act
        var (status, response) = await service.ListCompressCallbacksAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.TotalCount);
    }

    #endregion

    #region Compression Helper Methods

    private void SetupCompressCallbacksForResourceType(string resourceType, CompressCallbackDefinition[] callbacks)
    {
        var indexKey = $"compress-callback-index:{resourceType}";
        _simulatedCompressIndex[indexKey] = new HashSet<string>(callbacks.Select(c => c.SourceType));

        foreach (var callback in callbacks)
        {
            var callbackKey = $"compress-callback:{resourceType}:{callback.SourceType}";
            _mockCompressStore
                .Setup(s => s.GetAsync(callbackKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(callback);
        }
    }

    private static CompressCallbackDefinition CreateCompressCallback(string sourceType, int priority)
    {
        return new CompressCallbackDefinition
        {
            ResourceType = "character",
            SourceType = sourceType,
            ServiceName = sourceType,
            CompressEndpoint = $"/{sourceType}/get-compress-data",
            CompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}",
            DecompressEndpoint = $"/{sourceType}/restore-from-archive",
            DecompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\", \"data\": \"{{data}}\"}",
            Priority = priority,
            RegisteredAt = DateTimeOffset.UtcNow
        };
    }

    private void SetupSuccessfulLock()
    {
        var successLockResponse = new Mock<ILockResponse>();
        successLockResponse.Setup(l => l.Success).Returns(true);
        successLockResponse.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLockResponse.Object);
    }

    #endregion

    #region Snapshot Tests

    [Fact]
    public async Task ExecuteSnapshotAsync_NoCallbacks_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // No callbacks registered for this resource type

        var request = new ExecuteSnapshotRequest
        {
            ResourceType = "unknown-type",
            ResourceId = resourceId
        };

        // Act
        var (status, response) = await service.ExecuteSnapshotAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Contains("No compression callbacks", response.AbortReason);
        Assert.Null(response.SnapshotId);
        Assert.Empty(_capturedSnapshotSaves);
    }

    [Fact]
    public async Task ExecuteSnapshotAsync_DryRun_DoesNotStoreSnapshot()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0)
        });

        var request = new ExecuteSnapshotRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            DryRun = true
        };

        // Act
        var (status, response) = await service.ExecuteSnapshotAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.True(response.DryRun);
        Assert.Null(response.SnapshotId);
        Assert.Null(response.ExpiresAt);

        // Verify no snapshot was saved (dry run)
        Assert.Empty(_capturedSnapshotSaves);

        // Verify navigator was NOT called (dry run doesn't execute callbacks)
        _mockNavigator.Verify(
            n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteSnapshotAsync_UsesConfigurationDefaults_WhenNotSpecified()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0)
        });

        // Setup successful API call
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreboundApiResult
            {
                SubstitutionSucceeded = true,
                Result = new RawApiResult { StatusCode = 200, ResponseBody = "{\"data\": \"test\"}", Duration = TimeSpan.FromMilliseconds(50) }
            });

        var request = new ExecuteSnapshotRequest
        {
            ResourceType = "character",
            ResourceId = resourceId
            // No TtlSeconds or CompressionPolicy specified - should use config defaults
        };

        // Act
        var (status, response) = await service.ExecuteSnapshotAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotNull(response.SnapshotId);

        // Verify snapshot was saved with configured default TTL (3600 seconds = 1 hour)
        Assert.Single(_capturedSnapshotSaves);
        var (_, snapshot, options) = _capturedSnapshotSaves[0];
        Assert.NotNull(options);
        Assert.Equal(_configuration.SnapshotDefaultTtlSeconds, options.Ttl);

        // Verify expiration is approximately 1 hour from now
        Assert.NotNull(response.ExpiresAt);
        var expectedExpiry = DateTimeOffset.UtcNow.AddSeconds(_configuration.SnapshotDefaultTtlSeconds);
        var timeDiff = (expectedExpiry - response.ExpiresAt.Value).Duration();
        Assert.True(timeDiff < TimeSpan.FromSeconds(5), "ExpiresAt should be ~1 hour from now");
    }

    [Fact]
    public async Task ExecuteSnapshotAsync_ClampsTtlToConfiguredMinimum()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0)
        });

        // Setup successful API call
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreboundApiResult
            {
                SubstitutionSucceeded = true,
                Result = new RawApiResult { StatusCode = 200, ResponseBody = "{}", Duration = TimeSpan.FromMilliseconds(50) }
            });

        var request = new ExecuteSnapshotRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            TtlSeconds = 10 // Below minimum (60)
        };

        // Act
        var (status, response) = await service.ExecuteSnapshotAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);

        // Verify TTL was clamped to minimum (60)
        Assert.Single(_capturedSnapshotSaves);
        var (_, _, options) = _capturedSnapshotSaves[0];
        Assert.NotNull(options);
        Assert.Equal(_configuration.SnapshotMinTtlSeconds, options.Ttl);
    }

    [Fact]
    public async Task ExecuteSnapshotAsync_ClampsTtlToConfiguredMaximum()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0)
        });

        // Setup successful API call
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreboundApiResult
            {
                SubstitutionSucceeded = true,
                Result = new RawApiResult { StatusCode = 200, ResponseBody = "{}", Duration = TimeSpan.FromMilliseconds(50) }
            });

        var request = new ExecuteSnapshotRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            TtlSeconds = 1000000 // Above maximum (86400)
        };

        // Act
        var (status, response) = await service.ExecuteSnapshotAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);

        // Verify TTL was clamped to maximum (86400)
        Assert.Single(_capturedSnapshotSaves);
        var (_, _, options) = _capturedSnapshotSaves[0];
        Assert.NotNull(options);
        Assert.Equal(_configuration.SnapshotMaxTtlSeconds, options.Ttl);
    }

    [Fact]
    public async Task ExecuteSnapshotAsync_AllCallbacksSucceed_CreatesSnapshot()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0),
            CreateCompressCallback("character-personality", priority: 10)
        });

        // Setup successful API calls
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreboundApiResult
            {
                SubstitutionSucceeded = true,
                Result = new RawApiResult
                {
                    StatusCode = 200,
                    ResponseBody = "{\"characterId\": \"" + resourceId + "\", \"data\": \"test\"}",
                    Duration = TimeSpan.FromMilliseconds(50)
                }
            });

        var request = new ExecuteSnapshotRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            SnapshotType = "storyline-preview",
            TtlSeconds = 1800 // 30 minutes
        };

        // Act
        var (status, response) = await service.ExecuteSnapshotAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.False(response.DryRun);
        Assert.NotNull(response.SnapshotId);
        Assert.NotNull(response.ExpiresAt);
        Assert.Equal(2, response.CallbackResults.Count);
        Assert.All(response.CallbackResults, r => Assert.True(r.Success));

        // Verify snapshot was saved
        Assert.Single(_capturedSnapshotSaves);
        var (snapshotKey, snapshot, options) = _capturedSnapshotSaves[0];
        Assert.Contains(response.SnapshotId.ToString()!, snapshotKey);
        Assert.Equal(resourceId, snapshot.ResourceId);
        Assert.Equal("character", snapshot.ResourceType);
        Assert.Equal("storyline-preview", snapshot.SnapshotType);
        Assert.Equal(2, snapshot.Entries.Count);
        Assert.NotNull(options);
        Assert.Equal(1800, options.Ttl);
    }

    [Fact]
    public async Task ExecuteSnapshotAsync_PublishesSnapshotCreatedEvent()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0)
        });

        // Setup successful API call
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreboundApiResult
            {
                SubstitutionSucceeded = true,
                Result = new RawApiResult { StatusCode = 200, ResponseBody = "{}", Duration = TimeSpan.FromMilliseconds(50) }
            });

        var request = new ExecuteSnapshotRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            SnapshotType = "actor-state"
        };

        // Act
        await service.ExecuteSnapshotAsync(request, CancellationToken.None);

        // Assert - verify snapshot created event was published
        var snapshotEvent = _capturedPublishedEvents.FirstOrDefault(e => e.Topic == "resource.snapshot.created");
        Assert.NotNull(snapshotEvent.Event);
        var typedEvent = Assert.IsType<ResourceSnapshotCreatedEvent>(snapshotEvent.Event);
        Assert.Equal("character", typedEvent.ResourceType);
        Assert.Equal(resourceId, typedEvent.ResourceId);
        Assert.Equal("actor-state", typedEvent.SnapshotType);
        Assert.NotEqual(Guid.Empty, typedEvent.SnapshotId);
        Assert.True(typedEvent.ExpiresAt > DateTimeOffset.UtcNow, "ExpiresAt should be in the future");
        Assert.Equal(1, typedEvent.EntriesCount);
    }

    [Fact]
    public async Task ExecuteSnapshotAsync_CallbackFails_AllRequired_Aborts()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0),
            CreateCompressCallback("character-personality", priority: 10)
        });

        // Setup first callback to succeed, second to fail
        var callCount = 0;
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new PreboundApiResult
                    {
                        SubstitutionSucceeded = true,
                        Result = new RawApiResult { StatusCode = 200, ResponseBody = "{}", Duration = TimeSpan.FromMilliseconds(50) }
                    };
                }
                return new PreboundApiResult
                {
                    SubstitutionSucceeded = true,
                    Result = new RawApiResult { StatusCode = 500, ErrorMessage = "Service unavailable", Duration = TimeSpan.FromMilliseconds(50) }
                };
            });

        var request = new ExecuteSnapshotRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            CompressionPolicy = CompressionPolicy.ALL_REQUIRED
        };

        // Act
        var (status, response) = await service.ExecuteSnapshotAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Contains("ALL_REQUIRED", response.AbortReason);
        Assert.Null(response.SnapshotId);

        // Verify no snapshot was saved
        Assert.Empty(_capturedSnapshotSaves);
    }

    [Fact]
    public async Task ExecuteSnapshotAsync_CallbackFails_BestEffort_Continues()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0),
            CreateCompressCallback("character-personality", priority: 10)
        });

        // Setup first callback to succeed, second to fail
        var callCount = 0;
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new PreboundApiResult
                    {
                        SubstitutionSucceeded = true,
                        Result = new RawApiResult { StatusCode = 200, ResponseBody = "{\"data\": \"test\"}", Duration = TimeSpan.FromMilliseconds(50) }
                    };
                }
                return new PreboundApiResult
                {
                    SubstitutionSucceeded = true,
                    Result = new RawApiResult { StatusCode = 404, ErrorMessage = "Not found", Duration = TimeSpan.FromMilliseconds(50) }
                };
            });

        var request = new ExecuteSnapshotRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            CompressionPolicy = CompressionPolicy.BEST_EFFORT
        };

        // Act
        var (status, response) = await service.ExecuteSnapshotAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotNull(response.SnapshotId);

        // Verify partial results
        Assert.Equal(2, response.CallbackResults.Count);
        Assert.Single(response.CallbackResults, r => r.Success);
        Assert.Single(response.CallbackResults, r => !r.Success);

        // Verify snapshot was saved with partial data
        Assert.Single(_capturedSnapshotSaves);
        var (_, snapshot, _) = _capturedSnapshotSaves[0];
        Assert.Single(snapshot.Entries);
    }

    [Fact]
    public async Task ExecuteSnapshotAsync_AllCallbacksFail_BestEffort_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0)
        });

        // Setup all callbacks to fail
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreboundApiResult
            {
                SubstitutionSucceeded = true,
                Result = new RawApiResult { StatusCode = 500, ErrorMessage = "Server error", Duration = TimeSpan.FromMilliseconds(50) }
            });

        var request = new ExecuteSnapshotRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            CompressionPolicy = CompressionPolicy.BEST_EFFORT
        };

        // Act
        var (status, response) = await service.ExecuteSnapshotAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Contains("No successful snapshot callbacks", response.AbortReason);
        Assert.Null(response.SnapshotId);

        // Verify no snapshot was saved
        Assert.Empty(_capturedSnapshotSaves);
    }

    [Fact]
    public async Task GetSnapshotAsync_Exists_ReturnsSnapshot()
    {
        // Arrange
        var service = CreateService();
        var snapshotId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        // Pre-populate simulated snapshot
        var snapshotKey = $"snap:{snapshotId}";
        _simulatedSnapshots[snapshotKey] = new ResourceSnapshotModel
        {
            SnapshotId = snapshotId,
            ResourceType = "character",
            ResourceId = resourceId,
            SnapshotType = "actor-state",
            Entries = new List<ArchiveEntryModel>
            {
                new()
                {
                    SourceType = "character-base",
                    ServiceName = "character",
                    Data = "H4sIAAAAAAAAA6tWKkktLlGyUlAqS8wpTgUA3+5RLRAAAAA=", // Base64-encoded gzipped JSON
                    CompressedAt = DateTimeOffset.UtcNow,
                    DataChecksum = "abc123",
                    OriginalSizeBytes = 100
                }
            },
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(55)
        };

        var request = new GetSnapshotRequest
        {
            SnapshotId = snapshotId
        };

        // Act
        var (status, response) = await service.GetSnapshotAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Found);
        Assert.Equal(snapshotId, response.SnapshotId);
        Assert.NotNull(response.Snapshot);
        Assert.Equal(snapshotId, response.Snapshot.SnapshotId);
        Assert.Equal("character", response.Snapshot.ResourceType);
        Assert.Equal(resourceId, response.Snapshot.ResourceId);
        Assert.Equal("actor-state", response.Snapshot.SnapshotType);
        Assert.Single(response.Snapshot.Entries);
        Assert.Equal("character-base", response.Snapshot.Entries.First().SourceType);
    }

    [Fact]
    public async Task GetSnapshotAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var snapshotId = Guid.NewGuid();

        // Snapshot not in simulated store (simulating expired or non-existent)

        var request = new GetSnapshotRequest
        {
            SnapshotId = snapshotId
        };

        // Act
        var (status, response) = await service.GetSnapshotAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Found);
        Assert.Equal(snapshotId, response.SnapshotId);
        Assert.Null(response.Snapshot);
    }

    [Fact]
    public async Task ExecuteSnapshotAsync_CustomTtlWithinRange_UsesRequestedTtl()
    {
        // Arrange
        var service = CreateService();
        var resourceId = Guid.NewGuid();

        // Setup callbacks
        SetupCompressCallbacksForResourceType("character", new[]
        {
            CreateCompressCallback("character-base", priority: 0)
        });

        // Setup successful API call
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreboundApiResult
            {
                SubstitutionSucceeded = true,
                Result = new RawApiResult { StatusCode = 200, ResponseBody = "{}", Duration = TimeSpan.FromMilliseconds(50) }
            });

        var customTtl = 7200; // 2 hours - within range (60-86400)
        var request = new ExecuteSnapshotRequest
        {
            ResourceType = "character",
            ResourceId = resourceId,
            TtlSeconds = customTtl
        };

        // Act
        var (status, response) = await service.ExecuteSnapshotAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);

        // Verify TTL used requested value (within range, not clamped)
        Assert.Single(_capturedSnapshotSaves);
        var (_, _, options) = _capturedSnapshotSaves[0];
        Assert.NotNull(options);
        Assert.Equal(customTtl, options.Ttl);
    }

    #endregion

    #region Permission Registration Tests

    [Fact]
    public void ResourcePermissionRegistration_GetEndpoints_ReturnsAllEndpoints()
    {
        // Act
        var endpoints = ResourcePermissionRegistration.GetEndpoints();

        // Assert
        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);
        Assert.True(endpoints.Count >= 6, $"Expected at least 6 endpoints, got {endpoints.Count}");
    }

    [Fact]
    public void ResourcePermissionRegistration_GetEndpoints_AllEndpointsArePOST()
    {
        // Act
        var endpoints = ResourcePermissionRegistration.GetEndpoints();

        // Assert
        foreach (var endpoint in endpoints)
        {
            Assert.Equal(ServiceEndpointMethod.POST, endpoint.Method);
        }
    }

    [Fact]
    public void ResourcePermissionRegistration_GetEndpoints_AllEndpointsHavePermissions()
    {
        // Act
        var endpoints = ResourcePermissionRegistration.GetEndpoints();

        // Assert
        foreach (var endpoint in endpoints)
        {
            Assert.NotNull(endpoint.Permissions);
            Assert.NotEmpty(endpoint.Permissions);
        }
    }

    #endregion
}
