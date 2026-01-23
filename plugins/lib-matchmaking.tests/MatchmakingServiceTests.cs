using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Matchmaking.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Matchmaking;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Matchmaking.Tests;

/// <summary>
/// Comprehensive unit tests for MatchmakingService.
/// Tests all queue operations, ticket lifecycle, match formation, and error handling.
/// </summary>
public class MatchmakingServiceTests : ServiceTestBase<MatchmakingServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<QueueModel>> _mockQueueStore;
    private readonly Mock<IStateStore<TicketModel>> _mockTicketStore;
    private readonly Mock<IStateStore<MatchModel>> _mockMatchStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockTicketListStore;
    private readonly Mock<IStateStore<List<string>>> _mockQueueListStore;
    private readonly Mock<IStateStore<PendingMatchWrapper>> _mockPendingMatchStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<MatchmakingService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IClientEventPublisher> _mockClientEventPublisher;
    private readonly Mock<IGameSessionClient> _mockGameSessionClient;
    private readonly Mock<BeyondImmersion.BannouService.Permission.IPermissionClient> _mockPermissionClient;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;

    private const string STATE_STORE = "matchmaking-statestore";
    private const string QUEUE_PREFIX = "queue:";
    private const string QUEUE_LIST_KEY = "queue-list";
    private const string TICKET_PREFIX = "ticket:";
    private const string MATCH_PREFIX = "match:";
    private const string PLAYER_TICKETS_PREFIX = "player-tickets:";
    private const string PENDING_MATCH_PREFIX = "pending-match:";
    private const string QUEUE_TICKETS_PREFIX = "queue-tickets:";

    private const string TEST_SERVER_SALT = "test-server-salt-2025";
    private const string TEST_QUEUE_ID = "test-queue";
    private const string TEST_GAME_ID = "arcadia";

    public MatchmakingServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockQueueStore = new Mock<IStateStore<QueueModel>>();
        _mockTicketStore = new Mock<IStateStore<TicketModel>>();
        _mockMatchStore = new Mock<IStateStore<MatchModel>>();
        _mockTicketListStore = new Mock<IStateStore<List<Guid>>>();
        _mockQueueListStore = new Mock<IStateStore<List<string>>>();
        _mockPendingMatchStore = new Mock<IStateStore<PendingMatchWrapper>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<MatchmakingService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockClientEventPublisher = new Mock<IClientEventPublisher>();
        _mockGameSessionClient = new Mock<IGameSessionClient>();
        _mockPermissionClient = new Mock<BeyondImmersion.BannouService.Permission.IPermissionClient>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();

        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<QueueModel>(STATE_STORE)).Returns(_mockQueueStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<TicketModel>(STATE_STORE)).Returns(_mockTicketStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<MatchModel>(STATE_STORE)).Returns(_mockMatchStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(STATE_STORE)).Returns(_mockTicketListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE)).Returns(_mockQueueListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<PendingMatchWrapper>(STATE_STORE)).Returns(_mockPendingMatchStore.Object);
    }

    /// <summary>
    /// Override to provide test configuration with required ServerSalt.
    /// </summary>
    protected override MatchmakingServiceConfiguration CreateConfiguration()
    {
        return new MatchmakingServiceConfiguration
        {
            ServerSalt = TEST_SERVER_SALT,
            ProcessingIntervalSeconds = 15,
            DefaultMaxIntervals = 6,
            MaxConcurrentTicketsPerPlayer = 3,
            DefaultMatchAcceptTimeoutSeconds = 30,
            ImmediateMatchCheckEnabled = true,
            AutoRequeueOnDecline = true,
            DefaultReservationTtlSeconds = 120,
            DefaultJoinDeadlineSeconds = 120,
            PendingMatchRedisKeyTtlSeconds = 300
        };
    }

    private MatchmakingService CreateService()
    {
        return new MatchmakingService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            Configuration,
            _mockEventConsumer.Object,
            _mockClientEventPublisher.Object,
            _mockGameSessionClient.Object,
            _mockPermissionClient.Object,
            _mockLockProvider.Object);
    }

    /// <summary>
    /// Sets up an existing queue for tests.
    /// </summary>
    private void SetupExistingQueue(string queueId, QueueModel queue)
    {
        _mockQueueStore
            .Setup(s => s.GetAsync(QUEUE_PREFIX + queueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(queue);
    }

    /// <summary>
    /// Sets up the queue list.
    /// </summary>
    private void SetupQueueList(List<string> queueIds)
    {
        _mockQueueListStore
            .Setup(s => s.GetAsync(QUEUE_LIST_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(queueIds);
    }

    /// <summary>
    /// Creates a standard test queue model.
    /// </summary>
    private static QueueModel CreateTestQueue(string queueId = TEST_QUEUE_ID, int minCount = 2, int maxCount = 4)
    {
        return new QueueModel
        {
            QueueId = queueId,
            GameId = TEST_GAME_ID,
            SessionGameType = SessionGameType.Arcadia,
            DisplayName = "Test Queue",
            Description = "A test queue",
            Enabled = true,
            MinCount = minCount,
            MaxCount = maxCount,
            CountMultiple = 1,
            IntervalSeconds = 15,
            MaxIntervals = 6,
            UseSkillRating = true,
            MatchAcceptTimeoutSeconds = 30,
            CreatedAt = DateTimeOffset.UtcNow
        };
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
    public void MatchmakingService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<MatchmakingService>();

    #endregion

    #region ListQueues Tests

    [Fact]
    public async Task ListQueuesAsync_WithNoQueues_ShouldReturnEmptyList()
    {
        // Arrange
        var service = CreateService();
        var request = new ListQueuesRequest { GameId = TEST_GAME_ID };
        SetupQueueList(new List<string>());

        // Act
        var (status, response) = await service.ListQueuesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Queues);
    }

    [Fact]
    public async Task ListQueuesAsync_WithActiveQueues_ShouldReturnQueues()
    {
        // Arrange
        var service = CreateService();
        var request = new ListQueuesRequest { GameId = TEST_GAME_ID };

        SetupQueueList(new List<string> { TEST_QUEUE_ID });
        SetupExistingQueue(TEST_QUEUE_ID, CreateTestQueue());

        // Setup queue tickets list
        _mockTicketListStore
            .Setup(s => s.GetAsync(QUEUE_TICKETS_PREFIX + TEST_QUEUE_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Act
        var (status, response) = await service.ListQueuesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Queues);
        Assert.Equal(TEST_QUEUE_ID, response.Queues.First().QueueId);
    }

    [Fact]
    public async Task ListQueuesAsync_ShouldFilterByGameId()
    {
        // Arrange
        var service = CreateService();
        var request = new ListQueuesRequest { GameId = "other-game" };

        SetupQueueList(new List<string> { TEST_QUEUE_ID });
        SetupExistingQueue(TEST_QUEUE_ID, CreateTestQueue()); // This queue is for TEST_GAME_ID

        // Setup queue tickets list
        _mockTicketListStore
            .Setup(s => s.GetAsync(QUEUE_TICKETS_PREFIX + TEST_QUEUE_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Act
        var (status, response) = await service.ListQueuesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Queues); // Filtered out because gameId doesn't match
    }

    #endregion

    #region GetQueue Tests

    [Fact]
    public async Task GetQueueAsync_WhenQueueExists_ShouldReturnQueue()
    {
        // Arrange
        var service = CreateService();
        var request = new GetQueueRequest { QueueId = TEST_QUEUE_ID };
        SetupExistingQueue(TEST_QUEUE_ID, CreateTestQueue());

        // Act
        var (status, response) = await service.GetQueueAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TEST_QUEUE_ID, response.QueueId);
        Assert.Equal("Test Queue", response.DisplayName);
    }

    [Fact]
    public async Task GetQueueAsync_WhenQueueNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetQueueRequest { QueueId = "nonexistent-queue" };

        _mockQueueStore
            .Setup(s => s.GetAsync(QUEUE_PREFIX + "nonexistent-queue", It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueueModel?)null);

        // Act
        var (status, response) = await service.GetQueueAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region CreateQueue Tests

    [Fact]
    public async Task CreateQueueAsync_WithValidRequest_ShouldReturnCreated()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateQueueRequest
        {
            QueueId = TEST_QUEUE_ID,
            GameId = TEST_GAME_ID,
            SessionGameType = SessionGameType.Arcadia,
            DisplayName = "New Queue",
            MinCount = 2,
            MaxCount = 8,
            UseSkillRating = true,
            MatchAcceptTimeoutSeconds = 30
        };

        _mockQueueStore
            .Setup(s => s.GetAsync(QUEUE_PREFIX + TEST_QUEUE_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueueModel?)null);

        SetupQueueList(new List<string>());

        // Act
        var (status, response) = await service.CreateQueueAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TEST_QUEUE_ID, response.QueueId);
        Assert.Equal("New Queue", response.DisplayName);

        // Verify state was saved
        _mockQueueStore.Verify(s => s.SaveAsync(
            QUEUE_PREFIX + TEST_QUEUE_ID,
            It.IsAny<QueueModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify event published - use 3-parameter overload that service calls
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "matchmaking.queue-created",
            It.IsAny<MatchmakingQueueCreatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateQueueAsync_WhenQueueAlreadyExists_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateQueueRequest
        {
            QueueId = TEST_QUEUE_ID,
            GameId = TEST_GAME_ID,
            DisplayName = "Duplicate Queue",
            MinCount = 2,
            MaxCount = 4
        };

        SetupExistingQueue(TEST_QUEUE_ID, CreateTestQueue());

        // Act
        var (status, response) = await service.CreateQueueAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region UpdateQueue Tests

    [Fact]
    public async Task UpdateQueueAsync_WhenQueueExists_ShouldUpdateQueue()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateQueueRequest
        {
            QueueId = TEST_QUEUE_ID,
            DisplayName = "Updated Queue Name"
        };

        SetupExistingQueue(TEST_QUEUE_ID, CreateTestQueue());

        // Act
        var (status, response) = await service.UpdateQueueAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Queue Name", response.DisplayName);

        // Verify state was saved
        _mockQueueStore.Verify(s => s.SaveAsync(
            QUEUE_PREFIX + TEST_QUEUE_ID,
            It.Is<QueueModel>(q => q.DisplayName == "Updated Queue Name"),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateQueueAsync_WhenQueueNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateQueueRequest { QueueId = "nonexistent" };

        _mockQueueStore
            .Setup(s => s.GetAsync(QUEUE_PREFIX + "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueueModel?)null);

        // Act
        var (status, response) = await service.UpdateQueueAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region DeleteQueue Tests

    [Fact]
    public async Task DeleteQueueAsync_WhenQueueExists_ShouldDeleteQueue()
    {
        // Arrange
        var service = CreateService();
        var request = new DeleteQueueRequest { QueueId = TEST_QUEUE_ID };

        SetupExistingQueue(TEST_QUEUE_ID, CreateTestQueue());
        SetupQueueList(new List<string> { TEST_QUEUE_ID });

        _mockTicketListStore
            .Setup(s => s.GetAsync(QUEUE_TICKETS_PREFIX + TEST_QUEUE_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Act
        var status = await service.DeleteQueueAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify queue was deleted
        _mockQueueStore.Verify(s => s.DeleteAsync(
            QUEUE_PREFIX + TEST_QUEUE_ID,
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify event published - use 3-parameter overload that service calls
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "matchmaking.queue-deleted",
            It.IsAny<MatchmakingQueueDeletedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteQueueAsync_WhenQueueNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new DeleteQueueRequest { QueueId = "nonexistent" };

        _mockQueueStore
            .Setup(s => s.GetAsync(QUEUE_PREFIX + "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueueModel?)null);

        // Act
        var status = await service.DeleteQueueAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region JoinMatchmaking Tests

    [Fact]
    public async Task JoinMatchmakingAsync_WhenQueueNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new JoinMatchmakingRequest
        {
            QueueId = "nonexistent",
            AccountId = accountId,
            WebSocketSessionId = sessionId
        };

        _mockQueueStore
            .Setup(s => s.GetAsync(QUEUE_PREFIX + "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueueModel?)null);

        // Act
        var (status, response) = await service.JoinMatchmakingAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task JoinMatchmakingAsync_WhenQueueDisabled_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new JoinMatchmakingRequest
        {
            QueueId = TEST_QUEUE_ID,
            AccountId = accountId,
            WebSocketSessionId = sessionId
        };

        var disabledQueue = CreateTestQueue();
        disabledQueue.Enabled = false;
        SetupExistingQueue(TEST_QUEUE_ID, disabledQueue);

        // Act
        var (status, response) = await service.JoinMatchmakingAsync(request, CancellationToken.None);

        // Assert - service returns Conflict for disabled queues
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task JoinMatchmakingAsync_WhenPlayerExceedsMaxTickets_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new JoinMatchmakingRequest
        {
            QueueId = TEST_QUEUE_ID,
            AccountId = accountId,
            WebSocketSessionId = sessionId
        };

        SetupExistingQueue(TEST_QUEUE_ID, CreateTestQueue());

        // Player already has max tickets (3)
        var existingTickets = new List<Guid>
        {
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid()
        };

        _mockTicketListStore
            .Setup(s => s.GetAsync(PLAYER_TICKETS_PREFIX + accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTickets);

        // Act
        var (status, response) = await service.JoinMatchmakingAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task JoinMatchmakingAsync_WithValidRequest_ShouldCreateTicket()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new JoinMatchmakingRequest
        {
            QueueId = TEST_QUEUE_ID,
            AccountId = accountId,
            WebSocketSessionId = sessionId
        };

        SetupExistingQueue(TEST_QUEUE_ID, CreateTestQueue());

        // No existing tickets
        _mockTicketListStore
            .Setup(s => s.GetAsync(PLAYER_TICKETS_PREFIX + accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        _mockTicketListStore
            .Setup(s => s.GetAsync(QUEUE_TICKETS_PREFIX + TEST_QUEUE_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Mock Permission client for state update
        _mockPermissionClient
            .Setup(p => p.UpdateSessionStateAsync(It.IsAny<BeyondImmersion.BannouService.Permission.SessionStateUpdate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeyondImmersion.BannouService.Permission.SessionUpdateResponse());

        // Act
        var (status, response) = await service.JoinMatchmakingAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.TicketId);
        Assert.Equal(TEST_QUEUE_ID, response.QueueId);

        // Verify ticket was saved
        _mockTicketStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith(TICKET_PREFIX)),
            It.IsAny<TicketModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify event published - use 3-parameter overload that service calls
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "matchmaking.ticket-created",
            It.IsAny<MatchmakingTicketCreatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify client event sent
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            sessionId.ToString(),
            It.IsAny<QueueJoinedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region LeaveMatchmaking Tests

    [Fact]
    public async Task LeaveMatchmakingAsync_WithValidTicket_ShouldCancelTicket()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new LeaveMatchmakingRequest
        {
            TicketId = ticketId,
            AccountId = accountId
        };

        var ticket = new TicketModel
        {
            TicketId = ticketId,
            AccountId = accountId,
            QueueId = TEST_QUEUE_ID,
            WebSocketSessionId = sessionId,
            Status = TicketStatus.Searching,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockTicketStore
            .Setup(s => s.GetAsync(TICKET_PREFIX + ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        _mockTicketListStore
            .Setup(s => s.GetAsync(PLAYER_TICKETS_PREFIX + accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { ticketId });

        _mockTicketListStore
            .Setup(s => s.GetAsync(QUEUE_TICKETS_PREFIX + TEST_QUEUE_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { ticketId });

        SetupExistingQueue(TEST_QUEUE_ID, CreateTestQueue());

        _mockPermissionClient
            .Setup(p => p.ClearSessionStateAsync(It.IsAny<BeyondImmersion.BannouService.Permission.ClearSessionStateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeyondImmersion.BannouService.Permission.SessionUpdateResponse());

        // Act
        var status = await service.LeaveMatchmakingAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify ticket was deleted
        _mockTicketStore.Verify(s => s.DeleteAsync(
            TICKET_PREFIX + ticketId,
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify event published - use 3-parameter overload that service calls
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "matchmaking.ticket-cancelled",
            It.IsAny<MatchmakingTicketCancelledEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LeaveMatchmakingAsync_WhenTicketNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new LeaveMatchmakingRequest
        {
            TicketId = Guid.NewGuid(),
            AccountId = Guid.NewGuid()
        };

        _mockTicketStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TicketModel?)null);

        // Act
        var status = await service.LeaveMatchmakingAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region AcceptMatch Tests

    [Fact]
    public async Task AcceptMatchAsync_WithValidMatch_ShouldAcceptForPlayer()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var request = new AcceptMatchRequest
        {
            MatchId = matchId,
            AccountId = accountId
        };

        var match = new MatchModel
        {
            MatchId = matchId,
            QueueId = TEST_QUEUE_ID,
            PlayerCount = 2,
            AcceptDeadline = DateTimeOffset.UtcNow.AddSeconds(30),
            AcceptedPlayers = new List<Guid>(),
            MatchedTickets = new List<MatchedTicketModel>
            {
                new() { TicketId = ticketId, AccountId = accountId, WebSocketSessionId = sessionId },
                new() { TicketId = Guid.NewGuid(), AccountId = Guid.NewGuid(), WebSocketSessionId = Guid.NewGuid() }
            },
            CreatedAt = DateTimeOffset.UtcNow,
            Status = MatchStatus.Pending
        };

        _mockMatchStore
            .Setup(s => s.GetAsync(MATCH_PREFIX + matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _mockPendingMatchStore
            .Setup(s => s.GetAsync(PENDING_MATCH_PREFIX + accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PendingMatchWrapper { MatchId = matchId });

        // Act
        var (status, response) = await service.AcceptMatchAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(matchId, response.MatchId);
        Assert.Equal(1, response.AcceptedCount);
        Assert.False(response.AllAccepted);

        // Verify match state was saved
        _mockMatchStore.Verify(s => s.SaveAsync(
            MATCH_PREFIX + matchId,
            It.Is<MatchModel>(m => m.AcceptedPlayers.Contains(accountId)),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptMatchAsync_WhenMatchNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new AcceptMatchRequest
        {
            MatchId = Guid.NewGuid(),
            AccountId = Guid.NewGuid()
        };

        _mockMatchStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchModel?)null);

        // Act
        var (status, response) = await service.AcceptMatchAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AcceptMatchAsync_WhenAcceptDeadlinePassed_ShouldReturnGone()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        var request = new AcceptMatchRequest
        {
            MatchId = matchId,
            AccountId = accountId
        };

        var expiredMatch = new MatchModel
        {
            MatchId = matchId,
            QueueId = TEST_QUEUE_ID,
            AcceptDeadline = DateTimeOffset.UtcNow.AddSeconds(-10), // Already expired
            AcceptedPlayers = new List<Guid>(),
            MatchedTickets = new List<MatchedTicketModel>
            {
                new() { TicketId = Guid.NewGuid(), AccountId = accountId }
            },
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Status = MatchStatus.Pending
        };

        _mockMatchStore
            .Setup(s => s.GetAsync(MATCH_PREFIX + matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredMatch);

        _mockPendingMatchStore
            .Setup(s => s.GetAsync(PENDING_MATCH_PREFIX + accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PendingMatchWrapper { MatchId = matchId });

        // Act
        var (status, response) = await service.AcceptMatchAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region DeclineMatch Tests

    [Fact]
    public async Task DeclineMatchAsync_WithValidMatch_ShouldDecline()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var request = new DeclineMatchRequest
        {
            MatchId = matchId,
            AccountId = accountId
        };

        var match = new MatchModel
        {
            MatchId = matchId,
            QueueId = TEST_QUEUE_ID,
            PlayerCount = 2,
            AcceptDeadline = DateTimeOffset.UtcNow.AddSeconds(30),
            AcceptedPlayers = new List<Guid>(),
            MatchedTickets = new List<MatchedTicketModel>
            {
                new() { TicketId = ticketId, AccountId = accountId, WebSocketSessionId = sessionId },
                new() { TicketId = Guid.NewGuid(), AccountId = Guid.NewGuid(), WebSocketSessionId = Guid.NewGuid() }
            },
            CreatedAt = DateTimeOffset.UtcNow,
            Status = MatchStatus.Pending
        };

        _mockMatchStore
            .Setup(s => s.GetAsync(MATCH_PREFIX + matchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(match);

        _mockPendingMatchStore
            .Setup(s => s.GetAsync(PENDING_MATCH_PREFIX + accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PendingMatchWrapper { MatchId = matchId });

        SetupExistingQueue(TEST_QUEUE_ID, CreateTestQueue());

        // Setup ticket store for the decliner
        _mockTicketStore
            .Setup(s => s.GetAsync(TICKET_PREFIX + ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TicketModel
            {
                TicketId = ticketId,
                AccountId = accountId,
                QueueId = TEST_QUEUE_ID,
                CreatedAt = DateTimeOffset.UtcNow
            });

        _mockTicketListStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        _mockPermissionClient
            .Setup(p => p.ClearSessionStateAsync(It.IsAny<BeyondImmersion.BannouService.Permission.ClearSessionStateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BeyondImmersion.BannouService.Permission.SessionUpdateResponse());

        // Act
        var status = await service.DeclineMatchAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify decline event sent - use 3-parameter overload that service calls
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "matchmaking.match-declined",
            It.IsAny<MatchmakingMatchDeclinedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeclineMatchAsync_WhenMatchNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new DeclineMatchRequest
        {
            MatchId = Guid.NewGuid(),
            AccountId = Guid.NewGuid()
        };

        _mockMatchStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchModel?)null);

        // Act
        var status = await service.DeclineMatchAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region GetMatchmakingStatus Tests

    [Fact]
    public async Task GetMatchmakingStatusAsync_WithActiveTicket_ShouldReturnStatus()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var ticketId = Guid.NewGuid();

        var request = new GetMatchmakingStatusRequest
        {
            TicketId = ticketId,
            AccountId = accountId
        };

        var ticket = new TicketModel
        {
            TicketId = ticketId,
            AccountId = accountId,
            QueueId = TEST_QUEUE_ID,
            Status = TicketStatus.Searching,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        _mockTicketStore
            .Setup(s => s.GetAsync(TICKET_PREFIX + ticketId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ticket);

        SetupExistingQueue(TEST_QUEUE_ID, CreateTestQueue());

        // Act
        var (status, response) = await service.GetMatchmakingStatusAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(ticketId, response.TicketId);
        Assert.Equal(TicketStatus.Searching, response.Status);
    }

    [Fact]
    public async Task GetMatchmakingStatusAsync_WhenTicketNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetMatchmakingStatusRequest
        {
            TicketId = Guid.NewGuid(),
            AccountId = Guid.NewGuid()
        };

        _mockTicketStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TicketModel?)null);

        // Act
        var (status, response) = await service.GetMatchmakingStatusAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void Configuration_ShouldHaveCorrectDefaults()
    {
        // Arrange
        var config = new MatchmakingServiceConfiguration();

        // Assert
        Assert.Equal(15, config.ProcessingIntervalSeconds);
        Assert.Equal(6, config.DefaultMaxIntervals);
        Assert.Equal(3, config.MaxConcurrentTicketsPerPlayer);
        Assert.Equal(30, config.DefaultMatchAcceptTimeoutSeconds);
        Assert.True(config.ImmediateMatchCheckEnabled);
        Assert.True(config.AutoRequeueOnDecline);
        Assert.Equal(120, config.DefaultReservationTtlSeconds);
        Assert.Equal(120, config.DefaultJoinDeadlineSeconds);
    }

    #endregion
}

// Internal model types are accessed from MatchmakingService via [assembly: InternalsVisibleTo("lib-matchmaking.tests")]
// See lib-matchmaking/MatchmakingService.cs for type definitions
