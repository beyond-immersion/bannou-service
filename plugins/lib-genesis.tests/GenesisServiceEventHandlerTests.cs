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

/// <summary>
/// Unit tests for the self-subscription event handlers on <see cref="GenesisService"/> that
/// maintain the cross-node wallet map coherence.
/// </summary>
public class GenesisServiceEventHandlerTests : ServiceTestBase<GenesisServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory = new();
    private readonly Mock<IStateStore<GenesisTemplateModel>> _mockTemplateStore = new();
    private readonly Mock<IQueryableStateStore<GenesisTemplateModel>> _mockTemplateQueryStore = new();
    private readonly Mock<IStateStore<GenesisEntityModel>> _mockEntityStore = new();
    private readonly Mock<IQueryableStateStore<GenesisEntityModel>> _mockEntityQueryStore = new();
    private readonly Mock<IStateStore<GenesisTemplateListModel>> _mockTemplateListStore = new();
    private readonly Mock<IStateStore<GenesisEntityListModel>> _mockEntityListStore = new();
    private readonly Mock<IStateStore<CachedGenesisEntity>> _mockEntityCacheStore = new();
    private readonly Mock<IStateStore<CachedCapabilityManifest>> _mockCapsCacheStore = new();
    private readonly Mock<IStateStore<string>> _mockEntityIndexStore = new();
    private readonly Mock<IDistributedLockProvider> _mockLockProvider = new();
    private readonly Mock<IMessageBus> _mockMessageBus = new();
    private readonly Mock<ILogger<GenesisService>> _mockLogger = new();
    private readonly Mock<IResourceClient> _mockResourceClient = new();
    private readonly Mock<ISeedClient> _mockSeedClient = new();
    private readonly Mock<ICurrencyClient> _mockCurrencyClient = new();
    private readonly Mock<ICharacterClient> _mockCharacterClient = new();
    private readonly Mock<IActorClient> _mockActorClient = new();
    private readonly Mock<IInventoryClient> _mockInventoryClient = new();
    private readonly Mock<IItemClient> _mockItemClient = new();
    private readonly Mock<IRelationshipClient> _mockRelationshipClient = new();
    private readonly Mock<IRealmClient> _mockRealmClient = new();
    private readonly Mock<ISpeciesClient> _mockSpeciesClient = new();
    private readonly Mock<IGameServiceClient> _mockGameServiceClient = new();
    private readonly Mock<IEventConsumer> _mockEventConsumer = new();
    private readonly GenesisGrowthState _growthState = new();

    public GenesisServiceEventHandlerTests()
    {
        _mockStateStoreFactory.Setup(f => f.GetStore<GenesisTemplateModel>(StateStoreDefinitions.GenesisTemplates)).Returns(_mockTemplateStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetQueryableStore<GenesisTemplateModel>(StateStoreDefinitions.GenesisTemplates)).Returns(_mockTemplateQueryStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<GenesisTemplateListModel>(StateStoreDefinitions.GenesisTemplateIndexes)).Returns(_mockTemplateListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities)).Returns(_mockEntityStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetQueryableStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities)).Returns(_mockEntityQueryStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(StateStoreDefinitions.GenesisEntityIndexes)).Returns(_mockEntityIndexStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<GenesisEntityListModel>(StateStoreDefinitions.GenesisEntityIndexes)).Returns(_mockEntityListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<CachedGenesisEntity>(StateStoreDefinitions.GenesisEntityCache)).Returns(_mockEntityCacheStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<CachedCapabilityManifest>(StateStoreDefinitions.GenesisEntityCache)).Returns(_mockCapsCacheStore.Object);
    }

    private GenesisService CreateService() =>
        new(
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
            _mockEventConsumer.Object,
            _growthState);

    private static GenesisTemplateModel CreateTemplate() =>
        new()
        {
            TemplateCode = "treasure_chest",
            GameServiceId = Guid.NewGuid(),
            DisplayName = "Treasure Chest",
            Description = "Chest",
            Seed = new GenesisSeedConfig
            {
                SeedTypeCode = "treasure_chest",
                Domains = new List<GenesisSeedDomain> { new() { DomainCode = "awareness", DisplayName = "Awareness" } },
                Phases = new List<GenesisSeedPhase> { new() { PhaseName = "Dormant", Threshold = 0, CognitiveStage = CognitiveStage.Dormant } }
            },
            Economy = new GenesisEconomyConfig
            {
                Wallets = new List<GenesisWalletConfig> { new() { WalletCode = "mana", CurrencyCode = "mana" } },
                GrowthMappings = new List<GenesisGrowthMapping>
                {
                    new() { WalletCode = "mana", Domain = "awareness", Ratio = 1.0, Direction = GrowthDirection.Credit }
                }
            },
            Storage = new GenesisStorageConfig { Inventories = new List<GenesisInventoryConfig>() },
            Awakening = new GenesisAwakeningConfig { SystemRealmCode = "SENTIENT", CharacterSpeciesCode = "spirit" },
            PhysicalFormType = PhysicalFormType.Item,
            Bond = new GenesisBondConfig { Enabled = false, Cardinality = BondCardinality.None },
        };

    [Fact]
    public async Task HandleGenesisEntityCreatedAsync_PopulatesWalletMap()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        var template = CreateTemplate();

        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("treasure_chest"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var evt = new GenesisEntityCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EntityId = entityId,
            TemplateCode = "treasure_chest",
            GameServiceId = template.GameServiceId,
            RealmId = Guid.NewGuid(),
            WalletIds = new Dictionary<string, Guid> { ["mana"] = walletId },
            InventoryIds = new Dictionary<string, Guid>(),
            CurrentPhase = "Dormant",
            CognitiveStage = CognitiveStage.Dormant,
            PhysicalFormType = PhysicalFormType.Item,
            Status = GenesisEntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await service.HandleGenesisEntityCreatedAsync(evt);

        Assert.True(_growthState.TryGetWalletMapping(walletId, out var mapping));
        Assert.Equal(entityId, mapping.EntityId);
        Assert.Equal("treasure_chest", mapping.TemplateCode);
        Assert.Equal("mana", mapping.WalletCode);
        Assert.Single(mapping.GrowthMappings);
    }

    [Fact]
    public async Task HandleGenesisEntityCreatedAsync_TemplateMissing_DoesNotPopulate()
    {
        var service = CreateService();
        var walletId = Guid.NewGuid();
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisTemplateModel?)null);

        var evt = new GenesisEntityCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EntityId = Guid.NewGuid(),
            TemplateCode = "missing",
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            WalletIds = new Dictionary<string, Guid> { ["mana"] = walletId },
            InventoryIds = new Dictionary<string, Guid>(),
            CurrentPhase = "Dormant",
            CognitiveStage = CognitiveStage.Dormant,
            PhysicalFormType = PhysicalFormType.Item,
            Status = GenesisEntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await service.HandleGenesisEntityCreatedAsync(evt);

        Assert.False(_growthState.TryGetWalletMapping(walletId, out _));
    }

    [Fact]
    public async Task HandleGenesisEntityCreatedAsync_MultipleWallets_AllAdded()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var manaWalletId = Guid.NewGuid();
        var expWalletId = Guid.NewGuid();
        var template = CreateTemplate();

        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("treasure_chest"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var evt = new GenesisEntityCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EntityId = entityId,
            TemplateCode = "treasure_chest",
            GameServiceId = template.GameServiceId,
            RealmId = Guid.NewGuid(),
            WalletIds = new Dictionary<string, Guid>
            {
                ["mana"] = manaWalletId,
                ["experience"] = expWalletId,
            },
            InventoryIds = new Dictionary<string, Guid>(),
            CurrentPhase = "Dormant",
            CognitiveStage = CognitiveStage.Dormant,
            PhysicalFormType = PhysicalFormType.Item,
            Status = GenesisEntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await service.HandleGenesisEntityCreatedAsync(evt);

        Assert.True(_growthState.TryGetWalletMapping(manaWalletId, out var manaMapping));
        Assert.Equal("mana", manaMapping.WalletCode);
        Assert.True(_growthState.TryGetWalletMapping(expWalletId, out var expMapping));
        Assert.Equal("experience", expMapping.WalletCode);
    }

    [Fact]
    public async Task HandleGenesisEntityDeletedAsync_RemovesFromWalletMap()
    {
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var walletId = Guid.NewGuid();
        _growthState.SetWalletMapping(walletId, new GenesisWalletMapping(
            EntityId: entityId,
            TemplateCode: "treasure_chest",
            WalletCode: "mana",
            GrowthMappings: new List<GenesisGrowthMapping>()));

        var evt = new GenesisEntityDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EntityId = entityId,
            TemplateCode = "treasure_chest",
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            WalletIds = new Dictionary<string, Guid> { ["mana"] = walletId },
            InventoryIds = new Dictionary<string, Guid>(),
            CurrentPhase = "Dormant",
            CognitiveStage = CognitiveStage.Dormant,
            PhysicalFormType = PhysicalFormType.Item,
            Status = GenesisEntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await service.HandleGenesisEntityDeletedAsync(evt);

        Assert.False(_growthState.TryGetWalletMapping(walletId, out _));
    }

    [Fact]
    public async Task HandleGenesisEntityDeletedAsync_UnknownWallet_NoError()
    {
        var service = CreateService();
        var evt = new GenesisEntityDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EntityId = Guid.NewGuid(),
            TemplateCode = "treasure_chest",
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            WalletIds = new Dictionary<string, Guid> { ["mana"] = Guid.NewGuid() },
            InventoryIds = new Dictionary<string, Guid>(),
            CurrentPhase = "Dormant",
            CognitiveStage = CognitiveStage.Dormant,
            PhysicalFormType = PhysicalFormType.Item,
            Status = GenesisEntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Should not throw — idempotent
        await service.HandleGenesisEntityDeletedAsync(evt);
    }
}
