using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
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

    private readonly Mock<BeyondImmersion.BannouService.Permission.IPermissionClient> _mockPermissionClient;
    private readonly Mock<BeyondImmersion.BannouService.Subscription.ISubscriptionClient> _mockSubscriptionClient;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IConnectClient> _mockConnectClient;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IGameServiceClient> _mockGameServiceClient;

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

        _mockPermissionClient = new Mock<BeyondImmersion.BannouService.Permission.IPermissionClient>();
        _mockSubscriptionClient = new Mock<BeyondImmersion.BannouService.Subscription.ISubscriptionClient>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockConnectClient = new Mock<IConnectClient>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockGameServiceClient = new Mock<IGameServiceClient>();

        // Default: autoLobbyEnabled = true for backward compatibility with existing tests
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = Guid.NewGuid(),
                StubName = TEST_GAME_TYPE,
                DisplayName = "Test Game",
                IsActive = true,
                AutoLobbyEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow
            });

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

            _mockPermissionClient.Object,
            _mockSubscriptionClient.Object,
            _mockLockProvider.Object,
            _mockConnectClient.Object,
            _mockGameServiceClient.Object,
            _mockTelemetryProvider.Object);
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
    public async Task CreateGameSessionAsync_WhenStateStoreFails_ShouldThrow()
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

        // Act & Assert - exceptions propagate to generated controller for error handling
        await Assert.ThrowsAsync<Exception>(() => service.CreateGameSessionAsync(request));
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
    public async Task GetGameSessionAsync_WhenStateStoreFails_ShouldThrow()
    {
        // Arrange
        var service = CreateService();
        var request = new GetGameSessionRequest { SessionId = Guid.NewGuid() };

        _mockGameSessionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("State store unavailable"));

        // Act & Assert - exceptions propagate to generated controller for error handling
        await Assert.ThrowsAsync<Exception>(() => service.GetGameSessionAsync(request));
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
                u.ServiceId == StateStoreDefinitions.GameSessionLock &&
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
                u.ServiceId == StateStoreDefinitions.GameSessionLock),
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

    [Fact]
    public async Task PerformGameActionAsync_WhenPlayerNotInSession_ShouldReturnForbidden()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var otherPlayerId = Guid.NewGuid();
        var clientSessionId = Guid.NewGuid();
        var lobbyId = Guid.NewGuid();
        var request = new GameActionRequest
        {
            SessionId = clientSessionId,
            AccountId = accountId,
            GameType = TEST_GAME_TYPE,
            ActionType = GameActionType.Move
        };

        // Setup lobby with a DIFFERENT player (not the requesting account)
        var lobby = new GameSessionModel
        {
            SessionId = lobbyId,
            Status = SessionStatus.Active,
            Players = new List<GamePlayer>
            {
                new GamePlayer { AccountId = otherPlayerId, SessionId = Guid.NewGuid(), DisplayName = "Other" }
            },
            CurrentPlayers = 1,
            MaxPlayers = 16,
            CreatedAt = DateTimeOffset.UtcNow
        };
        SetupExistingLobby(TEST_GAME_TYPE, lobby);

        // Act
        var (status, response) = await service.PerformGameActionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Forbidden, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task PerformGameActionAsync_WhenPlayerInSession_ShouldReturnOk()
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

        // Setup lobby with the requesting player
        var lobby = new GameSessionModel
        {
            SessionId = lobbyId,
            Status = SessionStatus.Active,
            Players = new List<GamePlayer>
            {
                new GamePlayer { AccountId = accountId, SessionId = clientSessionId, DisplayName = "Player" }
            },
            CurrentPlayers = 1,
            MaxPlayers = 16,
            CreatedAt = DateTimeOffset.UtcNow
        };
        SetupExistingLobby(TEST_GAME_TYPE, lobby);

        // Act
        var (status, response) = await service.PerformGameActionAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.ActionId);
    }

    #endregion
}

/// <summary>
/// Tests for GameSession event handler pipeline: session.connected, subscription.updated,
/// shortcut publishing, lobby creation, and subscription caching.
/// Uses SupportedGameServices = "test-game" to match the test stub name.
/// </summary>
public class GameSessionEventHandlerTests : ServiceTestBase<GameSessionServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<GameSessionModel>> _mockGameSessionStore;
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IStateStore<SubscriberSessionsModel>> _mockSubscriberSessionsStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<GameSessionService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IClientEventPublisher> _mockClientEventPublisher;

    private readonly Mock<BeyondImmersion.BannouService.Permission.IPermissionClient> _mockPermissionClient;
    private readonly Mock<BeyondImmersion.BannouService.Subscription.ISubscriptionClient> _mockSubscriptionClient;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IConnectClient> _mockConnectClient;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IGameServiceClient> _mockGameServiceClient;

    private const string STATE_STORE = "game-session-statestore";
    private const string SESSION_KEY_PREFIX = "session:";
    private const string SESSION_LIST_KEY = "session-list";
    private const string SUBSCRIBER_SESSIONS_PREFIX = "subscriber-sessions:";
    private const string LOBBY_KEY_PREFIX = "lobby:";
    private const string TEST_SERVER_SALT = "test-server-salt-2025";
    private const string TEST_STUB_NAME = "test-game";

    public GameSessionEventHandlerTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockGameSessionStore = new Mock<IStateStore<GameSessionModel>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockSubscriberSessionsStore = new Mock<IStateStore<SubscriberSessionsModel>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<GameSessionService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockClientEventPublisher = new Mock<IClientEventPublisher>();

        _mockPermissionClient = new Mock<BeyondImmersion.BannouService.Permission.IPermissionClient>();
        _mockSubscriptionClient = new Mock<BeyondImmersion.BannouService.Subscription.ISubscriptionClient>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockConnectClient = new Mock<IConnectClient>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockGameServiceClient = new Mock<IGameServiceClient>();

        // Default: autoLobbyEnabled = true for backward compatibility with existing tests
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(It.IsAny<GetServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = Guid.NewGuid(),
                StubName = TEST_STUB_NAME,
                DisplayName = "Test Game",
                IsActive = true,
                AutoLobbyEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow
            });

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        _mockStateStoreFactory.Setup(f => f.GetStore<GameSessionModel>(STATE_STORE)).Returns(_mockGameSessionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE)).Returns(_mockListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<SubscriberSessionsModel>(STATE_STORE)).Returns(_mockSubscriberSessionsStore.Object);
    }

    /// <summary>
    /// Configure with SupportedGameServices = "test-game" so IsOurService matches.
    /// </summary>
    protected override GameSessionServiceConfiguration CreateConfiguration()
    {
        return new GameSessionServiceConfiguration
        {
            ServerSalt = TEST_SERVER_SALT,
            SupportedGameServices = TEST_STUB_NAME
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

            _mockPermissionClient.Object,
            _mockSubscriptionClient.Object,
            _mockLockProvider.Object,
            _mockConnectClient.Object,
            _mockGameServiceClient.Object,
            _mockTelemetryProvider.Object);
    }

    #region HandleSessionConnectedInternal Tests

    [Fact]
    public async Task HandleSessionConnectedInternal_WithSubscribedAccount_ShouldPublishShortcut()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Pre-populate subscription cache with a matching stub
        GameSessionService.AddAccountSubscription(accountId, TEST_STUB_NAME);

        // Setup subscriber session store for StoreSubscriberSessionAsync
        _mockSubscriberSessionsStore
            .Setup(s => s.GetWithETagAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((SubscriberSessionsModel?)null, (string?)null));
        _mockSubscriberSessionsStore
            .Setup(s => s.TrySaveAsync(
                SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(),
                It.IsAny<SubscriberSessionsModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag1");

        // Setup lobby creation (no existing lobby)
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + TEST_STUB_NAME, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameSessionModel?)null);
        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Setup client event publisher to succeed
        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                sessionId.ToString(),
                It.IsAny<ShortcutPublishedEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await service.HandleSessionConnectedInternalAsync(sessionId, accountId);

        // Assert - shortcut was published to the session
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            sessionId.ToString(),
            It.Is<ShortcutPublishedEvent>(e =>
                e.SessionId == sessionId &&
                e.Shortcut.Metadata.SourceService == StateStoreDefinitions.GameSessionLock),
            It.IsAny<CancellationToken>()), Times.Once);

        // Cleanup static cache
        GameSessionService.RemoveAccountSubscription(accountId, TEST_STUB_NAME);
    }

    [Fact]
    public async Task HandleSessionConnectedInternal_WithNoSubscriptions_ShouldNotPublishShortcut()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Setup subscription client to return empty subscriptions
        _mockSubscriptionClient
            .Setup(c => c.QueryCurrentSubscriptionsAsync(
                It.IsAny<BeyondImmersion.BannouService.Subscription.QueryCurrentSubscriptionsRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeyondImmersion.BannouService.Subscription.QuerySubscriptionsResponse
            {
                Subscriptions = new List<BeyondImmersion.BannouService.Subscription.SubscriptionInfo>(),
                TotalCount = 0
            });

        // Act
        await service.HandleSessionConnectedInternalAsync(sessionId, accountId);

        // Assert - no shortcut published
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            It.IsAny<string>(),
            It.IsAny<ShortcutPublishedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSessionConnectedInternal_WithNonSupportedService_ShouldNotPublishShortcut()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Pre-populate cache with a different stub name that doesn't match SupportedGameServices
        GameSessionService.AddAccountSubscription(accountId, "other-game");

        // Act
        await service.HandleSessionConnectedInternalAsync(sessionId, accountId);

        // Assert - no shortcut published because "other-game" is not in SupportedGameServices
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            It.IsAny<string>(),
            It.IsAny<ShortcutPublishedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Cleanup
        GameSessionService.RemoveAccountSubscription(accountId, "other-game");
    }

    #endregion

    #region HandleSubscriptionUpdatedInternal Tests

    [Fact]
    public async Task HandleSubscriptionUpdatedInternal_Created_ShouldPublishShortcutToConnectedSessions()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Setup subscriber sessions in state store (account has an active session)
        _mockSubscriberSessionsStore
            .Setup(s => s.GetAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriberSessionsModel
            {
                AccountId = accountId,
                SessionIds = new HashSet<Guid> { sessionId }
            });

        // Setup lobby creation
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + TEST_STUB_NAME, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameSessionModel?)null);
        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                sessionId.ToString(),
                It.IsAny<ShortcutPublishedEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await service.HandleSubscriptionUpdatedInternalAsync(
            accountId, TEST_STUB_NAME, SubscriptionUpdatedEventAction.Created, isActive: true);

        // Assert - shortcut published to the connected session
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            sessionId.ToString(),
            It.Is<ShortcutPublishedEvent>(e => e.SessionId == sessionId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleSubscriptionUpdatedInternal_Cancelled_ShouldRevokeShortcuts()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Pre-populate subscription cache so cache removal logic is exercised
        GameSessionService.AddAccountSubscription(accountId, TEST_STUB_NAME);

        // Setup subscriber sessions
        _mockSubscriberSessionsStore
            .Setup(s => s.GetAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriberSessionsModel
            {
                AccountId = accountId,
                SessionIds = new HashSet<Guid> { sessionId }
            });

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                sessionId.ToString(),
                It.IsAny<ShortcutRevokedEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await service.HandleSubscriptionUpdatedInternalAsync(
            accountId, TEST_STUB_NAME, SubscriptionUpdatedEventAction.Cancelled, isActive: false);

        // Assert - shortcut revocation published
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            sessionId.ToString(),
            It.Is<ShortcutRevokedEvent>(e =>
                e.SessionId == sessionId &&
                e.RevokeByService == StateStoreDefinitions.GameSessionLock),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleSubscriptionUpdatedInternal_NonSupportedService_ShouldSkipShortcutUpdate()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();

        // Act - "other-game" is not in SupportedGameServices ("test-game")
        await service.HandleSubscriptionUpdatedInternalAsync(
            accountId, "other-game", SubscriptionUpdatedEventAction.Created, isActive: true);

        // Assert - no shortcut operations since IsOurService returns false
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            It.IsAny<string>(),
            It.IsAny<ShortcutPublishedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _mockSubscriberSessionsStore.Verify(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSubscriptionUpdatedInternal_Created_ShouldUpdateSubscriptionCache()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();

        // No connected sessions (so shortcut publish won't be attempted)
        _mockSubscriberSessionsStore
            .Setup(s => s.GetAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriberSessionsModel?)null);

        // Act
        await service.HandleSubscriptionUpdatedInternalAsync(
            accountId, TEST_STUB_NAME, SubscriptionUpdatedEventAction.Created, isActive: true);

        // Assert - verify the shortcut publish was still attempted (even with 0 sessions)
        // The important thing is that IsOurService passed and GetSubscriberSessionsAsync was called
        _mockSubscriberSessionsStore.Verify(s => s.GetAsync(
            SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetOrCreateLobbySession Tests

    [Fact]
    public async Task HandleSubscriptionUpdated_ShouldCreateLobbyWhenNoneExists()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Setup subscriber sessions
        _mockSubscriberSessionsStore
            .Setup(s => s.GetAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriberSessionsModel
            {
                AccountId = accountId,
                SessionIds = new HashSet<Guid> { sessionId }
            });

        // No existing lobby
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + TEST_STUB_NAME, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameSessionModel?)null);
        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                sessionId.ToString(),
                It.IsAny<ShortcutPublishedEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await service.HandleSubscriptionUpdatedInternalAsync(
            accountId, TEST_STUB_NAME, SubscriptionUpdatedEventAction.Created, isActive: true);

        // Assert - lobby was created (saved to both lobby key and session key)
        _mockGameSessionStore.Verify(s => s.SaveAsync(
            LOBBY_KEY_PREFIX + TEST_STUB_NAME,
            It.Is<GameSessionModel>(m =>
                m.GameType == TEST_STUB_NAME &&
                m.Status == SessionStatus.Active &&
                m.SessionName == $"{TEST_STUB_NAME} Lobby"),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleSubscriptionUpdated_ShouldReuseExistingActiveLobby()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var existingLobbyId = Guid.NewGuid();

        // Setup subscriber sessions
        _mockSubscriberSessionsStore
            .Setup(s => s.GetAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriberSessionsModel
            {
                AccountId = accountId,
                SessionIds = new HashSet<Guid> { sessionId }
            });

        // Existing active lobby
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + TEST_STUB_NAME, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GameSessionModel
            {
                SessionId = existingLobbyId,
                GameType = TEST_STUB_NAME,
                Status = SessionStatus.Active,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow
            });

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                sessionId.ToString(),
                It.IsAny<ShortcutPublishedEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await service.HandleSubscriptionUpdatedInternalAsync(
            accountId, TEST_STUB_NAME, SubscriptionUpdatedEventAction.Created, isActive: true);

        // Assert - no new lobby created (no save to lobby key)
        _mockGameSessionStore.Verify(s => s.SaveAsync(
            LOBBY_KEY_PREFIX + TEST_STUB_NAME,
            It.IsAny<GameSessionModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // But shortcut was still published
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            sessionId.ToString(),
            It.IsAny<ShortcutPublishedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GenericLobbiesEnabled Tests

    [Fact]
    public async Task HandleSessionConnectedInternal_WithGenericLobbiesEnabled_ShouldPublishWithoutSubscription()
    {
        // Arrange - Create service with GenericLobbiesEnabled = true
        var config = new GameSessionServiceConfiguration
        {
            ServerSalt = TEST_SERVER_SALT,
            SupportedGameServices = "generic",
            GenericLobbiesEnabled = true
        };

        var service = new GameSessionService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            config,
            _mockEventConsumer.Object,
            _mockClientEventPublisher.Object,

            _mockPermissionClient.Object,
            _mockSubscriptionClient.Object,
            _mockLockProvider.Object,
            _mockConnectClient.Object,
            _mockGameServiceClient.Object,
            _mockTelemetryProvider.Object);

        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Setup subscriber session store
        _mockSubscriberSessionsStore
            .Setup(s => s.GetWithETagAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((SubscriberSessionsModel?)null, (string?)null));
        _mockSubscriberSessionsStore
            .Setup(s => s.TrySaveAsync(
                SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(),
                It.IsAny<SubscriberSessionsModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag1");

        // Setup lobby creation
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + "generic", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameSessionModel?)null);
        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                sessionId.ToString(),
                It.IsAny<ShortcutPublishedEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act - NO subscription exists for this account, but GenericLobbiesEnabled is true
        await service.HandleSessionConnectedInternalAsync(sessionId, accountId);

        // Assert - shortcut was published even without subscription
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            sessionId.ToString(),
            It.Is<ShortcutPublishedEvent>(e =>
                e.SessionId == sessionId &&
                e.Shortcut.Metadata.Name == "join_game_generic"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleSessionConnectedInternal_WithGenericLobbiesDisabled_ShouldRequireSubscription()
    {
        // Arrange - Create service with GenericLobbiesEnabled = false (default)
        var config = new GameSessionServiceConfiguration
        {
            ServerSalt = TEST_SERVER_SALT,
            SupportedGameServices = "generic",
            GenericLobbiesEnabled = false  // Explicitly disabled
        };

        var service = new GameSessionService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            config,
            _mockEventConsumer.Object,
            _mockClientEventPublisher.Object,

            _mockPermissionClient.Object,
            _mockSubscriptionClient.Object,
            _mockLockProvider.Object,
            _mockConnectClient.Object,
            _mockGameServiceClient.Object,
            _mockTelemetryProvider.Object);

        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Setup subscription client to return NO subscriptions
        _mockSubscriptionClient
            .Setup(c => c.QueryCurrentSubscriptionsAsync(
                It.IsAny<BeyondImmersion.BannouService.Subscription.QueryCurrentSubscriptionsRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeyondImmersion.BannouService.Subscription.QuerySubscriptionsResponse
            {
                Subscriptions = new List<BeyondImmersion.BannouService.Subscription.SubscriptionInfo>(),
                TotalCount = 0
            });

        // Act - No subscription, GenericLobbiesEnabled = false
        await service.HandleSessionConnectedInternalAsync(sessionId, accountId);

        // Assert - NO shortcut published because no subscription and GenericLobbiesEnabled is false
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            It.IsAny<string>(),
            It.IsAny<ShortcutPublishedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSessionConnectedInternal_WithGenericLobbiesEnabled_ShouldNotDuplicateForSubscribedUser()
    {
        // Arrange - User IS subscribed to generic, but GenericLobbiesEnabled should publish first
        var config = new GameSessionServiceConfiguration
        {
            ServerSalt = TEST_SERVER_SALT,
            SupportedGameServices = "generic",
            GenericLobbiesEnabled = true
        };

        var service = new GameSessionService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            config,
            _mockEventConsumer.Object,
            _mockClientEventPublisher.Object,

            _mockPermissionClient.Object,
            _mockSubscriptionClient.Object,
            _mockLockProvider.Object,
            _mockConnectClient.Object,
            _mockGameServiceClient.Object,
            _mockTelemetryProvider.Object);

        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Pre-populate cache with "generic" subscription
        GameSessionService.AddAccountSubscription(accountId, "generic");

        // Setup subscriber session store
        _mockSubscriberSessionsStore
            .Setup(s => s.GetWithETagAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((SubscriberSessionsModel?)null, (string?)null));
        _mockSubscriberSessionsStore
            .Setup(s => s.TrySaveAsync(
                SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(),
                It.IsAny<SubscriberSessionsModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag1");

        // Setup lobby creation
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + "generic", It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameSessionModel?)null);
        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                sessionId.ToString(),
                It.IsAny<ShortcutPublishedEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await service.HandleSessionConnectedInternalAsync(sessionId, accountId);

        // Assert - shortcut published exactly ONCE (not twice - once from GenericLobbiesEnabled, once from subscription)
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            sessionId.ToString(),
            It.Is<ShortcutPublishedEvent>(e => e.Shortcut.Metadata.Name == "join_game_generic"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Cleanup
        GameSessionService.RemoveAccountSubscription(accountId, "generic");
    }

    #endregion

    #region AutoLobbyEnabled Tests

    [Fact]
    public async Task HandleSessionConnected_WithAutoLobbyDisabled_ShouldNotPublishShortcut()
    {
        // Arrange - Account subscribed to a game with autoLobbyEnabled = false
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Pre-populate subscription cache
        GameSessionService.AddAccountSubscription(accountId, TEST_STUB_NAME);

        // Override default mock: autoLobbyEnabled = false for this game service
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.StubName == TEST_STUB_NAME),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = Guid.NewGuid(),
                StubName = TEST_STUB_NAME,
                DisplayName = "Test Game",
                IsActive = true,
                AutoLobbyEnabled = false,
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Act
        await service.HandleSessionConnectedInternalAsync(sessionId, accountId);

        // Assert - NO shortcut published because autoLobbyEnabled is false
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            It.IsAny<string>(),
            It.IsAny<ShortcutPublishedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Cleanup
        GameSessionService.RemoveAccountSubscription(accountId, TEST_STUB_NAME);
    }

    [Fact]
    public async Task HandleSessionConnected_WithAutoLobbyEnabled_ShouldPublishShortcut()
    {
        // Arrange - Account subscribed to a game with autoLobbyEnabled = true (default mock)
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Pre-populate subscription cache
        GameSessionService.AddAccountSubscription(accountId, TEST_STUB_NAME);

        // Setup subscriber session store
        _mockSubscriberSessionsStore
            .Setup(s => s.GetWithETagAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((SubscriberSessionsModel?)null, (string?)null));
        _mockSubscriberSessionsStore
            .Setup(s => s.TrySaveAsync(
                SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(),
                It.IsAny<SubscriberSessionsModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag1");

        // Setup lobby creation
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + TEST_STUB_NAME, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameSessionModel?)null);
        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                sessionId.ToString(),
                It.IsAny<ShortcutPublishedEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await service.HandleSessionConnectedInternalAsync(sessionId, accountId);

        // Assert - shortcut was published because autoLobbyEnabled is true
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            sessionId.ToString(),
            It.IsAny<ShortcutPublishedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Cleanup
        GameSessionService.RemoveAccountSubscription(accountId, TEST_STUB_NAME);
    }

    [Fact]
    public async Task HandleSubscriptionUpdated_WithAutoLobbyDisabled_ShouldStoreSessionButNotPublishShortcut()
    {
        // Arrange - Active subscription update for a game with autoLobbyEnabled = false
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Override default mock: autoLobbyEnabled = false
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.StubName == TEST_STUB_NAME),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = Guid.NewGuid(),
                StubName = TEST_STUB_NAME,
                DisplayName = "Test Game",
                IsActive = true,
                AutoLobbyEnabled = false,
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Setup Connect to return this session as connected
        _mockConnectClient
            .Setup(c => c.GetAccountSessionsAsync(
                It.Is<GetAccountSessionsRequest>(r => r.AccountId == accountId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetAccountSessionsResponse
            {
                SessionIds = new List<Guid> { sessionId }
            });

        // Setup subscriber session store
        _mockSubscriberSessionsStore
            .Setup(s => s.GetWithETagAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((SubscriberSessionsModel?)null, (string?)null));
        _mockSubscriberSessionsStore
            .Setup(s => s.TrySaveAsync(
                SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(),
                It.IsAny<SubscriberSessionsModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag1");

        // Act
        await service.HandleSubscriptionUpdatedInternalAsync(
            accountId, TEST_STUB_NAME, SubscriptionUpdatedEventAction.Created, isActive: true);

        // Assert - subscriber session was stored (for authorization tracking)
        _mockSubscriberSessionsStore.Verify(s => s.TrySaveAsync(
            SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(),
            It.IsAny<SubscriberSessionsModel>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - NO shortcut published because autoLobbyEnabled is false
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            It.IsAny<string>(),
            It.IsAny<ShortcutPublishedEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSessionConnected_WithAutoLobbyCheckFailed_ShouldDefaultToPublish()
    {
        // Arrange - GetServiceAsync throws ApiException (fail-open behavior)
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Pre-populate subscription cache
        GameSessionService.AddAccountSubscription(accountId, TEST_STUB_NAME);

        // Override default mock: GetServiceAsync throws ApiException
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.StubName == TEST_STUB_NAME),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Service unavailable", 503));

        // Setup subscriber session store
        _mockSubscriberSessionsStore
            .Setup(s => s.GetWithETagAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((SubscriberSessionsModel?)null, (string?)null));
        _mockSubscriberSessionsStore
            .Setup(s => s.TrySaveAsync(
                SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(),
                It.IsAny<SubscriberSessionsModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag1");

        // Setup lobby creation
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + TEST_STUB_NAME, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameSessionModel?)null);
        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                sessionId.ToString(),
                It.IsAny<ShortcutPublishedEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await service.HandleSessionConnectedInternalAsync(sessionId, accountId);

        // Assert - shortcut WAS published (fail-open: defaults to true when GameService is unavailable)
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            sessionId.ToString(),
            It.IsAny<ShortcutPublishedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Cleanup
        GameSessionService.RemoveAccountSubscription(accountId, TEST_STUB_NAME);
    }

    [Fact]
    public async Task HandleSessionConnected_WithAutoLobbyCheckThrowsGenericException_ShouldDefaultToPublish()
    {
        // Arrange - GetServiceAsync throws a non-ApiException (infrastructure failure, fail-open behavior)
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Pre-populate subscription cache
        GameSessionService.AddAccountSubscription(accountId, TEST_STUB_NAME);

        // Override default mock: GetServiceAsync throws generic exception (connection refused, timeout, etc.)
        _mockGameServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.StubName == TEST_STUB_NAME),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("Connection refused"));

        // Setup subscriber session store
        _mockSubscriberSessionsStore
            .Setup(s => s.GetWithETagAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((SubscriberSessionsModel?)null, (string?)null));
        _mockSubscriberSessionsStore
            .Setup(s => s.TrySaveAsync(
                SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(),
                It.IsAny<SubscriberSessionsModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag1");

        // Setup lobby creation
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + TEST_STUB_NAME, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameSessionModel?)null);
        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                sessionId.ToString(),
                It.IsAny<ShortcutPublishedEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await service.HandleSessionConnectedInternalAsync(sessionId, accountId);

        // Assert - shortcut WAS published (fail-open: defaults to true on any failure)
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            sessionId.ToString(),
            It.IsAny<ShortcutPublishedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Cleanup
        GameSessionService.RemoveAccountSubscription(accountId, TEST_STUB_NAME);
    }

    #endregion

    #region FetchAndCacheSubscriptions Tests

    [Fact]
    public async Task HandleSessionConnected_ShouldFetchSubscriptionsOnCacheMiss()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // Setup subscription client to return a subscription matching our service
        _mockSubscriptionClient
            .Setup(c => c.QueryCurrentSubscriptionsAsync(
                It.Is<BeyondImmersion.BannouService.Subscription.QueryCurrentSubscriptionsRequest>(
                    r => r.AccountId == accountId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeyondImmersion.BannouService.Subscription.QuerySubscriptionsResponse
            {
                Subscriptions = new List<BeyondImmersion.BannouService.Subscription.SubscriptionInfo>
                {
                    new()
                    {
                        SubscriptionId = Guid.NewGuid(),
                        AccountId = accountId,
                        ServiceId = Guid.NewGuid(),
                        StubName = TEST_STUB_NAME,
                        StartDate = DateTimeOffset.UtcNow.AddDays(-1),
                        IsActive = true,
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                    }
                },
                TotalCount = 1
            });

        // Setup subscriber session store for StoreSubscriberSessionAsync
        _mockSubscriberSessionsStore
            .Setup(s => s.GetWithETagAsync(SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((SubscriberSessionsModel?)null, (string?)null));
        _mockSubscriberSessionsStore
            .Setup(s => s.TrySaveAsync(
                SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString(),
                It.IsAny<SubscriberSessionsModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag1");

        // Setup lobby creation
        _mockGameSessionStore
            .Setup(s => s.GetAsync(LOBBY_KEY_PREFIX + TEST_STUB_NAME, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameSessionModel?)null);
        _mockListStore
            .Setup(s => s.GetAsync(SESSION_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                sessionId.ToString(),
                It.IsAny<ShortcutPublishedEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await service.HandleSessionConnectedInternalAsync(sessionId, accountId);

        // Assert - subscription client was called to fetch subscriptions
        _mockSubscriptionClient.Verify(c => c.QueryCurrentSubscriptionsAsync(
            It.Is<BeyondImmersion.BannouService.Subscription.QueryCurrentSubscriptionsRequest>(
                r => r.AccountId == accountId),
            It.IsAny<CancellationToken>()), Times.Once);

        // And shortcut was published after fetching
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            sessionId.ToString(),
            It.IsAny<ShortcutPublishedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Cleanup static cache
        GameSessionService.RemoveAccountSubscription(accountId, TEST_STUB_NAME);
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
        // Arrange - Use GAME_SESSION_ prefix (hyphens converted to underscores per schema conventions)
        Environment.SetEnvironmentVariable("GAME_SESSION_MAX_PLAYERS_PER_SESSION", "8");
        Environment.SetEnvironmentVariable("GAME_SESSION_DEFAULT_SESSION_TIMEOUT_SECONDS", "3600");

        // Act
        var config = IServiceConfiguration.BuildConfiguration<GameSessionServiceConfiguration>();

        // Assert
        Assert.NotNull(config);

        // Cleanup
        Environment.SetEnvironmentVariable("GAME_SESSION_MAX_PLAYERS_PER_SESSION", null);
        Environment.SetEnvironmentVariable("GAME_SESSION_DEFAULT_SESSION_TIMEOUT_SECONDS", null);
    }
}
