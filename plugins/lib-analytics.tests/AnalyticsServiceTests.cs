using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Analytics;
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
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<AnalyticsService>();
    }

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
            GameServiceId = Guid.NewGuid(),
            RatingType = "ranked",
            MatchId = Guid.NewGuid(),
            Results = new List<MatchResult>()
        };

        // Act
        var (status, _) = await service.UpdateSkillRatingAsync(request, CancellationToken.None);

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
            GameServiceId = Guid.NewGuid(),
            RatingType = "ranked",
            MatchId = Guid.NewGuid(),
            Results = new List<MatchResult>
            {
                new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Account, Outcome = 1.0 }
            }
        };

        // Act
        var (status, _) = await service.UpdateSkillRatingAsync(request, CancellationToken.None);

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
            GameServiceId = Guid.NewGuid(),
            Limit = 0
        };

        // Act
        var (status, _) = await service.QueryControllerHistoryAsync(request, CancellationToken.None);

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
            GameServiceId = Guid.NewGuid(),
            Limit = -5
        };

        // Act
        var (status, _) = await service.QueryControllerHistoryAsync(request, CancellationToken.None);

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
            GameServiceId = Guid.NewGuid(),
            Limit = 10,
            StartTime = now,
            EndTime = now.AddHours(-1) // End before start
        };

        // Act
        var (status, _) = await service.QueryControllerHistoryAsync(request, CancellationToken.None);

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
            GameServiceId = Guid.NewGuid(),
            Limit = 0
        };

        // Act
        var (status, _) = await service.QueryEntitySummariesAsync(request, CancellationToken.None);

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
            GameServiceId = Guid.NewGuid(),
            Limit = -10
        };

        // Act
        var (status, _) = await service.QueryEntitySummariesAsync(request, CancellationToken.None);

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
            GameServiceId = Guid.NewGuid(),
            Limit = 10,
            Offset = -1
        };

        // Act
        var (status, _) = await service.QueryEntitySummariesAsync(request, CancellationToken.None);

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
            GameServiceId = Guid.NewGuid(),
            Limit = 10,
            MinEvents = -5
        };

        // Act
        var (status, _) = await service.QueryEntitySummariesAsync(request, CancellationToken.None);

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
            GameServiceId = Guid.NewGuid(),
            Limit = 10,
            SortBy = "invalid_sort_field"
        };

        // Act
        var (status, _) = await service.QueryEntitySummariesAsync(request, CancellationToken.None);

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
            GameServiceId = Guid.NewGuid(),
            Limit = 10,
            SortBy = sortBy
        };

        // Act
        var (status, _) = await service.QueryEntitySummariesAsync(request, CancellationToken.None);

        // Assert - Should NOT be BadRequest (might be InternalServerError due to no state store mock, but not BadRequest)
        Assert.NotEqual(StatusCodes.BadRequest, status);
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
            GameServiceId = Guid.NewGuid(),
            EventType = "kill",
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Character,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var (status, _) = await service.IngestEventAsync(request, CancellationToken.None);

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
                    GameServiceId = Guid.NewGuid(),
                    EventType = "kill",
                    EntityId = Guid.NewGuid(),
                    EntityType = EntityType.Character,
                    Timestamp = DateTimeOffset.UtcNow
                }
            }
        };

        // Act
        var (status, response) = await service.IngestEventBatchAsync(request, CancellationToken.None);

        // Assert - Batch returns OK but reports rejections
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Accepted);
        Assert.Equal(1, response.Rejected);
    }

    #endregion
}
