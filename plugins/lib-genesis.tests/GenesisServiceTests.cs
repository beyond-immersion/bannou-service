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

public class GenesisServiceTemplateTests : ServiceTestBase<GenesisServiceConfiguration>
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

    public GenesisServiceTemplateTests()
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

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockTemplateListStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisTemplateListModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private GenesisService CreateService()
    {
        return new GenesisService(
            _mockStateStoreFactory.Object,
            _mockLockProvider.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            _mockTelemetryProvider.Object,
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

    private static RegisterTemplateRequest CreateValidRegisterRequest()
    {
        return new RegisterTemplateRequest
        {
            TemplateCode = "test_template",
            GameServiceId = Guid.NewGuid(),
            DisplayName = "Test Template",
            Description = "A test genesis template",
            Seed = new GenesisSeedConfig
            {
                SeedTypeCode = "test_seed_type",
                Domains = new List<GenesisSeedDomain> { new() { DomainCode = "growth", DisplayName = "Growth" } },
                Phases = new List<GenesisSeedPhase>
                {
                    new() { PhaseName = "Dormant", Threshold = 0, CognitiveStage = CognitiveStage.Dormant },
                    new() { PhaseName = "Stirring", Threshold = 100, CognitiveStage = CognitiveStage.EventBrain },
                }
            },
            Economy = new GenesisEconomyConfig
            {
                Wallets = new List<GenesisWalletConfig> { new() { WalletCode = "mana", CurrencyCode = "mana_currency" } },
                GrowthMappings = new List<GenesisGrowthMapping> { new() { WalletCode = "mana", Domain = "growth", Ratio = 1.0, Direction = GrowthDirection.Credit } }
            },
            Storage = new GenesisStorageConfig { Inventories = new List<GenesisInventoryConfig> { new() { InventoryCode = "loot", ConstraintModel = "Unlimited", Capacity = 20 } } },
            Awakening = new GenesisAwakeningConfig { SystemRealmCode = "SENTIENT_CONTAINERS", CharacterSpeciesCode = "treasure_spirit" },
            PhysicalFormType = PhysicalFormType.Item,
            Bond = new GenesisBondConfig { Enabled = false, Cardinality = BondCardinality.None },
            ArchiveOnDestruction = true
        };
    }

    // ===================================================================
    // RegisterTemplate
    // ===================================================================

    [Fact]
    public async Task RegisterTemplateAsync_ValidRequest_ReturnsOkAndSavesTemplate()
    {
        var service = CreateService();
        var request = CreateValidRegisterRequest();

        _mockRealmClient
            .Setup(r => r.GetRealmByCodeAsync(It.IsAny<GetRealmByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmResponse { RealmId = Guid.NewGuid(), IsSystemType = true });
        _mockSpeciesClient
            .Setup(s => s.GetSpeciesByCodeAsync(It.IsAny<GetSpeciesByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeciesResponse { SpeciesId = Guid.NewGuid() });
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey(request.TemplateCode), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisTemplateModel?)null);
        _mockSeedClient
            .Setup(s => s.RegisterSeedTypeAsync(It.IsAny<RegisterSeedTypeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeedTypeResponse());

        GenesisTemplateModel? savedModel = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GenesisTemplateModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("ok");

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) => { capturedTopic = t; capturedEvent = e; })
            .ReturnsAsync(true);

        var (status, response) = await service.RegisterTemplateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(request.TemplateCode, response.TemplateCode);
        Assert.NotNull(savedModel);
        Assert.Equal(request.TemplateCode, savedModel.TemplateCode);
        Assert.False(savedModel.IsDeprecated);
        Assert.Equal(GenesisPublishedTopics.TemplateCreated, capturedTopic);
        var typedEvent = Assert.IsType<TemplateCreatedEvent>(capturedEvent);
        Assert.Equal(request.TemplateCode, typedEvent.TemplateCode);
    }

    [Fact]
    public async Task RegisterTemplateAsync_InvalidGrowthMappings_ReturnsBadRequest()
    {
        var service = CreateService();
        var request = CreateValidRegisterRequest();
        request.Economy.GrowthMappings = new List<GenesisGrowthMapping>
        {
            new() { WalletCode = "nonexistent_wallet", Domain = "growth", Ratio = 1.0, Direction = GrowthDirection.Credit }
        };

        var (status, _) = await service.RegisterTemplateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task RegisterTemplateAsync_InvalidSystemRealm_ReturnsBadRequest()
    {
        var service = CreateService();
        var request = CreateValidRegisterRequest();
        _mockRealmClient
            .Setup(r => r.GetRealmByCodeAsync(It.IsAny<GetRealmByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var (status, _) = await service.RegisterTemplateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task RegisterTemplateAsync_RealmNotSystemType_ReturnsBadRequest()
    {
        var service = CreateService();
        var request = CreateValidRegisterRequest();
        _mockRealmClient
            .Setup(r => r.GetRealmByCodeAsync(It.IsAny<GetRealmByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmResponse { RealmId = Guid.NewGuid(), IsSystemType = false });

        var (status, _) = await service.RegisterTemplateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task RegisterTemplateAsync_ExistingTemplate_ReturnsIdempotent()
    {
        var service = CreateService();
        var request = CreateValidRegisterRequest();
        _mockRealmClient
            .Setup(r => r.GetRealmByCodeAsync(It.IsAny<GetRealmByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmResponse { RealmId = Guid.NewGuid(), IsSystemType = true });
        _mockSpeciesClient
            .Setup(s => s.GetSpeciesByCodeAsync(It.IsAny<GetSpeciesByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeciesResponse { SpeciesId = Guid.NewGuid() });

        var existing = CreateTestTemplate(request.TemplateCode);
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey(request.TemplateCode), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var (status, response) = await service.RegisterTemplateAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        _mockTemplateStore.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ===================================================================
    // GetTemplate
    // ===================================================================

    [Fact]
    public async Task GetTemplateAsync_ExistingTemplate_ReturnsOk()
    {
        var service = CreateService();
        var model = CreateTestTemplate("my_template");
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("my_template"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var (status, response) = await service.GetTemplateAsync(
            new GetTemplateRequest { TemplateCode = "my_template" }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("my_template", response.TemplateCode);
    }

    [Fact]
    public async Task GetTemplateAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisTemplateModel?)null);

        var (status, _) = await service.GetTemplateAsync(
            new GetTemplateRequest { TemplateCode = "missing" }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    // ===================================================================
    // DeprecateTemplate
    // ===================================================================

    [Fact]
    public async Task DeprecateTemplateAsync_ValidRequest_SetsDeprecationAndPublishes()
    {
        var service = CreateService();
        var model = CreateTestTemplate("deprecate_me");
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("deprecate_me"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        GenesisTemplateModel? savedModel = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GenesisTemplateModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("ok");

        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) => { capturedTopic = t; capturedEvent = e; })
            .ReturnsAsync(true);

        var (status, response) = await service.DeprecateTemplateAsync(
            new DeprecateTemplateRequest { TemplateCode = "deprecate_me", Reason = "No longer needed" },
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);
        Assert.NotNull(savedModel);
        Assert.True(savedModel.IsDeprecated);
        Assert.NotNull(savedModel.DeprecatedAt);
        Assert.Equal("No longer needed", savedModel.DeprecationReason);
        Assert.Equal(GenesisPublishedTopics.TemplateUpdated, capturedTopic);
        var typedEvent = Assert.IsType<TemplateUpdatedEvent>(capturedEvent);
        Assert.Contains("IsDeprecated", typedEvent.ChangedFields);
    }

    [Fact]
    public async Task DeprecateTemplateAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisTemplateModel?)null);

        var (status, _) = await service.DeprecateTemplateAsync(
            new DeprecateTemplateRequest { TemplateCode = "missing" }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeprecateTemplateAsync_AlreadyDeprecated_ReturnsOkIdempotent()
    {
        var service = CreateService();
        var model = CreateTestTemplate("already_dep");
        model.IsDeprecated = true;
        model.DeprecatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("already_dep"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var (status, response) = await service.DeprecateTemplateAsync(
            new DeprecateTemplateRequest { TemplateCode = "already_dep" }, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        _mockTemplateStore.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
