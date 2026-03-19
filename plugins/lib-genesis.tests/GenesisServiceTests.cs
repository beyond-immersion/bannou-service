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
/// Template endpoint tests for GenesisService.
/// Covers: RegisterTemplate, GetTemplate, ListTemplates, UpdateTemplate, DeprecateTemplate, CleanDeprecated.
/// </summary>
public class GenesisServiceTemplateTests : ServiceTestBase<GenesisServiceConfiguration>
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
        _mockEntityStore = new Mock<IStateStore<GenesisEntityModel>>();
        _mockEntityListStore = new Mock<IStateStore<GenesisEntityListModel>>();
        _mockEntityCacheStore = new Mock<IStateStore<CachedGenesisEntity>>();
        _mockCapsCacheStore = new Mock<IStateStore<CachedCapabilityManifest>>();
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

    private static RegisterTemplateRequest CreateValidRegisterRequest(string templateCode = "test_template")
    {
        return new RegisterTemplateRequest
        {
            TemplateCode = templateCode,
            GameServiceId = Guid.NewGuid(),
            DisplayName = "Test Template",
            Description = "A test genesis template",
            Seed = new GenesisSeedConfig
            {
                SeedTypeCode = "test_seed_type",
                Domains = new List<GenesisSeedDomain>
                {
                    new() { DomainCode = "growth", DisplayName = "Growth" }
                },
                Phases = new List<GenesisSeedPhase>
                {
                    new() { PhaseName = "Dormant", Threshold = 0, CognitiveStage = CognitiveStage.Dormant },
                    new() { PhaseName = "Stirring", Threshold = 100, CognitiveStage = CognitiveStage.EventBrain },
                    new() { PhaseName = "Awakened", Threshold = 500, CognitiveStage = CognitiveStage.CharacterBrain }
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
            Awakening = new GenesisAwakeningConfig
            {
                SystemRealmCode = "SENTIENT_CONTAINERS",
                CharacterSpeciesCode = "treasure_spirit"
            },
            PhysicalFormType = PhysicalFormType.Item,
            Bond = new GenesisBondConfig { Enabled = false, Cardinality = BondCardinality.None },
            ArchiveOnDestruction = true
        };
    }

    private void SetupRealmValidation(bool exists = true, bool isSystem = true)
    {
        if (exists)
        {
            _mockRealmClient
                .Setup(r => r.GetRealmByCodeAsync(It.IsAny<GetRealmByCodeRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RealmResponse { RealmId = Guid.NewGuid(), Code = "SENTIENT_CONTAINERS", IsSystemType = isSystem });
        }
        else
        {
            _mockRealmClient
                .Setup(r => r.GetRealmByCodeAsync(It.IsAny<GetRealmByCodeRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ApiException("Not found", 404, null, null, null));
        }
    }

    private void SetupSpeciesValidation(bool exists = true)
    {
        if (exists)
        {
            _mockSpeciesClient
                .Setup(s => s.GetSpeciesByCodeAsync(It.IsAny<GetSpeciesByCodeRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SpeciesResponse { SpeciesId = Guid.NewGuid(), Code = "treasure_spirit" });
        }
        else
        {
            _mockSpeciesClient
                .Setup(s => s.GetSpeciesByCodeAsync(It.IsAny<GetSpeciesByCodeRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ApiException("Not found", 404, null, null, null));
        }
    }

    // ===================================================================
    // RegisterTemplate
    // ===================================================================

    [Fact]
    public async Task RegisterTemplateAsync_ValidRequest_ReturnsOkAndSavesTemplate()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRegisterRequest();
        SetupRealmValidation();
        SetupSpeciesValidation();

        _mockTemplateStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("template:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisTemplateModel?)null);

        _mockSeedClient
            .Setup(s => s.RegisterSeedTypeAsync(It.IsAny<RegisterSeedTypeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterSeedTypeResponse());

        string? savedKey = null;
        GenesisTemplateModel? savedModel = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisTemplateModel>(), It.IsAny<CancellationToken>()))
            .Callback<string, GenesisTemplateModel, CancellationToken>((k, m, _) =>
            {
                savedKey = k;
                savedModel = m;
            })
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
        var (status, response) = await service.RegisterTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(request.TemplateCode, response.TemplateCode);
        Assert.Equal(request.GameServiceId, response.GameServiceId);

        Assert.Equal($"template:{request.TemplateCode}", savedKey);
        Assert.NotNull(savedModel);
        Assert.Equal(request.TemplateCode, savedModel.TemplateCode);
        Assert.Equal(request.DisplayName, savedModel.DisplayName);
        Assert.False(savedModel.IsDeprecated);

        Assert.Equal(GenesisPublishedTopics.TemplateCreated, capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<TemplateCreatedEvent>(capturedEvent);
        Assert.Equal(request.TemplateCode, typedEvent.TemplateCode);
        Assert.Equal(request.GameServiceId, typedEvent.GameServiceId);
    }

    [Fact]
    public async Task RegisterTemplateAsync_InvalidGrowthMappings_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRegisterRequest();
        request.Economy.GrowthMappings = new List<GenesisGrowthMapping>
        {
            new() { WalletCode = "nonexistent_wallet", Domain = "growth", Ratio = 1.0, Direction = GrowthDirection.Credit }
        };

        // Act
        var (status, response) = await service.RegisterTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
        _mockTemplateStore.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisTemplateModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterTemplateAsync_InvalidSystemRealm_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRegisterRequest();
        SetupRealmValidation(exists: false);

        // Act
        var (status, response) = await service.RegisterTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
        _mockTemplateStore.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisTemplateModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterTemplateAsync_InvalidSpecies_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRegisterRequest();
        SetupRealmValidation();
        SetupSpeciesValidation(exists: false);

        // Act
        var (status, response) = await service.RegisterTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
        _mockTemplateStore.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisTemplateModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterTemplateAsync_ExistingTemplate_ReturnsIdempotent()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRegisterRequest();
        SetupRealmValidation();
        SetupSpeciesValidation();

        var existing = new GenesisTemplateModel
        {
            TemplateCode = request.TemplateCode,
            GameServiceId = request.GameServiceId,
            DisplayName = request.DisplayName,
            Description = request.Description,
            Seed = request.Seed,
            Economy = request.Economy,
            Storage = request.Storage,
            Awakening = request.Awakening,
            PhysicalFormType = request.PhysicalFormType,
            Bond = request.Bond,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        _mockTemplateStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("template:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        // Act
        var (status, response) = await service.RegisterTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(request.TemplateCode, response.TemplateCode);
        // Idempotent — no new save, no event published
        _mockTemplateStore.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisTemplateModel>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockMessageBus.Verify(m => m.TryPublishAsync(GenesisPublishedTopics.TemplateCreated, It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ===================================================================
    // GetTemplate
    // ===================================================================

    [Fact]
    public async Task GetTemplateAsync_ExistingTemplate_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        var templateCode = "my_template";
        var model = new GenesisTemplateModel
        {
            TemplateCode = templateCode,
            GameServiceId = Guid.NewGuid(),
            DisplayName = "My Template",
            Description = "Description",
            Seed = new GenesisSeedConfig { SeedTypeCode = "s", Domains = new(), Phases = new() },
            Economy = new GenesisEconomyConfig { Wallets = new(), GrowthMappings = new() },
            Storage = new GenesisStorageConfig { Inventories = new() },
            Awakening = new GenesisAwakeningConfig { SystemRealmCode = "R", CharacterSpeciesCode = "S" },
            Bond = new GenesisBondConfig { Enabled = false, Cardinality = BondCardinality.None },
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateCode}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.GetTemplateAsync(new GetTemplateRequest { TemplateCode = templateCode });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(templateCode, response.TemplateCode);
        Assert.Equal(model.DisplayName, response.DisplayName);
    }

    [Fact]
    public async Task GetTemplateAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisTemplateModel?)null);

        // Act
        var (status, response) = await service.GetTemplateAsync(new GetTemplateRequest { TemplateCode = "missing" });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    // ===================================================================
    // DeprecateTemplate
    // ===================================================================

    [Fact]
    public async Task DeprecateTemplateAsync_ValidRequest_ReturnsOkAndSetsDeprecation()
    {
        // Arrange
        var service = CreateService();
        var templateCode = "deprecate_me";
        var model = new GenesisTemplateModel
        {
            TemplateCode = templateCode,
            GameServiceId = Guid.NewGuid(),
            DisplayName = "To Deprecate",
            Description = "Will be deprecated",
            Seed = new GenesisSeedConfig { SeedTypeCode = "s", Domains = new(), Phases = new() },
            Economy = new GenesisEconomyConfig { Wallets = new(), GrowthMappings = new() },
            Storage = new GenesisStorageConfig { Inventories = new() },
            Awakening = new GenesisAwakeningConfig { SystemRealmCode = "R", CharacterSpeciesCode = "S" },
            Bond = new GenesisBondConfig { Enabled = false, Cardinality = BondCardinality.None },
            IsDeprecated = false,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateCode}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        GenesisTemplateModel? savedModel = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisTemplateModel>(), It.IsAny<CancellationToken>()))
            .Callback<string, GenesisTemplateModel, CancellationToken>((_, m, _) => savedModel = m)
            .ReturnsAsync("etag-2");

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
        var (status, response) = await service.DeprecateTemplateAsync(
            new DeprecateTemplateRequest { TemplateCode = templateCode, Reason = "No longer needed" });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);

        Assert.NotNull(savedModel);
        Assert.True(savedModel.IsDeprecated);
        Assert.NotNull(savedModel.DeprecatedAt);
        Assert.Equal("No longer needed", savedModel.DeprecationReason);

        Assert.Equal(GenesisPublishedTopics.TemplateUpdated, capturedTopic);
        var typedEvent = Assert.IsType<TemplateUpdatedEvent>(capturedEvent);
        Assert.Equal(templateCode, typedEvent.TemplateCode);
        Assert.Contains("IsDeprecated", typedEvent.ChangedFields);
    }

    [Fact]
    public async Task DeprecateTemplateAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisTemplateModel?)null);

        // Act
        var (status, response) = await service.DeprecateTemplateAsync(
            new DeprecateTemplateRequest { TemplateCode = "missing" });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeprecateTemplateAsync_AlreadyDeprecated_ReturnsIdempotent()
    {
        // Arrange
        var service = CreateService();
        var templateCode = "already_deprecated";
        var model = new GenesisTemplateModel
        {
            TemplateCode = templateCode,
            GameServiceId = Guid.NewGuid(),
            DisplayName = "Already Deprecated",
            Description = "Already deprecated",
            Seed = new GenesisSeedConfig { SeedTypeCode = "s", Domains = new(), Phases = new() },
            Economy = new GenesisEconomyConfig { Wallets = new(), GrowthMappings = new() },
            Storage = new GenesisStorageConfig { Inventories = new() },
            Awakening = new GenesisAwakeningConfig { SystemRealmCode = "R", CharacterSpeciesCode = "S" },
            Bond = new GenesisBondConfig { Enabled = false, Cardinality = BondCardinality.None },
            IsDeprecated = true,
            DeprecatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            DeprecationReason = "Old reason",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateCode}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.DeprecateTemplateAsync(
            new DeprecateTemplateRequest { TemplateCode = templateCode });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        // Idempotent — no save, no event
        _mockTemplateStore.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GenesisTemplateModel>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockMessageBus.Verify(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
