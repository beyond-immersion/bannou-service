using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Gardener;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Gardener.Tests;

/// <summary>
/// Comprehensive unit tests for the GardenerService covering void management,
/// scenario lifecycle, template CRUD, phase management, and bond features.
/// </summary>
public class GardenerServiceTests : ServiceTestBase<GardenerServiceConfiguration>
{
    #region Mock Declarations

    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<ILogger<GardenerService>> _mockLogger;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ISeedClient> _mockSeedClient;
    private readonly Mock<IGameSessionClient> _mockGameSessionClient;
    private readonly Mock<IServiceProvider> _mockServiceProvider;

    // State stores
    private readonly Mock<IStateStore<GardenInstanceModel>> _mockGardenStore;
    private readonly Mock<IStateStore<PoiModel>> _mockPoiStore;
    private readonly Mock<IJsonQueryableStateStore<ScenarioTemplateModel>> _mockTemplateStore;
    private readonly Mock<IStateStore<ScenarioInstanceModel>> _mockScenarioStore;
    private readonly Mock<IJsonQueryableStateStore<ScenarioHistoryModel>> _mockHistoryStore;
    private readonly Mock<IStateStore<DeploymentPhaseConfigModel>> _mockPhaseStore;
    private readonly Mock<ICacheableStateStore<GardenInstanceModel>> _mockCacheStore;

    // Test data
    private readonly Guid _testAccountId = Guid.NewGuid();
    private readonly Guid _testSeedId = Guid.NewGuid();
    private readonly Guid _testSessionId = Guid.NewGuid();
    private readonly Guid _testGameSessionId = Guid.NewGuid();
    private readonly Guid _testTemplateId = Guid.NewGuid();

    #endregion

    public GardenerServiceTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockLogger = new Mock<ILogger<GardenerService>>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockSeedClient = new Mock<ISeedClient>();
        _mockGameSessionClient = new Mock<IGameSessionClient>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        _mockGardenStore = new Mock<IStateStore<GardenInstanceModel>>();
        _mockPoiStore = new Mock<IStateStore<PoiModel>>();
        _mockTemplateStore = new Mock<IJsonQueryableStateStore<ScenarioTemplateModel>>();
        _mockScenarioStore = new Mock<IStateStore<ScenarioInstanceModel>>();
        _mockHistoryStore = new Mock<IJsonQueryableStateStore<ScenarioHistoryModel>>();
        _mockPhaseStore = new Mock<IStateStore<DeploymentPhaseConfigModel>>();
        _mockCacheStore = new Mock<ICacheableStateStore<GardenInstanceModel>>();

        // Wire up state store factory
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GardenInstanceModel>(StateStoreDefinitions.GardenerVoidInstances))
            .Returns(_mockGardenStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<PoiModel>(StateStoreDefinitions.GardenerPois))
            .Returns(_mockPoiStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<ScenarioTemplateModel>(StateStoreDefinitions.GardenerScenarioTemplates))
            .Returns(_mockTemplateStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ScenarioInstanceModel>(StateStoreDefinitions.GardenerScenarioInstances))
            .Returns(_mockScenarioStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<ScenarioHistoryModel>(StateStoreDefinitions.GardenerScenarioHistory))
            .Returns(_mockHistoryStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<DeploymentPhaseConfigModel>(StateStoreDefinitions.GardenerPhaseConfig))
            .Returns(_mockPhaseStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<GardenInstanceModel>(StateStoreDefinitions.GardenerVoidInstances))
            .Returns(_mockCacheStore.Object);

        // Default lock succeeds
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(r => r.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Default message bus succeeds (3-arg overload used by service code)
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default game session creation
        _mockGameSessionClient
            .Setup(c => c.CreateGameSessionAsync(
                It.IsAny<CreateGameSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameSessionResponse { SessionId = _testGameSessionId });

        // Default configuration
        Configuration.SeedTypeCode = "guardian";
        Configuration.DefaultPhase = DeploymentPhase.Alpha;
        Configuration.MaxConcurrentScenariosGlobal = 100;
        Configuration.GrowthAwardMultiplier = 1.0f;
        Configuration.ScenarioTimeoutMinutes = 60;
        Configuration.AbandonDetectionMinutes = 10;
        Configuration.BondSharedVoidEnabled = true;
        Configuration.BondScenarioPriority = 1.5f;
        Configuration.MaxActivePoisPerVoid = 5;
        Configuration.PoiDefaultTtlMinutes = 30;
        Configuration.PoiSpawnRadiusMin = 10f;
        Configuration.PoiSpawnRadiusMax = 50f;
        Configuration.MinPoiSpacing = 5f;
        Configuration.AffinityWeight = 0.3f;
        Configuration.DiversityWeight = 0.2f;
        Configuration.NarrativeWeight = 0.3f;
        Configuration.RandomWeight = 0.2f;
        Configuration.RecentScenarioCooldownMinutes = 5;
        Configuration.VoidTickIntervalMs = 5000;
        Configuration.ScenarioLifecycleWorkerIntervalSeconds = 30;
        Configuration.BackgroundServiceStartupDelaySeconds = 5;
    }

    #region Service Creation Helpers

    private GardenerService CreateService() => new(
        _mockMessageBus.Object,
        _mockStateStoreFactory.Object,
        _mockLogger.Object,
        Configuration,
        _mockLockProvider.Object,
        _mockEventConsumer.Object,
        _mockSeedClient.Object,
        _mockGameSessionClient.Object,
        _mockServiceProvider.Object);

    private SeedResponse CreateTestSeedResponse(
        SeedStatus status = SeedStatus.Active, string? growthPhase = null) => new()
    {
        SeedId = _testSeedId,
        OwnerId = _testAccountId,
        OwnerType = "account",
        SeedTypeCode = "guardian",
        GrowthPhase = growthPhase ?? "nascent",
        Status = status,
        BondId = null,
        DisplayName = "Test Guardian",
        GameServiceId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
    };

    private GardenInstanceModel CreateTestGarden(Guid? accountId = null) => new()
    {
        GardenInstanceId = Guid.NewGuid(),
        SeedId = _testSeedId,
        AccountId = accountId ?? _testAccountId,
        SessionId = _testSessionId,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        Phase = DeploymentPhase.Alpha,
        CachedGrowthPhase = "nascent",
        NeedsReEvaluation = false
    };

    private ScenarioTemplateModel CreateTestTemplate(
        string code = "combat-01", TemplateStatus status = TemplateStatus.Active) => new()
    {
        ScenarioTemplateId = _testTemplateId,
        Code = code,
        DisplayName = "Test Scenario",
        Description = "A test scenario",
        Category = ScenarioCategory.Combat,
        ConnectivityMode = ConnectivityMode.Isolated,
        DomainWeights = new List<DomainWeightModel>
        {
            new() { Domain = "combat.melee", Weight = 1.0f },
            new() { Domain = "combat.tactics", Weight = 0.5f }
        },
        AllowedPhases = new List<DeploymentPhase>(),
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-7)
    };

    private ScenarioInstanceModel CreateTestScenario(
        ScenarioStatus status = ScenarioStatus.Active) => new()
    {
        ScenarioInstanceId = Guid.NewGuid(),
        ScenarioTemplateId = _testTemplateId,
        GameSessionId = _testGameSessionId,
        ConnectivityMode = ConnectivityMode.Isolated,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        ChainDepth = 0,
        Participants = new List<ScenarioParticipantModel>
        {
            new()
            {
                SeedId = _testSeedId,
                AccountId = _testAccountId,
                SessionId = _testSessionId,
                JoinedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                Role = "primary"
            }
        }
    };

    private PoiModel CreateTestPoi(Guid gardenInstanceId, PoiStatus status = PoiStatus.Active) => new()
    {
        PoiId = Guid.NewGuid(),
        GardenInstanceId = gardenInstanceId,
        ScenarioTemplateId = _testTemplateId,
        PoiType = PoiType.Visual,
        TriggerMode = TriggerMode.Interaction,
        TriggerRadius = 5.0f,
        Position = new Vec3Model { X = 10, Y = 0, Z = 10 },
        Status = status,
        SpawnedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(25)
    };

    private void SetupSeedsForAccount(params SeedResponse[] seeds)
    {
        _mockSeedClient
            .Setup(c => c.GetSeedsByOwnerAsync(
                It.IsAny<GetSeedsByOwnerRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListSeedsResponse { Seeds = seeds.ToList() });
    }

    private void SetupPhaseConfig(DeploymentPhase phase = DeploymentPhase.Alpha)
    {
        _mockPhaseStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeploymentPhaseConfigModel
            {
                CurrentPhase = phase,
                MaxConcurrentScenariosGlobal = 100,
                UpdatedAt = DateTimeOffset.UtcNow
            });
    }

    #endregion

    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void GardenerService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<GardenerService>();

    [Fact]
    public void GardenerServiceConfiguration_CanBeInstantiated()
    {
        var config = new GardenerServiceConfiguration();
        Assert.NotNull(config);
    }

    #endregion

    #region EnterVoidAsync

    [Fact]
    public async Task EnterVoidAsync_ValidRequest_CreatesGardenAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        SetupSeedsForAccount(CreateTestSeedResponse());
        SetupPhaseConfig();

        GardenInstanceModel? savedGarden = null;
        _mockGardenStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GardenInstanceModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GardenInstanceModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedGarden = model)
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.EnterVoidAsync(
            new EnterVoidRequest { AccountId = _testAccountId, SessionId = _testSessionId },
            CancellationToken.None);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(_testAccountId, response.AccountId);
        Assert.Equal(_testSeedId, response.SeedId);

        // Assert - Saved garden
        Assert.NotNull(savedGarden);
        Assert.Equal(_testAccountId, savedGarden.AccountId);
        Assert.Equal(_testSeedId, savedGarden.SeedId);
        Assert.Equal(_testSessionId, savedGarden.SessionId);
        Assert.True(savedGarden.NeedsReEvaluation);

        // Assert - Tracking set updated
        _mockCacheStore.Verify(c => c.AddToSetAsync<Guid>(
            GardenerService.ActiveVoidsTrackingKey, _testAccountId,
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "gardener.void.entered",
            It.Is<GardenerVoidEnteredEvent>(e =>
                e.AccountId == _testAccountId && e.SeedId == _testSeedId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnterVoidAsync_AlreadyActive_ReturnsConflict()
    {
        var service = CreateService();
        _mockGardenStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestGarden());

        var (status, response) = await service.EnterVoidAsync(
            new EnterVoidRequest { AccountId = _testAccountId, SessionId = _testSessionId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task EnterVoidAsync_NoActiveSeed_ReturnsNotFound()
    {
        var service = CreateService();
        SetupSeedsForAccount(CreateTestSeedResponse(status: SeedStatus.Dormant));
        SetupPhaseConfig();

        var (status, response) = await service.EnterVoidAsync(
            new EnterVoidRequest { AccountId = _testAccountId, SessionId = _testSessionId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetVoidStateAsync

    [Fact]
    public async Task GetVoidStateAsync_ExistingGarden_ReturnsState()
    {
        var service = CreateService();
        var garden = CreateTestGarden();
        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden);

        var (status, response) = await service.GetVoidStateAsync(
            new GetVoidStateRequest { AccountId = _testAccountId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(garden.GardenInstanceId, response.VoidInstanceId);
    }

    [Fact]
    public async Task GetVoidStateAsync_NoGarden_ReturnsNotFound()
    {
        var service = CreateService();

        var (status, response) = await service.GetVoidStateAsync(
            new GetVoidStateRequest { AccountId = _testAccountId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region LeaveVoidAsync

    [Fact]
    public async Task LeaveVoidAsync_ValidRequest_CleansUpAndPublishesEvent()
    {
        var service = CreateService();
        var garden = CreateTestGarden();
        var poiId = Guid.NewGuid();
        garden.ActivePoiIds.Add(poiId);

        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden);

        var (status, response) = await service.LeaveVoidAsync(
            new LeaveVoidRequest { AccountId = _testAccountId },
            CancellationToken.None);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(_testAccountId, response.AccountId);
        Assert.True(response.SessionDurationSeconds > 0);

        // Assert - POI cleaned up
        _mockPoiStore.Verify(s => s.DeleteAsync(
            $"poi:{garden.GardenInstanceId}:{poiId}", It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Garden deleted
        _mockGardenStore.Verify(s => s.DeleteAsync(
            $"void:{_testAccountId}", It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Tracking set updated
        _mockCacheStore.Verify(c => c.RemoveFromSetAsync<Guid>(
            GardenerService.ActiveVoidsTrackingKey, _testAccountId,
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "gardener.void.left",
            It.Is<GardenerVoidLeftEvent>(e => e.AccountId == _testAccountId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveVoidAsync_NoGarden_ReturnsNotFound()
    {
        var service = CreateService();

        var (status, response) = await service.LeaveVoidAsync(
            new LeaveVoidRequest { AccountId = _testAccountId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task LeaveVoidAsync_LockFailed_ReturnsConflict()
    {
        var service = CreateService();
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(r => r.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        var (status, response) = await service.LeaveVoidAsync(
            new LeaveVoidRequest { AccountId = _testAccountId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region UpdatePositionAsync

    [Fact]
    public async Task UpdatePositionAsync_ValidRequest_UpdatesPositionAndDriftMetrics()
    {
        var service = CreateService();
        var garden = CreateTestGarden();
        garden.Position = new Vec3Model { X = 0, Y = 0, Z = 0 };

        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden);

        GardenInstanceModel? savedGarden = null;
        _mockGardenStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GardenInstanceModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GardenInstanceModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedGarden = model)
            .ReturnsAsync("etag");

        var (status, response) = await service.UpdatePositionAsync(
            new UpdatePositionRequest
            {
                AccountId = _testAccountId,
                Position = new Vec3 { X = 10, Y = 0, Z = 10 }
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Acknowledged);

        // Verify drift metrics were accumulated
        Assert.NotNull(savedGarden);
        Assert.True(savedGarden.DriftMetrics.TotalDistance > 0);
        Assert.Equal(10f, savedGarden.DriftMetrics.DirectionalBiasX);
        Assert.Equal(10f, savedGarden.DriftMetrics.DirectionalBiasZ);
    }

    [Fact]
    public async Task UpdatePositionAsync_ProximityTrigger_TriggersPoiAndPublishesEvent()
    {
        var service = CreateService();
        var garden = CreateTestGarden();
        var poiId = Guid.NewGuid();
        garden.ActivePoiIds.Add(poiId);
        // Position the garden close to the POI
        garden.Position = new Vec3Model { X = 8, Y = 0, Z = 8 };

        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden);

        var poi = new PoiModel
        {
            PoiId = poiId,
            GardenInstanceId = garden.GardenInstanceId,
            ScenarioTemplateId = _testTemplateId,
            PoiType = PoiType.Visual,
            TriggerMode = TriggerMode.Proximity,
            TriggerRadius = 5.0f,
            Position = new Vec3Model { X = 10, Y = 0, Z = 10 },
            Status = PoiStatus.Active,
            SpawnedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(25)
        };

        _mockPoiStore
            .Setup(s => s.GetAsync($"poi:{garden.GardenInstanceId}:{poiId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(poi);

        var (status, response) = await service.UpdatePositionAsync(
            new UpdatePositionRequest
            {
                AccountId = _testAccountId,
                Position = new Vec3 { X = 10, Y = 0, Z = 10 }
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.TriggeredPois);
        Assert.Single(response.TriggeredPois);

        // Verify POI status updated
        _mockPoiStore.Verify(s => s.SaveAsync(
            $"poi:{garden.GardenInstanceId}:{poiId}", It.IsAny<PoiModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "gardener.poi.entered",
            It.Is<GardenerPoiEnteredEvent>(e => e.PoiId == poiId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdatePositionAsync_NoGarden_ReturnsNotFound()
    {
        var service = CreateService();

        var (status, _) = await service.UpdatePositionAsync(
            new UpdatePositionRequest
            {
                AccountId = _testAccountId,
                Position = new Vec3 { X = 1, Y = 0, Z = 1 }
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region InteractWithPoiAsync

    [Fact]
    public async Task InteractWithPoiAsync_PromptedMode_ReturnsPrompt()
    {
        var service = CreateService();
        var garden = CreateTestGarden();
        var poiId = Guid.NewGuid();

        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden);

        var poi = CreateTestPoi(garden.GardenInstanceId);
        poi.PoiId = poiId;
        poi.TriggerMode = TriggerMode.Prompted;

        _mockPoiStore
            .Setup(s => s.GetAsync($"poi:{garden.GardenInstanceId}:{poiId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(poi);

        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{_testTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var (status, response) = await service.InteractWithPoiAsync(
            new InteractWithPoiRequest { AccountId = _testAccountId, PoiId = poiId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(GardenerService.PoiInteractionResults.ScenarioPrompt, response.Result);
        Assert.NotNull(response.PromptText);
        Assert.NotNull(response.PromptChoices);
    }

    [Fact]
    public async Task InteractWithPoiAsync_InteractionMode_ReturnsScenarioEnter()
    {
        var service = CreateService();
        var garden = CreateTestGarden();
        var poiId = Guid.NewGuid();

        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden);

        var poi = CreateTestPoi(garden.GardenInstanceId);
        poi.PoiId = poiId;
        poi.TriggerMode = TriggerMode.Interaction;

        _mockPoiStore
            .Setup(s => s.GetAsync($"poi:{garden.GardenInstanceId}:{poiId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(poi);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{_testTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate());

        var (status, response) = await service.InteractWithPoiAsync(
            new InteractWithPoiRequest { AccountId = _testAccountId, PoiId = poiId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(GardenerService.PoiInteractionResults.ScenarioEnter, response.Result);
    }

    [Fact]
    public async Task InteractWithPoiAsync_NonActivePoi_ReturnsBadRequest()
    {
        var service = CreateService();
        var garden = CreateTestGarden();
        var poiId = Guid.NewGuid();

        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden);

        var poi = CreateTestPoi(garden.GardenInstanceId, status: PoiStatus.Entered);
        poi.PoiId = poiId;
        _mockPoiStore
            .Setup(s => s.GetAsync($"poi:{garden.GardenInstanceId}:{poiId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(poi);

        var (status, _) = await service.InteractWithPoiAsync(
            new InteractWithPoiRequest { AccountId = _testAccountId, PoiId = poiId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region DeclinePoiAsync

    [Fact]
    public async Task DeclinePoiAsync_ActivePoi_DeclinesAndUpdatesHistory()
    {
        var service = CreateService();
        var garden = CreateTestGarden();
        var poiId = Guid.NewGuid();

        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden);

        var poi = CreateTestPoi(garden.GardenInstanceId);
        poi.PoiId = poiId;
        _mockPoiStore
            .Setup(s => s.GetAsync($"poi:{garden.GardenInstanceId}:{poiId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(poi);

        PoiModel? savedPoi = null;
        _mockPoiStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PoiModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, PoiModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedPoi = model)
            .ReturnsAsync("etag");

        GardenInstanceModel? savedGarden = null;
        _mockGardenStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GardenInstanceModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GardenInstanceModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedGarden = model)
            .ReturnsAsync("etag");

        var (status, response) = await service.DeclinePoiAsync(
            new DeclinePoiRequest { AccountId = _testAccountId, PoiId = poiId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Acknowledged);

        // POI marked as declined
        Assert.NotNull(savedPoi);
        Assert.Equal(PoiStatus.Declined, savedPoi.Status);

        // Scenario history updated for diversity scoring
        Assert.NotNull(savedGarden);
        Assert.Contains(_testTemplateId, savedGarden.ScenarioHistory);
        Assert.True(savedGarden.NeedsReEvaluation);

        // Event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "gardener.poi.declined",
            It.Is<GardenerPoiDeclinedEvent>(e => e.PoiId == poiId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region EnterScenarioAsync

    [Fact]
    public async Task EnterScenarioAsync_ValidRequest_CreatesScenarioWithGameSession()
    {
        var service = CreateService();
        var garden = CreateTestGarden();
        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden);

        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{_testTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        SetupPhaseConfig();

        ScenarioInstanceModel? savedScenario = null;
        _mockScenarioStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ScenarioInstanceModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ScenarioInstanceModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedScenario = model)
            .ReturnsAsync("etag");

        var (status, response) = await service.EnterScenarioAsync(
            new EnterScenarioRequest
            {
                AccountId = _testAccountId,
                ScenarioTemplateId = _testTemplateId
            },
            CancellationToken.None);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(ScenarioStatus.Active, response.Status);
        Assert.Equal(_testGameSessionId, response.GameSessionId);

        // Assert - Saved scenario
        Assert.NotNull(savedScenario);
        Assert.Equal(_testTemplateId, savedScenario.ScenarioTemplateId);
        Assert.Equal(ScenarioStatus.Active, savedScenario.Status);
        Assert.Single(savedScenario.Participants);
        Assert.Equal(_testAccountId, savedScenario.Participants[0].AccountId);

        // Assert - Tracking sets updated
        _mockCacheStore.Verify(c => c.RemoveFromSetAsync<Guid>(
            GardenerService.ActiveVoidsTrackingKey, _testAccountId,
            It.IsAny<CancellationToken>()), Times.Once);
        _mockCacheStore.Verify(c => c.AddToSetAsync<Guid>(
            GardenerService.ActiveScenariosTrackingKey, _testAccountId,
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Garden cleaned up
        _mockGardenStore.Verify(s => s.DeleteAsync(
            $"void:{_testAccountId}", It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "gardener.scenario.started",
            It.Is<GardenerScenarioStartedEvent>(e =>
                e.AccountId == _testAccountId &&
                e.ScenarioTemplateId == _testTemplateId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnterScenarioAsync_NoGarden_ReturnsBadRequest()
    {
        var service = CreateService();

        var (status, _) = await service.EnterScenarioAsync(
            new EnterScenarioRequest
            {
                AccountId = _testAccountId,
                ScenarioTemplateId = _testTemplateId
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task EnterScenarioAsync_ActiveScenarioExists_ReturnsConflict()
    {
        var service = CreateService();
        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestGarden());
        _mockScenarioStore
            .Setup(s => s.GetAsync($"scenario:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestScenario());

        var (status, _) = await service.EnterScenarioAsync(
            new EnterScenarioRequest
            {
                AccountId = _testAccountId,
                ScenarioTemplateId = _testTemplateId
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task EnterScenarioAsync_TemplateNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestGarden());
        SetupPhaseConfig();

        var (status, _) = await service.EnterScenarioAsync(
            new EnterScenarioRequest
            {
                AccountId = _testAccountId,
                ScenarioTemplateId = _testTemplateId
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task EnterScenarioAsync_InactiveTemplate_ReturnsBadRequest()
    {
        var service = CreateService();
        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestGarden());
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{_testTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate(status: TemplateStatus.Deprecated));
        SetupPhaseConfig();

        var (status, _) = await service.EnterScenarioAsync(
            new EnterScenarioRequest
            {
                AccountId = _testAccountId,
                ScenarioTemplateId = _testTemplateId
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task EnterScenarioAsync_PhaseGated_ReturnsBadRequest()
    {
        var service = CreateService();
        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestGarden());

        var template = CreateTestTemplate();
        template.AllowedPhases = new List<DeploymentPhase> { DeploymentPhase.Release };
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{_testTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        SetupPhaseConfig(DeploymentPhase.Alpha);

        var (status, _) = await service.EnterScenarioAsync(
            new EnterScenarioRequest
            {
                AccountId = _testAccountId,
                ScenarioTemplateId = _testTemplateId
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region CompleteScenarioAsync

    [Fact]
    public async Task CompleteScenarioAsync_ValidRequest_AwardsGrowthAndPublishesEvent()
    {
        var service = CreateService();
        var scenario = CreateTestScenario();
        var scenarioId = scenario.ScenarioInstanceId;

        _mockScenarioStore
            .Setup(s => s.GetAsync($"scenario:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scenario);

        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{_testTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        ScenarioHistoryModel? savedHistory = null;
        _mockHistoryStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ScenarioHistoryModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ScenarioHistoryModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedHistory = model)
            .ReturnsAsync("etag");

        var (status, response) = await service.CompleteScenarioAsync(
            new CompleteScenarioRequest
            {
                AccountId = _testAccountId,
                ScenarioInstanceId = scenarioId
            },
            CancellationToken.None);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(scenarioId, response.ScenarioInstanceId);
        Assert.True(response.ReturnToVoid);
        Assert.NotEmpty(response.GrowthAwarded);
        Assert.True(response.GrowthAwarded.ContainsKey("combat.melee"));

        // Assert - Growth recorded via seed client
        _mockSeedClient.Verify(c => c.RecordGrowthBatchAsync(
            It.Is<RecordGrowthBatchRequest>(r =>
                r.SeedId == _testSeedId && r.Source == "gardener"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - History written
        Assert.NotNull(savedHistory);
        Assert.Equal(ScenarioStatus.Completed, savedHistory.Status);

        // Assert - Scenario cleaned up from Redis
        _mockScenarioStore.Verify(s => s.DeleteAsync(
            $"scenario:{_testAccountId}", It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Tracking set updated
        _mockCacheStore.Verify(c => c.RemoveFromSetAsync<Guid>(
            GardenerService.ActiveScenariosTrackingKey, _testAccountId,
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "gardener.scenario.completed",
            It.Is<GardenerScenarioCompletedEvent>(e =>
                e.ScenarioInstanceId == scenarioId &&
                e.AccountId == _testAccountId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteScenarioAsync_ScenarioIdMismatch_ReturnsBadRequest()
    {
        var service = CreateService();
        _mockScenarioStore
            .Setup(s => s.GetAsync($"scenario:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestScenario());

        var (status, _) = await service.CompleteScenarioAsync(
            new CompleteScenarioRequest
            {
                AccountId = _testAccountId,
                ScenarioInstanceId = Guid.NewGuid() // Different ID
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task CompleteScenarioAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();

        var (status, _) = await service.CompleteScenarioAsync(
            new CompleteScenarioRequest
            {
                AccountId = _testAccountId,
                ScenarioInstanceId = Guid.NewGuid()
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region AbandonScenarioAsync

    [Fact]
    public async Task AbandonScenarioAsync_ValidRequest_AwardsPartialGrowthAndPublishesEvent()
    {
        var service = CreateService();
        var scenario = CreateTestScenario();
        var scenarioId = scenario.ScenarioInstanceId;

        _mockScenarioStore
            .Setup(s => s.GetAsync($"scenario:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(scenario);

        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{_testTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var (status, response) = await service.AbandonScenarioAsync(
            new AbandonScenarioRequest
            {
                AccountId = _testAccountId,
                ScenarioInstanceId = scenarioId
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(scenarioId, response.ScenarioInstanceId);

        // Partial growth should be less than full completion
        if (response.PartialGrowthAwarded.Count > 0)
        {
            foreach (var amount in response.PartialGrowthAwarded.Values)
            {
                Assert.True(amount >= 0, "Partial growth should not be negative");
            }
        }

        // Assert - Event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "gardener.scenario.abandoned",
            It.Is<GardenerScenarioAbandonedEvent>(e =>
                e.ScenarioInstanceId == scenarioId &&
                e.AccountId == _testAccountId),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Tracking set updated
        _mockCacheStore.Verify(c => c.RemoveFromSetAsync<Guid>(
            GardenerService.ActiveScenariosTrackingKey, _testAccountId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ChainScenarioAsync

    [Fact]
    public async Task ChainScenarioAsync_ValidChain_CompletesCurrentAndCreatesNew()
    {
        var service = CreateService();
        var currentScenario = CreateTestScenario();
        var currentScenarioId = currentScenario.ScenarioInstanceId;

        _mockScenarioStore
            .Setup(s => s.GetAsync($"scenario:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentScenario);

        var currentTemplate = CreateTestTemplate(code: "combat-01");
        currentTemplate.Chaining = new ScenarioChainingModel
        {
            LeadsTo = new List<string> { "combat-02" },
            MaxChainDepth = 3
        };
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{_testTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentTemplate);

        var targetTemplateId = Guid.NewGuid();
        var targetTemplate = CreateTestTemplate(code: "combat-02");
        targetTemplate.ScenarioTemplateId = targetTemplateId;
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{targetTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetTemplate);

        ScenarioInstanceModel? savedScenario = null;
        _mockScenarioStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ScenarioInstanceModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ScenarioInstanceModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedScenario = model)
            .ReturnsAsync("etag");

        var (status, response) = await service.ChainScenarioAsync(
            new ChainScenarioRequest
            {
                AccountId = _testAccountId,
                CurrentScenarioInstanceId = currentScenarioId,
                TargetTemplateId = targetTemplateId
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.ChainDepth);
        Assert.Equal(currentScenarioId, response.ChainedFrom);

        // Assert - Event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "gardener.scenario.chained",
            It.Is<GardenerScenarioChainedEvent>(e =>
                e.PreviousScenarioInstanceId == currentScenarioId &&
                e.ChainDepth == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChainScenarioAsync_InvalidChainTarget_ReturnsBadRequest()
    {
        var service = CreateService();
        var currentScenario = CreateTestScenario();

        _mockScenarioStore
            .Setup(s => s.GetAsync($"scenario:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentScenario);

        var currentTemplate = CreateTestTemplate();
        currentTemplate.Chaining = new ScenarioChainingModel
        {
            LeadsTo = new List<string> { "combat-02" },
            MaxChainDepth = 3
        };
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{_testTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentTemplate);

        var targetTemplateId = Guid.NewGuid();
        var targetTemplate = CreateTestTemplate(code: "social-01"); // Not in LeadsTo
        targetTemplate.ScenarioTemplateId = targetTemplateId;
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{targetTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetTemplate);

        var (status, _) = await service.ChainScenarioAsync(
            new ChainScenarioRequest
            {
                AccountId = _testAccountId,
                CurrentScenarioInstanceId = currentScenario.ScenarioInstanceId,
                TargetTemplateId = targetTemplateId
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task ChainScenarioAsync_MaxChainDepth_ReturnsBadRequest()
    {
        var service = CreateService();
        var currentScenario = CreateTestScenario();
        currentScenario.ChainDepth = 2;

        _mockScenarioStore
            .Setup(s => s.GetAsync($"scenario:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentScenario);

        var currentTemplate = CreateTestTemplate(code: "combat-01");
        currentTemplate.Chaining = new ScenarioChainingModel
        {
            LeadsTo = new List<string> { "combat-02" },
            MaxChainDepth = 3 // depth 2 + 1 >= 3
        };
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{_testTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentTemplate);

        var targetTemplateId = Guid.NewGuid();
        var targetTemplate = CreateTestTemplate(code: "combat-02");
        targetTemplate.ScenarioTemplateId = targetTemplateId;
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{targetTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetTemplate);

        var (status, _) = await service.ChainScenarioAsync(
            new ChainScenarioRequest
            {
                AccountId = _testAccountId,
                CurrentScenarioInstanceId = currentScenario.ScenarioInstanceId,
                TargetTemplateId = targetTemplateId
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region Template Management

    [Fact]
    public async Task CreateTemplateAsync_ValidRequest_SavesAndReturnsTemplate()
    {
        var service = CreateService();

        // No existing template with same code
        _mockTemplateStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<List<QueryCondition>>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ScenarioTemplateModel>(
                new List<JsonQueryResult<ScenarioTemplateModel>>(),
                0, 0, 1));

        ScenarioTemplateModel? savedTemplate = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ScenarioTemplateModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ScenarioTemplateModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedTemplate = model)
            .ReturnsAsync("etag");

        var (status, response) = await service.CreateTemplateAsync(
            new CreateTemplateRequest
            {
                Code = "combat-test-01",
                DisplayName = "Test Combat Scenario",
                Description = "A test scenario",
                Category = ScenarioCategory.Combat,
                ConnectivityMode = ConnectivityMode.Isolated,
                DomainWeights = new List<DomainWeight>
                {
                    new() { Domain = "combat.melee", Weight = 1.0f }
                }
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("combat-test-01", response.Code);
        Assert.Equal(TemplateStatus.Active, response.Status);

        Assert.NotNull(savedTemplate);
        Assert.Equal("combat-test-01", savedTemplate.Code);
        Assert.Single(savedTemplate.DomainWeights);
    }

    [Fact]
    public async Task CreateTemplateAsync_DuplicateCode_ReturnsConflict()
    {
        var service = CreateService();

        _mockTemplateStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<List<QueryCondition>>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ScenarioTemplateModel>(
                new List<JsonQueryResult<ScenarioTemplateModel>>
                {
                    new("template:existing", CreateTestTemplate())
                },
                1, 0, 1));

        var (status, _) = await service.CreateTemplateAsync(
            new CreateTemplateRequest
            {
                Code = "combat-01",
                DisplayName = "Duplicate",
                Category = ScenarioCategory.Combat,
                ConnectivityMode = ConnectivityMode.Isolated,
                DomainWeights = new List<DomainWeight>
                {
                    new() { Domain = "combat.melee", Weight = 1.0f }
                }
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task GetTemplateAsync_Exists_ReturnsTemplate()
    {
        var service = CreateService();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{_testTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestTemplate());

        var (status, response) = await service.GetTemplateAsync(
            new GetTemplateRequest { ScenarioTemplateId = _testTemplateId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("combat-01", response.Code);
    }

    [Fact]
    public async Task GetTemplateAsync_NotFound_ReturnsNotFound()
    {
        var service = CreateService();

        var (status, _) = await service.GetTemplateAsync(
            new GetTemplateRequest { ScenarioTemplateId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeprecateTemplateAsync_ActiveTemplate_Deprecates()
    {
        var service = CreateService();
        var template = CreateTestTemplate();
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{_testTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        ScenarioTemplateModel? savedTemplate = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ScenarioTemplateModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ScenarioTemplateModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedTemplate = model)
            .ReturnsAsync("etag");

        var (status, response) = await service.DeprecateTemplateAsync(
            new DeprecateTemplateRequest { ScenarioTemplateId = _testTemplateId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TemplateStatus.Deprecated, response.Status);
        Assert.NotNull(savedTemplate);
        Assert.Equal(TemplateStatus.Deprecated, savedTemplate.Status);
    }

    #endregion

    #region Phase Management

    [Fact]
    public async Task GetPhaseConfigAsync_NoExisting_CreatesDefaultConfig()
    {
        var service = CreateService();

        // Phase store returns null (no config exists)
        DeploymentPhaseConfigModel? savedConfig = null;
        _mockPhaseStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<DeploymentPhaseConfigModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, DeploymentPhaseConfigModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedConfig = model)
            .ReturnsAsync("etag");

        var (status, response) = await service.GetPhaseConfigAsync(
            new GetPhaseConfigRequest(),
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(DeploymentPhase.Alpha, response.CurrentPhase);

        // Default config was saved
        Assert.NotNull(savedConfig);
        Assert.Equal(DeploymentPhase.Alpha, savedConfig.CurrentPhase);
    }

    [Fact]
    public async Task UpdatePhaseConfigAsync_PhaseChange_PublishesEvent()
    {
        var service = CreateService();
        SetupPhaseConfig(DeploymentPhase.Alpha);

        var (status, response) = await service.UpdatePhaseConfigAsync(
            new UpdatePhaseConfigRequest { CurrentPhase = DeploymentPhase.Beta },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(DeploymentPhase.Beta, response.CurrentPhase);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "gardener.phase.changed",
            It.Is<GardenerPhaseChangedEvent>(e =>
                e.PreviousPhase == DeploymentPhase.Alpha &&
                e.NewPhase == DeploymentPhase.Beta),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPhaseMetricsAsync_ReturnsCountsFromTrackingSets()
    {
        var service = CreateService();
        SetupPhaseConfig();

        _mockCacheStore
            .Setup(c => c.SetCountAsync(GardenerService.ActiveVoidsTrackingKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        _mockCacheStore
            .Setup(c => c.SetCountAsync(GardenerService.ActiveScenariosTrackingKey,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var (status, response) = await service.GetPhaseMetricsAsync(
            new GetPhaseMetricsRequest(),
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(5, response.ActiveVoidInstances);
        Assert.Equal(3, response.ActiveScenarioInstances);
        Assert.True(response.ScenarioCapacityUtilization > 0);
    }

    #endregion

    #region Bond Features

    [Fact]
    public async Task EnterScenarioTogetherAsync_BondDisabled_ReturnsBadRequest()
    {
        Configuration.BondSharedVoidEnabled = false;
        var service = CreateService();

        var (status, _) = await service.EnterScenarioTogetherAsync(
            new EnterTogetherRequest
            {
                BondId = Guid.NewGuid(),
                ScenarioTemplateId = _testTemplateId
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task EnterScenarioTogetherAsync_ValidBond_CreatesSharedScenario()
    {
        var service = CreateService();
        var partnerAccountId = Guid.NewGuid();
        var partnerSeedId = Guid.NewGuid();
        var bondId = Guid.NewGuid();

        _mockSeedClient
            .Setup(c => c.GetBondAsync(
                It.IsAny<GetBondRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BondResponse
            {
                BondId = bondId,
                SeedTypeCode = "guardian",
                Participants = new List<BondParticipant>
                {
                    new() { SeedId = _testSeedId, JoinedAt = DateTimeOffset.UtcNow },
                    new() { SeedId = partnerSeedId, JoinedAt = DateTimeOffset.UtcNow }
                }
            });

        // GetSeedAsync resolves participant SeedId → OwnerId
        _mockSeedClient
            .Setup(c => c.GetSeedAsync(
                It.Is<GetSeedRequest>(r => r.SeedId == _testSeedId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeedResponse { SeedId = _testSeedId, OwnerId = _testAccountId, OwnerType = "account" });
        _mockSeedClient
            .Setup(c => c.GetSeedAsync(
                It.Is<GetSeedRequest>(r => r.SeedId == partnerSeedId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SeedResponse { SeedId = partnerSeedId, OwnerId = partnerAccountId, OwnerType = "account" });

        var garden1 = CreateTestGarden(_testAccountId);
        var garden2 = CreateTestGarden(partnerAccountId);
        garden2.SeedId = partnerSeedId;

        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden1);
        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{partnerAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden2);

        var template = CreateTestTemplate();
        template.Multiplayer = new ScenarioMultiplayerModel
        {
            MinPlayers = 2,
            MaxPlayers = 2,
            BondPreferred = true
        };
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{_testTemplateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var (status, response) = await service.EnterScenarioTogetherAsync(
            new EnterTogetherRequest
            {
                BondId = bondId,
                ScenarioTemplateId = _testTemplateId
            },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(ScenarioStatus.Active, response.Status);

        // Assert - Tracking sets updated for both participants
        _mockCacheStore.Verify(c => c.RemoveFromSetAsync<Guid>(
            GardenerService.ActiveVoidsTrackingKey, It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockCacheStore.Verify(c => c.AddToSetAsync<Guid>(
            GardenerService.ActiveScenariosTrackingKey, It.IsAny<Guid>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Assert - Event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "gardener.bond.entered-together",
            It.Is<GardenerBondEnteredTogetherEvent>(e =>
                e.BondId == bondId &&
                e.Participants.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSharedVoidStateAsync_BondDisabled_ReturnsBadRequest()
    {
        Configuration.BondSharedVoidEnabled = false;
        var service = CreateService();

        var (status, _) = await service.GetSharedVoidStateAsync(
            new GetSharedVoidRequest { BondId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region Event Handlers

    [Fact]
    public async Task HandleSeedBondFormedAsync_UpdatesGardenBondId()
    {
        var service = CreateService();
        var bondId = Guid.NewGuid();
        var garden = CreateTestGarden();

        _mockSeedClient
            .Setup(c => c.GetSeedAsync(
                It.IsAny<GetSeedRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestSeedResponse());

        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden);

        GardenInstanceModel? savedGarden = null;
        _mockGardenStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GardenInstanceModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GardenInstanceModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedGarden = model)
            .ReturnsAsync("etag");

        await service.HandleSeedBondFormedAsync(new SeedBondFormedEvent
        {
            BondId = bondId,
            ParticipantSeedIds = new List<Guid> { _testSeedId, Guid.NewGuid() }
        });

        Assert.NotNull(savedGarden);
        Assert.Equal(bondId, savedGarden.BondId);
        Assert.True(savedGarden.NeedsReEvaluation);
    }

    [Fact]
    public async Task HandleSeedActivatedAsync_UpdatesGardenSeedId()
    {
        var service = CreateService();
        var newSeedId = Guid.NewGuid();
        var garden = CreateTestGarden();

        _mockGardenStore
            .Setup(s => s.GetAsync($"void:{_testAccountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden);

        GardenInstanceModel? savedGarden = null;
        _mockGardenStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GardenInstanceModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GardenInstanceModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedGarden = model)
            .ReturnsAsync("etag");

        await service.HandleSeedActivatedAsync(new SeedActivatedEvent
        {
            SeedId = newSeedId,
            OwnerId = _testAccountId
        });

        Assert.NotNull(savedGarden);
        Assert.Equal(newSeedId, savedGarden.SeedId);
        Assert.True(savedGarden.NeedsReEvaluation);
    }

    #endregion

    #region GardenerSeedEvolutionListener

    [Fact]
    public async Task SeedEvolutionListener_OnGrowthRecorded_MarksForReEvaluation()
    {
        var garden = CreateTestGarden();
        var gardenKey = $"void:{_testAccountId}";

        _mockGardenStore
            .Setup(s => s.GetAsync(gardenKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden);

        GardenInstanceModel? savedGarden = null;
        _mockGardenStore
            .Setup(s => s.SaveAsync(gardenKey, It.IsAny<GardenInstanceModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GardenInstanceModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedGarden = model)
            .ReturnsAsync("etag");

        // Create the listener with wired-up stores
        var mockStoreFactory = new Mock<IStateStoreFactory>();
        mockStoreFactory
            .Setup(f => f.GetStore<GardenInstanceModel>(StateStoreDefinitions.GardenerVoidInstances))
            .Returns(_mockGardenStore.Object);

        var listener = new GardenerSeedEvolutionListener(
            mockStoreFactory.Object,
            Configuration,
            Mock.Of<ILogger<GardenerSeedEvolutionListener>>());

        Assert.Contains("guardian", listener.InterestedSeedTypes);

        await listener.OnGrowthRecordedAsync(
            new SeedGrowthNotification(
                SeedId: _testSeedId,
                SeedTypeCode: "guardian",
                OwnerId: _testAccountId,
                OwnerType: "account",
                DomainChanges: new List<DomainChange>(),
                TotalGrowth: 10.0f,
                CrossPollinated: false,
                Source: "test"),
            CancellationToken.None);

        Assert.NotNull(savedGarden);
        Assert.True(savedGarden.NeedsReEvaluation);
    }

    [Fact]
    public async Task SeedEvolutionListener_OnPhaseChanged_UpdatesCachedPhase()
    {
        var garden = CreateTestGarden();
        var gardenKey = $"void:{_testAccountId}";

        _mockGardenStore
            .Setup(s => s.GetAsync(gardenKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(garden);

        GardenInstanceModel? savedGarden = null;
        _mockGardenStore
            .Setup(s => s.SaveAsync(gardenKey, It.IsAny<GardenInstanceModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, GardenInstanceModel, StateOptions?, CancellationToken>(
                (_, model, _, _) => savedGarden = model)
            .ReturnsAsync("etag");

        var mockStoreFactory = new Mock<IStateStoreFactory>();
        mockStoreFactory
            .Setup(f => f.GetStore<GardenInstanceModel>(StateStoreDefinitions.GardenerVoidInstances))
            .Returns(_mockGardenStore.Object);

        var listener = new GardenerSeedEvolutionListener(
            mockStoreFactory.Object,
            Configuration,
            Mock.Of<ILogger<GardenerSeedEvolutionListener>>());

        await listener.OnPhaseChangedAsync(
            new SeedPhaseNotification(
                SeedId: _testSeedId,
                SeedTypeCode: "guardian",
                OwnerId: _testAccountId,
                OwnerType: "account",
                PreviousPhase: "dormant",
                NewPhase: "awakening",
                TotalGrowth: 50.0f,
                Progressed: true),
            CancellationToken.None);

        Assert.NotNull(savedGarden);
        Assert.Equal("awakening", savedGarden.CachedGrowthPhase);
        Assert.True(savedGarden.NeedsReEvaluation);
    }

    [Fact]
    public void SeedEvolutionListener_UsesConfiguredSeedTypeCode()
    {
        Configuration.SeedTypeCode = "custom-type";

        var listener = new GardenerSeedEvolutionListener(
            Mock.Of<IStateStoreFactory>(),
            Configuration,
            Mock.Of<ILogger<GardenerSeedEvolutionListener>>());

        Assert.Contains("custom-type", listener.InterestedSeedTypes);
        Assert.DoesNotContain("guardian", listener.InterestedSeedTypes);
    }

    #endregion
}
