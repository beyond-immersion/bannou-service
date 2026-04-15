using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Genesis;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq.Expressions;
using Xunit;

namespace BeyondImmersion.BannouService.Genesis.Tests;

public class GenesisServiceCleanupTests : ServiceTestBase<GenesisServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<GenesisTemplateModel>> _mockTemplateStore;
    private readonly Mock<IStateStore<GenesisTemplateListModel>> _mockTemplateListStore;
    private readonly Mock<IQueryableStateStore<GenesisTemplateModel>> _mockTemplateQueryStore;
    private readonly Mock<IStateStore<GenesisEntityModel>> _mockEntityStore;
    private readonly Mock<IStateStore<GenesisEntityListModel>> _mockEntityListStore;
    private readonly Mock<IQueryableStateStore<GenesisEntityModel>> _mockEntityQueryStore;
    private readonly Mock<IStateStore<CachedGenesisEntity>> _mockEntityCacheStore;
    private readonly Mock<IStateStore<CachedCapabilityManifest>> _mockCapsCacheStore;
    private readonly Mock<IStateStore<string>> _mockEntityIndexStore;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<GenesisService>> _mockLogger;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
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
        _mockTemplateQueryStore = new Mock<IQueryableStateStore<GenesisTemplateModel>>();
        _mockEntityStore = new Mock<IStateStore<GenesisEntityModel>>();
        _mockEntityListStore = new Mock<IStateStore<GenesisEntityListModel>>();
        _mockEntityQueryStore = new Mock<IQueryableStateStore<GenesisEntityModel>>();
        _mockEntityCacheStore = new Mock<IStateStore<CachedGenesisEntity>>();
        _mockCapsCacheStore = new Mock<IStateStore<CachedCapabilityManifest>>();
        _mockEntityIndexStore = new Mock<IStateStore<string>>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<GenesisService>>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
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
        _mockStateStoreFactory.Setup(f => f.GetQueryableStore<GenesisTemplateModel>(StateStoreDefinitions.GenesisTemplates)).Returns(_mockTemplateQueryStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<GenesisTemplateListModel>(StateStoreDefinitions.GenesisTemplateIndexes)).Returns(_mockTemplateListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities)).Returns(_mockEntityStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetQueryableStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities)).Returns(_mockEntityQueryStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.GenesisEntityIndexes)).Returns(_mockEntityIndexStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<GenesisEntityListModel>(StateStoreDefinitions.GenesisEntityIndexes)).Returns(_mockEntityListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<CachedGenesisEntity>(StateStoreDefinitions.GenesisEntityCache)).Returns(_mockEntityCacheStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<CachedCapabilityManifest>(StateStoreDefinitions.GenesisEntityCache)).Returns(_mockCapsCacheStore.Object);

        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockEntityStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisEntityModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("ok");
        _mockEntityListStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisEntityListModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("ok");

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider.Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(mockLockResponse.Object);
    }

    private GenesisService CreateService()
    {
        return new GenesisService(
            _mockStateStoreFactory.Object, _mockLockProvider.Object, _mockMessageBus.Object, _mockLogger.Object,
            Configuration, _mockTelemetryProvider.Object, _mockResourceClient.Object, _mockSeedClient.Object,
            _mockCurrencyClient.Object, _mockCharacterClient.Object, _mockActorClient.Object, _mockInventoryClient.Object,
            _mockItemClient.Object, _mockRelationshipClient.Object, _mockRealmClient.Object, _mockSpeciesClient.Object,
            _mockGameServiceClient.Object, _mockEventConsumer.Object, new GenesisGrowthState());
    }

    private static GenesisEntityModel CreateTestEntity(Guid? entityId = null, Guid? characterId = null)
    {
        return new GenesisEntityModel
        {
            EntityId = entityId ?? Guid.NewGuid(),
            TemplateCode = "test_template",
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
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
    public async Task CleanupByCharacterAsync_MatchingEntities_DestroysAllAndPublishes()
    {
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var entity1 = CreateTestEntity(characterId: characterId);
        entity1.ActorId = Guid.NewGuid().ToString();
        entity1.BondId = Guid.NewGuid();
        entity1.BondTargetEntityId = Guid.NewGuid();
        entity1.BondTargetEntityType = EntityType.Character;
        var entity2 = CreateTestEntity(characterId: characterId);

        _mockEntityQueryStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<GenesisEntityModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GenesisEntityModel> { entity1, entity2 } as IReadOnlyList<GenesisEntityModel>);

        var capturedTopics = new List<string>();
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, _, _) => capturedTopics.Add(t)).ReturnsAsync(true);

        var status = await service.CleanupByCharacterAsync(
            new CleanupByCharacterRequest { CharacterId = characterId }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        _mockActorClient.Verify(a => a.StopActorAsync(It.IsAny<StopActorRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRelationshipClient.Verify(r => r.EndRelationshipAsync(It.IsAny<EndRelationshipRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockResourceClient.Verify(r => r.ExecuteCleanupAsync(It.IsAny<ExecuteCleanupRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal(2, capturedTopics.Count(t => t == GenesisPublishedTopics.EntityDeleted));
    }

    // ===================================================================
    // CleanupByRealm
    // ===================================================================

    [Fact]
    public async Task CleanupByRealmAsync_MatchingEntities_ArchivesCharacterIfConfigured()
    {
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var entity = CreateTestEntity(characterId: Guid.NewGuid());
        entity.RealmId = realmId;

        var template = new GenesisTemplateModel
        {
            TemplateCode = entity.TemplateCode,
            ArchiveOnDestruction = true,
            Seed = new GenesisSeedConfig { SeedTypeCode = "s", Domains = new List<GenesisSeedDomain>(), Phases = new List<GenesisSeedPhase>() },
            Economy = new GenesisEconomyConfig { Wallets = new List<GenesisWalletConfig>(), GrowthMappings = new List<GenesisGrowthMapping>() },
            Storage = new GenesisStorageConfig { Inventories = new List<GenesisInventoryConfig>() },
            Awakening = new GenesisAwakeningConfig { SystemRealmCode = "R", CharacterSpeciesCode = "S" },
            Bond = new GenesisBondConfig { Enabled = false, Cardinality = BondCardinality.None }
        };

        _mockEntityQueryStore
            .SetupSequence(s => s.QueryPagedAsync(
                It.IsAny<Expression<Func<GenesisEntityModel, bool>>>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<Expression<Func<GenesisEntityModel, object>>?>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<GenesisEntityModel>(
                new List<GenesisEntityModel> { entity }, 1, 1, 100))
            .ReturnsAsync(new PagedResult<GenesisEntityModel>(
                new List<GenesisEntityModel>(), 0, 1, 100));
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey(entity.TemplateCode), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var status = await service.CleanupByRealmAsync(
            new CleanupByRealmRequest { RealmId = realmId }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        _mockResourceClient.Verify(r => r.ExecuteCompressAsync(It.IsAny<ExecuteCompressRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockResourceClient.Verify(r => r.ExecuteCleanupAsync(It.IsAny<ExecuteCleanupRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===================================================================
    // RestoreFromArchive
    // ===================================================================

    [Fact]
    public async Task RestoreFromArchiveAsync_TemplateMissing_ReturnsBadRequest()
    {
        var service = CreateService();
        _mockTemplateStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((GenesisTemplateModel?)null);

        var archive = new GenesisArchive
        {
            Entities = new List<GenesisArchivedEntity>
            {
                new()
                {
                    EntityId = Guid.NewGuid(), TemplateCode = "gone", GameServiceId = Guid.NewGuid(),
                    RealmId = Guid.NewGuid(), WalletBalances = new Dictionary<string, double>(),
                    CurrentPhase = "Dormant", CognitiveStage = CognitiveStage.Dormant, CreatedAt = DateTimeOffset.UtcNow
                }
            }
        };

        var (status, _) = await service.RestoreFromArchiveAsync(
            new RestoreFromArchiveRequest { Archive = archive }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task RestoreFromArchiveAsync_ValidArchive_ReprovisionAndRestoresBalances()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        var containerId = Guid.NewGuid();

        var template = new GenesisTemplateModel
        {
            TemplateCode = "test_template",
            GameServiceId = Guid.NewGuid(),
            DisplayName = "T",
            Description = "T",
            Seed = new GenesisSeedConfig
            {
                SeedTypeCode = "test_seed",
                Domains = new List<GenesisSeedDomain> { new() { DomainCode = "growth", DisplayName = "Growth" } },
                Phases = new List<GenesisSeedPhase> { new() { PhaseName = "Dormant", Threshold = 0, CognitiveStage = CognitiveStage.Dormant } }
            },
            Economy = new GenesisEconomyConfig
            {
                Wallets = new List<GenesisWalletConfig> { new() { WalletCode = "mana", CurrencyCode = "mana_currency" } },
                GrowthMappings = new List<GenesisGrowthMapping>()
            },
            Storage = new GenesisStorageConfig { Inventories = new List<GenesisInventoryConfig> { new() { InventoryCode = "loot", ConstraintModel = "Unlimited", Capacity = 20 } } },
            Awakening = new GenesisAwakeningConfig { SystemRealmCode = "S", CharacterSpeciesCode = "S" },
            PhysicalFormType = PhysicalFormType.Item,
            Bond = new GenesisBondConfig { Enabled = false, Cardinality = BondCardinality.None }
        };

        _mockTemplateStore.Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("template:")), It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockSeedClient.Setup(s => s.CreateSeedAsync(It.IsAny<CreateSeedRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SeedResponse { SeedId = seedId });
        _mockCurrencyClient.Setup(c => c.GetCurrencyDefinitionAsync(It.IsAny<GetCurrencyDefinitionRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new CurrencyDefinitionResponse { DefinitionId = Guid.NewGuid(), Code = "mana_currency" });
        _mockCurrencyClient.Setup(c => c.CreateWalletAsync(It.IsAny<CreateWalletRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WalletResponse { WalletId = walletId });
        _mockCurrencyClient.Setup(c => c.CreditCurrencyAsync(It.IsAny<CreditCurrencyRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new CreditCurrencyResponse());
        _mockInventoryClient.Setup(i => i.CreateContainerAsync(It.IsAny<CreateContainerRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ContainerResponse { ContainerId = containerId });

        var capturedTopics = new List<string>();
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, _, _) => capturedTopics.Add(t)).ReturnsAsync(true);

        var archive = new GenesisArchive
        {
            Entities = new List<GenesisArchivedEntity>
            {
                new()
                {
                    EntityId = entityId, TemplateCode = template.TemplateCode, GameServiceId = template.GameServiceId,
                    RealmId = Guid.NewGuid(), WalletBalances = new Dictionary<string, double> { ["mana"] = 150.0 },
                    CurrentPhase = "Stirring", CognitiveStage = CognitiveStage.EventBrain, CreatedAt = DateTimeOffset.UtcNow.AddDays(-5)
                }
            }
        };

        var (status, response) = await service.RestoreFromArchiveAsync(
            new RestoreFromArchiveRequest { Archive = archive }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.RestoredCount);
        _mockSeedClient.Verify(s => s.CreateSeedAsync(It.IsAny<CreateSeedRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockCurrencyClient.Verify(c => c.CreateWalletAsync(It.IsAny<CreateWalletRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockCurrencyClient.Verify(c => c.CreditCurrencyAsync(It.IsAny<CreditCurrencyRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockInventoryClient.Verify(i => i.CreateContainerAsync(It.IsAny<CreateContainerRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(GenesisPublishedTopics.EntityCreated, capturedTopics);
    }
}
