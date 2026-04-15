using BeyondImmersion.Bannou.Core;
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
using Xunit;

namespace BeyondImmersion.BannouService.Genesis.Tests;

public class GenesisServiceEntityTests : ServiceTestBase<GenesisServiceConfiguration>
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

    public GenesisServiceEntityTests()
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

    private static GenesisTemplateModel CreateTestTemplate(string templateCode = "test_template")
    {
        return new GenesisTemplateModel
        {
            TemplateCode = templateCode,
            GameServiceId = Guid.NewGuid(),
            DisplayName = "Test",
            Description = "Test",
            Seed = new GenesisSeedConfig
            {
                SeedTypeCode = "test_seed",
                Domains = new List<GenesisSeedDomain> { new() { DomainCode = "growth", DisplayName = "Growth" } },
                Phases = new List<GenesisSeedPhase> { new() { PhaseName = "Dormant", Threshold = 0, CognitiveStage = CognitiveStage.Dormant } }
            },
            Economy = new GenesisEconomyConfig
            {
                Wallets = new List<GenesisWalletConfig> { new() { WalletCode = "mana", CurrencyCode = "mana_currency" } },
                GrowthMappings = new List<GenesisGrowthMapping> { new() { WalletCode = "mana", Domain = "growth", Ratio = 1.0, Direction = GrowthDirection.Credit } }
            },
            Storage = new GenesisStorageConfig { Inventories = new List<GenesisInventoryConfig> { new() { InventoryCode = "loot", ConstraintModel = "Unlimited", Capacity = 20 } } },
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
        return new GenesisEntityModel
        {
            EntityId = entityId ?? Guid.NewGuid(),
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
        var service = CreateService();
        _mockTemplateStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((GenesisTemplateModel?)null);

        var (status, _) = await service.CreateEntityAsync(
            new CreateEntityRequest { TemplateCode = "missing", GameServiceId = Guid.NewGuid(), RealmId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task CreateEntityAsync_TemplateDeprecated_ReturnsBadRequest()
    {
        var service = CreateService();
        var template = CreateTestTemplate();
        template.IsDeprecated = true;
        _mockTemplateStore.Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("template:")), It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var (status, _) = await service.CreateEntityAsync(
            new CreateEntityRequest { TemplateCode = template.TemplateCode, GameServiceId = Guid.NewGuid(), RealmId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task CreateEntityAsync_DuplicateCode_ReturnsConflict()
    {
        var service = CreateService();
        var template = CreateTestTemplate();
        var gameServiceId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        _mockTemplateStore.Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("template:")), It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockGameServiceClient.Setup(g => g.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = gameServiceId, StubName = "test", DisplayName = "Test", IsActive = true });
        _mockEntityIndexStore.Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("entity-code:")), It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid().ToString());

        var (status, _) = await service.CreateEntityAsync(
            new CreateEntityRequest { TemplateCode = template.TemplateCode, GameServiceId = gameServiceId, RealmId = realmId, Code = "dup" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task CreateEntityAsync_ValidRequest_ProvisionsSeedWalletsInventories()
    {
        var service = CreateService();
        var template = CreateTestTemplate();
        var gameServiceId = template.GameServiceId;
        var seedId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        var containerId = Guid.NewGuid();

        _mockTemplateStore.Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("template:")), It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockGameServiceClient.Setup(g => g.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = gameServiceId, StubName = "t", DisplayName = "T", IsActive = true });
        _mockEntityIndexStore.Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("entity-code:")), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        _mockSeedClient.Setup(s => s.CreateSeedAsync(It.IsAny<CreateSeedRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new SeedResponse { SeedId = seedId });
        _mockCurrencyClient.Setup(c => c.GetCurrencyDefinitionAsync(It.IsAny<GetCurrencyDefinitionRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new CurrencyDefinitionResponse { DefinitionId = Guid.NewGuid(), Code = "mana_currency" });
        _mockCurrencyClient.Setup(c => c.CreateWalletAsync(It.IsAny<CreateWalletRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WalletResponse { WalletId = walletId });
        _mockInventoryClient.Setup(i => i.CreateContainerAsync(It.IsAny<CreateContainerRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ContainerResponse { ContainerId = containerId });

        var capturedEvents = new List<(string Topic, object Event)>();
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) => capturedEvents.Add((t, e))).ReturnsAsync(true);

        var (status, response) = await service.CreateEntityAsync(
            new CreateEntityRequest { TemplateCode = template.TemplateCode, GameServiceId = gameServiceId, RealmId = Guid.NewGuid(), Code = "my_entity" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(CognitiveStage.Dormant, response.CognitiveStage);
        Assert.Contains("mana", response.WalletIds.Keys);
        Assert.Contains("loot", response.InventoryIds.Keys);
        _mockSeedClient.Verify(s => s.CreateSeedAsync(It.Is<CreateSeedRequest>(r => r.SeedTypeCode == template.Seed.SeedTypeCode), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(capturedEvents, e => e.Topic == GenesisPublishedTopics.GenesisEntityCreated);
    }

    // ===================================================================
    // GetEntity
    // ===================================================================

    [Fact]
    public async Task GetEntityAsync_CacheHit_SkipsMySqlRead()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var cached = new CachedGenesisEntity
        {
            EntityId = entityId,
            TemplateCode = "t",
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            SeedId = Guid.NewGuid(),
            WalletIds = new(),
            InventoryIds = new(),
            CurrentPhase = "Dormant",
            CognitiveStage = CognitiveStage.Dormant,
            PhysicalFormType = PhysicalFormType.None,
            Status = GenesisEntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _mockEntityCacheStore.Setup(s => s.GetAsync(GenesisService.BuildEntityCacheKey(entityId), It.IsAny<CancellationToken>())).ReturnsAsync(cached);

        var (status, response) = await service.GetEntityAsync(
            new GetEntityRequest { EntityId = entityId, IncludeBalances = false }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(entityId, response.EntityId);
        _mockEntityStore.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetEntityAsync_CacheMiss_ReadsThroughAndWritesCache()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);

        _mockEntityCacheStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((CachedGenesisEntity?)null);
        _mockEntityStore.Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _mockEntityCacheStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CachedGenesisEntity>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("ok");

        var (status, _) = await service.GetEntityAsync(
            new GetEntityRequest { EntityId = entityId, IncludeBalances = false }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        _mockEntityCacheStore.Verify(s => s.SaveAsync(
            GenesisService.BuildEntityCacheKey(entityId), It.IsAny<CachedGenesisEntity>(),
            It.Is<StateOptions?>(o => o != null && o.Ttl == Configuration.EntityCacheTtlMinutes * 60),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEntityAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockEntityCacheStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((CachedGenesisEntity?)null);
        _mockEntityStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((GenesisEntityModel?)null);

        var (status, _) = await service.GetEntityAsync(
            new GetEntityRequest { EntityId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    // ===================================================================
    // GetCapabilities
    // ===================================================================

    [Fact]
    public async Task GetCapabilitiesAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockEntityStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((GenesisEntityModel?)null);

        var (status, _) = await service.GetCapabilitiesAsync(
            new GetCapabilitiesRequest { EntityId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_CacheMiss_QueriesSeedAndCaches()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        _mockEntityStore.Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _mockCapsCacheStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((CachedCapabilityManifest?)null);
        _mockCapsCacheStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CachedCapabilityManifest>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("ok");

        _mockSeedClient.Setup(s => s.GetCapabilityManifestAsync(It.IsAny<GetCapabilityManifestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapabilityManifestResponse
            {
                SeedId = entity.SeedId,
                Capabilities = new List<Capability> { new() { CapabilityCode = "loot.basic", Unlocked = true } },
                Version = 1
            });

        var (status, response) = await service.GetCapabilitiesAsync(
            new GetCapabilitiesRequest { EntityId = entityId }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Capabilities);
        Assert.True(response.Capabilities.First().IsUnlocked);
        _mockCapsCacheStore.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CachedCapabilityManifest>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===================================================================
    // DestroyEntity
    // ===================================================================

    [Fact]
    public async Task DestroyEntityAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockEntityStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((GenesisEntityModel?)null);

        var status = await service.DestroyEntityAsync(
            new DestroyEntityRequest { EntityId = Guid.NewGuid() }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DestroyEntityAsync_FullEntity_StopsActorArchivesCharacterDeletesBond()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        entity.ActorId = Guid.NewGuid().ToString();
        entity.CharacterId = Guid.NewGuid();
        entity.BondId = Guid.NewGuid();
        entity.BondTargetEntityId = Guid.NewGuid();
        entity.BondTargetEntityType = EntityType.Character;
        var template = CreateTestTemplate(entity.TemplateCode);

        _mockEntityStore.Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _mockTemplateStore.Setup(s => s.GetAsync(GenesisService.BuildTemplateKey(entity.TemplateCode), It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var capturedTopics = new List<string>();
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, _, _) => capturedTopics.Add(t)).ReturnsAsync(true);

        var status = await service.DestroyEntityAsync(
            new DestroyEntityRequest { EntityId = entityId }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        _mockActorClient.Verify(a => a.StopActorAsync(It.IsAny<StopActorRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockResourceClient.Verify(r => r.ExecuteCompressAsync(It.IsAny<ExecuteCompressRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRelationshipClient.Verify(r => r.EndRelationshipAsync(It.IsAny<EndRelationshipRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockResourceClient.Verify(r => r.ExecuteCleanupAsync(It.IsAny<ExecuteCleanupRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockEntityStore.Verify(s => s.DeleteAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains(GenesisPublishedTopics.GenesisEntityDeleted, capturedTopics);
    }

    // ===================================================================
    // BindPhysicalForm
    // ===================================================================

    [Fact]
    public async Task BindPhysicalFormAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockEntityStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((GenesisEntityModel?)null);

        var (status, _) = await service.BindPhysicalFormAsync(
            new BindPhysicalFormRequest { EntityId = Guid.NewGuid(), PhysicalFormType = PhysicalFormType.Item, PhysicalFormId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task BindPhysicalFormAsync_FormTypeMismatch_ReturnsBadRequest()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        var template = CreateTestTemplate(entity.TemplateCode);
        template.PhysicalFormType = PhysicalFormType.Location;

        _mockEntityStore.Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _mockTemplateStore.Setup(s => s.GetAsync(GenesisService.BuildTemplateKey(entity.TemplateCode), It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var (status, _) = await service.BindPhysicalFormAsync(
            new BindPhysicalFormRequest { EntityId = entityId, PhysicalFormType = PhysicalFormType.Item, PhysicalFormId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task BindPhysicalFormAsync_ValidItem_UpdatesAndPublishes()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        var template = CreateTestTemplate(entity.TemplateCode);
        var physicalFormId = Guid.NewGuid();

        _mockEntityStore.Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _mockTemplateStore.Setup(s => s.GetAsync(GenesisService.BuildTemplateKey(entity.TemplateCode), It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockItemClient.Setup(i => i.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemInstanceResponse { InstanceId = physicalFormId });

        GenesisEntityModel? savedEntity = null;
        _mockEntityStore.Setup(s => s.SaveAsync(It.Is<string>(k => k == GenesisService.BuildEntityKey(entityId)),
                It.IsAny<GenesisEntityModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GenesisEntityModel, StateOptions?, CancellationToken>((_, m, _, _) => savedEntity = m).ReturnsAsync("ok");

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) => { capturedTopic = t; capturedEvent = e; }).ReturnsAsync(true);

        var (status, _) = await service.BindPhysicalFormAsync(
            new BindPhysicalFormRequest { EntityId = entityId, PhysicalFormType = PhysicalFormType.Item, PhysicalFormId = physicalFormId },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedEntity);
        Assert.Equal(PhysicalFormType.Item, savedEntity.PhysicalFormType);
        Assert.Equal(physicalFormId, savedEntity.PhysicalFormId);
        _mockEntityCacheStore.Verify(s => s.DeleteAsync(GenesisService.BuildEntityCacheKey(entityId), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(GenesisPublishedTopics.GenesisEntityUpdated, capturedTopic);
        var typedEvent = Assert.IsType<GenesisEntityUpdatedEvent>(capturedEvent);
        Assert.Contains("PhysicalFormType", typedEvent.ChangedFields);
    }

    [Fact]
    public async Task BindPhysicalFormAsync_ItemNotFound_ReturnsBadRequest()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var entity = CreateTestEntity(entityId);
        var template = CreateTestTemplate(entity.TemplateCode);

        _mockEntityStore.Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _mockTemplateStore.Setup(s => s.GetAsync(GenesisService.BuildTemplateKey(entity.TemplateCode), It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _mockItemClient.Setup(i => i.GetItemInstanceAsync(It.IsAny<GetItemInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var (status, _) = await service.BindPhysicalFormAsync(
            new BindPhysicalFormRequest { EntityId = entityId, PhysicalFormType = PhysicalFormType.Item, PhysicalFormId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.BadRequest, status);
    }
}
