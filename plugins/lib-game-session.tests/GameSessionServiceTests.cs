using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
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
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<GameSessionModel>> _mockGameSessionStore;
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IStateStore<SubscriberSessionsModel>> _mockSubscriberSessionsStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<GameSessionService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IClientEventPublisher> _mockClientEventPublisher;
    private readonly Mock<BeyondImmersion.BannouService.Voice.IVoiceClient> _mockVoiceClient;
    private readonly Mock<BeyondImmersion.BannouService.Permission.IPermissionClient> _mockPermissionClient;
    private readonly Mock<BeyondImmersion.BannouService.Subscription.ISubscriptionClient> _mockSubscriptionClient;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;

    private const string STATE_STORE = "game-session-statestore";
    private const string SESSION_KEY_PREFIX = "session:";
    private const string SESSION_LIST_KEY = "session-list";
    private const string SUBSCRIBER_SESSIONS_PREFIX = "subscriber-sessions:";
    private const string LOBBY_KEY_PREFIX = "lobby:";

    private const string TEST_SERVER_SALT = "test-server-salt-2025";
    private const string TEST_GAME_TYPE = "test-game";

    public GameSessionServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockGameSessionStore = new Mock<IStateStore<GameSessionModel>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockSubscriberSessionsStore = new Mock<IStateStore<SubscriberSessionsModel>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<GameSessionService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockClientEventPublisher = new Mock<IClientEventPublisher>();
        _mockVoiceClient = new Mock<BeyondImmersion.BannouService.Voice.IVoiceClient>();
        _mockPermissionClient = new Mock<BeyondImmersion.BannouService.Permission.IPermissionClient>();
        _mockSubscriptionClient = new Mock<BeyondImmersion.BannouService.Subscription.ISubscriptionClient>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<GameSessionModel>(STATE_STORE)).Returns(_mockGameSessionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE)).Returns(_mockListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<SubscriberSessionsModel>(STATE_STORE)).Returns(_mockSubscriberSessionsStore.Object);
    }

    /// <summary>
    /// Override to provide test configuration with required ServerSalt.
    /// </summary>
    protected override GameSessionServiceConfiguration CreateConfiguration()
    {
        return new GameSessionServiceConfiguration
        {
            ServerSalt = TEST_SERVER_SALT
        };
    }

    private GameSessionService CreateService()
    {
        return new GameSessionService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            _mockEventConsumer.Object,
            _mockClientEventPublisher.Object,
            _mockVoiceClient.Object,
            _mockPermissionClient.Object,
            _mockSubscriptionClient.Object,
            _mockLockProvider.Object);
    }

    /// <summary>
    /// Sets up a valid subscriber session so authorization checks pass.
    /// </summary>
    private void SetupValidSubscriberSession(Guid accountId, Guid sessionId)
    {
        var subscriberSessions = new SubscriberSessionsModel
        {
            AccountId = accountId,
            SessionIds = new HashSet<Guid> { sessionId }
        };

        _mockSubscriberSessionsStore
            .Setup(s => s.GetAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscriberSessions);
    }

    /// <summary>
    /// Sets up an existing lobby for a game type.
    /// </summary>
    private Guid SetupExistingLobby(string gameType, GameSessionModel lobby)
    {
        var lobbyId = lobby.SessionId;

        // Setup lookup by lobby key (lobby:{gameType})
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + gameType.ToLowerInvariant(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lobby);

        // Setup lookup by session key (session:{lobbyId})
        _mockGameSessionStore
            .Setup(s => s.GetAsync(SESSION_KEY_PREFIX + lobbyId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lobby);

        return lobbyId;
    }

    #region Constructor Tests

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    ///
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    ///
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void GameSessionService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<GameSessionService>();

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
            GameType = "test-game",
            MaxPlayers = 4,
            IsPrivate = false
        };

        // Setup empty session list
        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var (status, response) = await service.CreateGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Test Session", response.SessionName);
        Assert.Equal(4, response.MaxPlayers);
        Assert.Equal(0, response.CurrentPlayers);
        Assert.Equal(SessionStatus.Waiting, response.Status);

        // Verify state was saved
        _mockGameSessionStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith(SESSION_KEY_PREFIX)),
            It.IsAny<GameSessionModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify session added to list
        _mockListStore.Verify(s => s.SaveAsync(
            SESSION_LIST_KEY,
            It.IsAny<List<string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "game-session.created",
            It.IsAny<object>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
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
            GameType = "test-game",
            MaxPlayers = 2,
            IsPrivate = true
        };

        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var (status, response) = await service.CreateGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsPrivate);
    }

    [Fact]
    public async Task CreateGameSessionAsync_WhenStateStoreFails_ShouldReturnInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateGameSessionRequest
        {
            SessionName = "Test",
            GameType = "test-game",
            MaxPlayers = 4
        };

        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("State store connection failed"));

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
            SessionId = sessionId,
            SessionName = "Existing Session",
            GameType = "test-game",
            MaxPlayers = 4,
            CurrentPlayers = 2,
            Status = SessionStatus.Active,
            Players = new List<GamePlayer>(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockGameSessionStore
            .Setup(s => s.GetAsync(SESSION_KEY_PREFIX + sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionModel);

        // Act
        var (status, response) = await service.GetGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(sessionId, response.SessionId);
        Assert.Equal("Existing Session", response.SessionName);
    }

    [Fact]
    public async Task GetGameSessionAsync_WhenSessionNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var request = new GetGameSessionRequest { SessionId = sessionId };

        _mockGameSessionStore
            .Setup(s => s.GetAsync(SESSION_KEY_PREFIX + sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameSessionModel?)null);

        // Act
        var (status, response) = await service.GetGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetGameSessionAsync_WhenStateStoreFails_ShouldReturnInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var request = new GetGameSessionRequest { SessionId = Guid.NewGuid() };

        _mockGameSessionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
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
        var sessionId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { sessionId.ToString() });

        _mockGameSessionStore
            .Setup(s => s.GetAsync(SESSION_KEY_PREFIX + sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameSessionModel
            {
                SessionId = sessionId,
                SessionName = "Active Game",
                Status = SessionStatus.Active,
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
        var activeSessionId = Guid.NewGuid();
        var finishedSessionId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { activeSessionId.ToString(), finishedSessionId.ToString() });

        _mockGameSessionStore
            .Setup(s => s.GetAsync(SESSION_KEY_PREFIX + activeSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameSessionModel
            {
                SessionId = activeSessionId,
                SessionName = "Active",
                Status = SessionStatus.Active,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow
            });

        _mockGameSessionStore
            .Setup(s => s.GetAsync(SESSION_KEY_PREFIX + finishedSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameSessionModel
            {
                SessionId = finishedSessionId,
                SessionName = "Finished",
                Status = SessionStatus.Finished,
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
        var accountId = Guid.NewGuid();
        var clientSessionId = Guid.NewGuid();
        var request = new JoinGameSessionRequest
        {
            SessionId = clientSessionId,
            AccountId = accountId,
            GameType = TEST_GAME_TYPE
        };

        // Setup valid subscriber session so auth passes
        SetupValidSubscriberSession(accountId, clientSessionId);

        // Return a lobby from lobby key lookup, but nothing from session key lookup
        var lobbyId = Guid.NewGuid();
        var lobby = new GameSessionModel { SessionId = lobbyId };
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + TEST_GAME_TYPE.ToLowerInvariant(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lobby);
        _mockGameSessionStore
            .Setup(s => s.GetAsync(SESSION_KEY_PREFIX + lobbyId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameSessionModel?)null);

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
        var accountId = Guid.NewGuid();
        var clientSessionId = Guid.NewGuid();
        var request = new JoinGameSessionRequest
        {
            SessionId = clientSessionId,
            AccountId = accountId,
            GameType = TEST_GAME_TYPE
        };

        // Setup valid subscriber session
        SetupValidSubscriberSession(accountId, clientSessionId);

        // Setup full lobby
        var lobbyId = Guid.NewGuid();
        var lobby = new GameSessionModel
        {
            SessionId = lobbyId,
            MaxPlayers = 2,
            CurrentPlayers = 2,
            Status = SessionStatus.Full,
            Players = new List<GamePlayer>
            {
                new() { AccountId = Guid.NewGuid() },
                new() { AccountId = Guid.NewGuid() }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };
        SetupExistingLobby(TEST_GAME_TYPE, lobby);

        // Act
        var (status, response) = await service.JoinGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task JoinGameSessionAsync_WhenSessionFinished_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var clientSessionId = Guid.NewGuid();
        var request = new JoinGameSessionRequest
        {
            SessionId = clientSessionId,
            AccountId = accountId,
            GameType = TEST_GAME_TYPE
        };

        // Setup valid subscriber session
        SetupValidSubscriberSession(accountId, clientSessionId);

        // Setup finished lobby
        var lobbyId = Guid.NewGuid();
        var lobby = new GameSessionModel
        {
            SessionId = lobbyId,
            MaxPlayers = 4,
            CurrentPlayers = 0,
            Status = SessionStatus.Finished,
            Players = new List<GamePlayer>(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        SetupExistingLobby(TEST_GAME_TYPE, lobby);

        // Act
        var (status, response) = await service.JoinGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task JoinGameSessionAsync_WhenSuccessful_ShouldPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var clientSessionId = Guid.NewGuid();
        var request = new JoinGameSessionRequest
        {
            SessionId = clientSessionId,
            AccountId = accountId,
            GameType = TEST_GAME_TYPE
        };

        // Setup valid subscriber session
        SetupValidSubscriberSession(accountId, clientSessionId);

        // Setup active lobby
        var lobbyId = Guid.NewGuid();
        var lobby = new GameSessionModel
        {
            SessionId = lobbyId,
            MaxPlayers = 4,
            CurrentPlayers = 1,
            Status = SessionStatus.Active,
            Players = new List<GamePlayer>(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        SetupExistingLobby(TEST_GAME_TYPE, lobby);

        // Mock UpdateSessionStateAsync to succeed
        _mockPermissionClient
            .Setup(p => p.UpdateSessionStateAsync(It.IsAny<BeyondImmersion.BannouService.Permission.SessionStateUpdate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeyondImmersion.BannouService.Permission.SessionUpdateResponse());

        // Act
        var (status, response) = await service.JoinGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify event published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "game-session.player-joined",
            It.IsAny<object>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task JoinGameSessionAsync_WhenSuccessful_ShouldSetGameSessionInGameState()
    {
        // Arrange
        var service = CreateService();
        var lobbyId = Guid.NewGuid();
        var clientSessionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var request = new JoinGameSessionRequest
        {
            SessionId = clientSessionId,  // WebSocket session ID (from shortcut)
            AccountId = accountId,
            GameType = TEST_GAME_TYPE
        };

        // Setup valid subscriber session
        SetupValidSubscriberSession(accountId, clientSessionId);

        // Setup active lobby
        var lobby = new GameSessionModel
        {
            SessionId = lobbyId,
            MaxPlayers = 4,
            CurrentPlayers = 1,
            Status = SessionStatus.Active,
            Players = new List<GamePlayer>(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        SetupExistingLobby(TEST_GAME_TYPE, lobby);

        // Mock UpdateSessionStateAsync to succeed
        _mockPermissionClient
            .Setup(p => p.UpdateSessionStateAsync(It.IsAny<BeyondImmersion.BannouService.Permission.SessionStateUpdate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeyondImmersion.BannouService.Permission.SessionUpdateResponse());

        // Act
        var (status, response) = await service.JoinGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify game-session:in_game state was set via Permission client with the WebSocket session ID
        _mockPermissionClient.Verify(p => p.UpdateSessionStateAsync(
            It.Is<BeyondImmersion.BannouService.Permission.SessionStateUpdate>(u =>
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
        var service = CreateService();
        var lobbyId = Guid.NewGuid();
        var clientSessionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var request = new LeaveGameSessionRequest
        {
            SessionId = clientSessionId,  // WebSocket session ID (from shortcut)
            AccountId = accountId,
            GameType = TEST_GAME_TYPE
        };

        // Setup lobby with the player in it
        var lobby = new GameSessionModel
        {
            SessionId = lobbyId,
            MaxPlayers = 4,
            CurrentPlayers = 2,
            Status = SessionStatus.Active,
            Players = new List<GamePlayer>
            {
                new() { AccountId = accountId, DisplayName = "TestPlayer" },
                new() { AccountId = Guid.NewGuid(), DisplayName = "OtherPlayer" }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };
        SetupExistingLobby(TEST_GAME_TYPE, lobby);

        // Mock ClearSessionStateAsync to succeed
        _mockPermissionClient
            .Setup(p => p.ClearSessionStateAsync(It.IsAny<BeyondImmersion.BannouService.Permission.ClearSessionStateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeyondImmersion.BannouService.Permission.SessionUpdateResponse());

        // Act
        var status = await service.LeaveGameSessionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify game-session:in_game state was cleared via Permission client with the WebSocket session ID
        _mockPermissionClient.Verify(p => p.ClearSessionStateAsync(
            It.Is<BeyondImmersion.BannouService.Permission.ClearSessionStateRequest>(u =>
                u.SessionId == clientSessionId &&
                u.ServiceId == "game-session"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveGameSessionAsync_WhenSessionNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var clientSessionId = Guid.NewGuid();
        var request = new LeaveGameSessionRequest
        {
            SessionId = clientSessionId,
            AccountId = accountId,
            GameType = TEST_GAME_TYPE
        };

        // Return a lobby from lobby key lookup, but nothing from session key lookup
        var lobbyId = Guid.NewGuid();
        var lobby = new GameSessionModel { SessionId = lobbyId };
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + TEST_GAME_TYPE.ToLowerInvariant(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lobby);
        _mockGameSessionStore
            .Setup(s => s.GetAsync(SESSION_KEY_PREFIX + lobbyId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameSessionModel?)null);

        // Act
        var status = await service.LeaveGameSessionAsync(request);

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
        var accountId = Guid.NewGuid();
        var clientSessionId = Guid.NewGuid();
        var request = new GameActionRequest
        {
            SessionId = clientSessionId,
            AccountId = accountId,
            GameType = TEST_GAME_TYPE,
            ActionType = GameActionType.Move
        };

        // Return a lobby from lobby key lookup, but nothing from session key lookup
        var lobbyId = Guid.NewGuid();
        var lobby = new GameSessionModel { SessionId = lobbyId };
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + TEST_GAME_TYPE.ToLowerInvariant(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lobby);
        _mockGameSessionStore
            .Setup(s => s.GetAsync(SESSION_KEY_PREFIX + lobbyId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameSessionModel?)null);

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
        var accountId = Guid.NewGuid();
        var clientSessionId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var request = new GameActionRequest
        {
            SessionId = clientSessionId,
            AccountId = accountId,
            GameType = TEST_GAME_TYPE,
            ActionType = GameActionType.Move
        };

        // Setup finished lobby
        var lobby = new GameSessionModel
        {
            SessionId = lobbyId,
            Status = SessionStatus.Finished,
            Players = new List<GamePlayer>(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        SetupExistingLobby(TEST_GAME_TYPE, lobby);

        // Act
        var (status, response) = await service.PerformGameActionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
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
        // Arrange - Use GAMESESSION_ prefix (derived from BannouService("game-session"))
        Environment.SetEnvironmentVariable("GAMESESSION_MAXPLAYERSPERSESSION", "8");
        Environment.SetEnvironmentVariable("GAMESESSION_DEFAULTSESSIONTIMEOUTSECONDS", "3600");

        // Act
        var config = IServiceConfiguration.BuildConfiguration<GameSessionServiceConfiguration>();

        // Assert
        Assert.NotNull(config);

        // Cleanup
        Environment.SetEnvironmentVariable("GAMESESSION_MAXPLAYERSPERSESSION", null);
        Environment.SetEnvironmentVariable("GAMESESSION_DEFAULTSESSIONTIMEOUTSECONDS", null);
    }
}
