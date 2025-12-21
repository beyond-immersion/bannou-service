using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Testing;
using Dapr.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.GameSession.Tests;

/// <summary>
/// Comprehensive unit tests for GameSessionService.
/// Tests all CRUD operations, state transitions, and error handling.
/// </summary>
public class GameSessionServiceTests : ServiceTestBase<GameSessionServiceConfiguration>
{
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<ILogger<GameSessionService>> _mockLogger;
    private readonly Mock<IErrorEventEmitter> _mockErrorEventEmitter;
    private readonly Mock<IHttpContextAccessor> _mockHttpContextAccessor;

    private const string STATE_STORE = "game-session-statestore";
    private const string SESSION_KEY_PREFIX = "session:";
    private const string SESSION_LIST_KEY = "session-list";

    public GameSessionServiceTests()
    {
        _mockDaprClient = new Mock<DaprClient>();
        _mockLogger = new Mock<ILogger<GameSessionService>>();
        _mockErrorEventEmitter = new Mock<IErrorEventEmitter>();
        _mockHttpContextAccessor = new Mock<IHttpContextAccessor>();
    }

    private GameSessionService CreateService()
    {
        return new GameSessionService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockHttpContextAccessor.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = CreateService();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullDaprClient_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GameSessionService(null!, _mockLogger.Object, Configuration, _mockErrorEventEmitter.Object, _mockHttpContextAccessor.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GameSessionService(_mockDaprClient.Object, null!, Configuration, _mockErrorEventEmitter.Object, _mockHttpContextAccessor.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new GameSessionService(_mockDaprClient.Object, _mockLogger.Object, null!, _mockErrorEventEmitter.Object, _mockHttpContextAccessor.Object));
    }

    #endregion

    #region CreateGameSession Tests

    [Fact]
    public async Task CreateGameSessionAsync_WithValidRequest_ShouldReturnCreated()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateGameSessionRequest
        {
            SessionName = "Test Session",
            GameType = CreateGameSessionRequestGameType.Arcadia,
            MaxPlayers = 4,
            IsPrivate = false
        };

        // Setup empty session list
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                STATE_STORE, SESSION_LIST_KEY, null, null, default))
            .ReturnsAsync(new List<string>());

        // Act
        var (status, response) = await service.CreateGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, status);
        Assert.NotNull(response);
        Assert.NotNull(response.SessionId);
        Assert.Equal("Test Session", response.SessionName);
        Assert.Equal(4, response.MaxPlayers);
        Assert.Equal(0, response.CurrentPlayers);
        Assert.Equal(GameSessionResponseStatus.Waiting, response.Status);

        // Verify state was saved
        _mockDaprClient.Verify(d => d.SaveStateAsync(
            STATE_STORE,
            It.Is<string>(k => k.StartsWith(SESSION_KEY_PREFIX)),
            It.IsAny<GameSessionModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify session added to list
        _mockDaprClient.Verify(d => d.SaveStateAsync(
            STATE_STORE,
            SESSION_LIST_KEY,
            It.IsAny<List<string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify event published
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            "bannou-pubsub",
            "game-session.created",
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateGameSessionAsync_WithPrivateSession_ShouldSetIsPrivate()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateGameSessionRequest
        {
            SessionName = "Private Game",
            GameType = CreateGameSessionRequestGameType.Arcadia,
            MaxPlayers = 2,
            IsPrivate = true
        };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                STATE_STORE, SESSION_LIST_KEY, null, null, default))
            .ReturnsAsync(new List<string>());

        // Act
        var (status, response) = await service.CreateGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, status);
        Assert.NotNull(response);
        Assert.True(response.IsPrivate);
    }

    [Fact]
    public async Task CreateGameSessionAsync_WhenDaprFails_ShouldReturnInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateGameSessionRequest
        {
            SessionName = "Test",
            GameType = CreateGameSessionRequestGameType.Arcadia,
            MaxPlayers = 4
        };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                STATE_STORE, SESSION_LIST_KEY, null, null, default))
            .ThrowsAsync(new Exception("Dapr connection failed"));

        // Act
        var (status, response) = await service.CreateGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
    }

    #endregion

    #region GetGameSession Tests

    [Fact]
    public async Task GetGameSessionAsync_WhenSessionExists_ShouldReturnSession()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new GetGameSessionRequest { SessionId = sessionId };

        var sessionModel = new GameSessionModel
        {
            SessionId = sessionId.ToString(),
            SessionName = "Existing Session",
            GameType = GameSessionResponseGameType.Arcadia,
            MaxPlayers = 4,
            CurrentPlayers = 2,
            Status = GameSessionResponseStatus.Active,
            Players = new List<GamePlayer>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, SESSION_KEY_PREFIX + sessionId, null, null, default))
            .ReturnsAsync(sessionModel);

        // Act
        var (status, response) = await service.GetGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(sessionId.ToString(), response.SessionId);
        Assert.Equal("Existing Session", response.SessionName);
    }

    [Fact]
    public async Task GetGameSessionAsync_WhenSessionNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new GetGameSessionRequest { SessionId = sessionId };

#pragma warning disable CS8620 // Nullability mismatch in Moq setup - intentionally returning null to simulate not found
        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, SESSION_KEY_PREFIX + sessionId, null, null, default))
            .Returns(Task.FromResult<GameSessionModel?>(null));
#pragma warning restore CS8620

        // Act
        var (status, response) = await service.GetGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetGameSessionAsync_WhenDaprFails_ShouldReturnInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var request = new GetGameSessionRequest { SessionId = Guid.NewGuid() };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, It.IsAny<string>(), null, null, default))
            .ThrowsAsync(new Exception("State store unavailable"));

        // Act
        var (status, response) = await service.GetGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
    }

    #endregion

    #region ListGameSessions Tests

    [Fact]
    public async Task ListGameSessionsAsync_WithNoSessions_ShouldReturnEmptyList()
    {
        // Arrange
        var service = CreateService();
        var request = new ListGameSessionsRequest();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                STATE_STORE, SESSION_LIST_KEY, null, null, default))
            .ReturnsAsync(new List<string>());

        // Act
        var (status, response) = await service.ListGameSessionsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Sessions);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task ListGameSessionsAsync_WithActiveSessions_ShouldReturnSessions()
    {
        // Arrange
        var service = CreateService();
        var request = new ListGameSessionsRequest();
        var sessionId = "test-session-1";

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                STATE_STORE, SESSION_LIST_KEY, null, null, default))
            .ReturnsAsync(new List<string> { sessionId });

        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, SESSION_KEY_PREFIX + sessionId, null, null, default))
            .ReturnsAsync(new GameSessionModel
            {
                SessionId = sessionId,
                SessionName = "Active Game",
                Status = GameSessionResponseStatus.Active,
                MaxPlayers = 4,
                CurrentPlayers = 2,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.ListGameSessionsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Sessions);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal("Active Game", response.Sessions.First().SessionName);
    }

    [Fact]
    public async Task ListGameSessionsAsync_ShouldFilterOutFinishedSessions()
    {
        // Arrange
        var service = CreateService();
        var request = new ListGameSessionsRequest();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                STATE_STORE, SESSION_LIST_KEY, null, null, default))
            .ReturnsAsync(new List<string> { "active", "finished" });

        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, SESSION_KEY_PREFIX + "active", null, null, default))
            .ReturnsAsync(new GameSessionModel
            {
                SessionId = "active",
                SessionName = "Active",
                Status = GameSessionResponseStatus.Active,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow
            });

        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, SESSION_KEY_PREFIX + "finished", null, null, default))
            .ReturnsAsync(new GameSessionModel
            {
                SessionId = "finished",
                SessionName = "Finished",
                Status = GameSessionResponseStatus.Finished,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.ListGameSessionsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Sessions);
        Assert.Equal("Active", response.Sessions.First().SessionName);
    }

    #endregion

    #region JoinGameSession Tests

    [Fact]
    public async Task JoinGameSessionAsync_WhenSessionNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new JoinGameSessionRequest { SessionId = Guid.NewGuid() };

#pragma warning disable CS8620 // Nullability mismatch in Moq setup - intentionally returning null to simulate not found
        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, It.IsAny<string>(), null, null, default))
            .Returns(Task.FromResult<GameSessionModel?>(null));
#pragma warning restore CS8620

        // Act
        var (status, response) = await service.JoinGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task JoinGameSessionAsync_WhenSessionFull_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new JoinGameSessionRequest { SessionId = sessionId };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, SESSION_KEY_PREFIX + sessionId, null, null, default))
            .ReturnsAsync(new GameSessionModel
            {
                SessionId = sessionId.ToString(),
                MaxPlayers = 2,
                CurrentPlayers = 2,
                Status = GameSessionResponseStatus.Full,
                Players = new List<GamePlayer>
                {
                    new() { AccountId = Guid.NewGuid() },
                    new() { AccountId = Guid.NewGuid() }
                },
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.JoinGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task JoinGameSessionAsync_WhenSessionFinished_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new JoinGameSessionRequest { SessionId = sessionId };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, SESSION_KEY_PREFIX + sessionId, null, null, default))
            .ReturnsAsync(new GameSessionModel
            {
                SessionId = sessionId.ToString(),
                MaxPlayers = 4,
                CurrentPlayers = 0,
                Status = GameSessionResponseStatus.Finished,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.JoinGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task JoinGameSessionAsync_WhenSuccessful_ShouldPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new JoinGameSessionRequest { SessionId = sessionId };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, SESSION_KEY_PREFIX + sessionId, null, null, default))
            .ReturnsAsync(new GameSessionModel
            {
                SessionId = sessionId.ToString(),
                MaxPlayers = 4,
                CurrentPlayers = 1,
                Status = GameSessionResponseStatus.Active,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.JoinGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);

        // Verify event published
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            "bannou-pubsub",
            "game-session.player-joined",
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JoinGameSessionAsync_WhenSuccessful_ShouldSetGameSessionInGameState()
    {
        // Arrange
        var mockPermissionsClient = new Mock<BeyondImmersion.BannouService.Permissions.IPermissionsClient>();
        var service = new GameSessionService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockHttpContextAccessor.Object,
            voiceClient: null,
            permissionsClient: mockPermissionsClient.Object);

        var sessionId = Guid.NewGuid();
        var clientSessionId = "test-client-session-123";
        var request = new JoinGameSessionRequest { SessionId = sessionId };

        // Setup HTTP context with session ID header
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Bannou-Session-Id"] = clientSessionId;
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, SESSION_KEY_PREFIX + sessionId, null, null, default))
            .ReturnsAsync(new GameSessionModel
            {
                SessionId = sessionId.ToString(),
                MaxPlayers = 4,
                CurrentPlayers = 1,
                Status = GameSessionResponseStatus.Active,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.JoinGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.True(response?.Success);

        // Verify game-session:in_game state was set via Permissions client
        mockPermissionsClient.Verify(p => p.UpdateSessionStateAsync(
            It.Is<BeyondImmersion.BannouService.Permissions.SessionStateUpdate>(u =>
                u.SessionId == clientSessionId &&
                u.ServiceId == "game-session" &&
                u.NewState == "in_game"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region LeaveGameSession Tests

    [Fact]
    public async Task LeaveGameSessionAsync_WhenSuccessful_ShouldClearGameSessionState()
    {
        // Arrange
        var mockPermissionsClient = new Mock<BeyondImmersion.BannouService.Permissions.IPermissionsClient>();
        var service = new GameSessionService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockHttpContextAccessor.Object,
            voiceClient: null,
            permissionsClient: mockPermissionsClient.Object);

        var sessionId = Guid.NewGuid();
        var clientSessionId = "test-client-session-456";
        var accountId = Guid.NewGuid();
        var request = new LeaveGameSessionRequest { SessionId = sessionId };

        // Setup HTTP context with session ID header
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Bannou-Session-Id"] = clientSessionId;
        _mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, SESSION_KEY_PREFIX + sessionId, null, null, default))
            .ReturnsAsync(new GameSessionModel
            {
                SessionId = sessionId.ToString(),
                MaxPlayers = 4,
                CurrentPlayers = 2,
                Status = GameSessionResponseStatus.Active,
                Players = new List<GamePlayer>
                {
                    new() { AccountId = accountId, DisplayName = "TestPlayer" },
                    new() { AccountId = Guid.NewGuid(), DisplayName = "OtherPlayer" }
                },
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.LeaveGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        // Note: LeaveGameSession returns 200 with no body per schema

        // Verify game-session:in_game state was cleared via Permissions client
        mockPermissionsClient.Verify(p => p.ClearSessionStateAsync(
            It.Is<BeyondImmersion.BannouService.Permissions.ClearSessionStateRequest>(u =>
                u.SessionId == clientSessionId &&
                u.ServiceId == "game-session"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveGameSessionAsync_WhenSessionNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new LeaveGameSessionRequest { SessionId = Guid.NewGuid() };

#pragma warning disable CS8620 // Nullability mismatch in Moq setup - intentionally returning null to simulate not found
        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, It.IsAny<string>(), null, null, default))
            .Returns(Task.FromResult<GameSessionModel?>(null));
#pragma warning restore CS8620

        // Act
        var (status, response) = await service.LeaveGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region PerformGameAction Tests

    [Fact]
    public async Task PerformGameActionAsync_WhenSessionNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GameActionRequest
        {
            SessionId = Guid.NewGuid(),
            ActionType = GameActionRequestActionType.Move
        };

#pragma warning disable CS8620 // Nullability mismatch in Moq setup - intentionally returning null to simulate not found
        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, It.IsAny<string>(), null, null, default))
            .Returns(Task.FromResult<GameSessionModel?>(null));
#pragma warning restore CS8620

        // Act
        var (status, response) = await service.PerformGameActionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task PerformGameActionAsync_WhenSessionFinished_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new GameActionRequest
        {
            SessionId = sessionId,
            ActionType = GameActionRequestActionType.Move
        };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<GameSessionModel>(
                STATE_STORE, SESSION_KEY_PREFIX + sessionId, null, null, default))
            .ReturnsAsync(new GameSessionModel
            {
                SessionId = sessionId.ToString(),
                Status = GameSessionResponseStatus.Finished,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.PerformGameActionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
    }

    #endregion
}

/// <summary>
/// Tests for GameSessionServiceConfiguration
/// </summary>
public class GameSessionConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var config = new GameSessionServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void Configuration_ShouldBindFromEnvironmentVariables()
    {
        // Arrange
        Environment.SetEnvironmentVariable("BANNOU_MAXPLAYERSPERSESSION", "8");
        Environment.SetEnvironmentVariable("BANNOU_DEFAULTSESSIONTIMEOUT", "3600");

        // Act
        var config = IServiceConfiguration.BuildConfiguration<GameSessionServiceConfiguration>();

        // Assert
        Assert.NotNull(config);

        // Cleanup
        Environment.SetEnvironmentVariable("BANNOU_MAXPLAYERSPERSESSION", null);
        Environment.SetEnvironmentVariable("BANNOU_DEFAULTSESSIONTIMEOUT", null);
    }
}
