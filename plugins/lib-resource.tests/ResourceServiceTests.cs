using System.Collections.ObjectModel;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Resource.Tests;

/// <summary>
/// Unit tests for ResourceService reference counting and cleanup logic.
/// Tests verify atomic reference tracking, grace period management, and cleanup policies.
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

    // Capture containers for verifying side effects
    private readonly List<(string Key, ResourceReferenceEntry Entry)> _capturedSetAdds = new();
    private readonly List<string> _capturedSetDeletes = new();
    private readonly List<(string Key, GracePeriodRecord Record)> _capturedGraceSaves = new();
    private readonly List<string> _capturedGraceDeletes = new();
    private readonly List<(string Topic, object Event)> _capturedPublishedEvents = new();

    // Simulated set state for testing
    private readonly Dictionary<string, HashSet<ResourceReferenceEntry>> _simulatedSets = new();

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

        _configuration = new ResourceServiceConfiguration
        {
            DefaultGracePeriodSeconds = 3600, // 1 hour for faster tests
            CleanupCallbackTimeoutSeconds = 30,
            CleanupLockExpirySeconds = 300,
            DefaultCleanupPolicy = CleanupPolicy.BEST_EFFORT
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
    }

    private ResourceService CreateService()
    {
        return new ResourceService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockNavigator.Object,
            _mockLockProvider.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object);
    }

    private void ClearCaptures()
    {
        _capturedSetAdds.Clear();
        _capturedSetDeletes.Clear();
        _capturedGraceSaves.Clear();
        _capturedGraceDeletes.Clear();
        _capturedPublishedEvents.Clear();
        _simulatedSets.Clear();
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

        Assert.Equal(604800, config.DefaultGracePeriodSeconds); // 7 days
        Assert.Equal(30, config.CleanupCallbackTimeoutSeconds);
        Assert.Equal(300, config.CleanupLockExpirySeconds);
        Assert.Equal(CleanupPolicy.BEST_EFFORT, config.DefaultCleanupPolicy);
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
