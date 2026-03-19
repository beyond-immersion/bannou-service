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
/// Bond endpoint tests for GenesisService.
/// Covers: CreateBond, GetBond, DissolveBond.
/// </summary>
public class GenesisServiceBondTests : ServiceTestBase<GenesisServiceConfiguration>
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

    public GenesisServiceBondTests()
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

    private static GenesisEntityModel CreateTestEntity(Guid? entityId = null)
    {
        return new GenesisEntityModel
        {
            EntityId = entityId ?? Guid.NewGuid(),
            TemplateCode = "bonded_template",
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            SeedId = Guid.NewGuid(),
            WalletIds = new Dictionary<string, Guid>(),
            InventoryIds = new Dictionary<string, Guid>(),
            CurrentPhase = "Dormant",
            CognitiveStage = CognitiveStage.Dormant,
            PhysicalFormType = PhysicalFormType.Item,
            Status = GenesisEntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
    }

    private static GenesisTemplateModel CreateBondTemplate(bool enabled = true, BondCardinality cardinality = BondCardinality.OptionalOne)
    {
        return new GenesisTemplateModel
        {
            TemplateCode = "bonded_template",
            GameServiceId = Guid.NewGuid(),
            DisplayName = "Bonded Template",
            Description = "Template with bonds",
            Seed = new GenesisSeedConfig { SeedTypeCode = "s", Domains = new(), Phases = new() },
            Economy = new GenesisEconomyConfig { Wallets = new(), GrowthMappings = new() },
            Storage = new GenesisStorageConfig { Inventories = new() },
            Awakening = new GenesisAwakeningConfig { SystemRealmCode = "R", CharacterSpeciesCode = "S" },
            Bond = new GenesisBondConfig { Enabled = enabled, Cardinality = cardinality, RelationshipTypeCode = "weapon_wielder" },
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
    }

    // ===================================================================
    // CreateBond
    // ===================================================================

    [Fact]
    public async Task CreateBondAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockEntityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisEntityModel?)null);

        // Act
        var (status, response) = await service.CreateBondAsync(
            new CreateBondRequest { EntityId = Guid.NewGuid(), TargetEntityType = EntityType.Character, TargetEntityId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateBondAsync_BondsNotEnabled_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{entity.TemplateCode}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBondTemplate(enabled: false));

        // Act
        var (status, response) = await service.CreateBondAsync(
            new CreateBondRequest { EntityId = entityId, TargetEntityType = EntityType.Character, TargetEntityId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateBondAsync_CardinalityNone_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{entity.TemplateCode}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBondTemplate(cardinality: BondCardinality.None));

        // Act
        var (status, response) = await service.CreateBondAsync(
            new CreateBondRequest { EntityId = entityId, TargetEntityType = EntityType.Character, TargetEntityId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateBondAsync_AlreadyBonded_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        entity.BondTargetEntityId = Guid.NewGuid();
        entity.BondTargetEntityType = EntityType.Character;

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{entity.TemplateCode}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBondTemplate(cardinality: BondCardinality.OptionalOne));

        // Act
        var (status, response) = await service.CreateBondAsync(
            new CreateBondRequest { EntityId = entityId, TargetEntityType = EntityType.Character, TargetEntityId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateBondAsync_DormantEntity_StoresBondIntentWithoutRelationship()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        var targetId = Guid.NewGuid();

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{entity.TemplateCode}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBondTemplate());

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
        var (status, response) = await service.CreateBondAsync(
            new CreateBondRequest { EntityId = entityId, TargetEntityType = EntityType.Character, TargetEntityId = targetId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        Assert.NotNull(savedEntity);
        Assert.Equal(EntityType.Character, savedEntity.BondTargetEntityType);
        Assert.Equal(targetId, savedEntity.BondTargetEntityId);
        Assert.Null(savedEntity.BondId); // Deferred — not awakened

        // No relationship created (dormant entity)
        _mockRelationshipClient.Verify(r => r.CreateRelationshipAsync(It.IsAny<CreateRelationshipRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal(GenesisPublishedTopics.GenesisEntityBondCreated, capturedTopic);
        var typedEvent = Assert.IsType<GenesisEntityBondCreatedEvent>(capturedEvent);
        Assert.Equal(entityId, typedEvent.EntityId);
        Assert.Equal(targetId, typedEvent.TargetEntityId);
        Assert.Null(typedEvent.BondId);
    }

    [Fact]
    public async Task CreateBondAsync_AwakenedEntity_CreatesRelationshipImmediately()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        entity.CognitiveStage = CognitiveStage.CharacterBrain;
        entity.CharacterId = Guid.NewGuid();
        entity.ActorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var relationshipId = Guid.NewGuid();
        var relTypeId = Guid.NewGuid();

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{entity.TemplateCode}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBondTemplate());

        _mockRelationshipClient
            .Setup(r => r.GetRelationshipTypeByCodeAsync(It.IsAny<GetRelationshipTypeByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipTypeResponse { RelationshipTypeId = relTypeId });

        _mockRelationshipClient
            .Setup(r => r.CreateRelationshipAsync(It.IsAny<CreateRelationshipRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipResponse { RelationshipId = relationshipId });

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
        var (status, response) = await service.CreateBondAsync(
            new CreateBondRequest { EntityId = entityId, TargetEntityType = EntityType.Character, TargetEntityId = targetId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedEntity);
        Assert.Equal(relationshipId, savedEntity.BondId);

        _mockRelationshipClient.Verify(r => r.CreateRelationshipAsync(
            It.Is<CreateRelationshipRequest>(req => req.Entity1Id == entity.CharacterId && req.Entity2Id == targetId),
            It.IsAny<CancellationToken>()), Times.Once);

        var typedEvent = Assert.IsType<GenesisEntityBondCreatedEvent>(capturedEvent);
        Assert.Equal(relationshipId, typedEvent.BondId);
    }

    // ===================================================================
    // GetBond
    // ===================================================================

    [Fact]
    public async Task GetBondAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockEntityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisEntityModel?)null);

        // Act
        var (status, response) = await service.GetBondAsync(new GetBondRequest { EntityId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetBondAsync_NoBond_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        entity.BondTargetEntityId = null;

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Act
        var (status, response) = await service.GetBondAsync(new GetBondRequest { EntityId = entityId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetBondAsync_ActiveBond_ReturnsBondResponse()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        var targetId = Guid.NewGuid();
        var bondId = Guid.NewGuid();
        entity.BondTargetEntityType = EntityType.Character;
        entity.BondTargetEntityId = targetId;
        entity.BondId = bondId;

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Act
        var (status, response) = await service.GetBondAsync(new GetBondRequest { EntityId = entityId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(bondId, response.BondId);
        Assert.Equal(EntityType.Character, response.BondTargetEntityType);
        Assert.Equal(targetId, response.BondTargetEntityId);
    }

    // ===================================================================
    // DissolveBond
    // ===================================================================

    [Fact]
    public async Task DissolveBondAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockEntityStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisEntityModel?)null);

        // Act
        var status = await service.DissolveBondAsync(new DissolveBondRequest { EntityId = Guid.NewGuid() });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DissolveBondAsync_NoBond_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        entity.BondTargetEntityId = null;

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Act
        var status = await service.DissolveBondAsync(new DissolveBondRequest { EntityId = entityId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DissolveBondAsync_PreAwakened_ClearsBondWithoutDeletingRelationship()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        entity.BondTargetEntityType = EntityType.Character;
        entity.BondTargetEntityId = Guid.NewGuid();
        entity.BondId = null; // Pre-awakened: intent only, no relationship

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        GenesisEntityModel? savedEntity = null;
        _mockEntityStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisEntityModel>(), It.IsAny<CancellationToken>()))
            .Callback<string, GenesisEntityModel, CancellationToken>((_, m, _) => savedEntity = m)
            .ReturnsAsync("etag-1");

        string? capturedTopic = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, _, _) => capturedTopic = t)
            .ReturnsAsync(true);

        // Act
        var status = await service.DissolveBondAsync(new DissolveBondRequest { EntityId = entityId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        Assert.NotNull(savedEntity);
        Assert.Null(savedEntity.BondTargetEntityType);
        Assert.Null(savedEntity.BondTargetEntityId);
        Assert.Null(savedEntity.BondId);

        // No relationship deletion (bondId was null)
        _mockRelationshipClient.Verify(r => r.EndRelationshipAsync(It.IsAny<EndRelationshipRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal(GenesisPublishedTopics.GenesisEntityBondDissolved, capturedTopic);
    }

    [Fact]
    public async Task DissolveBondAsync_MaterializedRelationship_DeletesRelationship()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        var bondId = Guid.NewGuid();
        entity.BondTargetEntityType = EntityType.Character;
        entity.BondTargetEntityId = Guid.NewGuid();
        entity.BondId = bondId;

        _mockEntityStore
            .Setup(s => s.GetAsync($"entity:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _mockEntityStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisEntityModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var status = await service.DissolveBondAsync(new DissolveBondRequest { EntityId = entityId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockRelationshipClient.Verify(r => r.EndRelationshipAsync(
            It.Is<EndRelationshipRequest>(req => req.RelationshipId == bondId),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
