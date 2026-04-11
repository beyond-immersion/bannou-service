using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Genesis;
using BeyondImmersion.BannouService.Genesis.Services;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Genesis.Tests;

/// <summary>
/// Unit tests for <see cref="GenesisGrowthFlushWorkerService"/>. Tests the flush cycle logic
/// via a reflection-invoked helper rather than driving the full <c>ExecuteAsync</c> loop.
/// </summary>
public class GenesisGrowthFlushWorkerServiceTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory = new();
    private readonly Mock<IStateStore<GenesisEntityModel>> _mockEntityStore = new();
    private readonly Mock<IStateStore<GenesisTemplateModel>> _mockTemplateStore = new();
    private readonly Mock<ISeedClient> _mockSeedClient = new();
    private readonly Mock<IMessageBus> _mockMessageBus = new();
    private readonly Mock<ILogger<GenesisGrowthFlushWorkerService>> _mockLogger = new();
    private readonly GenesisGrowthState _state = new();
    private readonly GenesisServiceConfiguration _configuration = new()
    {
        GrowthFlushIntervalSeconds = 5,
        StartupDelaySeconds = 0,
    };
    private readonly IServiceProvider _serviceProvider;

    public GenesisGrowthFlushWorkerServiceTests()
    {
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities))
            .Returns(_mockEntityStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GenesisTemplateModel>(StateStoreDefinitions.GenesisTemplates))
            .Returns(_mockTemplateStore.Object);

        var services = new ServiceCollection();
        services.AddSingleton(_mockStateStoreFactory.Object);
        services.AddSingleton(_mockSeedClient.Object);
        services.AddSingleton(_mockMessageBus.Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    private GenesisGrowthFlushWorkerService CreateWorker() =>
        new(_serviceProvider, _state, _mockLogger.Object, _configuration, new NullTelemetryProvider());

    /// <summary>
    /// Invokes the private ProcessFlushCycleAsync via reflection. The worker's ExecuteAsync
    /// loop would otherwise require a full BackgroundService hosting environment.
    /// </summary>
    private static async Task InvokeProcessFlushCycleAsync(GenesisGrowthFlushWorkerService worker)
    {
        var method = typeof(GenesisGrowthFlushWorkerService)
            .GetMethod("ProcessFlushCycleAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("ProcessFlushCycleAsync not found");
        var task = (Task)method.Invoke(worker, new object[] { CancellationToken.None })!;
        await task;
    }

    private static GenesisEntityModel CreateEntity(Guid entityId, Guid seedId, string templateCode = "treasure_chest") =>
        new()
        {
            EntityId = entityId,
            TemplateCode = templateCode,
            GameServiceId = Guid.NewGuid(),
            RealmId = Guid.NewGuid(),
            SeedId = seedId,
            WalletIds = new Dictionary<string, Guid>(),
            InventoryIds = new Dictionary<string, Guid>(),
            CurrentPhase = "Dormant",
            CognitiveStage = CognitiveStage.Dormant,
            PhysicalFormType = PhysicalFormType.Item,
            Status = GenesisEntityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static GenesisTemplateModel CreateTemplate(
        string templateCode = "treasure_chest",
        IEnumerable<GenesisGrowthMapping>? mappings = null) =>
        new()
        {
            TemplateCode = templateCode,
            GameServiceId = Guid.NewGuid(),
            DisplayName = "Treasure Chest",
            Description = "A chest",
            Seed = new GenesisSeedConfig
            {
                SeedTypeCode = "treasure_chest",
                Domains = new List<GenesisSeedDomain> { new() { DomainCode = "awareness", DisplayName = "Awareness" } },
                Phases = new List<GenesisSeedPhase> { new() { PhaseName = "Dormant", Threshold = 0, CognitiveStage = CognitiveStage.Dormant } }
            },
            Economy = new GenesisEconomyConfig
            {
                Wallets = new List<GenesisWalletConfig> { new() { WalletCode = "mana", CurrencyCode = "mana" } },
                GrowthMappings = mappings?.ToList() ?? new List<GenesisGrowthMapping>
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
    public async Task FlushCycle_EmptyAccumulator_DoesNothing()
    {
        using var worker = CreateWorker();

        await InvokeProcessFlushCycleAsync(worker);

        _mockSeedClient.Verify(
            s => s.RecordGrowthBatchAsync(It.IsAny<RecordGrowthBatchRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FlushCycle_BufferedCredits_CallsRecordGrowthBatchForEachEntity()
    {
        var entityId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var entity = CreateEntity(entityId, seedId);
        var template = CreateTemplate();

        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("treasure_chest"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 10.0, GrowthDirection.Credit));
        _state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 15.0, GrowthDirection.Credit));

        RecordGrowthBatchRequest? capturedRequest = null;
        _mockSeedClient
            .Setup(s => s.RecordGrowthBatchAsync(It.IsAny<RecordGrowthBatchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RecordGrowthBatchRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new GrowthResponse { SeedId = seedId, TotalGrowth = 25.0f, Domains = new Dictionary<string, float>() });

        using var worker = CreateWorker();
        await InvokeProcessFlushCycleAsync(worker);

        Assert.NotNull(capturedRequest);
        Assert.Equal(seedId, capturedRequest.SeedId);
        Assert.Single(capturedRequest.Entries);
        var entry = capturedRequest.Entries.First();
        Assert.Equal("awareness", entry.Domain);
        Assert.Equal(25.0f, entry.Amount, 0.01f);
        Assert.Equal("genesis-growth-flush", capturedRequest.Source);
    }

    [Fact]
    public async Task FlushCycle_AppliesRatioToBufferedAmount()
    {
        var entityId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var entity = CreateEntity(entityId, seedId);
        var template = CreateTemplate(mappings: new[]
        {
            new GenesisGrowthMapping { WalletCode = "mana", Domain = "awareness", Ratio = 0.5, Direction = GrowthDirection.Credit }
        });

        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("treasure_chest"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 100.0, GrowthDirection.Credit));

        RecordGrowthBatchRequest? capturedRequest = null;
        _mockSeedClient
            .Setup(s => s.RecordGrowthBatchAsync(It.IsAny<RecordGrowthBatchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RecordGrowthBatchRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new GrowthResponse { SeedId = seedId, TotalGrowth = 50.0f, Domains = new Dictionary<string, float>() });

        using var worker = CreateWorker();
        await InvokeProcessFlushCycleAsync(worker);

        Assert.NotNull(capturedRequest);
        Assert.Equal(50.0f, capturedRequest.Entries.First().Amount, 0.01f);
    }

    [Fact]
    public async Task FlushCycle_CreditEntryWithDebitMapping_NoGrowthApplied()
    {
        var entityId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var entity = CreateEntity(entityId, seedId);
        var template = CreateTemplate(mappings: new[]
        {
            new GenesisGrowthMapping { WalletCode = "mana", Domain = "awareness", Ratio = 1.0, Direction = GrowthDirection.Debit }
        });

        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("treasure_chest"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 100.0, GrowthDirection.Credit));

        using var worker = CreateWorker();
        await InvokeProcessFlushCycleAsync(worker);

        _mockSeedClient.Verify(
            s => s.RecordGrowthBatchAsync(It.IsAny<RecordGrowthBatchRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FlushCycle_BothDirectionMapping_AcceptsCreditAndDebit()
    {
        var entityId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var entity = CreateEntity(entityId, seedId);
        var template = CreateTemplate(mappings: new[]
        {
            new GenesisGrowthMapping { WalletCode = "mana", Domain = "awareness", Ratio = 1.0, Direction = GrowthDirection.Both }
        });

        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _mockTemplateStore
            .Setup(s => s.GetAsync(GenesisService.BuildTemplateKey("treasure_chest"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        _state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 10.0, GrowthDirection.Credit));
        _state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 20.0, GrowthDirection.Debit));

        RecordGrowthBatchRequest? capturedRequest = null;
        _mockSeedClient
            .Setup(s => s.RecordGrowthBatchAsync(It.IsAny<RecordGrowthBatchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RecordGrowthBatchRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new GrowthResponse { SeedId = seedId, TotalGrowth = 30.0f, Domains = new Dictionary<string, float>() });

        using var worker = CreateWorker();
        await InvokeProcessFlushCycleAsync(worker);

        Assert.NotNull(capturedRequest);
        Assert.Equal(30.0f, capturedRequest.Entries.First().Amount, 0.01f);
    }

    [Fact]
    public async Task FlushCycle_EntityDestroyedBetweenBufferAndFlush_Skipped()
    {
        var entityId = Guid.NewGuid();
        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GenesisEntityModel?)null);

        _state.BufferGrowth(entityId, new GrowthBufferEntry("mana", 10.0, GrowthDirection.Credit));

        using var worker = CreateWorker();
        await InvokeProcessFlushCycleAsync(worker);

        _mockSeedClient.Verify(
            s => s.RecordGrowthBatchAsync(It.IsAny<RecordGrowthBatchRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FlushCycle_MultipleEntities_OneCallPerEntity()
    {
        var entityA = Guid.NewGuid();
        var entityB = Guid.NewGuid();
        var seedA = Guid.NewGuid();
        var seedB = Guid.NewGuid();

        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEntity(entityA, seedA));
        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(entityB), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEntity(entityB, seedB));
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTemplate());

        _state.BufferGrowth(entityA, new GrowthBufferEntry("mana", 10.0, GrowthDirection.Credit));
        _state.BufferGrowth(entityA, new GrowthBufferEntry("mana", 5.0, GrowthDirection.Credit));
        _state.BufferGrowth(entityB, new GrowthBufferEntry("mana", 20.0, GrowthDirection.Credit));

        var seedCallCount = 0;
        _mockSeedClient
            .Setup(s => s.RecordGrowthBatchAsync(It.IsAny<RecordGrowthBatchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RecordGrowthBatchRequest, CancellationToken>((_, _) => seedCallCount++)
            .ReturnsAsync(new GrowthResponse { TotalGrowth = 0, Domains = new Dictionary<string, float>() });

        using var worker = CreateWorker();
        await InvokeProcessFlushCycleAsync(worker);

        Assert.Equal(2, seedCallCount);
    }

    [Fact]
    public async Task FlushCycle_OneEntityFailure_OthersStillProcessed()
    {
        var failingEntity = Guid.NewGuid();
        var healthyEntity = Guid.NewGuid();
        var failingSeed = Guid.NewGuid();
        var healthySeed = Guid.NewGuid();

        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(failingEntity), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEntity(failingEntity, failingSeed));
        _mockEntityStore
            .Setup(s => s.GetAsync(GenesisService.BuildEntityKey(healthyEntity), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEntity(healthyEntity, healthySeed));
        _mockTemplateStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTemplate());

        _mockSeedClient
            .Setup(s => s.RecordGrowthBatchAsync(It.Is<RecordGrowthBatchRequest>(r => r.SeedId == failingSeed), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated failure"));
        _mockSeedClient
            .Setup(s => s.RecordGrowthBatchAsync(It.Is<RecordGrowthBatchRequest>(r => r.SeedId == healthySeed), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GrowthResponse { SeedId = healthySeed, TotalGrowth = 10.0f, Domains = new Dictionary<string, float>() });

        _state.BufferGrowth(failingEntity, new GrowthBufferEntry("mana", 5.0, GrowthDirection.Credit));
        _state.BufferGrowth(healthyEntity, new GrowthBufferEntry("mana", 10.0, GrowthDirection.Credit));

        using var worker = CreateWorker();
        await InvokeProcessFlushCycleAsync(worker);

        // Healthy entity should still have been processed despite failing entity's exception
        _mockSeedClient.Verify(
            s => s.RecordGrowthBatchAsync(It.Is<RecordGrowthBatchRequest>(r => r.SeedId == healthySeed), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
