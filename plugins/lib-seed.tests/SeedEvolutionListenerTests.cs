using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Seed.Tests;

/// <summary>
/// Unit tests for the ISeedEvolutionListener dispatch pattern.
/// Tests filtering, notification content, error isolation, and integration through SeedService.
/// </summary>
public class SeedEvolutionListenerTests : ServiceTestBase<SeedServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<SeedModel>> _mockSeedStore;
    private readonly Mock<IJsonQueryableStateStore<SeedModel>> _mockSeedQueryStore;
    private readonly Mock<IStateStore<SeedGrowthModel>> _mockGrowthStore;
    private readonly Mock<IStateStore<CapabilityManifestModel>> _mockCapabilitiesStore;
    private readonly Mock<IStateStore<SeedBondModel>> _mockBondStore;
    private readonly Mock<IStateStore<SeedTypeDefinitionModel>> _mockTypeStore;
    private readonly Mock<IJsonQueryableStateStore<SeedTypeDefinitionModel>> _mockTypeQueryStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<SeedService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IGameServiceClient> _mockGameServiceClient;

    private readonly Guid _testGameServiceId = Guid.NewGuid();
    private readonly Guid _testOwnerId = Guid.NewGuid();

    public SeedEvolutionListenerTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockSeedStore = new Mock<IStateStore<SeedModel>>();
        _mockSeedQueryStore = new Mock<IJsonQueryableStateStore<SeedModel>>();
        _mockGrowthStore = new Mock<IStateStore<SeedGrowthModel>>();
        _mockCapabilitiesStore = new Mock<IStateStore<CapabilityManifestModel>>();
        _mockBondStore = new Mock<IStateStore<SeedBondModel>>();
        _mockTypeStore = new Mock<IStateStore<SeedTypeDefinitionModel>>();
        _mockTypeQueryStore = new Mock<IJsonQueryableStateStore<SeedTypeDefinitionModel>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<SeedService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockGameServiceClient = new Mock<IGameServiceClient>();

        _mockStateStoreFactory
            .Setup(f => f.GetStore<SeedModel>(StateStoreDefinitions.Seed))
            .Returns(_mockSeedStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<SeedModel>(StateStoreDefinitions.Seed))
            .Returns(_mockSeedQueryStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<SeedGrowthModel>(StateStoreDefinitions.SeedGrowth))
            .Returns(_mockGrowthStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CapabilityManifestModel>(StateStoreDefinitions.SeedCapabilitiesCache))
            .Returns(_mockCapabilitiesStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<SeedBondModel>(StateStoreDefinitions.SeedBonds))
            .Returns(_mockBondStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions))
            .Returns(_mockTypeStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions))
            .Returns(_mockTypeQueryStore.Object);

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(r => r.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        Configuration.DefaultMaxSeedsPerOwner = 3;
        Configuration.MaxSeedTypesPerGameService = 50;
        Configuration.BondSharedGrowthMultiplier = 1.5f;
        Configuration.CapabilityRecomputeDebounceMs = 5000;
        Configuration.GrowthDecayEnabled = false;
        Configuration.GrowthDecayRatePerDay = 0.01f;
        Configuration.BondStrengthGrowthRate = 0.1f;
        Configuration.DefaultQueryPageSize = 100;
    }

    private SeedService CreateService(
        IEnumerable<ISeedEvolutionListener>? evolutionListeners = null) => new SeedService(
        _mockMessageBus.Object,
        _mockStateStoreFactory.Object,
        _mockLockProvider.Object,
        _mockLogger.Object,
        new NullTelemetryProvider(),
        Configuration,
        _mockEventConsumer.Object,
        _mockGameServiceClient.Object,
        evolutionListeners ?? Enumerable.Empty<ISeedEvolutionListener>());

    private SeedTypeDefinitionModel CreateTestSeedType(
        string seedTypeCode = "guardian",
        Guid? gameServiceId = null) => new SeedTypeDefinitionModel
        {
            SeedTypeCode = seedTypeCode,
            GameServiceId = gameServiceId ?? _testGameServiceId,
            DisplayName = "Guardian",
            Description = "A guardian seed type",
            MaxPerOwner = 3,
            AllowedOwnerTypes = new List<EntityType> { EntityType.Account, EntityType.Character },
            GrowthPhases = new List<GrowthPhaseDefinition>
            {
                new GrowthPhaseDefinition { PhaseCode = "nascent", DisplayName = "Nascent", MinTotalGrowth = 0 },
                new GrowthPhaseDefinition { PhaseCode = "awakening", DisplayName = "Awakening", MinTotalGrowth = 10 },
                new GrowthPhaseDefinition { PhaseCode = "mature", DisplayName = "Mature", MinTotalGrowth = 50 }
            },
            BondCardinality = 1,
            BondPermanent = false,
            CapabilityRules = new List<CapabilityRule>
            {
                new CapabilityRule { CapabilityCode = "combat.stance", Domain = "combat.melee", UnlockThreshold = 5f, FidelityFormula = "linear" }
            }
        };

    private SeedModel CreateTestSeed(
        Guid? seedId = null,
        Guid? ownerId = null,
        EntityType ownerType = EntityType.Character,
        string seedTypeCode = "guardian",
        Guid? gameServiceId = null,
        SeedStatus status = SeedStatus.Active) => new SeedModel
        {
            SeedId = seedId ?? Guid.NewGuid(),
            OwnerId = ownerId ?? _testOwnerId,
            OwnerType = ownerType,
            SeedTypeCode = seedTypeCode,
            GameServiceId = gameServiceId ?? _testGameServiceId,
            CreatedAt = DateTimeOffset.UtcNow,
            GrowthPhase = "nascent",
            TotalGrowth = 0f,
            DisplayName = "Test Seed",
            Status = status
        };

    private void SetupDefaultSaveReturns()
    {
        _mockSeedStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockGrowthStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SeedGrowthModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
    }

    #region Dispatch Filtering Tests

    [Fact]
    public async Task DispatchGrowthRecorded_ListenerInterestedInType_ReceivesNotification()
    {
        // Arrange
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        var seedType = CreateTestSeedType();

        var mockListener = new Mock<ISeedEvolutionListener>();
        mockListener.Setup(l => l.InterestedSeedTypes)
            .Returns(new HashSet<string> { "guardian" });

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        SetupDefaultSaveReturns();

        var service = CreateService(new[] { mockListener.Object });

        // Act
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 3f, Source = "test" },
            CancellationToken.None);

        // Assert
        mockListener.Verify(l => l.OnGrowthRecordedAsync(
            It.Is<SeedGrowthNotification>(n => n.SeedId == seedId && n.SeedTypeCode == "guardian"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchGrowthRecorded_ListenerNotInterestedInType_SkipsNotification()
    {
        // Arrange
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, seedTypeCode: "dungeon_core");
        var seedType = CreateTestSeedType(seedTypeCode: "dungeon_core");

        var mockListener = new Mock<ISeedEvolutionListener>();
        mockListener.Setup(l => l.InterestedSeedTypes)
            .Returns(new HashSet<string> { "guardian" });

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        SetupDefaultSaveReturns();

        var service = CreateService(new[] { mockListener.Object });

        // Act
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "dungeon.depth", Amount = 5f, Source = "test" },
            CancellationToken.None);

        // Assert
        mockListener.Verify(l => l.OnGrowthRecordedAsync(
            It.IsAny<SeedGrowthNotification>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DispatchGrowthRecorded_ListenerInterestedInAllTypes_ReceivesAll()
    {
        // Arrange
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId, seedTypeCode: "dungeon_core");
        var seedType = CreateTestSeedType(seedTypeCode: "dungeon_core");

        var mockListener = new Mock<ISeedEvolutionListener>();
        mockListener.Setup(l => l.InterestedSeedTypes)
            .Returns(new HashSet<string>()); // empty = wildcard

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        SetupDefaultSaveReturns();

        var service = CreateService(new[] { mockListener.Object });

        // Act
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "dungeon.depth", Amount = 2f, Source = "test" },
            CancellationToken.None);

        // Assert
        mockListener.Verify(l => l.OnGrowthRecordedAsync(
            It.Is<SeedGrowthNotification>(n => n.SeedTypeCode == "dungeon_core"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchGrowthRecorded_NoListeners_NoErrors()
    {
        // Arrange
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        var seedType = CreateTestSeedType();

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        SetupDefaultSaveReturns();

        var service = CreateService(); // no listeners

        // Act
        var (status, _) = await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 1f, Source = "test" },
            CancellationToken.None);

        // Assert - no exception, growth succeeds
        Assert.Equal(StatusCodes.OK, status);
    }

    #endregion

    #region Growth Notification Content Tests

    [Fact]
    public async Task GrowthNotification_ContainsAllDomainChanges()
    {
        // Arrange
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        var seedType = CreateTestSeedType();

        SeedGrowthNotification? capturedNotification = null;
        var mockListener = new Mock<ISeedEvolutionListener>();
        mockListener.Setup(l => l.InterestedSeedTypes).Returns(new HashSet<string>());
        mockListener.Setup(l => l.OnGrowthRecordedAsync(It.IsAny<SeedGrowthNotification>(), It.IsAny<CancellationToken>()))
            .Callback<SeedGrowthNotification, CancellationToken>((n, _) => capturedNotification = n)
            .Returns(Task.CompletedTask);

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        SetupDefaultSaveReturns();

        var service = CreateService(new[] { mockListener.Object });

        // Act - batch growth across 3 domains
        await service.RecordGrowthBatchAsync(new RecordGrowthBatchRequest
        {
            SeedId = seedId,
            Entries = new List<GrowthEntry>
            {
                new GrowthEntry { Domain = "combat.melee", Amount = 2f },
                new GrowthEntry { Domain = "magic.fire", Amount = 3f },
                new GrowthEntry { Domain = "social.diplomacy", Amount = 1f }
            },
            Source = "test-batch"
        }, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedNotification);
        Assert.Equal(seedId, capturedNotification.SeedId);
        Assert.Equal("guardian", capturedNotification.SeedTypeCode);
        Assert.Equal(_testOwnerId, capturedNotification.OwnerId);
        Assert.Equal(EntityType.Character, capturedNotification.OwnerType);
        Assert.Equal(3, capturedNotification.DomainChanges.Count);
        Assert.False(capturedNotification.CrossPollinated);
        Assert.Equal("test-batch", capturedNotification.Source);
        Assert.Equal(6f, capturedNotification.TotalGrowth);

        // Verify individual domain changes
        var melee = capturedNotification.DomainChanges.First(d => d.Domain == "combat.melee");
        Assert.Equal(0f, melee.PreviousDepth);
        Assert.Equal(2f, melee.NewDepth);

        var fire = capturedNotification.DomainChanges.First(d => d.Domain == "magic.fire");
        Assert.Equal(0f, fire.PreviousDepth);
        Assert.Equal(3f, fire.NewDepth);
    }

    [Fact]
    public async Task GrowthNotification_CrossPollinatedFlag_SetCorrectly()
    {
        // Arrange - test cross-pollination by setting up a sibling seed
        var seedId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        var siblingModel = CreateTestSeed(seedId: siblingId, status: SeedStatus.Dormant);
        var seedType = CreateTestSeedType();
        seedType.SameOwnerGrowthMultiplier = 0.5f;

        var capturedNotifications = new List<SeedGrowthNotification>();
        var mockListener = new Mock<ISeedEvolutionListener>();
        mockListener.Setup(l => l.InterestedSeedTypes).Returns(new HashSet<string>());
        mockListener.Setup(l => l.OnGrowthRecordedAsync(It.IsAny<SeedGrowthNotification>(), It.IsAny<CancellationToken>()))
            .Callback<SeedGrowthNotification, CancellationToken>((n, _) => capturedNotifications.Add(n))
            .Returns(Task.CompletedTask);

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockSeedStore.Setup(s => s.GetAsync($"seed:{siblingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(siblingModel);
        _mockGrowthStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        SetupDefaultSaveReturns();

        // Setup sibling query to return the sibling
        var siblingQueryResults = new List<JsonQueryResult<SeedModel>>
        {
            new JsonQueryResult<SeedModel>($"seed:{seedId}", seed),
            new JsonQueryResult<SeedModel>($"seed:{siblingId}", siblingModel)
        };
        _mockSeedQueryStore.Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<SeedModel>(siblingQueryResults, 2, 0, 100));

        var service = CreateService(new[] { mockListener.Object });

        // Act
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 4f, Source = "test" },
            CancellationToken.None);

        // Assert - should have two growth notifications: primary (not cross-pollinated) and sibling (cross-pollinated)
        Assert.Equal(2, capturedNotifications.Count);

        var primaryNotification = capturedNotifications.First(n => n.SeedId == seedId);
        Assert.False(primaryNotification.CrossPollinated);

        var crossPollinatedNotification = capturedNotifications.First(n => n.SeedId == siblingId);
        Assert.True(crossPollinatedNotification.CrossPollinated);
    }

    #endregion

    #region Phase Change Notification Tests

    [Fact]
    public async Task PhaseChanged_ListenerReceivesNotification()
    {
        // Arrange - seed at 8f growth, adding 3f should cross the 10f threshold to "awakening"
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        seed.GrowthPhase = "nascent";
        seed.TotalGrowth = 8f;
        var seedType = CreateTestSeedType();

        var existingGrowth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 8f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 8f } }
            }
        };

        SeedPhaseNotification? capturedNotification = null;
        var mockListener = new Mock<ISeedEvolutionListener>();
        mockListener.Setup(l => l.InterestedSeedTypes).Returns(new HashSet<string>());
        mockListener.Setup(l => l.OnPhaseChangedAsync(It.IsAny<SeedPhaseNotification>(), It.IsAny<CancellationToken>()))
            .Callback<SeedPhaseNotification, CancellationToken>((n, _) => capturedNotification = n)
            .Returns(Task.CompletedTask);
        mockListener.Setup(l => l.OnGrowthRecordedAsync(It.IsAny<SeedGrowthNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingGrowth);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        SetupDefaultSaveReturns();

        var service = CreateService(new[] { mockListener.Object });

        // Act
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 3f, Source = "test" },
            CancellationToken.None);

        // Assert
        Assert.NotNull(capturedNotification);
        Assert.Equal(seedId, capturedNotification.SeedId);
        Assert.Equal("nascent", capturedNotification.PreviousPhase);
        Assert.Equal("awakening", capturedNotification.NewPhase);
        Assert.Equal(11f, capturedNotification.TotalGrowth);
        Assert.True(capturedNotification.Progressed);
    }

    [Fact]
    public async Task PhaseChanged_NoTransition_ListenerNotCalled()
    {
        // Arrange - growth doesn't cross threshold
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        seed.GrowthPhase = "nascent";
        seed.TotalGrowth = 2f;
        var seedType = CreateTestSeedType();

        var existingGrowth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 2f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 2f } }
            }
        };

        var mockListener = new Mock<ISeedEvolutionListener>();
        mockListener.Setup(l => l.InterestedSeedTypes).Returns(new HashSet<string>());
        mockListener.Setup(l => l.OnGrowthRecordedAsync(It.IsAny<SeedGrowthNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingGrowth);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        SetupDefaultSaveReturns();

        var service = CreateService(new[] { mockListener.Object });

        // Act
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 1f, Source = "test" },
            CancellationToken.None);

        // Assert - growth listener called, but phase listener NOT called
        mockListener.Verify(l => l.OnGrowthRecordedAsync(
            It.IsAny<SeedGrowthNotification>(), It.IsAny<CancellationToken>()), Times.Once);
        mockListener.Verify(l => l.OnPhaseChangedAsync(
            It.IsAny<SeedPhaseNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Capability Notification Tests

    [Fact]
    public async Task CapabilityUpdated_ListenerReceivesManifest()
    {
        // Arrange
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        seed.TotalGrowth = 10f;
        var seedType = CreateTestSeedType();

        var growth = new SeedGrowthModel
        {
            SeedId = seedId,
            Domains = new Dictionary<string, DomainGrowthEntry>
            {
                { "combat.melee", new DomainGrowthEntry { Depth = 10f, LastActivityAt = DateTimeOffset.UtcNow, PeakDepth = 10f } }
            }
        };

        SeedCapabilityNotification? capturedNotification = null;
        var mockListener = new Mock<ISeedEvolutionListener>();
        mockListener.Setup(l => l.InterestedSeedTypes).Returns(new HashSet<string>());
        mockListener.Setup(l => l.OnCapabilitiesChangedAsync(It.IsAny<SeedCapabilityNotification>(), It.IsAny<CancellationToken>()))
            .Callback<SeedCapabilityNotification, CancellationToken>((n, _) => capturedNotification = n)
            .Returns(Task.CompletedTask);

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(growth);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        _mockCapabilitiesStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CapabilityManifestModel?)null); // cache miss triggers computation
        _mockCapabilitiesStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CapabilityManifestModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var service = CreateService(new[] { mockListener.Object });

        // Act - trigger capability computation
        await service.GetCapabilityManifestAsync(
            new GetCapabilityManifestRequest { SeedId = seedId },
            CancellationToken.None);

        // Assert
        Assert.NotNull(capturedNotification);
        Assert.Equal(seedId, capturedNotification.SeedId);
        Assert.Equal("guardian", capturedNotification.SeedTypeCode);
        Assert.Equal(1, capturedNotification.Version);
        Assert.Single(capturedNotification.Capabilities);

        var cap = capturedNotification.Capabilities[0];
        Assert.Equal("combat.stance", cap.CapabilityCode);
        Assert.Equal("combat.melee", cap.Domain);
        Assert.True(cap.Unlocked); // depth 10 >= threshold 5
    }

    #endregion

    #region Error Isolation Tests

    [Fact]
    public async Task ListenerThrows_OtherListenersStillCalled()
    {
        // Arrange
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        var seedType = CreateTestSeedType();

        var throwingListener = new Mock<ISeedEvolutionListener>();
        throwingListener.Setup(l => l.InterestedSeedTypes).Returns(new HashSet<string>());
        throwingListener.Setup(l => l.OnGrowthRecordedAsync(It.IsAny<SeedGrowthNotification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated listener failure"));

        var healthyListener = new Mock<ISeedEvolutionListener>();
        healthyListener.Setup(l => l.InterestedSeedTypes).Returns(new HashSet<string>());
        healthyListener.Setup(l => l.OnGrowthRecordedAsync(It.IsAny<SeedGrowthNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        SetupDefaultSaveReturns();

        var service = CreateService(new[] { throwingListener.Object, healthyListener.Object });

        // Act
        await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 2f, Source = "test" },
            CancellationToken.None);

        // Assert - healthy listener still called despite throwing listener
        healthyListener.Verify(l => l.OnGrowthRecordedAsync(
            It.IsAny<SeedGrowthNotification>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListenerThrows_CoreSeedLogicUnaffected()
    {
        // Arrange
        var seedId = Guid.NewGuid();
        var seed = CreateTestSeed(seedId: seedId);
        var seedType = CreateTestSeedType();

        var throwingListener = new Mock<ISeedEvolutionListener>();
        throwingListener.Setup(l => l.InterestedSeedTypes).Returns(new HashSet<string>());
        throwingListener.Setup(l => l.OnGrowthRecordedAsync(It.IsAny<SeedGrowthNotification>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Listener boom"));

        _mockSeedStore.Setup(s => s.GetAsync($"seed:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seed);
        _mockGrowthStore.Setup(s => s.GetAsync($"growth:{seedId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SeedGrowthModel?)null);
        _mockTypeStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedType);
        SetupDefaultSaveReturns();

        var service = CreateService(new[] { throwingListener.Object });

        // Act
        var (status, response) = await service.RecordGrowthAsync(
            new RecordGrowthRequest { SeedId = seedId, Domain = "combat.melee", Amount = 5f, Source = "test" },
            CancellationToken.None);

        // Assert - growth recording succeeds despite listener failure
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(5f, response.TotalGrowth);

        // Verify state was saved correctly
        _mockGrowthStore.Verify(s => s.SaveAsync(
            $"growth:{seedId}", It.IsAny<SeedGrowthModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void SeedService_ConstructorWithListeners_IsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<SeedService>();

    #endregion
}
