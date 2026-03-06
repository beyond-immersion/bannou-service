using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Chat;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Chat.Tests;

/// <summary>
/// Base class for ChatService unit tests providing shared mock infrastructure,
/// state store wiring, and test data builders.
/// </summary>
public abstract class ChatServiceTestBase : ServiceTestBase<ChatServiceConfiguration>
{
    // Infrastructure mocks
    protected readonly Mock<IMessageBus> MockMessageBus;
    protected readonly Mock<IStateStoreFactory> MockStateStoreFactory;
    protected readonly Mock<IClientEventPublisher> MockClientEventPublisher;
    protected readonly Mock<IDistributedLockProvider> MockLockProvider;
    protected readonly Mock<ILogger<ChatService>> MockLogger;
    protected readonly Mock<IEventConsumer> MockEventConsumer;

    // Service client mocks
    protected readonly Mock<IContractClient> MockContractClient;
    protected readonly Mock<IResourceClient> MockResourceClient;
    protected readonly Mock<IPermissionClient> MockPermissionClient;
    protected readonly Mock<IEntitySessionRegistry> MockEntitySessionRegistry;
    protected readonly Mock<ITelemetryProvider> MockTelemetryProvider;

    // State store mocks (private protected: internal model types not visible outside assembly)
    private protected readonly Mock<IJsonQueryableStateStore<ChatRoomTypeModel>> MockRoomTypeStore;
    private protected readonly Mock<IJsonQueryableStateStore<ChatRoomModel>> MockRoomStore;
    private protected readonly Mock<IStateStore<ChatRoomModel>> MockRoomCache;
    private protected readonly Mock<IJsonQueryableStateStore<ChatMessageModel>> MockMessageStore;
    private protected readonly Mock<IStateStore<ChatMessageModel>> MockMessageBuffer;
    private protected readonly Mock<ICacheableStateStore<ChatParticipantModel>> MockParticipantStore;
    private protected readonly Mock<IJsonQueryableStateStore<ChatBanModel>> MockBanStore;

    // Test data
    protected readonly Guid TestRoomId = Guid.NewGuid();
    protected readonly Guid TestSessionId = Guid.NewGuid();
    protected readonly Guid TestGameServiceId = Guid.NewGuid();
    protected readonly Guid TestContractId = Guid.NewGuid();

    protected ChatServiceTestBase()
    {
        // Initialize infrastructure mocks
        MockMessageBus = new Mock<IMessageBus>();
        MockStateStoreFactory = new Mock<IStateStoreFactory>();
        MockClientEventPublisher = new Mock<IClientEventPublisher>();
        MockLockProvider = new Mock<IDistributedLockProvider>();
        MockLogger = new Mock<ILogger<ChatService>>();
        MockEventConsumer = new Mock<IEventConsumer>();

        // Initialize service client mocks
        MockContractClient = new Mock<IContractClient>();
        MockResourceClient = new Mock<IResourceClient>();
        MockPermissionClient = new Mock<IPermissionClient>();
        MockEntitySessionRegistry = new Mock<IEntitySessionRegistry>();
        MockTelemetryProvider = new Mock<ITelemetryProvider>();

        // Initialize state store mocks
        MockRoomTypeStore = new Mock<IJsonQueryableStateStore<ChatRoomTypeModel>>();
        MockRoomStore = new Mock<IJsonQueryableStateStore<ChatRoomModel>>();
        MockRoomCache = new Mock<IStateStore<ChatRoomModel>>();
        MockMessageStore = new Mock<IJsonQueryableStateStore<ChatMessageModel>>();
        MockMessageBuffer = new Mock<IStateStore<ChatMessageModel>>();
        MockParticipantStore = new Mock<ICacheableStateStore<ChatParticipantModel>>();
        MockBanStore = new Mock<IJsonQueryableStateStore<ChatBanModel>>();

        // Wire up state store factory
        MockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<ChatRoomTypeModel>(StateStoreDefinitions.ChatRoomTypes))
            .Returns(MockRoomTypeStore.Object);
        MockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<ChatRoomModel>(StateStoreDefinitions.ChatRooms))
            .Returns(MockRoomStore.Object);
        MockStateStoreFactory
            .Setup(f => f.GetStore<ChatRoomModel>(StateStoreDefinitions.ChatRoomsCache))
            .Returns(MockRoomCache.Object);
        MockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<ChatMessageModel>(StateStoreDefinitions.ChatMessages))
            .Returns(MockMessageStore.Object);
        MockStateStoreFactory
            .Setup(f => f.GetStore<ChatMessageModel>(StateStoreDefinitions.ChatMessagesEphemeral))
            .Returns(MockMessageBuffer.Object);
        MockStateStoreFactory
            .Setup(f => f.GetCacheableStore<ChatParticipantModel>(StateStoreDefinitions.ChatParticipants))
            .Returns(MockParticipantStore.Object);
        MockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<ChatBanModel>(StateStoreDefinitions.ChatBans))
            .Returns(MockBanStore.Object);

        // Default: distributed lock succeeds
        SetupLockSuccess();

        // Default: message bus fire-and-forget succeeds
        MockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(),
                It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        MockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Note: PublishToSessionsAsync<TEvent> has BaseClientEvent constraint.
        // Individual tests mock specific event types as needed. No default mock here.

        // Default: participant store returns empty hash (no participants)
        MockParticipantStore
            .Setup(s => s.HashGetAllAsync<ChatParticipantModel>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ChatParticipantModel>());

        // Default: participant store count returns 0
        MockParticipantStore
            .Setup(s => s.HashCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        // Default: permission client succeeds silently
        MockPermissionClient
            .Setup(p => p.UpdateSessionStateAsync(
                It.IsAny<SessionStateUpdate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionUpdateResponse());
        MockPermissionClient
            .Setup(p => p.ClearSessionStateAsync(
                It.IsAny<ClearSessionStateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionUpdateResponse());
    }

    /// <summary>
    /// Creates a new ChatService instance with all mocked dependencies.
    /// </summary>
    protected ChatService CreateService()
    {
        return new ChatService(
            MockMessageBus.Object,
            MockStateStoreFactory.Object,
            MockClientEventPublisher.Object,
            MockLockProvider.Object,
            MockLogger.Object,
            Configuration,
            MockEventConsumer.Object,
            MockContractClient.Object,
            MockResourceClient.Object,
            MockPermissionClient.Object,
            MockEntitySessionRegistry.Object,
            MockTelemetryProvider.Object);
    }

    // ============================================================================
    // LOCK HELPERS
    // ============================================================================

    /// <summary>
    /// Configures the lock provider to always succeed.
    /// </summary>
    protected void SetupLockSuccess()
    {
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(r => r.Success).Returns(true);
        mockLockResponse.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
        MockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    /// <summary>
    /// Configures the lock provider to always fail.
    /// </summary>
    protected void SetupLockFailure()
    {
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(r => r.Success).Returns(false);
        mockLockResponse.Setup(r => r.DisposeAsync()).Returns(ValueTask.CompletedTask);
        MockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    // ============================================================================
    // SESSION CONTEXT HELPERS
    // ============================================================================

    /// <summary>
    /// Sets the ServiceRequestContext.SessionId for the current test.
    /// Must be called before invoking any service method that reads SessionId.
    /// </summary>
    protected void SetCallerSession(Guid sessionId)
    {
        ServiceRequestContext.SessionId = sessionId.ToString();
    }

    /// <summary>
    /// Clears the ServiceRequestContext.SessionId.
    /// </summary>
    protected void ClearCallerSession()
    {
        ServiceRequestContext.SessionId = null;
    }

    // ============================================================================
    // TEST DATA BUILDERS
    // ============================================================================

    /// <summary>
    /// Creates a test room type model with sensible defaults.
    /// </summary>
    private protected ChatRoomTypeModel CreateTestRoomType(
        string code = "text",
        MessageFormat messageFormat = MessageFormat.Text,
        PersistenceMode persistenceMode = PersistenceMode.Persistent,
        Guid? gameServiceId = null,
        RoomTypeStatus status = RoomTypeStatus.Active,
        int? rateLimitPerMinute = null,
        int? defaultMaxParticipants = null)
    {
        return new ChatRoomTypeModel
        {
            Code = code,
            DisplayName = $"Test {code}",
            Description = $"Test room type: {code}",
            GameServiceId = gameServiceId,
            MessageFormat = messageFormat,
            PersistenceMode = persistenceMode,
            DefaultMaxParticipants = defaultMaxParticipants,
            RateLimitPerMinute = rateLimitPerMinute,
            AllowAnonymousSenders = false,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
        };
    }

    /// <summary>
    /// Creates a test room model with sensible defaults.
    /// </summary>
    private protected ChatRoomModel CreateTestRoom(
        Guid? roomId = null,
        string roomTypeCode = "text",
        Guid? sessionId = null,
        Guid? contractId = null,
        ChatRoomStatus status = ChatRoomStatus.Active,
        bool isArchived = false,
        int? maxParticipants = null)
    {
        return new ChatRoomModel
        {
            RoomId = roomId ?? TestRoomId,
            RoomTypeCode = roomTypeCode,
            SessionId = sessionId,
            ContractId = contractId,
            DisplayName = "Test Room",
            Status = status,
            MaxParticipants = maxParticipants,
            IsArchived = isArchived,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        };
    }

    /// <summary>
    /// Creates a test participant model.
    /// </summary>
    private protected ChatParticipantModel CreateTestParticipant(
        Guid? roomId = null,
        Guid? sessionId = null,
        ChatParticipantRole role = ChatParticipantRole.Member,
        bool isMuted = false)
    {
        return new ChatParticipantModel
        {
            RoomId = roomId ?? TestRoomId,
            SessionId = sessionId ?? TestSessionId,
            SenderType = "player",
            SenderId = Guid.NewGuid(),
            DisplayName = "TestPlayer",
            Role = role,
            JoinedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            IsMuted = isMuted,
        };
    }

    /// <summary>
    /// Creates a test message model.
    /// </summary>
    private protected ChatMessageModel CreateTestMessage(
        Guid? roomId = null,
        Guid? messageId = null,
        string? textContent = "Hello world",
        bool isPinned = false)
    {
        return new ChatMessageModel
        {
            MessageId = messageId ?? Guid.NewGuid(),
            RoomId = roomId ?? TestRoomId,
            SenderType = "player",
            SenderId = Guid.NewGuid(),
            DisplayName = "TestSender",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
            MessageFormat = MessageFormat.Text,
            TextContent = textContent,
            IsPinned = isPinned,
        };
    }

    // ============================================================================
    // STORE SETUP HELPERS
    // ============================================================================

    /// <summary>
    /// Sets up a room type to be found by direct key lookup.
    /// </summary>
    private protected void SetupRoomType(ChatRoomTypeModel roomType, Guid? gameServiceId = null)
    {
        var scope = gameServiceId.HasValue ? gameServiceId.Value.ToString() : "global";
        var typeKey = $"type:{scope}:{roomType.Code}";
        MockRoomTypeStore
            .Setup(s => s.GetAsync(typeKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roomType);
    }

    /// <summary>
    /// Sets up FindRoomTypeByCodeAsync to find a room type via query.
    /// </summary>
    private protected void SetupFindRoomTypeByCode(ChatRoomTypeModel roomType)
    {
        var queryResult = new JsonQueryResult<ChatRoomTypeModel>(
            $"type:global:{roomType.Code}", roomType);
        MockRoomTypeStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.Is<IReadOnlyList<QueryCondition>>(c =>
                    c.Any(q => q.Path == "$.Code" && q.Operator == QueryOperator.Equals && (string)q.Value == roomType.Code)),
                0, 1, It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomTypeModel>(
                new List<JsonQueryResult<ChatRoomTypeModel>> { queryResult }, 1, 0, 1));
    }

    /// <summary>
    /// Sets up a room to be found by key lookup (MySQL store).
    /// </summary>
    private protected void SetupRoom(ChatRoomModel room)
    {
        var roomKey = $"room:{room.RoomId}";
        MockRoomStore
            .Setup(s => s.GetAsync(roomKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(room);
    }

    /// <summary>
    /// Sets up a room to be found via cache (Redis).
    /// </summary>
    private protected void SetupRoomCache(ChatRoomModel room)
    {
        var roomKey = $"room:{room.RoomId}";
        MockRoomCache
            .Setup(s => s.GetAsync(roomKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(room);
    }

    /// <summary>
    /// Sets up participants for a room in the hash store.
    /// </summary>
    private protected void SetupParticipants(Guid roomId, params ChatParticipantModel[] participants)
    {
        var dict = participants.ToDictionary(p => p.SessionId.ToString(), p => p);

        MockParticipantStore
            .Setup(s => s.HashGetAllAsync<ChatParticipantModel>(
                roomId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dict);

        MockParticipantStore
            .Setup(s => s.HashCountAsync(roomId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(participants.Length);

        foreach (var p in participants)
        {
            MockParticipantStore
                .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                    roomId.ToString(), p.SessionId.ToString(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(p);
        }
    }

    /// <summary>
    /// Sets up a ban record for a session in a room.
    /// </summary>
    private protected void SetupBan(Guid roomId, Guid targetSessionId, ChatBanModel ban)
    {
        var banKey = $"ban:{roomId}:{targetSessionId}";
        MockBanStore
            .Setup(s => s.GetAsync(banKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ban);
    }

    /// <summary>
    /// Sets up room query results for ListRooms/AdminListRooms.
    /// </summary>
    private protected void SetupRoomQuery(List<ChatRoomModel> rooms, long totalCount, int offset = 0, int limit = 20)
    {
        var queryResults = rooms.Select(r =>
            new JsonQueryResult<ChatRoomModel>($"room:{r.RoomId}", r)).ToList();
        MockRoomStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(queryResults, totalCount, offset, limit));
    }

    /// <summary>
    /// Sets up room type query results for ListRoomTypes.
    /// </summary>
    private protected void SetupRoomTypeQuery(List<ChatRoomTypeModel> types, long totalCount, int offset = 0, int limit = 20)
    {
        var queryResults = types.Select(t =>
            new JsonQueryResult<ChatRoomTypeModel>($"type:global:{t.Code}", t)).ToList();
        MockRoomTypeStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomTypeModel>(queryResults, totalCount, offset, limit));
    }

    /// <summary>
    /// Sets up message query results for GetMessageHistory/SearchMessages.
    /// </summary>
    private protected void SetupMessageQuery(List<ChatMessageModel> messages, long totalCount, int offset = 0, int limit = 50)
    {
        var queryResults = messages.Select(m =>
            new JsonQueryResult<ChatMessageModel>($"{m.RoomId}:{m.MessageId}", m)).ToList();
        MockMessageStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatMessageModel>(queryResults, totalCount, offset, limit));
    }
}
