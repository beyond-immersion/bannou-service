using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Worldstate.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Telemetry;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using BeyondImmersion.BannouService.Worldstate;
using System.Linq.Expressions;

namespace BeyondImmersion.BannouService.Worldstate.Tests;

/// <summary>
/// Unit tests for the Worldstate service.
/// </summary>
public class WorldstateServiceTests : ServiceTestBase<WorldstateServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<RealmClockModel>> _mockClockStore;
    private readonly Mock<IStateStore<CalendarTemplateModel>> _mockCalendarStore;
    private readonly Mock<IStateStore<TimeRatioHistoryModel>> _mockRatioHistoryStore;
    private readonly Mock<IStateStore<RealmWorldstateConfigModel>> _mockRealmConfigStore;
    private readonly Mock<IQueryableStateStore<CalendarTemplateModel>> _mockQueryableCalendarStore;
    private readonly Mock<IQueryableStateStore<RealmWorldstateConfigModel>> _mockQueryableRealmConfigStore;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<IRealmClient> _mockRealmClient;
    private readonly Mock<IGameServiceClient> _mockGameServiceClient;
    private readonly Mock<IEntitySessionRegistry> _mockEntitySessionRegistry;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<WorldstateService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IWorldstateTimeCalculator> _mockTimeCalculator;
    private readonly Mock<ICalendarTemplateCache> _mockCalendarTemplateCache;
    private readonly Mock<IRealmClockCache> _mockRealmClockCache;

    private readonly Guid _testRealmId = Guid.NewGuid();
    private readonly Guid _testGameServiceId = Guid.NewGuid();

    public WorldstateServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockClockStore = new Mock<IStateStore<RealmClockModel>>();
        _mockCalendarStore = new Mock<IStateStore<CalendarTemplateModel>>();
        _mockRatioHistoryStore = new Mock<IStateStore<TimeRatioHistoryModel>>();
        _mockRealmConfigStore = new Mock<IStateStore<RealmWorldstateConfigModel>>();
        _mockQueryableCalendarStore = new Mock<IQueryableStateStore<CalendarTemplateModel>>();
        _mockQueryableRealmConfigStore = new Mock<IQueryableStateStore<RealmWorldstateConfigModel>>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockRealmClient = new Mock<IRealmClient>();
        _mockGameServiceClient = new Mock<IGameServiceClient>();
        _mockEntitySessionRegistry = new Mock<IEntitySessionRegistry>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<WorldstateService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTimeCalculator = new Mock<IWorldstateTimeCalculator>();
        _mockCalendarTemplateCache = new Mock<ICalendarTemplateCache>();
        _mockRealmClockCache = new Mock<IRealmClockCache>();

        // Wire up state store factory
        _mockStateStoreFactory
            .Setup(f => f.GetStore<RealmClockModel>(StateStoreDefinitions.WorldstateRealmClock))
            .Returns(_mockClockStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CalendarTemplateModel>(StateStoreDefinitions.WorldstateCalendar))
            .Returns(_mockCalendarStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<TimeRatioHistoryModel>(StateStoreDefinitions.WorldstateRatioHistory))
            .Returns(_mockRatioHistoryStore.Object);
        // RealmConfigStore also uses WorldstateCalendar store name (same MySQL table, different key patterns)
        _mockStateStoreFactory
            .Setup(f => f.GetStore<RealmWorldstateConfigModel>(StateStoreDefinitions.WorldstateCalendar))
            .Returns(_mockRealmConfigStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<CalendarTemplateModel>(StateStoreDefinitions.WorldstateCalendar))
            .Returns(_mockQueryableCalendarStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<RealmWorldstateConfigModel>(StateStoreDefinitions.WorldstateCalendar))
            .Returns(_mockQueryableRealmConfigStore.Object);

        // Default lock to succeed
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(r => r.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Default configuration
        Configuration.DistributedLockTimeoutSeconds = 10;
        Configuration.DefaultTimeRatio = 60.0f;
        Configuration.DefaultDowntimePolicy = DowntimePolicy.Pause;
        Configuration.MaxBatchRealmTimeQueries = 50;
        Configuration.MaxCalendarsPerGameService = 10;
        Configuration.BoundaryEventBatchSize = 100;
        Configuration.DefaultCalendarTemplateCode = "default";
    }

    private WorldstateService CreateService() => new WorldstateService(
        _mockMessageBus.Object,
        _mockStateStoreFactory.Object,
        _mockLockProvider.Object,
        _mockResourceClient.Object,
        _mockRealmClient.Object,
        _mockGameServiceClient.Object,
        _mockEntitySessionRegistry.Object,
        _mockLogger.Object,
        new NullTelemetryProvider(),
        Configuration,
        _mockEventConsumer.Object,
        _mockTimeCalculator.Object,
        _mockCalendarTemplateCache.Object,
        _mockRealmClockCache.Object);

    private CalendarTemplateModel CreateTestCalendar(
        string templateCode = "standard",
        Guid? gameServiceId = null) => new CalendarTemplateModel
        {
            TemplateCode = templateCode,
            GameServiceId = gameServiceId ?? _testGameServiceId,
            GameHoursPerDay = 24,
            DayPeriods = new List<DayPeriodDefinition>
            {
                new DayPeriodDefinition { Code = "dawn", StartHour = 0, EndHour = 6, IsDaylight = false },
                new DayPeriodDefinition { Code = "day", StartHour = 6, EndHour = 18, IsDaylight = true },
                new DayPeriodDefinition { Code = "dusk", StartHour = 18, EndHour = 24, IsDaylight = false }
            },
            Months = new List<MonthDefinition>
            {
                new MonthDefinition { Code = "frostmere", Name = "Frostmere", DaysInMonth = 30, SeasonCode = "winter" },
                new MonthDefinition { Code = "sunpeak", Name = "Sunpeak", DaysInMonth = 30, SeasonCode = "summer" }
            },
            Seasons = new List<SeasonDefinition>
            {
                new SeasonDefinition { Code = "winter", Name = "Winter", Ordinal = 0 },
                new SeasonDefinition { Code = "summer", Name = "Summer", Ordinal = 1 }
            },
            DaysPerYear = 60,
            MonthsPerYear = 2,
            SeasonsPerYear = 2
        };

    private RealmClockModel CreateTestClock(
        Guid? realmId = null,
        Guid? gameServiceId = null,
        float timeRatio = 60.0f) => new RealmClockModel
        {
            RealmId = realmId ?? _testRealmId,
            GameServiceId = gameServiceId ?? _testGameServiceId,
            CalendarTemplateCode = "standard",
            Year = 1,
            MonthIndex = 0,
            MonthCode = "frostmere",
            DayOfMonth = 1,
            DayOfYear = 1,
            Hour = 0,
            Minute = 0,
            Period = "dawn",
            IsDaylight = false,
            Season = "winter",
            SeasonIndex = 0,
            SeasonProgress = 0.0f,
            TotalGameSecondsSinceEpoch = 0,
            TimeRatio = timeRatio,
            LastAdvancedRealTime = DateTimeOffset.UtcNow.AddMinutes(-5),
            RealmEpoch = DateTimeOffset.UtcNow.AddHours(-1),
            DowntimePolicy = DowntimePolicy.Pause,
            IsActive = timeRatio > 0.0f
        };

    private RealmWorldstateConfigModel CreateTestRealmConfig(
        Guid? realmId = null,
        Guid? gameServiceId = null) => new RealmWorldstateConfigModel
        {
            RealmId = realmId ?? _testRealmId,
            GameServiceId = gameServiceId ?? _testGameServiceId,
            CalendarTemplateCode = "standard",
            TimeRatio = 60.0f,
            DowntimePolicy = DowntimePolicy.Pause,
            RealmEpoch = DateTimeOffset.UtcNow.AddHours(-1)
        };

    private void SetupLockFails()
    {
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(r => r.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);
    }

    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void WorldstateService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<WorldstateService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void WorldstateServiceConfiguration_CanBeInstantiated()
    {
        var config = new WorldstateServiceConfiguration();
        Assert.NotNull(config);
    }

    #endregion

    #region GetRealmTimeAsync Tests

    [Fact]
    public async Task GetRealmTime_ClockExists_ReturnsSnapshot()
    {
        // Arrange
        var service = CreateService();
        var clock = CreateTestClock();

        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clock);

        // Act
        var (status, response) = await service.GetRealmTimeAsync(
            new GetRealmTimeRequest { RealmId = _testRealmId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(_testRealmId, response.RealmId);
        Assert.Equal(clock.Year, response.Year);
        Assert.Equal(clock.MonthCode, response.MonthCode);
        Assert.Equal(clock.Period, response.Period);
    }

    [Fact]
    public async Task GetRealmTime_NoClock_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockClockStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmClockModel?)null);

        // Act
        var (status, response) = await service.GetRealmTimeAsync(
            new GetRealmTimeRequest { RealmId = _testRealmId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetRealmTimeByCodeAsync Tests

    [Fact]
    public async Task GetRealmTimeByCode_ValidCode_ReturnsSnapshot()
    {
        // Arrange
        var service = CreateService();
        var clock = CreateTestClock();

        _mockRealmClient
            .Setup(c => c.GetRealmByCodeAsync(
                It.Is<GetRealmByCodeRequest>(r => r.Code == "omega"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmResponse { RealmId = _testRealmId, GameServiceId = _testGameServiceId });

        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clock);

        // Act
        var (status, response) = await service.GetRealmTimeByCodeAsync(
            new GetRealmTimeByCodeRequest { RealmCode = "omega" }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(_testRealmId, response.RealmId);
    }

    [Fact]
    public async Task GetRealmTimeByCode_RealmNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockRealmClient
            .Setup(c => c.GetRealmByCodeAsync(It.IsAny<GetRealmByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        // Act
        var (status, response) = await service.GetRealmTimeByCodeAsync(
            new GetRealmTimeByCodeRequest { RealmCode = "nonexistent" }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region BatchGetRealmTimesAsync Tests

    [Fact]
    public async Task BatchGetRealmTimes_ValidRequest_ReturnsSnapshots()
    {
        // Arrange
        var service = CreateService();
        var realmId1 = Guid.NewGuid();
        var realmId2 = Guid.NewGuid();
        var clock1 = CreateTestClock(realmId: realmId1);
        var clock2 = CreateTestClock(realmId: realmId2);

        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{realmId1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clock1);
        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{realmId2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clock2);

        // Act
        var (status, response) = await service.BatchGetRealmTimesAsync(
            new BatchGetRealmTimesRequest { RealmIds = new List<Guid> { realmId1, realmId2 } },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Snapshots.Count);
        Assert.Empty(response.NotFoundRealmIds);
    }

    [Fact]
    public async Task BatchGetRealmTimes_SomeNotFound_ReturnsPartialWithNotFoundList()
    {
        // Arrange
        var service = CreateService();
        var foundRealmId = Guid.NewGuid();
        var missingRealmId = Guid.NewGuid();
        var clock = CreateTestClock(realmId: foundRealmId);

        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{foundRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clock);
        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{missingRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmClockModel?)null);

        // Act
        var (status, response) = await service.BatchGetRealmTimesAsync(
            new BatchGetRealmTimesRequest { RealmIds = new List<Guid> { foundRealmId, missingRealmId } },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Snapshots);
        Assert.Single(response.NotFoundRealmIds);
        Assert.Contains(missingRealmId, response.NotFoundRealmIds);
    }

    [Fact]
    public async Task BatchGetRealmTimes_ExceedsMaxBatch_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        Configuration.MaxBatchRealmTimeQueries = 2;

        var realmIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();

        // Act
        var (status, response) = await service.BatchGetRealmTimesAsync(
            new BatchGetRealmTimesRequest { RealmIds = realmIds },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region InitializeRealmClockAsync Tests

    [Fact]
    public async Task InitializeRealmClock_ValidRequest_CreatesClockAndPublishesEvents()
    {
        // Arrange
        var service = CreateService();

        _mockRealmClient
            .Setup(c => c.GetRealmAsync(
                It.Is<GetRealmRequest>(r => r.RealmId == _testRealmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmResponse { RealmId = _testRealmId, GameServiceId = _testGameServiceId });

        var calendar = CreateTestCalendar();
        _mockCalendarStore
            .Setup(s => s.GetAsync($"calendar:{_testGameServiceId}:standard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);

        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmClockModel?)null);

        _mockTimeCalculator
            .Setup(t => t.ResolveDayPeriod(It.IsAny<CalendarTemplateModel>(), 0))
            .Returns(("dawn", false));
        _mockTimeCalculator
            .Setup(t => t.ResolveSeason(It.IsAny<CalendarTemplateModel>(), 0))
            .Returns(("winter", 0));

        RealmClockModel? savedClock = null;
        _mockClockStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("realm:")),
                It.IsAny<RealmClockModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, RealmClockModel, StateOptions?, CancellationToken>((_, m, _, _) => savedClock = m)
            .ReturnsAsync("etag");

        RealmWorldstateConfigModel? savedConfig = null;
        _mockRealmConfigStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("realm-config:")),
                It.IsAny<RealmWorldstateConfigModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, RealmWorldstateConfigModel, StateOptions?, CancellationToken>((_, m, _, _) => savedConfig = m)
            .ReturnsAsync("etag");

        TimeRatioHistoryModel? savedRatioHistory = null;
        _mockRatioHistoryStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("ratio:")),
                It.IsAny<TimeRatioHistoryModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, TimeRatioHistoryModel, StateOptions?, CancellationToken>((_, m, _, _) => savedRatioHistory = m)
            .ReturnsAsync("etag");

        var request = new InitializeRealmClockRequest
        {
            RealmId = _testRealmId,
            CalendarTemplateCode = "standard",
            TimeRatio = 60.0f,
            StartingYear = 1
        };

        // Act
        var (status, response) = await service.InitializeRealmClockAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(_testGameServiceId, response.GameServiceId);
        Assert.Equal("standard", response.CalendarTemplateCode);
        Assert.Equal(60.0f, response.TimeRatio);
        Assert.Equal(1, response.StartingYear);

        // Verify clock was saved correctly
        Assert.NotNull(savedClock);
        Assert.Equal(_testRealmId, savedClock.RealmId);
        Assert.Equal(1, savedClock.Year);
        Assert.Equal(0, savedClock.MonthIndex);
        Assert.Equal("frostmere", savedClock.MonthCode);
        Assert.Equal(1, savedClock.DayOfMonth);
        Assert.Equal("dawn", savedClock.Period);
        Assert.Equal("winter", savedClock.Season);
        Assert.True(savedClock.IsActive);

        // Verify config was saved
        Assert.NotNull(savedConfig);
        Assert.Equal(_testRealmId, savedConfig.RealmId);
        Assert.Equal("standard", savedConfig.CalendarTemplateCode);

        // Verify ratio history was saved with initial segment
        Assert.NotNull(savedRatioHistory);
        Assert.Single(savedRatioHistory.Segments);
        Assert.Equal(60.0f, savedRatioHistory.Segments[0].TimeRatio);
        Assert.Equal(TimeRatioChangeReason.Initial, savedRatioHistory.Segments[0].Reason);

        // Verify events published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "worldstate.realm-clock.initialized",
            It.Is<WorldstateRealmClockInitializedEvent>(e =>
                e.RealmId == _testRealmId &&
                e.CalendarTemplateCode == "standard" &&
                e.InitialTimeRatio == 60.0f),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "worldstate.realm-config.created",
            It.Is<RealmConfigCreatedEvent>(e => e.RealmId == _testRealmId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitializeRealmClock_RealmNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockRealmClient
            .Setup(c => c.GetRealmAsync(It.IsAny<GetRealmRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        // Act
        var (status, response) = await service.InitializeRealmClockAsync(
            new InitializeRealmClockRequest { RealmId = _testRealmId, CalendarTemplateCode = "standard" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task InitializeRealmClock_NoTemplateCodeAndNoDefault_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        Configuration.DefaultCalendarTemplateCode = null;

        _mockRealmClient
            .Setup(c => c.GetRealmAsync(It.IsAny<GetRealmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmResponse { RealmId = _testRealmId, GameServiceId = _testGameServiceId });

        // Act
        var (status, response) = await service.InitializeRealmClockAsync(
            new InitializeRealmClockRequest { RealmId = _testRealmId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task InitializeRealmClock_CalendarNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockRealmClient
            .Setup(c => c.GetRealmAsync(It.IsAny<GetRealmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmResponse { RealmId = _testRealmId, GameServiceId = _testGameServiceId });
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarTemplateModel?)null);

        // Act
        var (status, response) = await service.InitializeRealmClockAsync(
            new InitializeRealmClockRequest { RealmId = _testRealmId, CalendarTemplateCode = "nonexistent" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task InitializeRealmClock_LockFails_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        SetupLockFails();

        _mockRealmClient
            .Setup(c => c.GetRealmAsync(It.IsAny<GetRealmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmResponse { RealmId = _testRealmId, GameServiceId = _testGameServiceId });
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCalendar());

        // Act
        var (status, response) = await service.InitializeRealmClockAsync(
            new InitializeRealmClockRequest { RealmId = _testRealmId, CalendarTemplateCode = "standard" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task InitializeRealmClock_ClockAlreadyExists_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        _mockRealmClient
            .Setup(c => c.GetRealmAsync(It.IsAny<GetRealmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmResponse { RealmId = _testRealmId, GameServiceId = _testGameServiceId });
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCalendar());

        // Existing clock already present
        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestClock());

        // Act
        var (status, response) = await service.InitializeRealmClockAsync(
            new InitializeRealmClockRequest { RealmId = _testRealmId, CalendarTemplateCode = "standard" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region SetTimeRatioAsync Tests

    [Fact]
    public async Task SetTimeRatio_ValidRequest_UpdatesRatioAndPublishesEvents()
    {
        // Arrange
        var service = CreateService();
        var clock = CreateTestClock(timeRatio: 60.0f);

        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clock);
        _mockCalendarStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("calendar:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCalendar());

        var realmConfig = CreateTestRealmConfig();
        _mockRealmConfigStore
            .Setup(s => s.GetAsync($"realm-config:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmConfig);

        var ratioHistory = new TimeRatioHistoryModel
        {
            RealmId = _testRealmId,
            Segments = new List<TimeRatioSegmentEntry>
            {
                new TimeRatioSegmentEntry { SegmentStartRealTime = DateTimeOffset.UtcNow.AddHours(-1), TimeRatio = 60.0f, Reason = TimeRatioChangeReason.Initial }
            }
        };
        _mockRatioHistoryStore
            .Setup(s => s.GetAsync($"ratio:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ratioHistory);

        _mockTimeCalculator
            .Setup(t => t.AdvanceGameTime(It.IsAny<RealmClockModel>(), It.IsAny<CalendarTemplateModel>(), It.IsAny<long>()))
            .Returns(new List<BoundaryCrossing>());

        var request = new SetTimeRatioRequest
        {
            RealmId = _testRealmId,
            NewRatio = 120.0f,
            Reason = TimeRatioChangeReason.AdminAdjustment
        };

        // Act
        var (status, response) = await service.SetTimeRatioAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(60.0f, response.PreviousRatio);

        // Verify cache invalidated
        _mockRealmClockCache.Verify(c => c.Invalidate(_testRealmId), Times.Once);

        // Verify ratio changed event
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "worldstate.ratio-changed",
            It.Is<WorldstateRatioChangedEvent>(e =>
                e.RealmId == _testRealmId &&
                e.PreviousRatio == 60.0f &&
                e.NewRatio == 120.0f &&
                e.Reason == TimeRatioChangeReason.AdminAdjustment),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetTimeRatio_NegativeRatio_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        // Act
        var (status, response) = await service.SetTimeRatioAsync(
            new SetTimeRatioRequest { RealmId = _testRealmId, NewRatio = -1.0f, Reason = TimeRatioChangeReason.AdminAdjustment },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SetTimeRatio_LockFails_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        SetupLockFails();

        // Act
        var (status, response) = await service.SetTimeRatioAsync(
            new SetTimeRatioRequest { RealmId = _testRealmId, NewRatio = 120.0f, Reason = TimeRatioChangeReason.AdminAdjustment },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SetTimeRatio_NoClock_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockClockStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmClockModel?)null);

        // Act
        var (status, response) = await service.SetTimeRatioAsync(
            new SetTimeRatioRequest { RealmId = _testRealmId, NewRatio = 120.0f, Reason = TimeRatioChangeReason.AdminAdjustment },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SetTimeRatio_ZeroRatio_SetsInactive()
    {
        // Arrange
        var service = CreateService();
        var clock = CreateTestClock(timeRatio: 60.0f);

        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clock);
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCalendar());
        _mockRealmConfigStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestRealmConfig());
        _mockRatioHistoryStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TimeRatioHistoryModel { RealmId = _testRealmId, Segments = new List<TimeRatioSegmentEntry>() });
        _mockTimeCalculator
            .Setup(t => t.AdvanceGameTime(It.IsAny<RealmClockModel>(), It.IsAny<CalendarTemplateModel>(), It.IsAny<long>()))
            .Returns(new List<BoundaryCrossing>());

        // Act
        var (status, _) = await service.SetTimeRatioAsync(
            new SetTimeRatioRequest { RealmId = _testRealmId, NewRatio = 0.0f, Reason = TimeRatioChangeReason.Pause },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        // Clock should be set to inactive
        Assert.False(clock.IsActive);
        Assert.Equal(0.0f, clock.TimeRatio);
    }

    #endregion

    #region AdvanceClockAsync Tests

    [Fact]
    public async Task AdvanceClock_ValidGameSeconds_AdvancesAndPublishesBoundaryEvents()
    {
        // Arrange
        var service = CreateService();
        var clock = CreateTestClock();
        var calendar = CreateTestCalendar();
        var realmConfig = CreateTestRealmConfig();

        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clock);
        _mockRealmConfigStore
            .Setup(s => s.GetAsync($"realm-config:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmConfig);
        _mockCalendarStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("calendar:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);

        var boundaries = new List<BoundaryCrossing>
        {
            new BoundaryCrossing
            {
                Type = BoundaryType.Hour,
                PreviousIntValue = 0,
                NewIntValue = 1,
                CountCrossed = 1
            }
        };
        _mockTimeCalculator
            .Setup(t => t.AdvanceGameTime(It.IsAny<RealmClockModel>(), It.IsAny<CalendarTemplateModel>(), 3600L))
            .Returns(boundaries);

        // Act
        var (status, response) = await service.AdvanceClockAsync(
            new AdvanceClockRequest { RealmId = _testRealmId, GameSeconds = 3600 },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.BoundaryEventsPublished);
        Assert.NotNull(response.PreviousTime);
        Assert.NotNull(response.NewTime);

        // Verify cache invalidated
        _mockRealmClockCache.Verify(c => c.Invalidate(_testRealmId), Times.Once);

        // Verify clock-advanced event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "worldstate.clock-advanced",
            It.Is<WorldstateClockAdvancedEvent>(e => e.RealmId == _testRealmId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdvanceClock_UsingGameDays_ConvertsToSeconds()
    {
        // Arrange
        var service = CreateService();
        var clock = CreateTestClock();
        var calendar = CreateTestCalendar(); // 24 hours per day
        var realmConfig = CreateTestRealmConfig();

        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clock);
        _mockRealmConfigStore
            .Setup(s => s.GetAsync($"realm-config:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmConfig);
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);

        // 1 game day = 24 hours * 3600 = 86400 game-seconds
        _mockTimeCalculator
            .Setup(t => t.AdvanceGameTime(It.IsAny<RealmClockModel>(), It.IsAny<CalendarTemplateModel>(), 86400L))
            .Returns(new List<BoundaryCrossing>());

        // Act
        var (status, response) = await service.AdvanceClockAsync(
            new AdvanceClockRequest { RealmId = _testRealmId, GameDays = 1 },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        _mockTimeCalculator.Verify(t => t.AdvanceGameTime(
            It.IsAny<RealmClockModel>(), It.IsAny<CalendarTemplateModel>(), 86400L), Times.Once);
    }

    [Fact]
    public async Task AdvanceClock_NoTimeParams_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var clock = CreateTestClock();

        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clock);
        _mockRealmConfigStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestRealmConfig());
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCalendar());

        // Act - no time parameters specified
        var (status, response) = await service.AdvanceClockAsync(
            new AdvanceClockRequest { RealmId = _testRealmId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AdvanceClock_LockFails_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        SetupLockFails();

        // Act
        var (status, response) = await service.AdvanceClockAsync(
            new AdvanceClockRequest { RealmId = _testRealmId, GameSeconds = 100 },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AdvanceClock_NoClock_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockClockStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmClockModel?)null);

        // Act
        var (status, response) = await service.AdvanceClockAsync(
            new AdvanceClockRequest { RealmId = _testRealmId, GameSeconds = 100 },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AdvanceClock_BoundariesExceedBatchSize_TruncatesPublishing()
    {
        // Arrange
        var service = CreateService();
        Configuration.BoundaryEventBatchSize = 2;

        var clock = CreateTestClock();
        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clock);
        _mockRealmConfigStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestRealmConfig());
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCalendar());

        // Return 5 boundary crossings
        var boundaries = Enumerable.Range(0, 5).Select(i => new BoundaryCrossing
        {
            Type = BoundaryType.Hour,
            PreviousIntValue = i,
            NewIntValue = i + 1,
            CountCrossed = 1
        }).ToList();

        _mockTimeCalculator
            .Setup(t => t.AdvanceGameTime(It.IsAny<RealmClockModel>(), It.IsAny<CalendarTemplateModel>(), It.IsAny<long>()))
            .Returns(boundaries);

        // Act
        var (status, response) = await service.AdvanceClockAsync(
            new AdvanceClockRequest { RealmId = _testRealmId, GameSeconds = 100000 },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.BoundaryEventsPublished);
    }

    #endregion

    #region SeedCalendarAsync Tests

    [Fact]
    public async Task SeedCalendar_ValidRequest_CreatesCalendarAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();

        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = _testGameServiceId, StubName = "test-game", DisplayName = "Test Game", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarTemplateModel?)null);
        _mockQueryableCalendarStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<CalendarTemplateModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarTemplateModel>() as IReadOnlyList<CalendarTemplateModel>);

        CalendarTemplateModel? savedCalendar = null;
        _mockCalendarStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("calendar:")),
                It.IsAny<CalendarTemplateModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CalendarTemplateModel, StateOptions?, CancellationToken>((_, m, _, _) => savedCalendar = m)
            .ReturnsAsync("etag");

        var request = new SeedCalendarRequest
        {
            TemplateCode = "arcadian",
            GameServiceId = _testGameServiceId,
            GameHoursPerDay = 24,
            DayPeriods = new List<DayPeriodDefinition>
            {
                new DayPeriodDefinition { Code = "dawn", StartHour = 0, EndHour = 6, IsDaylight = false },
                new DayPeriodDefinition { Code = "day", StartHour = 6, EndHour = 18, IsDaylight = true },
                new DayPeriodDefinition { Code = "dusk", StartHour = 18, EndHour = 24, IsDaylight = false }
            },
            Months = new List<MonthDefinition>
            {
                new MonthDefinition { Code = "frostmere", Name = "Frostmere", DaysInMonth = 30, SeasonCode = "winter" }
            },
            Seasons = new List<SeasonDefinition>
            {
                new SeasonDefinition { Code = "winter", Name = "Winter", Ordinal = 0 }
            }
        };

        // Act
        var (status, response) = await service.SeedCalendarAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("arcadian", response.TemplateCode);
        Assert.Equal(24, response.GameHoursPerDay);
        Assert.Equal(30, response.DaysPerYear);
        Assert.Equal(1, response.MonthsPerYear);

        // Verify saved model
        Assert.NotNull(savedCalendar);
        Assert.Equal("arcadian", savedCalendar.TemplateCode);
        Assert.Equal(30, savedCalendar.DaysPerYear);

        // Verify event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "worldstate.calendar-template.created",
            It.Is<CalendarTemplateCreatedEvent>(e =>
                e.TemplateCode == "arcadian" &&
                e.GameServiceId == _testGameServiceId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeedCalendar_GameServiceNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var request = new SeedCalendarRequest
        {
            TemplateCode = "test",
            GameServiceId = Guid.NewGuid(),
            GameHoursPerDay = 24,
            DayPeriods = new List<DayPeriodDefinition>(),
            Months = new List<MonthDefinition>(),
            Seasons = new List<SeasonDefinition>()
        };

        // Act
        var (status, response) = await service.SeedCalendarAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SeedCalendar_DuplicateTemplateCode_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = _testGameServiceId, StubName = "test-game", DisplayName = "Test Game", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCalendar());

        var request = new SeedCalendarRequest
        {
            TemplateCode = "standard",
            GameServiceId = _testGameServiceId,
            GameHoursPerDay = 24,
            DayPeriods = new List<DayPeriodDefinition>(),
            Months = new List<MonthDefinition>(),
            Seasons = new List<SeasonDefinition>()
        };

        // Act
        var (status, response) = await service.SeedCalendarAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SeedCalendar_InvalidStructure_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = _testGameServiceId, StubName = "test-game", DisplayName = "Test Game", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarTemplateModel?)null);
        _mockQueryableCalendarStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<CalendarTemplateModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarTemplateModel>() as IReadOnlyList<CalendarTemplateModel>);

        // Invalid: gameHoursPerDay = 0
        var request = new SeedCalendarRequest
        {
            TemplateCode = "invalid",
            GameServiceId = _testGameServiceId,
            GameHoursPerDay = 0,
            DayPeriods = new List<DayPeriodDefinition>
            {
                new DayPeriodDefinition { Code = "dawn", StartHour = 0, EndHour = 6, IsDaylight = false }
            },
            Months = new List<MonthDefinition>
            {
                new MonthDefinition { Code = "m1", Name = "Month 1", DaysInMonth = 30, SeasonCode = "s1" }
            },
            Seasons = new List<SeasonDefinition>
            {
                new SeasonDefinition { Code = "s1", Name = "Season 1", Ordinal = 0 }
            }
        };

        // Act
        var (status, response) = await service.SeedCalendarAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SeedCalendar_MaxCalendarsReached_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        Configuration.MaxCalendarsPerGameService = 1;

        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo { ServiceId = _testGameServiceId, StubName = "test-game", DisplayName = "Test Game", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarTemplateModel?)null);

        // Already 1 existing calendar (max)
        _mockQueryableCalendarStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<CalendarTemplateModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarTemplateModel> { CreateTestCalendar() } as IReadOnlyList<CalendarTemplateModel>);

        var request = new SeedCalendarRequest
        {
            TemplateCode = "new-calendar",
            GameServiceId = _testGameServiceId,
            GameHoursPerDay = 24,
            DayPeriods = new List<DayPeriodDefinition>
            {
                new DayPeriodDefinition { Code = "all-day", StartHour = 0, EndHour = 24, IsDaylight = true }
            },
            Months = new List<MonthDefinition>
            {
                new MonthDefinition { Code = "m1", Name = "M1", DaysInMonth = 30, SeasonCode = "s1" }
            },
            Seasons = new List<SeasonDefinition>
            {
                new SeasonDefinition { Code = "s1", Name = "S1", Ordinal = 0 }
            }
        };

        // Act
        var (status, response) = await service.SeedCalendarAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region GetElapsedGameTimeAsync Tests

    [Fact]
    public async Task GetElapsedGameTime_ValidRange_ReturnsDecomposedTime()
    {
        // Arrange
        var service = CreateService();
        var from = DateTimeOffset.UtcNow.AddHours(-2);
        var to = DateTimeOffset.UtcNow;

        _mockRatioHistoryStore
            .Setup(s => s.GetAsync($"ratio:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TimeRatioHistoryModel
            {
                RealmId = _testRealmId,
                Segments = new List<TimeRatioSegmentEntry>
                {
                    new TimeRatioSegmentEntry { SegmentStartRealTime = from.AddHours(-1), TimeRatio = 60.0f, Reason = TimeRatioChangeReason.Initial }
                }
            });

        _mockTimeCalculator
            .Setup(t => t.ComputeElapsedGameTime(It.IsAny<List<TimeRatioSegmentEntry>>(), from, to))
            .Returns(432000L); // 5 game days in seconds

        _mockRealmConfigStore
            .Setup(s => s.GetAsync($"realm-config:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestRealmConfig());

        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCalendar());

        _mockTimeCalculator
            .Setup(t => t.DecomposeGameSeconds(It.IsAny<CalendarTemplateModel>(), 432000L))
            .Returns((5, 0, 0));

        // Act
        var (status, response) = await service.GetElapsedGameTimeAsync(
            new GetElapsedGameTimeRequest { RealmId = _testRealmId, FromRealTime = from, ToRealTime = to },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(432000L, response.TotalGameSeconds);
        Assert.Equal(5, response.GameDays);
        Assert.Equal(0, response.GameHours);
        Assert.Equal(0, response.GameMinutes);
    }

    [Fact]
    public async Task GetElapsedGameTime_InvalidRange_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;

        // from >= to is invalid
        // Act
        var (status, response) = await service.GetElapsedGameTimeAsync(
            new GetElapsedGameTimeRequest { RealmId = _testRealmId, FromRealTime = now, ToRealTime = now.AddSeconds(-1) },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetElapsedGameTime_NoRatioHistory_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockRatioHistoryStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TimeRatioHistoryModel?)null);

        // Act
        var (status, response) = await service.GetElapsedGameTimeAsync(
            new GetElapsedGameTimeRequest
            {
                RealmId = _testRealmId,
                FromRealTime = DateTimeOffset.UtcNow.AddHours(-1),
                ToRealTime = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region UpdateCalendarAsync Tests

    [Fact]
    public async Task UpdateCalendar_ValidPartialUpdate_UpdatesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var calendar = CreateTestCalendar();

        _mockCalendarStore
            .Setup(s => s.GetAsync($"calendar:{_testGameServiceId}:standard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);

        CalendarTemplateModel? savedCalendar = null;
        _mockCalendarStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<CalendarTemplateModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CalendarTemplateModel, StateOptions?, CancellationToken>((_, m, _, _) => savedCalendar = m)
            .ReturnsAsync("etag");

        var request = new UpdateCalendarRequest
        {
            GameServiceId = _testGameServiceId,
            TemplateCode = "standard",
            GameHoursPerDay = 48,
            DayPeriods = new List<DayPeriodDefinition>
            {
                new DayPeriodDefinition { Code = "dawn", StartHour = 0, EndHour = 12, IsDaylight = false },
                new DayPeriodDefinition { Code = "day", StartHour = 12, EndHour = 36, IsDaylight = true },
                new DayPeriodDefinition { Code = "dusk", StartHour = 36, EndHour = 48, IsDaylight = false }
            }
        };

        // Act
        var (status, response) = await service.UpdateCalendarAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(48, response.GameHoursPerDay);

        Assert.NotNull(savedCalendar);
        Assert.Equal(48, savedCalendar.GameHoursPerDay);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "worldstate.calendar-template.updated",
            It.Is<CalendarTemplateUpdatedEvent>(e =>
                e.TemplateCode == "standard" &&
                e.ChangedFields.Contains("gameHoursPerDay") &&
                e.ChangedFields.Contains("dayPeriods")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCalendar_NoChanges_ReturnsOkWithoutSaving()
    {
        // Arrange
        var service = CreateService();
        var calendar = CreateTestCalendar();

        _mockCalendarStore
            .Setup(s => s.GetAsync($"calendar:{_testGameServiceId}:standard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);

        // No fields specified in update
        var request = new UpdateCalendarRequest
        {
            GameServiceId = _testGameServiceId,
            TemplateCode = "standard"
        };

        // Act
        var (status, response) = await service.UpdateCalendarAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // No save or event should happen
        _mockCalendarStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<CalendarTemplateModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            It.Is<string>(t => t.Contains("updated")),
            It.IsAny<CalendarTemplateUpdatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateCalendar_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarTemplateModel?)null);

        // Act
        var (status, response) = await service.UpdateCalendarAsync(
            new UpdateCalendarRequest { GameServiceId = _testGameServiceId, TemplateCode = "nonexistent" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region DeleteCalendarAsync Tests

    [Fact]
    public async Task DeleteCalendar_NoReferences_DeletesAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var calendar = CreateTestCalendar();

        _mockCalendarStore
            .Setup(s => s.GetAsync($"calendar:{_testGameServiceId}:standard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);
        _mockQueryableRealmConfigStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<RealmWorldstateConfigModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RealmWorldstateConfigModel>() as IReadOnlyList<RealmWorldstateConfigModel>);

        // Act
        var (status, response) = await service.DeleteCalendarAsync(
            new DeleteCalendarRequest { GameServiceId = _testGameServiceId, TemplateCode = "standard" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        _mockCalendarStore.Verify(s => s.DeleteAsync(
            $"calendar:{_testGameServiceId}:standard", It.IsAny<CancellationToken>()), Times.Once);

        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "worldstate.calendar-template.deleted",
            It.Is<CalendarTemplateDeletedEvent>(e => e.TemplateCode == "standard"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCalendar_HasReferences_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var calendar = CreateTestCalendar();

        _mockCalendarStore
            .Setup(s => s.GetAsync($"calendar:{_testGameServiceId}:standard", It.IsAny<CancellationToken>()))
            .ReturnsAsync(calendar);

        // There are realm configs referencing this calendar
        _mockQueryableRealmConfigStore
            .Setup(s => s.QueryAsync(It.IsAny<Expression<Func<RealmWorldstateConfigModel, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RealmWorldstateConfigModel> { CreateTestRealmConfig() } as IReadOnlyList<RealmWorldstateConfigModel>);

        // Act
        var (status, response) = await service.DeleteCalendarAsync(
            new DeleteCalendarRequest { GameServiceId = _testGameServiceId, TemplateCode = "standard" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteCalendar_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarTemplateModel?)null);

        // Act
        var (status, response) = await service.DeleteCalendarAsync(
            new DeleteCalendarRequest { GameServiceId = _testGameServiceId, TemplateCode = "nonexistent" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region UpdateRealmConfigAsync Tests

    [Fact]
    public async Task UpdateRealmConfig_ChangeDowntimePolicy_UpdatesConfigAndClock()
    {
        // Arrange
        var service = CreateService();
        var realmConfig = CreateTestRealmConfig();
        var clock = CreateTestClock();

        _mockRealmConfigStore
            .Setup(s => s.GetAsync($"realm-config:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmConfig);
        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clock);

        // Act
        var (status, response) = await service.UpdateRealmConfigAsync(
            new UpdateRealmConfigRequest { RealmId = _testRealmId, DowntimePolicy = DowntimePolicy.Advance },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(DowntimePolicy.Advance, response.DowntimePolicy);

        // Verify config saved
        _mockRealmConfigStore.Verify(s => s.SaveAsync(
            $"realm-config:{_testRealmId}", It.IsAny<RealmWorldstateConfigModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify clock also saved (downtimePolicy synced)
        _mockClockStore.Verify(s => s.SaveAsync(
            $"realm:{_testRealmId}", It.IsAny<RealmClockModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify event
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "worldstate.realm-config.updated",
            It.Is<RealmConfigUpdatedEvent>(e =>
                e.RealmId == _testRealmId &&
                e.ChangedFields.Contains("downtimePolicy")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRealmConfig_ChangeCalendarTemplate_ValidatesNewTemplate()
    {
        // Arrange
        var service = CreateService();
        var realmConfig = CreateTestRealmConfig();
        var clock = CreateTestClock();

        _mockRealmConfigStore
            .Setup(s => s.GetAsync($"realm-config:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmConfig);
        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(clock);

        // New calendar template exists
        _mockCalendarStore
            .Setup(s => s.GetAsync($"calendar:{_testGameServiceId}:new-template", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCalendar(templateCode: "new-template"));

        // Act
        var (status, response) = await service.UpdateRealmConfigAsync(
            new UpdateRealmConfigRequest { RealmId = _testRealmId, CalendarTemplateCode = "new-template" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("new-template", response.CalendarTemplateCode);
    }

    [Fact]
    public async Task UpdateRealmConfig_ChangeToNonexistentCalendar_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var realmConfig = CreateTestRealmConfig();

        _mockRealmConfigStore
            .Setup(s => s.GetAsync($"realm-config:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmConfig);

        // New calendar does not exist
        _mockCalendarStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("nonexistent")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CalendarTemplateModel?)null);

        // Act
        var (status, response) = await service.UpdateRealmConfigAsync(
            new UpdateRealmConfigRequest { RealmId = _testRealmId, CalendarTemplateCode = "nonexistent" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateRealmConfig_NoChanges_ReturnsOkWithoutSaving()
    {
        // Arrange
        var service = CreateService();
        var realmConfig = CreateTestRealmConfig();

        _mockRealmConfigStore
            .Setup(s => s.GetAsync($"realm-config:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmConfig);
        _mockClockStore
            .Setup(s => s.GetAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestClock());

        // Act - empty update
        var (status, response) = await service.UpdateRealmConfigAsync(
            new UpdateRealmConfigRequest { RealmId = _testRealmId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        _mockRealmConfigStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<RealmWorldstateConfigModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRealmConfig_LockFails_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        SetupLockFails();

        // Act
        var (status, response) = await service.UpdateRealmConfigAsync(
            new UpdateRealmConfigRequest { RealmId = _testRealmId, DowntimePolicy = DowntimePolicy.Advance },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region CleanupByRealmAsync Tests

    [Fact]
    public async Task CleanupByRealm_ExistingRealm_DeletesAllDataAndPublishesEvent()
    {
        // Arrange
        var service = CreateService();
        var realmConfig = CreateTestRealmConfig();

        _mockRealmConfigStore
            .Setup(s => s.GetAsync($"realm-config:{_testRealmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmConfig);

        // Act
        var (status, response) = await service.CleanupByRealmAsync(
            new CleanupByRealmRequest { RealmId = _testRealmId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify all stores deleted
        _mockClockStore.Verify(s => s.DeleteAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()), Times.Once);
        _mockRatioHistoryStore.Verify(s => s.DeleteAsync($"ratio:{_testRealmId}", It.IsAny<CancellationToken>()), Times.Once);
        _mockRealmConfigStore.Verify(s => s.DeleteAsync($"realm-config:{_testRealmId}", It.IsAny<CancellationToken>()), Times.Once);

        // Verify caches invalidated
        _mockRealmClockCache.Verify(c => c.Invalidate(_testRealmId), Times.Once);
        _mockCalendarTemplateCache.Verify(c => c.Invalidate(
            realmConfig.GameServiceId, realmConfig.CalendarTemplateCode), Times.Once);

        // Verify event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "worldstate.realm-config.deleted",
            It.Is<RealmConfigDeletedEvent>(e => e.RealmId == _testRealmId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CleanupByRealm_NoConfig_StillDeletesStoresWithoutEvent()
    {
        // Arrange
        var service = CreateService();
        _mockRealmConfigStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmWorldstateConfigModel?)null);

        // Act
        var (status, response) = await service.CleanupByRealmAsync(
            new CleanupByRealmRequest { RealmId = _testRealmId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Stores still cleaned up
        _mockClockStore.Verify(s => s.DeleteAsync($"realm:{_testRealmId}", It.IsAny<CancellationToken>()), Times.Once);
        _mockRatioHistoryStore.Verify(s => s.DeleteAsync($"ratio:{_testRealmId}", It.IsAny<CancellationToken>()), Times.Once);

        // No event since config was null (no sentinel values per IMPLEMENTATION TENETS)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "worldstate.realm-config.deleted",
            It.IsAny<RealmConfigDeletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region CleanupByGameServiceAsync Tests

    [Fact]
    public async Task CleanupByGameService_WithCalendars_DeletesAllAndPublishesEvents()
    {
        // Arrange
        var service = CreateService();
        var calendar1 = CreateTestCalendar(templateCode: "cal1");
        var calendar2 = CreateTestCalendar(templateCode: "cal2");

        _mockQueryableCalendarStore
            .Setup(s => s.QueryAsync(
                It.IsAny<Expression<Func<CalendarTemplateModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarTemplateModel> { calendar1, calendar2 } as IReadOnlyList<CalendarTemplateModel>);

        // Act
        var (status, response) = await service.CleanupByGameServiceAsync(
            new CleanupByGameServiceRequest { GameServiceId = _testGameServiceId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify both calendars deleted
        _mockCalendarStore.Verify(s => s.DeleteAsync(
            $"calendar:{_testGameServiceId}:cal1", It.IsAny<CancellationToken>()), Times.Once);
        _mockCalendarStore.Verify(s => s.DeleteAsync(
            $"calendar:{_testGameServiceId}:cal2", It.IsAny<CancellationToken>()), Times.Once);

        // Verify events published for each
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "worldstate.calendar-template.deleted",
            It.IsAny<CalendarTemplateDeletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Verify caches invalidated for each
        _mockCalendarTemplateCache.Verify(c => c.Invalidate(_testGameServiceId, "cal1"), Times.Once);
        _mockCalendarTemplateCache.Verify(c => c.Invalidate(_testGameServiceId, "cal2"), Times.Once);
    }

    [Fact]
    public async Task CleanupByGameService_NoCalendars_ReturnsOk()
    {
        // Arrange
        var service = CreateService();
        _mockQueryableCalendarStore
            .Setup(s => s.QueryAsync(
                It.IsAny<Expression<Func<CalendarTemplateModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarTemplateModel>() as IReadOnlyList<CalendarTemplateModel>);

        // Act
        var (status, response) = await service.CleanupByGameServiceAsync(
            new CleanupByGameServiceRequest { GameServiceId = _testGameServiceId },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    #endregion

    #region Event Handler Tests

    [Fact]
    public async Task HandleCalendarTemplateUpdated_InvalidatesCalendarCache()
    {
        // Arrange
        var service = CreateService();
        var evt = new CalendarTemplateUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TemplateCode = "standard",
            GameServiceId = _testGameServiceId,
            GameHoursPerDay = 24,
            DayPeriods = new List<DayPeriodDefinition>(),
            Months = new List<MonthDefinition>(),
            Seasons = new List<SeasonDefinition>(),
            DaysPerYear = 60,
            MonthsPerYear = 2,
            SeasonsPerYear = 2,
            ChangedFields = new List<string> { "gameHoursPerDay" }
        };

        // Act
        await service.HandleCalendarTemplateUpdatedAsync(evt);

        // Assert
        _mockCalendarTemplateCache.Verify(c => c.Invalidate(_testGameServiceId, "standard"), Times.Once);
    }

    [Fact]
    public async Task HandleCalendarTemplateDeleted_InvalidatesCalendarCache()
    {
        // Arrange
        var service = CreateService();
        var evt = new CalendarTemplateDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TemplateCode = "standard",
            GameServiceId = _testGameServiceId,
            GameHoursPerDay = 24,
            DayPeriods = new List<DayPeriodDefinition>(),
            Months = new List<MonthDefinition>(),
            Seasons = new List<SeasonDefinition>(),
            DaysPerYear = 60,
            MonthsPerYear = 2,
            SeasonsPerYear = 2
        };

        // Act
        await service.HandleCalendarTemplateDeletedAsync(evt);

        // Assert
        _mockCalendarTemplateCache.Verify(c => c.Invalidate(_testGameServiceId, "standard"), Times.Once);
    }

    [Fact]
    public async Task HandleRatioChanged_InvalidatesClockCache()
    {
        // Arrange
        var service = CreateService();
        var evt = new WorldstateRatioChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RealmId = _testRealmId,
            PreviousRatio = 60.0f,
            NewRatio = 120.0f,
            Reason = TimeRatioChangeReason.AdminAdjustment
        };

        // Act
        await service.HandleRatioChangedAsync(evt);

        // Assert
        _mockRealmClockCache.Verify(c => c.Invalidate(_testRealmId), Times.Once);
    }

    [Fact]
    public async Task HandleRealmConfigDeleted_InvalidatesClockCache()
    {
        // Arrange
        var service = CreateService();
        var evt = new RealmConfigDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RealmId = _testRealmId,
            GameServiceId = _testGameServiceId,
            CalendarTemplateCode = "standard",
            CurrentTimeRatio = 60.0f,
            DowntimePolicy = DowntimePolicy.Pause,
            RealmEpoch = DateTimeOffset.UtcNow
        };

        // Act
        await service.HandleRealmConfigDeletedAsync(evt);

        // Assert
        _mockRealmClockCache.Verify(c => c.Invalidate(_testRealmId), Times.Once);
    }

    [Fact]
    public async Task HandleClockAdvanced_InvalidatesClockCache()
    {
        // Arrange
        var service = CreateService();
        var evt = new WorldstateClockAdvancedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RealmId = _testRealmId
        };

        // Act
        await service.HandleClockAdvancedAsync(evt);

        // Assert
        _mockRealmClockCache.Verify(c => c.Invalidate(_testRealmId), Times.Once);
    }

    #endregion
}
