using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Analytics;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Analytics.Tests;

/// <summary>
/// Unit tests for AnalyticsService.
/// Tests focus on validation logic and constructor validation.
/// Full CRUD, Glicko-2 calculations, and integration flows are covered by HTTP tests.
/// </summary>
/// <remarks>
/// The AnalyticsService has complex internal state store dependencies (buffering, sorted sets,
/// multi-store operations) that make comprehensive unit testing via mocks impractical.
/// These tests verify input validation and service construction; HTTP tests verify business logic.
/// </remarks>
public class AnalyticsServiceTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IGameServiceClient> _mockGameServiceClient;
    private readonly Mock<IGameSessionClient> _mockGameSessionClient;
    private readonly Mock<IRealmClient> _mockRealmClient;
    private readonly Mock<ICharacterClient> _mockCharacterClient;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<AnalyticsService>> _mockLogger;
    private readonly AnalyticsServiceConfiguration _configuration;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    public AnalyticsServiceTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockGameServiceClient = new Mock<IGameServiceClient>();
        _mockGameSessionClient = new Mock<IGameSessionClient>();
        _mockRealmClient = new Mock<IRealmClient>();
        _mockCharacterClient = new Mock<ICharacterClient>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<AnalyticsService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        _configuration = new AnalyticsServiceConfiguration
        {
            Glicko2DefaultRating = 1500.0,
            Glicko2DefaultDeviation = 350.0,
            Glicko2DefaultVolatility = 0.06,
            Glicko2SystemConstant = 0.5,
            EventBufferSize = 100,
            EventBufferFlushIntervalSeconds = 5,
            ResolutionCacheTtlSeconds = 300,
            SessionMappingTtlSeconds = 3600
        };

        // Minimal mock setup - just enough to construct the service
        SetupMinimalMocks();
    }

    private void SetupMinimalMocks()
    {
        // Return MySQL backend to prevent buffering attempts (simplifies tests)
        _mockStateStoreFactory
            .Setup(f => f.GetBackendType(It.IsAny<string>()))
            .Returns(StateBackend.MySql);

        // Mock the lock provider
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(false);
        mockLockResponse.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(p => p.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Default TryPublishErrorAsync to prevent exceptions during error handling
        _mockMessageBus
            .Setup(m => m.TryPublishErrorAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Mock JSON queryable store for entity summary queries
        var mockJsonQueryStore = new Mock<IJsonQueryableStateStore<EntitySummaryData>>();
        mockJsonQueryStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<EntitySummaryData>(
                new List<JsonQueryResult<EntitySummaryData>>(), 0, 0, 10));
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<EntitySummaryData>(It.IsAny<string>()))
            .Returns(mockJsonQueryStore.Object);
    }

    private AnalyticsService CreateService()
    {
        return new AnalyticsService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockGameServiceClient.Object,
            _mockGameSessionClient.Object,
            _mockRealmClient.Object,
            _mockCharacterClient.Object,
            _mockLockProvider.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object,
            _mockTelemetryProvider.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Configuration_DefaultValues_AreValid()
    {
        var config = new AnalyticsServiceConfiguration();

        Assert.Equal(1500.0, config.Glicko2DefaultRating);
        Assert.Equal(350.0, config.Glicko2DefaultDeviation);
        Assert.Equal(0.06, config.Glicko2DefaultVolatility);
        Assert.Equal(0.5, config.Glicko2SystemConstant);
        Assert.Equal(1000, config.EventBufferSize);
        Assert.Equal(5, config.EventBufferFlushIntervalSeconds);
        Assert.Equal(300, config.ResolutionCacheTtlSeconds);
        Assert.Equal(3600, config.SessionMappingTtlSeconds);
        Assert.Equal(30, config.RatingUpdateLockExpirySeconds);
    }

    [Fact]
    public void Configuration_CanSetCustomValues()
    {
        var config = new AnalyticsServiceConfiguration
        {
            Glicko2DefaultRating = 1200.0,
            Glicko2DefaultDeviation = 400.0,
            Glicko2DefaultVolatility = 0.08,
            Glicko2SystemConstant = 0.6,
            EventBufferSize = 500
        };

        Assert.Equal(1200.0, config.Glicko2DefaultRating);
        Assert.Equal(400.0, config.Glicko2DefaultDeviation);
        Assert.Equal(0.08, config.Glicko2DefaultVolatility);
        Assert.Equal(0.6, config.Glicko2SystemConstant);
        Assert.Equal(500, config.EventBufferSize);
    }

    #endregion

    #region UpdateSkillRating Validation Tests

    [Fact]
    public async Task UpdateSkillRatingAsync_EmptyResults_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateSkillRatingRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = Guid.NewGuid().ToString(),
            RatingType = "ranked",
            MatchId = Guid.NewGuid(),
            Results = new List<MatchResult>()
        };

        // Act
        var (status, _) = await service.UpdateSkillRatingAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task UpdateSkillRatingAsync_SinglePlayer_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateSkillRatingRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = Guid.NewGuid().ToString(),
            RatingType = "ranked",
            MatchId = Guid.NewGuid(),
            Results = new List<MatchResult>
            {
                new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Account, Outcome = 1.0 }
            }
        };

        // Act
        var (status, _) = await service.UpdateSkillRatingAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region QueryControllerHistory Validation Tests

    [Fact]
    public async Task QueryControllerHistoryAsync_ZeroLimit_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryControllerHistoryRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = Guid.NewGuid().ToString(),
            Limit = 0
        };

        // Act
        var (status, _) = await service.QueryControllerHistoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task QueryControllerHistoryAsync_NegativeLimit_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryControllerHistoryRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = Guid.NewGuid().ToString(),
            Limit = -5
        };

        // Act
        var (status, _) = await service.QueryControllerHistoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task QueryControllerHistoryAsync_EndTimeBeforeStartTime_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var now = DateTimeOffset.UtcNow;
        var request = new QueryControllerHistoryRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = Guid.NewGuid().ToString(),
            Limit = 10,
            StartTime = now,
            EndTime = now.AddHours(-1) // End before start
        };

        // Act
        var (status, _) = await service.QueryControllerHistoryAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region QueryEntitySummaries Validation Tests

    [Fact]
    public async Task QueryEntitySummariesAsync_ZeroLimit_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryEntitySummariesRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = Guid.NewGuid().ToString(),
            Limit = 0
        };

        // Act
        var (status, _) = await service.QueryEntitySummariesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task QueryEntitySummariesAsync_NegativeLimit_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryEntitySummariesRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = Guid.NewGuid().ToString(),
            Limit = -10
        };

        // Act
        var (status, _) = await service.QueryEntitySummariesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task QueryEntitySummariesAsync_NegativeOffset_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryEntitySummariesRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = Guid.NewGuid().ToString(),
            Limit = 10,
            Offset = -1
        };

        // Act
        var (status, _) = await service.QueryEntitySummariesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task QueryEntitySummariesAsync_NegativeMinEvents_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryEntitySummariesRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = Guid.NewGuid().ToString(),
            Limit = 10,
            MinEvents = -5
        };

        // Act
        var (status, _) = await service.QueryEntitySummariesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task QueryEntitySummariesAsync_InvalidSortBy_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryEntitySummariesRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = Guid.NewGuid().ToString(),
            Limit = 10,
            SortBy = "invalid_sort_field"
        };

        // Act
        var (status, _) = await service.QueryEntitySummariesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Theory]
    [InlineData("totalevents")]
    [InlineData("firsteventat")]
    [InlineData("lasteventat")]
    [InlineData("eventcount")]
    [InlineData("TotalEvents")] // Case-insensitive
    [InlineData("FIRSTEVENTAT")] // Case-insensitive
    public async Task QueryEntitySummariesAsync_ValidSortBy_DoesNotReturnBadRequest(string sortBy)
    {
        // Arrange
        var service = CreateService();
        var request = new QueryEntitySummariesRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = Guid.NewGuid().ToString(),
            Limit = 10,
            SortBy = sortBy
        };

        // Act
        var (status, _) = await service.QueryEntitySummariesAsync(request, TestContext.Current.CancellationToken);

        // Assert - Should NOT be BadRequest (might be InternalServerError due to no state store mock, but not BadRequest)
        Assert.NotEqual(StatusCodes.BadRequest, status);
    }

    #endregion

    #region FlushBufferedEvents Metric Emission Tests

    /// <summary>
    /// Creates a fully configured state store factory mock for Redis-backed buffer flush tests.
    /// Uses a fresh factory mock to avoid Moq generic type resolution conflicts.
    /// </summary>
    private (Mock<IStateStoreFactory> factory,
            Mock<ICacheableStateStore<BufferedAnalyticsEvent>> bufferStore,
            Mock<ICacheableStateStore<object>> indexStore,
            Mock<IStateStore<EntitySummaryData>> summaryStore) CreateRedisBackedStoreFactory()
    {
        var factory = new Mock<IStateStoreFactory>();

        // Redis backend for summary store (enables buffering)
        factory.Setup(f => f.GetBackendType(It.IsAny<string>())).Returns(StateBackend.Redis);

        // Cacheable stores for event buffer
        // Order matters: <object> first, then <BufferedAnalyticsEvent> — Moq/Castle
        // generic method dispatch requires the specific internal type setup last
        var bufferStore = new Mock<ICacheableStateStore<BufferedAnalyticsEvent>>();
        var indexStore = new Mock<ICacheableStateStore<object>>();
        factory.Setup(f => f.GetCacheableStore<object>(It.IsAny<string>())).Returns(indexStore.Object);
        factory.Setup(f => f.GetCacheableStore<BufferedAnalyticsEvent>(It.IsAny<string>())).Returns(bufferStore.Object);

        // Summary data store with ETag support
        var summaryStore = new Mock<IStateStore<EntitySummaryData>>();
        factory.Setup(f => f.GetStore<EntitySummaryData>(It.IsAny<string>())).Returns(summaryStore.Object);

        // Remaining stores needed by constructor (default mocks)
        factory.Setup(f => f.GetStore<SkillRatingData>(It.IsAny<string>())).Returns(new Mock<IStateStore<SkillRatingData>>().Object);
        factory.Setup(f => f.GetStore<ControllerHistoryData>(It.IsAny<string>())).Returns(new Mock<IStateStore<ControllerHistoryData>>().Object);
        factory.Setup(f => f.GetStore<GameServiceCacheEntry>(It.IsAny<string>())).Returns(new Mock<IStateStore<GameServiceCacheEntry>>().Object);
        factory.Setup(f => f.GetStore<GameSessionMappingData>(It.IsAny<string>())).Returns(new Mock<IStateStore<GameSessionMappingData>>().Object);
        factory.Setup(f => f.GetStore<RealmGameServiceCacheEntry>(It.IsAny<string>())).Returns(new Mock<IStateStore<RealmGameServiceCacheEntry>>().Object);
        factory.Setup(f => f.GetStore<CharacterRealmCacheEntry>(It.IsAny<string>())).Returns(new Mock<IStateStore<CharacterRealmCacheEntry>>().Object);
        factory.Setup(f => f.GetStore<string>(It.IsAny<string>())).Returns(new Mock<IStateStore<string>>().Object);

        var mockJsonQueryStore = new Mock<IJsonQueryableStateStore<EntitySummaryData>>();
        mockJsonQueryStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<EntitySummaryData>(
                new List<JsonQueryResult<EntitySummaryData>>(), 0, 0, 10));
        factory.Setup(f => f.GetJsonQueryableStore<EntitySummaryData>(It.IsAny<string>())).Returns(mockJsonQueryStore.Object);
        factory.Setup(f => f.GetJsonQueryableStore<ControllerHistoryData>(It.IsAny<string>()))
            .Returns(new Mock<IJsonQueryableStateStore<ControllerHistoryData>>().Object);

        return (factory, bufferStore, indexStore, summaryStore);
    }

    private AnalyticsService CreateRedisBackedService(Mock<IStateStoreFactory> factory)
    {
        return new AnalyticsService(
            _mockMessageBus.Object,
            factory.Object,
            _mockGameServiceClient.Object,
            _mockGameSessionClient.Object,
            _mockRealmClient.Object,
            _mockCharacterClient.Object,
            _mockLockProvider.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object,
            _mockTelemetryProvider.Object);
    }

    [Fact]
    public async Task FlushBufferedEvents_ScoreEvent_EmitsScoreProcessedCounter()
    {
        // Arrange — configure Redis backend to enable buffering/flushing
        var gameServiceId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var entityType = EntityType.Character;
        var eventType = "kills";
        var eventValue = 5.0;
        var eventKey = $"analytics-event-buffer-entry:{Guid.NewGuid()}";

        var (storeFactory, mockEventBufferStore, mockEventBufferIndexStore, mockSummaryDataStore) =
            CreateRedisBackedStoreFactory();

        // Buffer size = 10 with count >= 10 triggers flush; range returns 1 entry
        // so entries.Count (1) < batchSize (10) exits the flush loop after one iteration
        _configuration.EventBufferSize = 10;
        _configuration.EventBufferFlushIntervalSeconds = 0;
        _configuration.EventBufferLockExpiryBaseSeconds = 10;

        // SortedSetCountAsync returns 10 (>= buffer size, triggers flush)
        mockEventBufferIndexStore
            .Setup(s => s.SortedSetCountAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);

        // SortedSetRangeByRankAsync returns one entry (< batchSize, exits loop)
        mockEventBufferIndexStore
            .Setup(s => s.SortedSetRangeByRankAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string member, double score)>
            {
                (eventKey, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            });

        // GetBulkAsync returns the buffered event
        mockEventBufferStore
            .Setup(s => s.GetBulkAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, BufferedAnalyticsEvent>
            {
                [eventKey] = new BufferedAnalyticsEvent
                {
                    EventId = Guid.NewGuid(),
                    ServiceType = AnalyticsServiceType.Game,
                    ServiceId = gameServiceId.ToString(),
                    EntityId = entityId,
                    EntityType = entityType,
                    EventType = eventType,
                    Timestamp = DateTimeOffset.UtcNow,
                    Value = eventValue,
                    SessionId = null
                }
            });

        // GetWithETagAsync returns null summary (new entity)
        mockSummaryDataStore
            .Setup(s => s.GetWithETagAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((EntitySummaryData?)null, (string?)null));

        // TrySaveAsync succeeds (returns new ETag)
        mockSummaryDataStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(), It.IsAny<EntitySummaryData>(),
                It.IsAny<string>(), It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Lock succeeds
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        mockLockResponse.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(p => p.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Capture RecordCounter calls
        var capturedCounterCalls = new List<(string component, string metric, long value, KeyValuePair<string, object?>[] tags)>();
        _mockTelemetryProvider
            .Setup(t => t.RecordCounter(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<KeyValuePair<string, object?>[]>()))
            .Callback<string, string, long, KeyValuePair<string, object?>[]>(
                (component, metric, value, tags) =>
                    capturedCounterCalls.Add((component, metric, value, tags)));

        // PublishAnalyticsScoreUpdatedAsync setup (generated extension method calls TryPublishAsync)
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateRedisBackedService(storeFactory);

        // Act
        var request = new IngestEventRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = gameServiceId.ToString(),
            EventType = eventType,
            EntityId = entityId,
            EntityType = entityType,
            Timestamp = DateTimeOffset.UtcNow,
            Value = eventValue
        };
        await service.IngestEventAsync(request, TestContext.Current.CancellationToken);

        // Assert — verify score processed counter was emitted
        var scoreCounterCall = capturedCounterCalls.FirstOrDefault(
            c => c.metric == TelemetryMetrics.AnalyticsScoreProcessed);
        Assert.NotNull(scoreCounterCall.metric);
        Assert.Equal("bannou.analytics", scoreCounterCall.component);
        Assert.Equal((long)eventValue, scoreCounterCall.value);

        var scoreTags = scoreCounterCall.tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal(gameServiceId.ToString(), scoreTags["service_id"]);
        Assert.Equal(entityType.ToString(), scoreTags["entity_type"]);
        Assert.Equal(eventType, scoreTags["score_type"]);

        // Assert — verify events processed counter was emitted
        var eventsCounterCall = capturedCounterCalls.FirstOrDefault(
            c => c.metric == TelemetryMetrics.AnalyticsEventsProcessed);
        Assert.NotNull(eventsCounterCall.metric);
        Assert.Equal("bannou.analytics", eventsCounterCall.component);
        Assert.Equal(1, eventsCounterCall.value);

        var eventsTags = eventsCounterCall.tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal(gameServiceId.ToString(), eventsTags["service_id"]);
    }

    [Fact]
    public async Task FlushBufferedEvents_NoValueEvent_DoesNotEmitScoreCounter()
    {
        // Arrange — event without a value should not produce a score counter
        var gameServiceId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var eventKey = $"analytics-event-buffer-entry:{Guid.NewGuid()}";

        var (storeFactory, mockEventBufferStore, mockEventBufferIndexStore, mockSummaryDataStore) =
            CreateRedisBackedStoreFactory();

        _configuration.EventBufferSize = 10;
        _configuration.EventBufferFlushIntervalSeconds = 0;
        _configuration.EventBufferLockExpiryBaseSeconds = 10;

        mockEventBufferIndexStore
            .Setup(s => s.SortedSetCountAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);

        mockEventBufferIndexStore
            .Setup(s => s.SortedSetRangeByRankAsync(
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string member, double score)>
            {
                (eventKey, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            });

        mockEventBufferStore
            .Setup(s => s.GetBulkAsync(
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, BufferedAnalyticsEvent>
            {
                [eventKey] = new BufferedAnalyticsEvent
                {
                    EventId = Guid.NewGuid(),
                    ServiceType = AnalyticsServiceType.Game,
                    ServiceId = gameServiceId.ToString(),
                    EntityId = entityId,
                    EntityType = EntityType.Character,
                    EventType = "session.created",
                    Timestamp = DateTimeOffset.UtcNow,
                    Value = null, // No value — no score event
                    SessionId = null
                }
            });

        mockSummaryDataStore
            .Setup(s => s.GetWithETagAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((EntitySummaryData?)null, (string?)null));

        mockSummaryDataStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(), It.IsAny<EntitySummaryData>(),
                It.IsAny<string>(), It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        mockLockResponse.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(p => p.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        var capturedCounterCalls = new List<(string component, string metric, long value, KeyValuePair<string, object?>[] tags)>();
        _mockTelemetryProvider
            .Setup(t => t.RecordCounter(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<KeyValuePair<string, object?>[]>()))
            .Callback<string, string, long, KeyValuePair<string, object?>[]>(
                (component, metric, value, tags) =>
                    capturedCounterCalls.Add((component, metric, value, tags)));

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateRedisBackedService(storeFactory);

        // Act
        var request = new IngestEventRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = gameServiceId.ToString(),
            EventType = "session.created",
            EntityId = entityId,
            EntityType = EntityType.Character,
            Timestamp = DateTimeOffset.UtcNow,
            Value = null // No value
        };
        await service.IngestEventAsync(request, TestContext.Current.CancellationToken);

        // Assert — score counter should NOT be emitted (no value → no score event)
        var scoreCounterCalls = capturedCounterCalls.Where(
            c => c.metric == TelemetryMetrics.AnalyticsScoreProcessed).ToList();
        Assert.Empty(scoreCounterCalls);

        // Assert — events processed counter SHOULD still be emitted
        var eventsCounterCall = capturedCounterCalls.FirstOrDefault(
            c => c.metric == TelemetryMetrics.AnalyticsEventsProcessed);
        Assert.NotNull(eventsCounterCall.metric);
        Assert.Equal(1, eventsCounterCall.value);
    }

    #endregion

    #region IngestEvent Backend Tests

    [Fact]
    public async Task IngestEventAsync_NonRedisBackend_ReturnsInternalServerError()
    {
        // Arrange - MySql backend configured in SetupMinimalMocks
        var service = CreateService();
        var request = new IngestEventRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = Guid.NewGuid().ToString(),
            EventType = "kill",
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Character,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var (status, _) = await service.IngestEventAsync(request, TestContext.Current.CancellationToken);

        // Assert - Analytics requires Redis for buffered ingestion
        Assert.Equal(StatusCodes.InternalServerError, status);
    }

    [Fact]
    public async Task IngestEventBatchAsync_NonRedisBackend_ReturnsOkWithRejections()
    {
        // Arrange - MySql backend configured in SetupMinimalMocks
        var service = CreateService();
        var request = new IngestEventBatchRequest
        {
            Events = new List<IngestEventRequest>
            {
                new()
                {
                    ServiceType = AnalyticsServiceType.Game,
                    ServiceId = Guid.NewGuid().ToString(),
                    EventType = "kill",
                    EntityId = Guid.NewGuid(),
                    EntityType = EntityType.Character,
                    Timestamp = DateTimeOffset.UtcNow
                }
            }
        };

        // Act
        var (status, response) = await service.IngestEventBatchAsync(request, TestContext.Current.CancellationToken);

        // Assert - Batch returns OK but reports rejections
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Accepted);
        Assert.Equal(1, response.Rejected);
    }

    #endregion

    #region Auth Event Handler Tests (#142)

    [Fact]
    public async Task HandleAuthLoginSuccessfulAsync_BuffersEventWithSystemServiceType()
    {
        // Arrange — Redis backend required for buffering
        var accountId = Guid.NewGuid();
        var (storeFactory, mockEventBufferStore, mockEventBufferIndexStore, _) =
            CreateRedisBackedStoreFactory();

        // Capture saved buffered event
        BufferedAnalyticsEvent? capturedEvent = null;
        mockEventBufferStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<BufferedAnalyticsEvent>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BufferedAnalyticsEvent, StateOptions?, CancellationToken>(
                (_, evt, _, _) => capturedEvent = evt)
            .ReturnsAsync("etag");

        // Buffer count below threshold to skip flush
        mockEventBufferIndexStore
            .Setup(s => s.SortedSetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = CreateRedisBackedService(storeFactory);

        // Act
        await service.HandleAuthLoginSuccessfulAsync(new AuthLoginSuccessfulEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AccountId = accountId,
            Username = "testuser",
            SessionId = Guid.NewGuid()
        });

        // Assert — captured event uses System service type and auth service ID
        Assert.NotNull(capturedEvent);
        Assert.Equal(AnalyticsServiceType.System, capturedEvent.ServiceType);
        Assert.Equal("auth", capturedEvent.ServiceId);
        Assert.Equal(EntityType.Account, capturedEvent.EntityType);
        Assert.Equal(accountId, capturedEvent.EntityId);
        Assert.Equal("auth.login.successful", capturedEvent.EventType);
        Assert.Equal(1, capturedEvent.Value);
    }

    [Fact]
    public async Task HandleAuthLoginFailedAsync_WithAccountId_BuffersEvent()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var (storeFactory, mockEventBufferStore, mockEventBufferIndexStore, _) =
            CreateRedisBackedStoreFactory();

        BufferedAnalyticsEvent? capturedEvent = null;
        mockEventBufferStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<BufferedAnalyticsEvent>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BufferedAnalyticsEvent, StateOptions?, CancellationToken>(
                (_, evt, _, _) => capturedEvent = evt)
            .ReturnsAsync("etag");

        mockEventBufferIndexStore
            .Setup(s => s.SortedSetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = CreateRedisBackedService(storeFactory);

        // Act
        await service.HandleAuthLoginFailedAsync(new AuthLoginFailedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Username = "testuser",
            Reason = AuthLoginFailedReason.InvalidCredentials,
            AccountId = accountId
        });

        // Assert — event buffered with the account ID
        Assert.NotNull(capturedEvent);
        Assert.Equal(AnalyticsServiceType.System, capturedEvent.ServiceType);
        Assert.Equal("auth", capturedEvent.ServiceId);
        Assert.Equal(accountId, capturedEvent.EntityId);
        Assert.Equal("auth.login.failed", capturedEvent.EventType);
    }

    [Fact]
    public async Task HandleAuthLoginFailedAsync_WithNullAccountId_DropsEvent()
    {
        // Arrange — null accountId means no target account (enumeration attempt)
        var (storeFactory, mockEventBufferStore, mockEventBufferIndexStore, _) =
            CreateRedisBackedStoreFactory();

        BufferedAnalyticsEvent? capturedEvent = null;
        mockEventBufferStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<BufferedAnalyticsEvent>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BufferedAnalyticsEvent, StateOptions?, CancellationToken>(
                (_, evt, _, _) => capturedEvent = evt)
            .ReturnsAsync("etag");

        mockEventBufferIndexStore
            .Setup(s => s.SortedSetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = CreateRedisBackedService(storeFactory);

        // Act
        await service.HandleAuthLoginFailedAsync(new AuthLoginFailedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Username = "unknown_user",
            Reason = AuthLoginFailedReason.AccountNotFound,
            AccountId = null  // No account matched — drop from per-account aggregation
        });

        // Assert — event was NOT buffered (null accountId dropped)
        Assert.Null(capturedEvent);
    }

    [Fact]
    public async Task HandleAuthMfaEnabledAsync_BuffersEventWithCorrectEventType()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var (storeFactory, mockEventBufferStore, mockEventBufferIndexStore, _) =
            CreateRedisBackedStoreFactory();

        BufferedAnalyticsEvent? capturedEvent = null;
        mockEventBufferStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<BufferedAnalyticsEvent>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, BufferedAnalyticsEvent, StateOptions?, CancellationToken>(
                (_, evt, _, _) => capturedEvent = evt)
            .ReturnsAsync("etag");

        mockEventBufferIndexStore
            .Setup(s => s.SortedSetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = CreateRedisBackedService(storeFactory);

        // Act
        await service.HandleAuthMfaEnabledAsync(new AuthMfaEnabledEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AccountId = accountId
        });

        // Assert — correct event type for MFA enabled
        Assert.NotNull(capturedEvent);
        Assert.Equal("auth.mfa.enabled", capturedEvent.EventType);
        Assert.Equal(accountId, capturedEvent.EntityId);
        Assert.Equal(AnalyticsServiceType.System, capturedEvent.ServiceType);
    }

    #endregion

    #region ResetEntitySummaries Tests (#707)

    [Fact]
    public async Task ResetEntitySummariesAsync_LockFails_ReturnsConflict()
    {
        // Arrange — default mock setup has lock returning failure
        var service = CreateService();
        var request = new ResetEntitySummariesRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = Guid.NewGuid().ToString(),
            DryRun = false
        };

        // Act
        var (status, _) = await service.ResetEntitySummariesAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task ResetEntitySummariesAsync_DryRun_ReturnsCountWithoutDeleting()
    {
        // Arrange — lock succeeds, JsonCountAsync returns 42
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        mockLockResponse.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(p => p.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        var mockJsonQueryStore = new Mock<IJsonQueryableStateStore<EntitySummaryData>>();
        mockJsonQueryStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);
        // Also need the paged query setup (used by constructor via SetupMinimalMocks)
        mockJsonQueryStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<EntitySummaryData>(
                new List<JsonQueryResult<EntitySummaryData>>(), 0, 0, 10));
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<EntitySummaryData>(It.IsAny<string>()))
            .Returns(mockJsonQueryStore.Object);

        // Capture query conditions to verify filters
        IReadOnlyList<QueryCondition>? capturedConditions = null;
        mockJsonQueryStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, CancellationToken>((cond, _) => capturedConditions = cond)
            .ReturnsAsync(42);

        var service = CreateService();
        var serviceId = Guid.NewGuid().ToString();
        var request = new ResetEntitySummariesRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = serviceId,
            DryRun = true // Default, but explicit for clarity
        };

        // Act
        var (status, response) = await service.ResetEntitySummariesAsync(request, TestContext.Current.CancellationToken);

        // Assert — returns count without deleting
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(42, response.DeletedCount);

        // Assert — query conditions include serviceType and serviceId
        Assert.NotNull(capturedConditions);
        Assert.Equal(2, capturedConditions.Count);
        Assert.Equal("$.ServiceType", capturedConditions[0].Path);
        Assert.Equal("$.ServiceId", capturedConditions[1].Path);
    }

    [Fact]
    public async Task ResetEntitySummariesAsync_DryRunWithFilters_IncludesEntityTypeAndEntityIdConditions()
    {
        // Arrange — lock succeeds
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        mockLockResponse.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(p => p.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        var mockJsonQueryStore = new Mock<IJsonQueryableStateStore<EntitySummaryData>>();
        IReadOnlyList<QueryCondition>? capturedConditions = null;
        mockJsonQueryStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, CancellationToken>((cond, _) => capturedConditions = cond)
            .ReturnsAsync(1);
        mockJsonQueryStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<EntitySummaryData>(
                new List<JsonQueryResult<EntitySummaryData>>(), 0, 0, 10));
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<EntitySummaryData>(It.IsAny<string>()))
            .Returns(mockJsonQueryStore.Object);

        var service = CreateService();
        var entityId = Guid.NewGuid();
        var request = new ResetEntitySummariesRequest
        {
            ServiceType = AnalyticsServiceType.System,
            ServiceId = "auth",
            EntityType = EntityType.Account,
            EntityId = entityId,
            DryRun = true
        };

        // Act
        var (status, response) = await service.ResetEntitySummariesAsync(request, TestContext.Current.CancellationToken);

        // Assert — 4 conditions: serviceType, serviceId, entityType, entityId
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(capturedConditions);
        Assert.Equal(4, capturedConditions.Count);
        Assert.Equal("$.ServiceType", capturedConditions[0].Path);
        Assert.Equal("$.ServiceId", capturedConditions[1].Path);
        Assert.Equal("$.EntityType", capturedConditions[2].Path);
        Assert.Equal("$.EntityId", capturedConditions[3].Path);
    }

    [Fact]
    public async Task ResetEntitySummariesAsync_NotDryRun_DeletesMatchingRecords()
    {
        // Arrange — lock succeeds
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        mockLockResponse.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider
            .Setup(p => p.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Setup query to return 2 items on first call, then 0 items on second call
        var mockJsonQueryStore = new Mock<IJsonQueryableStateStore<EntitySummaryData>>();
        var callCount = 0;
        var summaryKey1 = "analytics-entity:Game:svc1:Character:id1";
        var summaryKey2 = "analytics-entity:Game:svc1:Character:id2";
        mockJsonQueryStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new JsonPagedResult<EntitySummaryData>(
                        new List<JsonQueryResult<EntitySummaryData>>
                        {
                            new(summaryKey1, new EntitySummaryData { EntityId = Guid.NewGuid(), EntityType = EntityType.Character }),
                            new(summaryKey2, new EntitySummaryData { EntityId = Guid.NewGuid(), EntityType = EntityType.Character })
                        }, 0, 0, 100);
                }
                return new JsonPagedResult<EntitySummaryData>(
                    new List<JsonQueryResult<EntitySummaryData>>(), 0, 0, 100);
            });
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<EntitySummaryData>(It.IsAny<string>()))
            .Returns(mockJsonQueryStore.Object);

        // Track delete calls
        var deletedKeys = new List<string>();
        var mockSummaryDataStore = new Mock<IStateStore<EntitySummaryData>>();
        mockSummaryDataStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => deletedKeys.Add(key))
            .ReturnsAsync(true);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<EntitySummaryData>(It.IsAny<string>()))
            .Returns(mockSummaryDataStore.Object);

        var service = CreateService();
        var request = new ResetEntitySummariesRequest
        {
            ServiceType = AnalyticsServiceType.Game,
            ServiceId = "svc1",
            DryRun = false
        };

        // Act
        var (status, response) = await service.ResetEntitySummariesAsync(request, TestContext.Current.CancellationToken);

        // Assert — 2 records deleted
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.DeletedCount);
        Assert.Equal(2, deletedKeys.Count);
        Assert.Contains(summaryKey1, deletedKeys);
        Assert.Contains(summaryKey2, deletedKeys);
    }

    #endregion
}
