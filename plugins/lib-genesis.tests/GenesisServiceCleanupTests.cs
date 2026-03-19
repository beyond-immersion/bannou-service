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
/// Cleanup and archive endpoint tests for GenesisService.
/// Covers: CleanupByCharacter, CleanupByRealm, GetCompressData, RestoreFromArchive.
/// </summary>
public class GenesisServiceCleanupTests : ServiceTestBase<GenesisServiceConfiguration>
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

    public GenesisServiceCleanupTests()
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

    private static GenesisEntityModel CreateTestEntity(Guid? entityId = null, Guid? characterId = null, Guid? realmId = null)
    {
        return new GenesisEntityModel
        {
            EntityId = entityId ?? Guid.NewGuid(),
            TemplateCode = "test_template",
            GameServiceId = Guid.NewGuid(),
            RealmId = realmId ?? Guid.NewGuid(),
            SeedId = Guid.NewGuid(),
            CharacterId = characterId,
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
    // CleanupByCharacter
    // ===================================================================

    [Fact]
    public async Task CleanupByCharacterAsync_ValidRequest_DestroysMatchingEntities()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var entity1 = CreateTestEntity(characterId: characterId);
        entity1.ActorId = Guid.NewGuid();
        entity1.BondId = Guid.NewGuid();
        entity1.BondTargetEntityId = Guid.NewGuid();
        entity1.BondTargetEntityType = EntityType.Character;
        var entity2 = CreateTestEntity(characterId: characterId);

        _mockEntityStore
            .Setup(s => s.QueryAsync(It.IsAny<Func<GenesisEntityModel, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GenesisEntityModel> { entity1, entity2 });

        var deletedTopics = new List<string>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, _, _) => deletedTopics.Add(t))
            .ReturnsAsync(true);

        // Act
        var status = await service.CleanupByCharacterAsync(
            new CleanupByCharacterRequest { CharacterId = characterId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Actor stopped for entity1 (has actorId)
        _mockActorClient.Verify(a => a.StopActorAsync(It.IsAny<StopActorRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        // Bond dissolved for entity1 (has bondId)
        _mockRelationshipClient.Verify(r => r.EndRelationshipAsync(It.IsAny<EndRelationshipRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        // Resource cleanup for both
        _mockResourceClient.Verify(r => r.ExecuteCleanupAsync(It.IsAny<ExecuteCleanupRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Two delete events published
        Assert.Equal(2, deletedTopics.Count(t => t == GenesisPublishedTopics.EntityDeleted));
    }

    // ===================================================================
    // CleanupByRealm
    // ===================================================================

    [Fact]
    public async Task CleanupByRealmAsync_ValidRequest_BatchDestroysEntities()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var entity = CreateTestEntity(realmId: realmId);

        var template = new GenesisTemplateModel
        {
            TemplateCode = entity.TemplateCode,
            ArchiveOnDestruction = false,
            Seed = new GenesisSeedConfig { SeedTypeCode = "s", Domains = new(), Phases = new() },
            Economy = new GenesisEconomyConfig { Wallets = new(), GrowthMappings = new() },
            Storage = new GenesisStorageConfig { Inventories = new() },
            Awakening = new GenesisAwakeningConfig { SystemRealmCode = "R", CharacterSpeciesCode = "S" },
            Bond = new GenesisBondConfig { Enabled = false, Cardinality = BondCardinality.None }
        };

        _mockTemplateStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("template:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        // First query returns a batch, second returns empty (loop terminates)
        var callCount = 0;
        _mockEntityStore
            .Setup(s => s.QueryAsync(It.IsAny<Func<GenesisEntityModel, bool>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new List<GenesisEntityModel> { entity }
                    : new List<GenesisEntityModel>();
            });

        // Act
        var status = await service.CleanupByRealmAsync(
            new CleanupByRealmRequest { RealmId = realmId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockResourceClient.Verify(r => r.ExecuteCleanupAsync(It.IsAny<ExecuteCleanupRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockMessageBus.Verify(m => m.TryPublishAsync(GenesisPublishedTopics.EntityDeleted, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===================================================================
    // RestoreFromArchive
    // ===================================================================

    [Fact]
    public async Task RestoreFromArchiveAsync_TemplateMissing_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisTemplateModel?)null);

        var archive = new GenesisArchive
        {
            Entities = new List<GenesisArchivedEntity>
            {
                new()
                {
                    EntityId = Guid.NewGuid(),
                    TemplateCode = "gone_template",
                    GameServiceId = Guid.NewGuid(),
                    RealmId = Guid.NewGuid(),
                    WalletBalances = new Dictionary<string, double>(),
                    CurrentPhase = "Dormant",
                    CognitiveStage = CognitiveStage.Dormant,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            }
        };

        // Act
        var (status, response) = await service.RestoreFromArchiveAsync(
            new RestoreFromArchiveRequest { Archive = archive });

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }
}
