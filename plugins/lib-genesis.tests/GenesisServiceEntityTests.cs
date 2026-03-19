using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Genesis;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Collection;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Species;

namespace BeyondImmersion.BannouService.Genesis.Tests;

/// <summary>
/// Entity endpoint tests for GenesisService.
/// Covers: CreateEntity, GetEntity, ListEntities, GetCapabilities, DestroyEntity, BindPhysicalForm.
/// </summary>
public class GenesisServiceEntityTests : ServiceTestBase<GenesisServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<GenesisTemplateModel>> _mockTemplateStore;
    private readonly Mock<IStateStore<GenesisTemplateListModel>> _mockTemplateListStore;
    private readonly Mock<IStateStore<GenesisEntityModel>> _mockEntityStore;
    private readonly Mock<IStateStore<GenesisEntityListModel>> _mockEntityListStore;
    private readonly Mock<IStateStore<CachedGenesisEntity>> _mockEntityCacheStore;
    private readonly Mock<IStateStore<CachedCapabilityManifest>> _mockCapsCacheStore;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<GenesisService>> _mockLogger;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<ISeedClient> _mockSeedClient;
    private readonly Mock<ICurrencyClient> _mockCurrencyClient;
    private readonly Mock<ICharacterClient> _mockCharacterClient;
    private readonly Mock<IActorClient> _mockActorClient;
    private readonly Mock<IInventoryClient> _mockInventoryClient;
    private readonly Mock<IItemClient> _mockItemClient;
    private readonly Mock<IRelationshipClient> _mockRelationshipClient;
    private readonly Mock<IRealmClient> _mockRealmClient;
    private readonly Mock<ISpeciesClient> _mockSpeciesClient;
    private readonly Mock<IGameServiceClient> _mockGameServiceClient;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public GenesisServiceEntityTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockTemplateStore = new Mock<IStateStore<GenesisTemplateModel>>();
        _mockTemplateListStore = new Mock<IStateStore<GenesisTemplateListModel>>();
        _mockEntityStore = new Mock<IStateStore<GenesisEntityModel>>();
        _mockEntityListStore = new Mock<IStateStore<GenesisEntityListModel>>();
        _mockEntityCacheStore = new Mock<IStateStore<CachedGenesisEntity>>();
        _mockCapsCacheStore = new Mock<IStateStore<CachedCapabilityManifest>>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<GenesisService>>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockSeedClient = new Mock<ISeedClient>();
        _mockCurrencyClient = new Mock<ICurrencyClient>();
        _mockCharacterClient = new Mock<ICharacterClient>();
        _mockActorClient = new Mock<IActorClient>();
        _mockInventoryClient = new Mock<IInventoryClient>();
        _mockItemClient = new Mock<IItemClient>();
        _mockRelationshipClient = new Mock<IRelationshipClient>();
        _mockRealmClient = new Mock<IRealmClient>();
        _mockSpeciesClient = new Mock<ISpeciesClient>();
        _mockGameServiceClient = new Mock<IGameServiceClient>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        _mockStateStoreFactory.Setup(f => f.GetStore<GenesisTemplateModel>(StateStoreDefinitions.GenesisTemplates)).Returns(_mockTemplateStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<GenesisTemplateListModel>(StateStoreDefinitions.GenesisTemplates)).Returns(_mockTemplateListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities)).Returns(_mockEntityStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<GenesisEntityListModel>(StateStoreDefinitions.GenesisEntities)).Returns(_mockEntityListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<CachedGenesisEntity>(StateStoreDefinitions.GenesisEntityCache)).Returns(_mockEntityCacheStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<CachedCapabilityManifest>(StateStoreDefinitions.GenesisEntityCache)).Returns(_mockCapsCacheStore.Object);

        SetupDefaultLock();
        SetupDefaultMessageBus();
    }

    private void SetupDefaultLock()
    {
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        mockLockResponse.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private void SetupDefaultMessageBus()
    {
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private GenesisService CreateService()
    {
        return new GenesisService(
            _mockStateStoreFactory.Object,
            _mockLockProvider.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            new NullTelemetryProvider(),
            _mockResourceClient.Object,
            _mockSeedClient.Object,
            _mockCurrencyClient.Object,
            _mockCharacterClient.Object,
            _mockActorClient.Object,
            _mockInventoryClient.Object,
            _mockItemClient.Object,
            _mockRelationshipClient.Object,
            _mockRealmClient.Object,
            _mockSpeciesClient.Object,
            _mockGameServiceClient.Object,
            _mockEventConsumer.Object);
    }

    private static GenesisTemplateModel CreateTestTemplate(string templateCode = "test_template")
    {
        return new GenesisTemplateModel
        {
            TemplateCode = templateCode,
            GameServiceId = Guid.NewGuid(),
            DisplayName = "Test Template",
            Description = "A test template",
            Seed = new GenesisSeedConfig
            {
                SeedTypeCode = "test_seed",
                Domains = new List<GenesisSeedDomain> { new() { DomainCode = "growth", DisplayName = "Growth" } },
                Phases = new List<GenesisSeedPhase>
                {
                    new() { PhaseName = "Dormant", Threshold = 0, CognitiveStage = CognitiveStage.Dormant },
                    new() { PhaseName = "Stirring", Threshold = 100, CognitiveStage = CognitiveStage.EventBrain }
                }
            },
            Economy = new GenesisEconomyConfig
            {
                Wallets = new List<GenesisWalletConfig>
                {
                    new() { WalletCode = "mana", CurrencyCode = "mana_currency" }
                },
                GrowthMappings = new List<GenesisGrowthMapping>
                {
                    new() { WalletCode = "mana", Domain = "growth", Ratio = 1.0, Direction = GrowthDirection.Credit }
                }
            },
            Storage = new GenesisStorageConfig
            {
                Inventories = new List<GenesisInventoryConfig>
                {
                    new() { InventoryCode = "loot", ConstraintModel = "Unlimited", Capacity = 20 }
                }
            },
            Awakening = new GenesisAwakeningConfig { SystemRealmCode = "SYSTEM", CharacterSpeciesCode = "spirit" },
            PhysicalFormType = PhysicalFormType.Item,
            Bond = new GenesisBondConfig { Enabled = false, Cardinality = BondCardinality.None },
            ArchiveOnDestruction = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
    }

    private static GenesisEntityModel CreateTestEntity(Guid? entityId = null, string templateCode = "test_template")
    {
        var id = entityId ?? Guid.NewGuid();
        return new GenesisEntityModel
        {
            EntityId = id,
            TemplateCode = templateCode,
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            Code = "test_entity",
            DisplayName = "Test Entity",
            SeedId = Guid.NewGuid(),
            WalletIds = new Dictionary<string, Guid> { ["mana"] = Guid.NewGuid() },
            InventoryIds = new Dictionary<string, Guid> { ["loot"] = Guid.NewGuid() },
            CurrentPhase = "Dormant",
            CognitiveStage = CognitiveStage.Dormant,
            PhysicalFormType = PhysicalFormType.Item,
            Status = GenesisEntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
    }

    // ===================================================================
    // CreateEntity
    // ===================================================================

    [Fact]
    public async Task CreateEntityAsync_TemplateNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisTemplateModel?)null);

        // Act
        var (status, response) = await service.CreateEntityAsync(
            new CreateEntityRequest { TemplateCode = "missing", GameServiceId = Guid.NewGuid(), RealmId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateEntityAsync_TemplateDeprecated_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var template = CreateTestTemplate();
        template.IsDeprecated = true;

        _mockTemplateStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("template:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var (status, response) = await service.CreateEntityAsync(
            new CreateEntityRequest { TemplateCode = template.TemplateCode, GameServiceId = Guid.NewGuid(), RealmId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateEntityAsync_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var template = CreateTestTemplate();
        var gameServiceId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        _mockTemplateStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("template:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockGameServiceClient
            .Setup(g => g.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetServiceResponse { ServiceId = gameServiceId });

        _mockCurrencyClient
            .Setup(c => c.GetCurrencyDefinitionAsync(It.IsAny<GetCurrencyDefinitionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrencyDefinitionResponse { DefinitionId = Guid.NewGuid() });

        // Code uniqueness check returns existing entity
        _mockEntityStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("entity-code:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestEntity());

        // Act
        var (status, response) = await service.CreateEntityAsync(
            new CreateEntityRequest { TemplateCode = template.TemplateCode, GameServiceId = gameServiceId, RealmId = realmId, Code = "duplicate" });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    // ===================================================================
    // GetEntity
    // ===================================================================

    [Fact]
    public async Task GetEntityAsync_CacheHit_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var cached = new CachedGenesisEntity
        {
            EntityId = entityId,
            TemplateCode = "test_template",
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            WalletIds = new Dictionary<string, Guid>(),
            InventoryIds = new Dictionary<string, Guid>(),
            CurrentPhase = "Dormant",
            CognitiveStage = CognitiveStage.Dormant,
            PhysicalFormType = PhysicalFormType.None,
            Status = GenesisEntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockEntityCacheStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        // Act
        var (status, response) = await service.GetEntityAsync(
            new GetEntityRequest { EntityId = entityId, IncludeBalances = false });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(entityId, response.EntityId);
        // Cache hit — entity store should NOT be read
        _mockEntityStore.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetEntityAsync_CacheMiss_ReadsThroughAndCaches()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);

        _mockEntityCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CachedGenesisEntity?)null);

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        CachedGenesisEntity? cachedModel = null;
        _mockEntityCacheStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CachedGenesisEntity>(), It.IsAny<CancellationToken>()))
            .Callback<string, CachedGenesisEntity, CancellationToken>((_, m, _) => cachedModel = m)
            .ReturnsAsync("etag-1");

        // Act
        var (status, response) = await service.GetEntityAsync(
            new GetEntityRequest { EntityId = entityId, IncludeBalances = false });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(entityId, response.EntityId);
        Assert.NotNull(cachedModel);
        Assert.Equal(entityId, cachedModel.EntityId);
    }

    [Fact]
    public async Task GetEntityAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockEntityCacheStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CachedGenesisEntity?)null);
        _mockEntityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisEntityModel?)null);

        // Act
        var (status, response) = await service.GetEntityAsync(
            new GetEntityRequest { EntityId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    // ===================================================================
    // GetCapabilities
    // ===================================================================

    [Fact]
    public async Task GetCapabilitiesAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockEntityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisEntityModel?)null);

        // Act
        var (status, response) = await service.GetCapabilitiesAsync(
            new GetCapabilitiesRequest { EntityId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    // ===================================================================
    // DestroyEntity
    // ===================================================================

    [Fact]
    public async Task DestroyEntityAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockEntityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisEntityModel?)null);

        // Act
        var status = await service.DestroyEntityAsync(
            new DestroyEntityRequest { EntityId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DestroyEntityAsync_ValidRequest_DestroysAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        entity.ActorId = Guid.NewGuid();
        entity.CharacterId = Guid.NewGuid();
        entity.BondId = Guid.NewGuid();
        entity.BondTargetEntityId = Guid.NewGuid();
        entity.BondTargetEntityType = EntityType.Character;

        var template = CreateTestTemplate(entity.TemplateCode);

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{entity.TemplateCode}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        // Act
        var status = await service.DestroyEntityAsync(
            new DestroyEntityRequest { EntityId = entityId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Actor stopped
        _mockActorClient.Verify(a => a.StopActorAsync(It.IsAny<StopActorRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        // Character archived (archiveOnDestruction = true)
        _mockResourceClient.Verify(r => r.ExecuteCompressAsync(It.IsAny<ExecuteCompressRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        // Bond dissolved
        _mockRelationshipClient.Verify(r => r.EndRelationshipAsync(It.IsAny<EndRelationshipRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        // Resource cleanup called
        _mockResourceClient.Verify(r => r.ExecuteCleanupAsync(It.IsAny<ExecuteCleanupRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        // Entity record deleted
        _mockEntityStore.Verify(s => s.DeleteAsync($"entity:{entityId}", It.IsAny<CancellationToken>()), Times.Once);

        // Event published
        Assert.Equal(GenesisPublishedTopics.EntityDeleted, capturedTopic);
        var typedEvent = Assert.IsType<EntityDeletedEvent>(capturedEvent);
        Assert.Equal(entityId, typedEvent.EntityId);
        Assert.Equal(entity.TemplateCode, typedEvent.TemplateCode);
    }

    // ===================================================================
    // BindPhysicalForm
    // ===================================================================

    [Fact]
    public async Task BindPhysicalFormAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockEntityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisEntityModel?)null);

        // Act
        var (status, response) = await service.BindPhysicalFormAsync(
            new BindPhysicalFormRequest { EntityId = Guid.NewGuid(), PhysicalFormType = PhysicalFormType.Item, PhysicalFormId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BindPhysicalFormAsync_FormTypeMismatch_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        var template = CreateTestTemplate(entity.TemplateCode);
        template.PhysicalFormType = PhysicalFormType.Location;

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{entity.TemplateCode}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // Act
        var (status, response) = await service.BindPhysicalFormAsync(
            new BindPhysicalFormRequest { EntityId = entityId, PhysicalFormType = PhysicalFormType.Item, PhysicalFormId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BindPhysicalFormAsync_ValidRequest_ReturnsOkAndUpdatesEntity()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        var template = CreateTestTemplate(entity.TemplateCode);
        var physicalFormId = Guid.NewGuid();

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{entity.TemplateCode}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _mockItemClient
            .Setup(i => i.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = physicalFormId });

        GenesisEntityModel? savedEntity = null;
        _mockEntityStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisEntityModel>(), It.IsAny<CancellationToken>()))
            .Callback<string, GenesisEntityModel, CancellationToken>((_, m, _) => savedEntity = m)
            .ReturnsAsync("etag-1");

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.BindPhysicalFormAsync(
            new BindPhysicalFormRequest { EntityId = entityId, PhysicalFormType = PhysicalFormType.Item, PhysicalFormId = physicalFormId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        Assert.NotNull(savedEntity);
        Assert.Equal(PhysicalFormType.Item, savedEntity.PhysicalFormType);
        Assert.Equal(physicalFormId, savedEntity.PhysicalFormId);

        // Cache invalidated
        _mockEntityCacheStore.Verify(s => s.DeleteAsync($"entity:{entityId}", It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(GenesisPublishedTopics.EntityUpdated, capturedTopic);
        var typedEvent = Assert.IsType<EntityUpdatedEvent>(capturedEvent);
        Assert.Equal(entityId, typedEvent.EntityId);
        Assert.Contains("PhysicalFormType", typedEvent.ChangedFields);
    }
}
