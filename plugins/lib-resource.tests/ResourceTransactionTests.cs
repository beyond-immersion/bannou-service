using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Resource.Tests;

/// <summary>
/// Unit tests for ResourceService transaction management: BeginTransaction, RegisterProvision,
/// ConfirmProvision, CommitTransaction, AbortTransaction, GetTransactionStatus.
/// Tests verify transaction lifecycle, provision registration, compensation, and reference
/// registration during commit.
/// </summary>
public class ResourceTransactionTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IServiceNavigator> _mockNavigator;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<ResourceService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly ResourceServiceConfiguration _configuration;

    // Reference management stores (needed for CommitTransaction reference registration)
    private readonly Mock<ICacheableStateStore<ResourceReferenceEntry>> _mockRefStore;
    private readonly Mock<IStateStore<GracePeriodRecord>> _mockGraceStore;
    private readonly Mock<IStateStore<CleanupCallbackDefinition>> _mockCleanupStore;
    private readonly Mock<ICacheableStateStore<string>> _mockCleanupIndexStore;
    private readonly Mock<IStateStore<CompressCallbackDefinition>> _mockCompressStore;
    private readonly Mock<IStateStore<ResourceArchiveModel>> _mockArchiveStore;
    private readonly Mock<ICacheableStateStore<string>> _mockCompressIndexStore;
    private readonly Mock<IStateStore<ResourceSnapshotModel>> _mockSnapshotStore;

    // Migration stores
    private readonly Mock<IStateStore<MigrateCallbackDefinition>> _mockMigrateStore;
    private readonly Mock<ICacheableStateStore<string>> _mockMigrateIndexStore;

    // Transaction-specific stores
    private readonly Mock<IStateStore<ResourceTransactionModel>> _mockTransactionStore;
    private readonly Mock<IStateStore<ResourceProvisionModel>> _mockProvisionStore;
    private readonly Mock<IStateStore<string>> _mockProvisionStringStore;

    // Capture containers for verifying side effects
    private readonly List<(string Topic, object Event)> _capturedPublishedEvents = new();
    private readonly List<(string Key, ResourceTransactionModel Transaction)> _capturedTransactionSaves = new();
    private readonly List<(string Key, ResourceProvisionModel Provision)> _capturedProvisionSaves = new();

    // Simulated state
    private readonly Dictionary<string, ResourceTransactionModel> _simulatedTransactions = new();
    private readonly Dictionary<string, ResourceProvisionModel> _simulatedProvisions = new();
    private readonly Dictionary<string, string> _simulatedProvisionIndexes = new();
    private readonly Dictionary<string, HashSet<ResourceReferenceEntry>> _simulatedRefSets = new();

    public ResourceTransactionTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockNavigator = new Mock<IServiceNavigator>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<ResourceService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        // Reference stores
        _mockRefStore = new Mock<ICacheableStateStore<ResourceReferenceEntry>>();
        _mockGraceStore = new Mock<IStateStore<GracePeriodRecord>>();
        _mockCleanupStore = new Mock<IStateStore<CleanupCallbackDefinition>>();
        _mockCleanupIndexStore = new Mock<ICacheableStateStore<string>>();
        _mockCompressStore = new Mock<IStateStore<CompressCallbackDefinition>>();
        _mockArchiveStore = new Mock<IStateStore<ResourceArchiveModel>>();
        _mockCompressIndexStore = new Mock<ICacheableStateStore<string>>();
        _mockSnapshotStore = new Mock<IStateStore<ResourceSnapshotModel>>();

        // Migration stores
        _mockMigrateStore = new Mock<IStateStore<MigrateCallbackDefinition>>();
        _mockMigrateIndexStore = new Mock<ICacheableStateStore<string>>();

        // Transaction stores
        _mockTransactionStore = new Mock<IStateStore<ResourceTransactionModel>>();
        _mockProvisionStore = new Mock<IStateStore<ResourceProvisionModel>>();
        _mockProvisionStringStore = new Mock<IStateStore<string>>();

        _configuration = new ResourceServiceConfiguration
        {
            DefaultGracePeriodSeconds = 3600,
            CleanupCallbackTimeoutSeconds = 30,
            CleanupLockExpirySeconds = 300,
            DefaultCleanupPolicy = CleanupPolicy.BestEffort,
            DefaultCompressionPolicy = CompressionPolicy.AllRequired,
            CompressionCallbackTimeoutSeconds = 60,
            CompressionLockExpirySeconds = 600,
            SnapshotDefaultTtlSeconds = 3600,
            SnapshotMinTtlSeconds = 60,
            SnapshotMaxTtlSeconds = 86400,
            // Transaction configuration
            TransactionDefaultTtlSeconds = 120,
            TransactionMinTtlSeconds = 10,
            TransactionMaxTtlSeconds = 600,
            TransactionRecoveryWorkerIntervalSeconds = 30,
            TransactionRecoveryWorkerStartupDelaySeconds = 15,
            TransactionCompensationMaxRetries = 10,
            TransactionCompensationBackoffBaseSeconds = 5,
            TransactionCommitMaxRetries = 10,
            TransactionValidationMaxRetries = 5,
            TransactionRetentionDays = 7,
            ProvisionIndexMaxRetries = 3,
        };

        SetupMocks();
    }

    private void SetupMocks()
    {
        // Factory → reference stores
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
            .Returns(_mockCleanupIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CompressCallbackDefinition>(StateStoreDefinitions.ResourceCompress))
            .Returns(_mockCompressStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ResourceArchiveModel>(StateStoreDefinitions.ResourceArchives))
            .Returns(_mockArchiveStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<string>(StateStoreDefinitions.ResourceCompress))
            .Returns(_mockCompressIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ResourceSnapshotModel>(StateStoreDefinitions.ResourceSnapshots))
            .Returns(_mockSnapshotStore.Object);

        // Factory → migration stores
        _mockStateStoreFactory
            .Setup(f => f.GetStore<MigrateCallbackDefinition>(StateStoreDefinitions.ResourceMigrate))
            .Returns(_mockMigrateStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<string>(StateStoreDefinitions.ResourceMigrate))
            .Returns(_mockMigrateIndexStore.Object);

        // Factory → transaction stores
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ResourceTransactionModel>(StateStoreDefinitions.ResourceTransactions))
            .Returns(_mockTransactionStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ResourceProvisionModel>(StateStoreDefinitions.ResourceProvisions))
            .Returns(_mockProvisionStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<string>(StateStoreDefinitions.ResourceProvisions))
            .Returns(_mockProvisionStringStore.Object);

        // Transaction store: GetAsync with simulated state
        _mockTransactionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                _simulatedTransactions.TryGetValue(key, out var tx) ? tx : null);

        // Transaction store: GetWithETagAsync with simulated state
        _mockTransactionStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                _simulatedTransactions.TryGetValue(key, out var tx) ? (tx, "mock-etag") : (null, null));

        // Transaction store: SaveAsync with capture and simulated state update
        _mockTransactionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ResourceTransactionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ResourceTransactionModel, StateOptions?, CancellationToken>((key, tx, _, _) =>
            {
                _capturedTransactionSaves.Add((key, tx));
                _simulatedTransactions[key] = tx;
            })
            .ReturnsAsync("mock-etag");

        // Transaction store: TrySaveAsync (optimistic concurrency)
        _mockTransactionStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ResourceTransactionModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ResourceTransactionModel, string, StateOptions?, CancellationToken>((key, tx, _, _, _) =>
            {
                _capturedTransactionSaves.Add((key, tx));
                _simulatedTransactions[key] = tx;
            })
            .ReturnsAsync("mock-etag-new");

        // Provision store: GetAsync with simulated state
        _mockProvisionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                _simulatedProvisions.TryGetValue(key, out var prov) ? prov : null);

        // Provision store: SaveAsync with capture and simulated state update
        _mockProvisionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ResourceProvisionModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ResourceProvisionModel, StateOptions?, CancellationToken>((key, prov, _, _) =>
            {
                _capturedProvisionSaves.Add((key, prov));
                _simulatedProvisions[key] = prov;
            })
            .ReturnsAsync("mock-etag");

        // Provision string store: GetAsync with simulated index state
        _mockProvisionStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                _simulatedProvisionIndexes.TryGetValue(key, out var json) ? json : null);

        // Provision string store: AddToStringListAsync (ETag-based helper)
        // Simulate by appending to the JSON list
        _mockProvisionStringStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                _simulatedProvisionIndexes.TryGetValue(key, out var json) ? (json, "mock-etag") : (null, null));

        _mockProvisionStringStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, StateOptions?, CancellationToken>((key, value, _, _, _) =>
                _simulatedProvisionIndexes[key] = value)
            .ReturnsAsync("mock-etag-new");

        _mockProvisionStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, StateOptions?, CancellationToken>((key, value, _, _) =>
                _simulatedProvisionIndexes[key] = value)
            .ReturnsAsync("mock-etag");

        // Reference store (needed for CommitTransaction)
        _mockRefStore
            .Setup(s => s.AddToSetAsync(It.IsAny<string>(), It.IsAny<ResourceReferenceEntry>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ResourceReferenceEntry, StateOptions?, CancellationToken>((key, entry, _, _) =>
            {
                if (!_simulatedRefSets.ContainsKey(key))
                    _simulatedRefSets[key] = new HashSet<ResourceReferenceEntry>();
                _simulatedRefSets[key].Add(entry);
            })
            .ReturnsAsync(true);

        _mockRefStore
            .Setup(s => s.SetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                _simulatedRefSets.ContainsKey(key) ? _simulatedRefSets[key].Count : 0);

        // Grace store defaults
        _mockGraceStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Message bus with capture
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
            _mockEventConsumer.Object,
            Array.Empty<ISeededResourceProvider>(),
            _mockTelemetryProvider.Object);
    }

    /// <summary>
    /// Seeds a transaction into simulated state for tests that need a pre-existing transaction.
    /// </summary>
    private ResourceTransactionModel SeedTransaction(
        Guid? transactionId = null,
        TransactionStatus status = TransactionStatus.Active,
        string ownerService = "genesis",
        string parentResourceType = "character",
        Guid? parentResourceId = null,
        int ttlSeconds = 120)
    {
        var txId = transactionId ?? Guid.NewGuid();
        var parentId = parentResourceId ?? Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var transaction = new ResourceTransactionModel
        {
            TransactionId = txId,
            OwnerService = ownerService,
            ParentResourceType = parentResourceType,
            ParentResourceId = parentId,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
            TtlSeconds = ttlSeconds,
            ValidationAttempts = 0,
        };

        _simulatedTransactions[$"tx:{txId}"] = transaction;
        return transaction;
    }

    /// <summary>
    /// Seeds a provision into simulated state and adds it to the transaction's provision index.
    /// </summary>
    private ResourceProvisionModel SeedProvision(
        Guid transactionId,
        Guid? provisionId = null,
        Guid? resourceId = null,
        int sequenceNumber = 0,
        ProvisionStatus status = ProvisionStatus.Pending,
        string resourceType = "seed",
        string compensation = "{}")
    {
        var provId = provisionId ?? Guid.NewGuid();
        var resId = resourceId ?? Guid.NewGuid();

        var provision = new ResourceProvisionModel
        {
            ProvisionId = provId,
            TransactionId = transactionId,
            SequenceNumber = sequenceNumber,
            ResourceType = resourceType,
            ResourceId = resId,
            Status = status,
            RegisteredAt = DateTimeOffset.UtcNow,
            CompensationAttempts = 0,
            Compensation = compensation,
        };

        _simulatedProvisions[$"prov:{provId}"] = provision;

        // Append to the provision index for this transaction
        var indexKey = $"prov-tx:{transactionId}";
        if (_simulatedProvisionIndexes.TryGetValue(indexKey, out var existing))
        {
            var list = BannouJson.Deserialize<List<string>>(existing) ?? new List<string>();
            list.Add(provId.ToString());
            _simulatedProvisionIndexes[indexKey] = BannouJson.Serialize(list);
        }
        else
        {
            _simulatedProvisionIndexes[indexKey] = BannouJson.Serialize(new List<string> { provId.ToString() });
        }

        return provision;
    }

    // =========================================================================
    // BeginTransaction Tests
    // =========================================================================

    #region BeginTransaction Tests

    [Fact]
    public async Task BeginTransactionAsync_CreatesActiveTransaction_WithDefaultTtl()
    {
        // Arrange
        var service = CreateService();
        var parentResourceId = Guid.NewGuid();
        var request = new BeginTransactionRequest
        {
            OwnerService = "genesis",
            ParentResourceType = "character",
            ParentResourceId = parentResourceId,
        };

        // Act
        var (status, response) = await service.BeginTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.TransactionId);
        Assert.Equal(TransactionStatus.Active, response.Status);
        Assert.Equal(_configuration.TransactionDefaultTtlSeconds, response.TtlSeconds);
        Assert.True(response.ExpiresAt > DateTimeOffset.UtcNow);

        // Verify transaction was saved
        Assert.Single(_capturedTransactionSaves);
        var (key, savedTx) = _capturedTransactionSaves[0];
        Assert.Equal($"tx:{response.TransactionId}", key);
        Assert.Equal("genesis", savedTx.OwnerService);
        Assert.Equal("character", savedTx.ParentResourceType);
        Assert.Equal(parentResourceId, savedTx.ParentResourceId);
        Assert.Equal(TransactionStatus.Active, savedTx.Status);
        Assert.Equal(_configuration.TransactionDefaultTtlSeconds, savedTx.TtlSeconds);
        Assert.Null(savedTx.CompletionValidation);

        // Verify event published
        Assert.Single(_capturedPublishedEvents);
        var (topic, evt) = _capturedPublishedEvents[0];
        Assert.Equal("resource.transaction.created", topic);
        var createdEvent = Assert.IsType<ResourceTransactionCreatedEvent>(evt);
        Assert.Equal(response.TransactionId, createdEvent.TransactionId);
        Assert.Equal("genesis", createdEvent.OwnerService);
        Assert.Equal("character", createdEvent.ParentResourceType);
        Assert.Equal(parentResourceId, createdEvent.ParentResourceId);
    }

    [Fact]
    public async Task BeginTransactionAsync_ClampsTtl_ToConfiguredMinimum()
    {
        // Arrange
        var service = CreateService();
        var request = new BeginTransactionRequest
        {
            OwnerService = "genesis",
            ParentResourceType = "character",
            ParentResourceId = Guid.NewGuid(),
            TtlSeconds = 1, // Below configured minimum of 10
        };

        // Act
        var (status, response) = await service.BeginTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(_configuration.TransactionMinTtlSeconds, response.TtlSeconds);
    }

    [Fact]
    public async Task BeginTransactionAsync_ClampsTtl_ToConfiguredMaximum()
    {
        // Arrange
        var service = CreateService();
        var request = new BeginTransactionRequest
        {
            OwnerService = "genesis",
            ParentResourceType = "character",
            ParentResourceId = Guid.NewGuid(),
            TtlSeconds = 99999, // Above configured maximum of 600
        };

        // Act
        var (status, response) = await service.BeginTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(_configuration.TransactionMaxTtlSeconds, response.TtlSeconds);
    }

    [Fact]
    public async Task BeginTransactionAsync_StoresCompletionValidation_WhenProvided()
    {
        // Arrange
        var service = CreateService();
        var completionValidation = new PreboundApi
        {
            ServiceName = "character",
            Endpoint = "/character/exists",
            PayloadTemplate = "{\"characterId\": \"{{parentResourceId}}\"}",
        };
        var request = new BeginTransactionRequest
        {
            OwnerService = "genesis",
            ParentResourceType = "character",
            ParentResourceId = Guid.NewGuid(),
            CompletionValidation = completionValidation,
        };

        // Act
        var (status, response) = await service.BeginTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify completion validation was serialized and stored
        var (_, savedTx) = _capturedTransactionSaves[0];
        Assert.NotNull(savedTx.CompletionValidation);
        Assert.Contains("character", savedTx.CompletionValidation);
        Assert.Contains("/character/exists", savedTx.CompletionValidation);
    }

    [Fact]
    public async Task BeginTransactionAsync_StoresExpectedProvisionCount_WhenProvided()
    {
        // Arrange
        var service = CreateService();
        var request = new BeginTransactionRequest
        {
            OwnerService = "genesis",
            ParentResourceType = "character",
            ParentResourceId = Guid.NewGuid(),
            ExpectedProvisionCount = 5,
        };

        // Act
        var (_, response) = await service.BeginTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(response);
        var (_, savedTx) = _capturedTransactionSaves[0];
        Assert.Equal(5, savedTx.ExpectedProvisionCount);
    }

    #endregion

    // =========================================================================
    // RegisterProvision Tests
    // =========================================================================

    #region RegisterProvision Tests

    [Fact]
    public async Task RegisterProvisionAsync_ValidTransaction_CreatesPendingProvision()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction();
        var resourceId = Guid.NewGuid();

        var request = new RegisterProvisionRequest
        {
            TransactionId = transaction.TransactionId,
            ResourceType = "seed",
            ResourceId = resourceId,
            Compensation = new PreboundApi
            {
                ServiceName = "seed",
                Endpoint = "/seed/delete",
                PayloadTemplate = "{\"seedId\": \"{{provisionResourceId}}\"}",
            },
        };

        // Act
        var (status, response) = await service.RegisterProvisionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.ProvisionId);
        Assert.Equal(0, response.SequenceNumber);
        Assert.Equal(ProvisionStatus.Pending, response.Status);

        // Verify provision was saved
        var savedProvision = _capturedProvisionSaves.First(p => p.Provision.ProvisionId == response.ProvisionId);
        Assert.Equal(transaction.TransactionId, savedProvision.Provision.TransactionId);
        Assert.Equal("seed", savedProvision.Provision.ResourceType);
        Assert.Equal(resourceId, savedProvision.Provision.ResourceId);
        Assert.Equal(ProvisionStatus.Pending, savedProvision.Provision.Status);
        Assert.NotEmpty(savedProvision.Provision.Compensation);
    }

    [Fact]
    public async Task RegisterProvisionAsync_TransactionNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new RegisterProvisionRequest
        {
            TransactionId = Guid.NewGuid(), // Does not exist
            ResourceType = "seed",
            ResourceId = Guid.NewGuid(),
            Compensation = new PreboundApi
            {
                ServiceName = "seed",
                Endpoint = "/seed/delete",
                PayloadTemplate = "{}",
            },
        };

        // Act
        var (status, response) = await service.RegisterProvisionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RegisterProvisionAsync_TransactionNotActive_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction(status: TransactionStatus.Committing);

        var request = new RegisterProvisionRequest
        {
            TransactionId = transaction.TransactionId,
            ResourceType = "seed",
            ResourceId = Guid.NewGuid(),
            Compensation = new PreboundApi
            {
                ServiceName = "seed",
                Endpoint = "/seed/delete",
                PayloadTemplate = "{}",
            },
        };

        // Act
        var (status, response) = await service.RegisterProvisionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RegisterProvisionAsync_IncrementsSequenceNumber_ForMultipleProvisions()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction();

        // Register first provision to pre-populate the index
        SeedProvision(transaction.TransactionId, sequenceNumber: 0);

        var request = new RegisterProvisionRequest
        {
            TransactionId = transaction.TransactionId,
            ResourceType = "currency-wallet",
            ResourceId = Guid.NewGuid(),
            Compensation = new PreboundApi
            {
                ServiceName = "currency",
                Endpoint = "/currency/wallet/delete",
                PayloadTemplate = "{}",
            },
        };

        // Act
        var (status, response) = await service.RegisterProvisionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.SequenceNumber); // Second provision = sequence 1
    }

    #endregion

    // =========================================================================
    // ConfirmProvision Tests
    // =========================================================================

    #region ConfirmProvision Tests

    [Fact]
    public async Task ConfirmProvisionAsync_PendingProvision_TransitionsToProvisioned()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction();
        var provision = SeedProvision(transaction.TransactionId, status: ProvisionStatus.Pending);

        var request = new ConfirmProvisionRequest
        {
            TransactionId = transaction.TransactionId,
            ResourceId = provision.ResourceId,
        };

        // Act
        var (status, response) = await service.ConfirmProvisionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(provision.ProvisionId, response.ProvisionId);
        Assert.Equal(ProvisionStatus.Provisioned, response.Status);

        // Verify provision saved with updated status
        var saved = _capturedProvisionSaves.Last(p => p.Provision.ProvisionId == provision.ProvisionId);
        Assert.Equal(ProvisionStatus.Provisioned, saved.Provision.Status);
        Assert.NotNull(saved.Provision.ProvisionedAt);
    }

    [Fact]
    public async Task ConfirmProvisionAsync_TransactionNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new ConfirmProvisionRequest
        {
            TransactionId = Guid.NewGuid(),
            ResourceId = Guid.NewGuid(),
        };

        // Act
        var (status, response) = await service.ConfirmProvisionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ConfirmProvisionAsync_TransactionNotActive_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction(status: TransactionStatus.Committed);

        var request = new ConfirmProvisionRequest
        {
            TransactionId = transaction.TransactionId,
            ResourceId = Guid.NewGuid(),
        };

        // Act
        var (status, response) = await service.ConfirmProvisionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ConfirmProvisionAsync_ProvisionNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction();

        var request = new ConfirmProvisionRequest
        {
            TransactionId = transaction.TransactionId,
            ResourceId = Guid.NewGuid(), // No matching provision
        };

        // Act
        var (status, response) = await service.ConfirmProvisionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ConfirmProvisionAsync_ProvisionAlreadyProvisioned_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction();
        var provision = SeedProvision(transaction.TransactionId, status: ProvisionStatus.Provisioned);

        var request = new ConfirmProvisionRequest
        {
            TransactionId = transaction.TransactionId,
            ResourceId = provision.ResourceId,
        };

        // Act
        var (status, response) = await service.ConfirmProvisionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    // =========================================================================
    // CommitTransaction Tests
    // =========================================================================

    #region CommitTransaction Tests

    [Fact]
    public async Task CommitTransactionAsync_AllProvisioned_RegistersReferencesAndCommits()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction();
        var provision1 = SeedProvision(transaction.TransactionId, sequenceNumber: 0,
            status: ProvisionStatus.Provisioned, resourceType: "seed");
        var provision2 = SeedProvision(transaction.TransactionId, sequenceNumber: 1,
            status: ProvisionStatus.Provisioned, resourceType: "currency-wallet");

        var request = new CommitTransactionRequest
        {
            TransactionId = transaction.TransactionId,
        };

        // Act
        var (status, response) = await service.CommitTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(transaction.TransactionId, response.TransactionId);
        Assert.Equal(TransactionStatus.Committed, response.Status);
        Assert.Equal(2, response.ReferencesRegistered);

        // Verify references were registered via ref store
        Assert.Equal(2, _simulatedRefSets.Values.Sum(s => s.Count));

        // Verify committed event published
        var committedEvent = _capturedPublishedEvents
            .FirstOrDefault(e => e.Event is ResourceTransactionCommittedEvent);
        Assert.NotNull(committedEvent.Event);
        var evt = Assert.IsType<ResourceTransactionCommittedEvent>(committedEvent.Event);
        Assert.Equal(transaction.TransactionId, evt.TransactionId);
        Assert.Equal(2, evt.ProvisionCount);
    }

    [Fact]
    public async Task CommitTransactionAsync_TransactionNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new CommitTransactionRequest
        {
            TransactionId = Guid.NewGuid(),
        };

        // Act
        var (status, response) = await service.CommitTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CommitTransactionAsync_TransactionNotActive_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction(status: TransactionStatus.Aborted);

        var request = new CommitTransactionRequest
        {
            TransactionId = transaction.TransactionId,
        };

        // Act
        var (status, response) = await service.CommitTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CommitTransactionAsync_TransitionsToCommittingFirst_ThenCommitted()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction();

        // Track the status values at each TrySaveAsync call (captures by value, not reference)
        var trySaveStatuses = new List<TransactionStatus>();
        _mockTransactionStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ResourceTransactionModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ResourceTransactionModel, string, StateOptions?, CancellationToken>((key, tx, _, _, _) =>
            {
                trySaveStatuses.Add(tx.Status); // Capture value at call time
                _capturedTransactionSaves.Add((key, tx));
                _simulatedTransactions[key] = tx;
            })
            .ReturnsAsync("mock-etag-new");

        // No provisions — commit transitions directly
        var request = new CommitTransactionRequest
        {
            TransactionId = transaction.TransactionId,
        };

        // Act
        var (status, response) = await service.CommitTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TransactionStatus.Committed, response.Status);

        // Verify TrySaveAsync was called with Committing status (Phase 1 checkpoint)
        Assert.Single(trySaveStatuses);
        Assert.Equal(TransactionStatus.Committing, trySaveStatuses[0]);
    }

    #endregion

    // =========================================================================
    // AbortTransaction Tests
    // =========================================================================

    #region AbortTransaction Tests

    [Fact]
    public async Task AbortTransactionAsync_PendingProvisions_MarkedCompensatedDirectly()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction();
        var provision = SeedProvision(transaction.TransactionId, status: ProvisionStatus.Pending);

        var request = new AbortTransactionRequest
        {
            TransactionId = transaction.TransactionId,
            Reason = "Test abort",
        };

        // Act
        var (status, response) = await service.AbortTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TransactionStatus.Aborted, response.Status);
        Assert.Equal(0, response.CompensatedCount); // Pending provisions counted separately
        Assert.Equal(0, response.FailedCount);
        Assert.Equal(1, response.PendingCount);

        // Verify provision was marked compensated
        var savedProv = _capturedProvisionSaves.Last(p => p.Provision.ProvisionId == provision.ProvisionId);
        Assert.Equal(ProvisionStatus.Compensated, savedProv.Provision.Status);
        Assert.NotNull(savedProv.Provision.CompensatedAt);

        // Verify aborted event published
        var abortedEvent = _capturedPublishedEvents
            .FirstOrDefault(e => e.Event is ResourceTransactionAbortedEvent);
        Assert.NotNull(abortedEvent.Event);
    }

    [Fact]
    public async Task AbortTransactionAsync_TransactionNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new AbortTransactionRequest
        {
            TransactionId = Guid.NewGuid(),
        };

        // Act
        var (status, response) = await service.AbortTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AbortTransactionAsync_AlreadyCommitted_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction(status: TransactionStatus.Committed);

        var request = new AbortTransactionRequest
        {
            TransactionId = transaction.TransactionId,
        };

        // Act
        var (status, response) = await service.AbortTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AbortTransactionAsync_AlreadyAborting_ResumesSafely()
    {
        // Arrange — transaction already in Aborting state (e.g., worker started abort)
        var service = CreateService();
        var transaction = SeedTransaction(status: TransactionStatus.Aborting);
        transaction.AbortReason = "Worker started abort";

        var request = new AbortTransactionRequest
        {
            TransactionId = transaction.TransactionId,
        };

        // Act — should not fail or return BadRequest
        var (status, response) = await service.AbortTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TransactionStatus.Aborted, response.Status);
    }

    [Fact]
    public async Task AbortTransactionAsync_CompensationFailure_RemainsAborting()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction();
        var provision = SeedProvision(transaction.TransactionId,
            status: ProvisionStatus.Provisioned,
            compensation: BannouJson.Serialize(new PreboundApi
            {
                ServiceName = "seed",
                Endpoint = "/seed/delete",
                PayloadTemplate = "{\"seedId\": \"{{provisionResourceId}}\"}",
            }));

        // Make the navigator return a failure
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(It.IsAny<PreboundApi>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreboundApiResult
            {
                Result = RawApiResult.Failure("Internal Server Error", statusCode: 500),
            });

        var request = new AbortTransactionRequest
        {
            TransactionId = transaction.TransactionId,
            Reason = "Compensation failure test",
        };

        // Act
        var (status, response) = await service.AbortTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TransactionStatus.Aborting, response.Status); // Remains aborting
        Assert.Equal(0, response.CompensatedCount);
        Assert.Equal(1, response.FailedCount);

        // Verify provision marked as CompensationFailed
        var savedProv = _capturedProvisionSaves.Last(p => p.Provision.ProvisionId == provision.ProvisionId);
        Assert.Equal(ProvisionStatus.CompensationFailed, savedProv.Provision.Status);
        Assert.Equal(1, savedProv.Provision.CompensationAttempts);
    }

    [Fact]
    public async Task AbortTransactionAsync_ProvisionedProvision_CompensatesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction();
        var provision = SeedProvision(transaction.TransactionId,
            status: ProvisionStatus.Provisioned,
            compensation: BannouJson.Serialize(new PreboundApi
            {
                ServiceName = "seed",
                Endpoint = "/seed/delete",
                PayloadTemplate = "{\"seedId\": \"{{provisionResourceId}}\"}",
            }));

        // Make the navigator return success
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(It.IsAny<PreboundApi>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreboundApiResult
            {
                Result = RawApiResult.Success(200, "{}", TimeSpan.FromMilliseconds(10)),
            });

        var request = new AbortTransactionRequest
        {
            TransactionId = transaction.TransactionId,
            Reason = "Test abort with compensation",
        };

        // Act
        var (status, response) = await service.AbortTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TransactionStatus.Aborted, response.Status);
        Assert.Equal(1, response.CompensatedCount);
        Assert.Equal(0, response.FailedCount);

        // Verify provision marked as Compensated
        var savedProv = _capturedProvisionSaves.Last(p => p.Provision.ProvisionId == provision.ProvisionId);
        Assert.Equal(ProvisionStatus.Compensated, savedProv.Provision.Status);
        Assert.NotNull(savedProv.Provision.CompensatedAt);
    }

    [Fact]
    public async Task AbortTransactionAsync_NotFoundCompensation_TreatsAsSuccess()
    {
        // Arrange — a 404 from compensation means the resource is already gone
        var service = CreateService();
        var transaction = SeedTransaction();
        var provision = SeedProvision(transaction.TransactionId,
            status: ProvisionStatus.Provisioned,
            compensation: BannouJson.Serialize(new PreboundApi
            {
                ServiceName = "seed",
                Endpoint = "/seed/delete",
                PayloadTemplate = "{}",
            }));

        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(It.IsAny<PreboundApi>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PreboundApiResult
            {
                Result = RawApiResult.Failure("Not Found", statusCode: 404),
            });

        var request = new AbortTransactionRequest
        {
            TransactionId = transaction.TransactionId,
        };

        // Act
        var (status, response) = await service.AbortTransactionAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TransactionStatus.Aborted, response.Status);
        Assert.Equal(1, response.CompensatedCount); // 404 = success
    }

    #endregion

    // =========================================================================
    // GetTransactionStatus Tests
    // =========================================================================

    #region GetTransactionStatus Tests

    [Fact]
    public async Task GetTransactionStatusAsync_ExistingTransaction_ReturnsFullStatus()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction(ownerService: "genesis", parentResourceType: "character");
        var provision = SeedProvision(transaction.TransactionId,
            sequenceNumber: 0, status: ProvisionStatus.Provisioned, resourceType: "seed");

        var request = new GetTransactionStatusRequest
        {
            TransactionId = transaction.TransactionId,
        };

        // Act
        var (status, response) = await service.GetTransactionStatusAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(transaction.TransactionId, response.TransactionId);
        Assert.Equal("genesis", response.OwnerService);
        Assert.Equal("character", response.ParentResourceType);
        Assert.Equal(transaction.ParentResourceId, response.ParentResourceId);
        Assert.Equal(TransactionStatus.Active, response.Status);
        Assert.Equal(120, response.TtlSeconds);
        Assert.True(response.ExpiresAt > DateTimeOffset.UtcNow);

        // Verify provisions in response
        Assert.Single(response.Provisions);
        var provDetail = response.Provisions.First();
        Assert.Equal(provision.ProvisionId, provDetail.ProvisionId);
        Assert.Equal(0, provDetail.SequenceNumber);
        Assert.Equal("seed", provDetail.ResourceType);
        Assert.Equal(provision.ResourceId, provDetail.ResourceId);
        Assert.Equal(ProvisionStatus.Provisioned, provDetail.Status);
    }

    [Fact]
    public async Task GetTransactionStatusAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetTransactionStatusRequest
        {
            TransactionId = Guid.NewGuid(),
        };

        // Act
        var (status, response) = await service.GetTransactionStatusAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetTransactionStatusAsync_NoProvisions_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var transaction = SeedTransaction();

        var request = new GetTransactionStatusRequest
        {
            TransactionId = transaction.TransactionId,
        };

        // Act
        var (status, response) = await service.GetTransactionStatusAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Provisions);
    }

    #endregion

    // =========================================================================
    // Internal Model Default Tests
    // =========================================================================

    #region Internal Model Defaults

    [Fact]
    public void ResourceTransactionModel_DefaultValues_AreCorrect()
    {
        var model = new ResourceTransactionModel();

        Assert.Equal(Guid.Empty, model.TransactionId);
        Assert.Equal(string.Empty, model.OwnerService);
        Assert.Equal(string.Empty, model.ParentResourceType);
        Assert.Equal(Guid.Empty, model.ParentResourceId);
        Assert.Equal(TransactionStatus.Active, model.Status);
        Assert.Equal(default(DateTimeOffset), model.CreatedAt);
        Assert.Equal(default(DateTimeOffset), model.UpdatedAt);
        Assert.Equal(0, model.TtlSeconds);
        Assert.Null(model.ExpectedProvisionCount);
        Assert.Equal(0, model.ValidationAttempts);
        Assert.Null(model.CompletionValidation);
        Assert.Null(model.AbortReason);
    }

    [Fact]
    public void ResourceProvisionModel_DefaultValues_AreCorrect()
    {
        var model = new ResourceProvisionModel();

        Assert.Equal(Guid.Empty, model.ProvisionId);
        Assert.Equal(Guid.Empty, model.TransactionId);
        Assert.Equal(0, model.SequenceNumber);
        Assert.Equal(string.Empty, model.ResourceType);
        Assert.Equal(Guid.Empty, model.ResourceId);
        Assert.Equal(ProvisionStatus.Pending, model.Status);
        Assert.Equal(default(DateTimeOffset), model.RegisteredAt);
        Assert.Null(model.ProvisionedAt);
        Assert.Null(model.CompensatedAt);
        Assert.Equal(0, model.CompensationAttempts);
        Assert.Null(model.LastCompensationError);
        Assert.Equal(string.Empty, model.Compensation);
        Assert.Null(model.Verification);
    }

    [Fact]
    public void MigrateCallbackDefinition_DefaultValues_AreCorrect()
    {
        var model = new MigrateCallbackDefinition();

        Assert.Equal(string.Empty, model.ResourceType);
        Assert.Equal(string.Empty, model.SourceType);
        Assert.Equal(string.Empty, model.ServiceName);
        Assert.Equal(string.Empty, model.MigrateEndpoint);
        Assert.Equal(string.Empty, model.MigratePayloadTemplate);
        Assert.Null(model.Description);
        Assert.Equal(default(DateTimeOffset), model.RegisteredAt);
    }

    #endregion
}
