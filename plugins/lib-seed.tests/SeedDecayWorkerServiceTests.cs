using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;

namespace BeyondImmersion.BannouService.Seed.Tests;

/// <summary>
/// Unit tests for the SeedDecayWorkerService background worker.
/// Tests decay formula, per-type config resolution, phase regression, and pagination.
/// </summary>
public class SeedDecayWorkerServiceTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<SeedModel>> _mockSeedStore;
    private readonly Mock<IJsonQueryableStateStore<SeedModel>> _mockSeedQueryStore;
    private readonly Mock<IStateStore<SeedGrowthModel>> _mockGrowthStore;
    private readonly Mock<IStateStore<CapabilityManifestModel>> _mockCapabilitiesStore;
    private readonly Mock<IJsonQueryableStateStore<SeedTypeDefinitionModel>> _mockTypeQueryStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<SeedDecayWorkerService>> _mockLogger;
    private readonly SeedServiceConfiguration _configuration;

    private readonly Guid _testGameServiceId = Guid.NewGuid();

    public SeedDecayWorkerServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockSeedStore = new Mock<IStateStore<SeedModel>>();
        _mockSeedQueryStore = new Mock<IJsonQueryableStateStore<SeedModel>>();
        _mockGrowthStore = new Mock<IStateStore<SeedGrowthModel>>();
        _mockCapabilitiesStore = new Mock<IStateStore<CapabilityManifestModel>>();
        _mockTypeQueryStore = new Mock<IJsonQueryableStateStore<SeedTypeDefinitionModel>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<SeedDecayWorkerService>>();

        _configuration = new SeedServiceConfiguration
        {
            GrowthDecayEnabled = false,
            GrowthDecayRatePerDay = 0.01f,
            DecayWorkerIntervalSeconds = 900,
            DecayWorkerStartupDelaySeconds = 0,
            DefaultQueryPageSize = 100
        };

        // Wire up state store factory
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions))
            .Returns(_mockTypeQueryStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<SeedModel>(StateStoreDefinitions.Seed))
            .Returns(_mockSeedQueryStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<SeedModel>(StateStoreDefinitions.Seed))
            .Returns(_mockSeedStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<SeedGrowthModel>(StateStoreDefinitions.SeedGrowth))
            .Returns(_mockGrowthStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CapabilityManifestModel>(StateStoreDefinitions.SeedCapabilitiesCache))
            .Returns(_mockCapabilitiesStore.Object);

        // Default lock to succeed
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(r => r.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    /// <summary>
    /// Creates a worker wired to the mock service provider.
    /// Uses startup delay = 0 so ExecuteAsync runs its first cycle immediately.
    /// </summary>
    private SeedDecayWorkerService CreateWorker()
    {
        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(CreateMockScopedProvider());

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider
            .Setup(p => p.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);

        return new SeedDecayWorkerService(
            mockServiceProvider.Object, _mockLogger.Object, _configuration,
            Enumerable.Empty<ISeedEvolutionListener>());
    }

    /// <summary>
    /// Builds the scoped service provider that CreateScope().ServiceProvider returns.
    /// </summary>
    private IServiceProvider CreateMockScopedProvider()
    {
        var mock = new Mock<IServiceProvider>();
        mock.Setup(p => p.GetService(typeof(IStateStoreFactory))).Returns(_mockStateStoreFactory.Object);
        mock.Setup(p => p.GetService(typeof(IDistributedLockProvider))).Returns(_mockLockProvider.Object);
        mock.Setup(p => p.GetService(typeof(IMessageBus))).Returns(_mockMessageBus.Object);
        return mock.Object;
    }

    private SeedTypeDefinitionModel CreateTestType(
        bool? decayEnabled = null,
        float? decayRate = null) => new()
        {
            SeedTypeCode = "guardian",
            GameServiceId = _testGameServiceId,
            DisplayName = "Guardian",
            Description = "Test type",
            MaxPerOwner = 3,
            AllowedOwnerTypes = new List<EntityType> { EntityType.Character },
            GrowthPhases = new List<GrowthPhaseDefinition>
        {
            new() { PhaseCode = "nascent", DisplayName = "Nascent", MinTotalGrowth = 0 },
            new() { PhaseCode = "awakening", DisplayName = "Awakening", MinTotalGrowth = 10 },
            new() { PhaseCode = "mature", DisplayName = "Mature", MinTotalGrowth = 50 }
        },
            BondCardinality = 0,
            GrowthDecayEnabled = decayEnabled,
            GrowthDecayRatePerDay = decayRate
        };

    /// <summary>
    /// Runs one decay cycle by starting the worker and cancelling after a short delay.
    /// </summary>
    private async Task RunOneCycleAsync(SeedDecayWorkerService worker)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await worker.StartAsync(cts.Token);
            // Give the cycle time to execute, then cancel
            await Task.Delay(500, cts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    #region Worker Skips Disabled Types

    [Fact]
    public async Task Worker_SkipsTypesWithDecayDisabled()
    {
        // Arrange: global disabled, type also not overriding
        _configuration.GrowthDecayEnabled = false;

        var typeWithoutDecay = CreateTestType(decayEnabled: null, decayRate: null);
        SetupTypeQuery(new List<SeedTypeDefinitionModel> { typeWithoutDecay });

        using var worker = CreateWorker();

        // Act
        await RunOneCycleAsync(worker);

        // Assert: seed query store was NEVER called (no types to process)
        _mockSeedQueryStore.Verify(s => s.JsonQueryPagedAsync(
            It.IsAny<IReadOnlyList<QueryCondition>?>(),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Worker_ProcessesTypeWithOverrideEnabled_WhenGlobalDisabled()
    {
        // Arrange: global disabled, but this type has decay enabled
        _configuration.GrowthDecayEnabled = false;

        var typeWithDecay = CreateTestType(decayEnabled: true, decayRate: 0.05f);
        SetupTypeQuery(new List<SeedTypeDefinitionModel> { typeWithDecay });

        // Set up empty seed results so the cycle completes
        SetupSeedQuery(new List<SeedModel>(), 0);

        using var worker = CreateWorker();

        // Act
        await RunOneCycleAsync(worker);

        // Assert: seed query store WAS called (type resolved to decay-enabled)
        _mockSeedQueryStore.Verify(s => s.JsonQueryPagedAsync(
            It.IsAny<IReadOnlyList<QueryCondition>?>(),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    #endregion

    #region Decay Formula Tests

    [Fact]
    public async Task Worker_AppliesExponentialDecayFormula()
    {
        // Arrange: type with 10% daily decay, seed inactive for 1 day
        _configuration.GrowthDecayEnabled = true;
        _configuration.GrowthDecayRatePerDay = 0.1f;

        var seedType = CreateTestType(decayEnabled: null, decayRate: null);
        SetupTypeQuery(new List<SeedTypeDefinitionModel> { seedType });

        var seedId = Guid.NewGuid();
        var seed = new SeedModel
        {
            SeedId = seedId,
            GameServiceId = _testGameServiceId,
            SeedTypeCode = "guardian",
            GrowthPhase = "nascent",
            TotalGrowth = 10f,
            Status = SeedStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        SetupSeedQuery(new List<SeedModel> { seed }, 1);
        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        var lastActivity = DateTimeOffset.UtcNow.AddDays(-1);
        var growth = new SeedGrowthModel
        {
            SeedId = seedId,
            LastDecayedAt = null,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 10f, LastActivityAt = lastActivity, PeakDepth = 10f } }
            }
        };
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(growth);

        SeedGrowthModel? savedGrowth = null;
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedGrowthModel, StateOptions?, CancellationToken>((_, m, _, _) => savedGrowth = m)
            .ReturnsAsync("etag");
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        using var worker = CreateWorker();

        // Act
        await RunOneCycleAsync(worker);

        // Assert: depth should be approximately 10 * (1 - 0.1)^1 = 9.0
        Assert.NotNull(savedGrowth);
        var entry = savedGrowth.Domains["combat.melee"];
        Assert.True(entry.Depth > 8.5f && entry.Depth < 9.5f,
            $"Expected depth ~9.0 after 1 day of 10% decay, got {entry.Depth}");
        Assert.NotNull(savedGrowth.LastDecayedAt);
    }

    [Fact]
    public async Task Worker_DecayIsIndependentPerDomain()
    {
        // Arrange: two domains with different LastActivityAt times
        _configuration.GrowthDecayEnabled = true;
        _configuration.GrowthDecayRatePerDay = 0.1f;

        var seedType = CreateTestType();
        SetupTypeQuery(new List<SeedTypeDefinitionModel> { seedType });

        var seedId = Guid.NewGuid();
        var seed = new SeedModel
        {
            SeedId = seedId,
            GameServiceId = _testGameServiceId,
            SeedTypeCode = "guardian",
            GrowthPhase = "awakening",
            TotalGrowth = 20f,
            Status = SeedStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        SetupSeedQuery(new List<SeedModel> { seed }, 1);
        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        var growth = new SeedGrowthModel
        {
            SeedId = seedId,
            LastDecayedAt = null,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                // combat: inactive for 2 days → more decay
                { "combat.melee", new DomainGrowthEntry { Depth = 10f, LastActivityAt = DateTimeOffset.UtcNow.AddDays(-2), PeakDepth = 10f } },
                // magic: inactive for 0.5 days → less decay
                { "magic.fire", new DomainGrowthEntry { Depth = 10f, LastActivityAt = DateTimeOffset.UtcNow.AddHours(-12), PeakDepth = 10f } }
            }
        };
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(growth);

        SeedGrowthModel? savedGrowth = null;
        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SeedGrowthModel, StateOptions?, CancellationToken>((_, m, _, _) => savedGrowth = m)
            .ReturnsAsync("etag");
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        using var worker = CreateWorker();

        // Act
        await RunOneCycleAsync(worker);

        // Assert: combat should have decayed more than magic
        Assert.NotNull(savedGrowth);
        var combat = savedGrowth.Domains["combat.melee"].Depth;
        var magic = savedGrowth.Domains["magic.fire"].Depth;
        Assert.True(combat < magic,
            $"Combat ({combat:F3}) should have decayed more than magic ({magic:F3})");
    }

    #endregion

    #region Phase Regression Tests

    [Fact]
    public async Task Worker_PhaseRegression_PublishesRegressedDirection()
    {
        // Arrange: seed at "awakening" (requires 10), decay will drop total below 10
        _configuration.GrowthDecayEnabled = true;
        _configuration.GrowthDecayRatePerDay = 0.5f;

        var seedType = CreateTestType();
        SetupTypeQuery(new List<SeedTypeDefinitionModel> { seedType });

        var seedId = Guid.NewGuid();
        var seed = new SeedModel
        {
            SeedId = seedId,
            GameServiceId = _testGameServiceId,
            SeedTypeCode = "guardian",
            GrowthPhase = "awakening",
            TotalGrowth = 11f,
            Status = SeedStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        SetupSeedQuery(new List<SeedModel> { seed }, 1);
        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);

        // 50% daily decay, inactive for 2 days → 11 * 0.5^2 = 2.75 → drops below 10 threshold
        var growth = new SeedGrowthModel
        {
            SeedId = seedId,
            LastDecayedAt = null,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 11f, LastActivityAt = DateTimeOffset.UtcNow.AddDays(-2), PeakDepth = 11f } }
            }
        };
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(growth);

        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        using var worker = CreateWorker();

        // Act
        await RunOneCycleAsync(worker);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "seed.phase.changed",
            It.Is<SeedPhaseChangedEvent>(e =>
                e.SeedId == seedId &&
                e.PreviousPhase == "awakening" &&
                e.NewPhase == "nascent" &&
                e.Direction == PhaseChangeDirection.Regressed),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Pagination Tests

    [Fact]
    public async Task Worker_ProcessesPaginatedSeeds()
    {
        // Arrange: page size = 2, 3 seeds total → requires 2 pages
        _configuration.GrowthDecayEnabled = true;
        _configuration.GrowthDecayRatePerDay = 0.1f;
        _configuration.DefaultQueryPageSize = 2;

        var seedType = CreateTestType();
        SetupTypeQuery(new List<SeedTypeDefinitionModel> { seedType });

        var seed1 = CreateSeedForWorker(Guid.NewGuid());
        var seed2 = CreateSeedForWorker(Guid.NewGuid());
        var seed3 = CreateSeedForWorker(Guid.NewGuid());

        // Page 1: returns 2 seeds, total count = 3
        var page1Results = new List<SeedModel> { seed1, seed2 }
            .Select(s => new JsonQueryResult<SeedModel>($"seed:{s.SeedId}", s)).ToList();
        // Page 2: returns 1 seed
        var page2Results = new List<SeedModel> { seed3 }
            .Select(s => new JsonQueryResult<SeedModel>($"seed:{s.SeedId}", s)).ToList();

        var callCount = 0;
        _mockSeedQueryStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new JsonPagedResult<SeedModel>(page1Results, 3, 0, 2)
                    : new JsonPagedResult<SeedModel>(page2Results, 3, 2, 2);
            });

        // Set up each seed for decay processing
        foreach (var seed in new[] { seed1, seed2, seed3 })
        {
            _mockSeedStore.Setup(s => s.GetAsync($"seed:{seed.SeedId}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(seed);
            _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seed.SeedId}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SeedGrowthModel
                {
                    SeedId = seed.SeedId,
                    Domains = new Dictionary<string, DomainGrowthEntry>
                    {
                        { "combat", new DomainGrowthEntry { Depth = 5f, LastActivityAt = DateTimeOffset.UtcNow.AddDays(-1), PeakDepth = 5f } }
                    }
                });
        }

        _mockGrowthStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockSeedStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        using var worker = CreateWorker();

        // Act
        await RunOneCycleAsync(worker);

        // Assert: growth was saved for all 3 seeds (2 pages processed)
        _mockGrowthStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith("growth:")),
            It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    #endregion

    #region Helpers

    private SeedModel CreateSeedForWorker(Guid seedId) => new()
    {
        SeedId = seedId,
        GameServiceId = _testGameServiceId,
        SeedTypeCode = "guardian",
        GrowthPhase = "nascent",
        TotalGrowth = 5f,
        Status = SeedStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
    };

    private void SetupTypeQuery(List<SeedTypeDefinitionModel> types)
    {
        var results = types.Select(t =>
            new JsonQueryResult<SeedTypeDefinitionModel>($"type:{t.GameServiceId}:{t.SeedTypeCode}", t)).ToList();

        _mockTypeQueryStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<SeedTypeDefinitionModel>(results, results.Count, 0, 500));
    }

    private void SetupSeedQuery(List<SeedModel> seeds, long totalCount)
    {
        var results = seeds.Select(s =>
            new JsonQueryResult<SeedModel>($"seed:{s.SeedId}", s)).ToList();

        _mockSeedQueryStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<SeedModel>(results, totalCount, 0, 100));
    }

    #endregion
}
