using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Leaderboard;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Leaderboard.Tests;

/// <summary>
/// Unit tests for LeaderboardService.
/// Tests focus on validation logic and constructor validation.
/// Full CRUD and Redis Sorted Set operations are covered by HTTP tests.
/// </summary>
/// <remarks>
/// The LeaderboardService uses Redis Sorted Sets for rankings which require
/// real Redis infrastructure. These tests verify input validation and service construction;
/// HTTP tests verify business logic with actual sorted set operations.
/// </remarks>
public class LeaderboardServiceTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<ILogger<LeaderboardService>> _mockLogger;
    private readonly LeaderboardServiceConfiguration _configuration;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IStateStore<LeaderboardDefinitionData>> _mockDefinitionStore;

    public LeaderboardServiceTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockLogger = new Mock<ILogger<LeaderboardService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockDefinitionStore = new Mock<IStateStore<LeaderboardDefinitionData>>();

        _configuration = new LeaderboardServiceConfiguration
        {
            DefinitionStoreName = "test-definition",
            RankingStoreName = "test-ranking",
            SeasonStoreName = "test-season",
            MaxEntriesPerQuery = 100,
            ScoreUpdateBatchSize = 50,
            RankCacheTtlSeconds = 60
        };

        SetupMinimalMocks();
    }

    private void SetupMinimalMocks()
    {
        // Setup state store factory to return definition store mock
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LeaderboardDefinitionData>(It.IsAny<string>()))
            .Returns(_mockDefinitionStore.Object);

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
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private LeaderboardService CreateService()
    {
        return new LeaderboardService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            _configuration,
            _mockEventConsumer.Object);
    }

    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void ConstructorIsValid()
        => ServiceConstructorValidator.ValidateServiceConstructor<LeaderboardService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void Configuration_DefaultValues_AreValid()
    {
        var config = new LeaderboardServiceConfiguration();

        Assert.Equal(1000, config.MaxEntriesPerQuery);
        Assert.Equal(1000, config.ScoreUpdateBatchSize);
        Assert.Equal(60, config.RankCacheTtlSeconds);
        Assert.True(config.AutoArchiveOnSeasonEnd);
    }

    [Fact]
    public void Configuration_CanSetCustomValues()
    {
        var config = new LeaderboardServiceConfiguration
        {
            MaxEntriesPerQuery = 500,
            ScoreUpdateBatchSize = 200,
            RankCacheTtlSeconds = 120,
            AutoArchiveOnSeasonEnd = false
        };

        Assert.Equal(500, config.MaxEntriesPerQuery);
        Assert.Equal(200, config.ScoreUpdateBatchSize);
        Assert.Equal(120, config.RankCacheTtlSeconds);
        Assert.False(config.AutoArchiveOnSeasonEnd);
    }

    #endregion

    #region SubmitScore Entity Type Validation Tests

    [Fact]
    public async Task SubmitScoreAsync_EntityTypeNotAllowed_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var leaderboardId = "test-leaderboard";

        // Setup definition that only allows Account type
        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaderboardDefinitionData
            {
                GameServiceId = gameServiceId,
                LeaderboardId = leaderboardId,
                EntityTypes = new List<EntityType> { EntityType.Account },
                SortOrder = SortOrder.Descending,
                UpdateMode = UpdateMode.Replace
            });

        var request = new SubmitScoreRequest
        {
            GameServiceId = gameServiceId,
            LeaderboardId = leaderboardId,
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Character, // Not allowed
            Score = 100.0
        };

        // Act
        var (status, _) = await service.SubmitScoreAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region SubmitScoreBatch Size Validation Tests

    [Fact]
    public async Task SubmitScoreBatchAsync_ExceedsMaxBatchSize_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var leaderboardId = "test-leaderboard";

        // Setup definition
        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaderboardDefinitionData
            {
                GameServiceId = gameServiceId,
                LeaderboardId = leaderboardId,
                EntityTypes = new List<EntityType> { EntityType.Account },
                SortOrder = SortOrder.Descending,
                UpdateMode = UpdateMode.Replace
            });

        // Create batch that exceeds ScoreUpdateBatchSize (50 in test config)
        var scores = Enumerable.Range(0, 51)
            .Select(i => new BatchScoreEntry
            {
                EntityId = Guid.NewGuid(),
                EntityType = EntityType.Account,
                Score = i * 10.0
            })
            .ToList();

        var request = new SubmitScoreBatchRequest
        {
            GameServiceId = gameServiceId,
            LeaderboardId = leaderboardId,
            Scores = scores
        };

        // Act
        var (status, _) = await service.SubmitScoreBatchAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region GetTopRanks Validation Tests

    [Fact]
    public async Task GetTopRanksAsync_CountExceedsMax_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        var request = new GetTopRanksRequest
        {
            GameServiceId = Guid.NewGuid(),
            LeaderboardId = "test-leaderboard",
            Count = 101 // Exceeds MaxEntriesPerQuery (100 in test config)
        };

        // Act
        var (status, _) = await service.GetTopRanksAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task GetTopRanksAsync_ZeroCount_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        var request = new GetTopRanksRequest
        {
            GameServiceId = Guid.NewGuid(),
            LeaderboardId = "test-leaderboard",
            Count = 0
        };

        // Act
        var (status, _) = await service.GetTopRanksAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task GetTopRanksAsync_NegativeCount_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        var request = new GetTopRanksRequest
        {
            GameServiceId = Guid.NewGuid(),
            LeaderboardId = "test-leaderboard",
            Count = -5
        };

        // Act
        var (status, _) = await service.GetTopRanksAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task GetTopRanksAsync_NegativeOffset_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        var request = new GetTopRanksRequest
        {
            GameServiceId = Guid.NewGuid(),
            LeaderboardId = "test-leaderboard",
            Count = 10,
            Offset = -1
        };

        // Act
        var (status, _) = await service.GetTopRanksAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region GetRanksAround Validation Tests

    [Fact]
    public async Task GetRanksAroundAsync_NegativeCountBefore_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        var request = new GetRanksAroundRequest
        {
            GameServiceId = Guid.NewGuid(),
            LeaderboardId = "test-leaderboard",
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Account,
            CountBefore = -1,
            CountAfter = 5
        };

        // Act
        var (status, _) = await service.GetRanksAroundAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task GetRanksAroundAsync_NegativeCountAfter_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        var request = new GetRanksAroundRequest
        {
            GameServiceId = Guid.NewGuid(),
            LeaderboardId = "test-leaderboard",
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Account,
            CountBefore = 5,
            CountAfter = -1
        };

        // Act
        var (status, _) = await service.GetRanksAroundAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task GetRanksAroundAsync_TotalExceedsMax_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        // Total = CountBefore + CountAfter + 1 (entity itself) = 60 + 60 + 1 = 121 > 100
        var request = new GetRanksAroundRequest
        {
            GameServiceId = Guid.NewGuid(),
            LeaderboardId = "test-leaderboard",
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Account,
            CountBefore = 60,
            CountAfter = 60
        };

        // Act
        var (status, _) = await service.GetRanksAroundAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region CreateSeason Validation Tests

    [Fact]
    public async Task CreateSeasonAsync_NonSeasonalLeaderboard_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var leaderboardId = "test-leaderboard";

        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeaderboardDefinitionData
            {
                GameServiceId = gameServiceId,
                LeaderboardId = leaderboardId,
                EntityTypes = new List<EntityType> { EntityType.Account },
                SortOrder = SortOrder.Descending,
                UpdateMode = UpdateMode.Replace,
                IsSeasonal = false // Not seasonal
            });

        var request = new CreateSeasonRequest
        {
            GameServiceId = gameServiceId,
            LeaderboardId = leaderboardId
        };

        // Act
        var (status, _) = await service.CreateSeasonAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region Definition NotFound Tests

    [Fact]
    public async Task GetLeaderboardDefinitionAsync_NotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();

        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeaderboardDefinitionData?)null);

        var request = new GetLeaderboardDefinitionRequest
        {
            GameServiceId = Guid.NewGuid(),
            LeaderboardId = "non-existent"
        };

        // Act
        var (status, _) = await service.GetLeaderboardDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task SubmitScoreAsync_LeaderboardNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();

        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeaderboardDefinitionData?)null);

        var request = new SubmitScoreRequest
        {
            GameServiceId = Guid.NewGuid(),
            LeaderboardId = "non-existent",
            EntityId = Guid.NewGuid(),
            EntityType = EntityType.Account,
            Score = 100.0
        };

        // Act
        var (status, _) = await service.SubmitScoreAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task CreateSeasonAsync_LeaderboardNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();

        _mockDefinitionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeaderboardDefinitionData?)null);

        var request = new CreateSeasonRequest
        {
            GameServiceId = Guid.NewGuid(),
            LeaderboardId = "non-existent"
        };

        // Act
        var (status, _) = await service.CreateSeasonAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion
}
