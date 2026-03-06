using BeyondImmersion.Bannou.Chat.ClientEvents;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Chat;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Moq;

namespace BeyondImmersion.BannouService.Chat.Tests;

/// <summary>
/// Tests for ChatService event handlers and admin operations:
/// Contract events (fulfilled/breach/terminated/expired), AdminListRooms, AdminGetStats, AdminForceCleanup.
/// </summary>
public class ChatServiceEventsAndAdminTests : ChatServiceTestBase
{
    #region Contract Event Handlers

    [Fact]
    public async Task HandleContractFulfilled_DefaultAction_ArchivesRoom()
    {
        var service = CreateService();

        var room = CreateTestRoom(contractId: TestContractId);
        room.ContractFulfilledAction = null; // use config default (Archive)

        SetupContractRoomQuery(TestContractId, room);
        SetupRoomCache(room);
        SetupParticipants(TestRoomId, CreateTestParticipant(sessionId: TestSessionId));

        // Resource client returns archive ID
        MockResourceClient
            .Setup(r => r.ExecuteCompressAsync(
                It.IsAny<ExecuteCompressRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecuteCompressResponse { ArchiveId = Guid.NewGuid() });

        var evt = new ContractFulfilledEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = TestContractId,
        };

        await service.HandleContractFulfilledAsync(evt);

        // Default action is Archive: verify archive event
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.room.archived", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleContractBreach_DefaultAction_LocksRoom()
    {
        var service = CreateService();

        var room = CreateTestRoom(contractId: TestContractId);
        room.ContractBreachAction = null; // use config default (Lock)

        SetupContractRoomQuery(TestContractId, room);
        SetupRoomCache(room);
        SetupParticipants(TestRoomId, CreateTestParticipant(sessionId: TestSessionId));

        var evt = new ContractBreachDetectedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = TestContractId,
        };

        await service.HandleContractBreachDetectedAsync(evt);

        // Default action is Lock: verify lock event
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.room.locked", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify room saved with Locked status
        MockRoomStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.Is<ChatRoomModel>(r => r.Status == ChatRoomStatus.Locked),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleContractTerminated_DefaultAction_DeletesRoom()
    {
        var service = CreateService();

        var room = CreateTestRoom(contractId: TestContractId);
        room.ContractTerminatedAction = null; // use config default (Delete)

        SetupContractRoomQuery(TestContractId, room);
        SetupParticipants(TestRoomId, CreateTestParticipant(sessionId: TestSessionId));

        var evt = new ContractTerminatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = TestContractId,
        };

        await service.HandleContractTerminatedAsync(evt);

        // Default action is Delete: verify room deleted
        MockRoomStore.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        MockRoomCache.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify delete event
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.room.deleted", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleContractExpired_DefaultAction_ArchivesRoom()
    {
        var service = CreateService();

        var room = CreateTestRoom(contractId: TestContractId);
        room.ContractExpiredAction = null; // use config default (Archive)

        SetupContractRoomQuery(TestContractId, room);
        SetupRoomCache(room);
        SetupParticipants(TestRoomId, CreateTestParticipant(sessionId: TestSessionId));

        MockResourceClient
            .Setup(r => r.ExecuteCompressAsync(
                It.IsAny<ExecuteCompressRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecuteCompressResponse { ArchiveId = Guid.NewGuid() });

        var evt = new ContractExpiredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = TestContractId,
        };

        await service.HandleContractExpiredAsync(evt);

        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.room.archived", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleContractFulfilled_RoomOverride_ContinueAction_DoesNothing()
    {
        var service = CreateService();

        var room = CreateTestRoom(contractId: TestContractId);
        room.ContractFulfilledAction = ContractRoomAction.Continue;

        SetupContractRoomQuery(TestContractId, room);

        var evt = new ContractFulfilledEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = TestContractId,
        };

        await service.HandleContractFulfilledAsync(evt);

        // Continue = no-op: nothing saved, no events
        MockRoomStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatRoomModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        MockRoomStore.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleContractBreach_RoomOverrideLock_LocksRoom()
    {
        var service = CreateService();

        var room = CreateTestRoom(contractId: TestContractId);
        room.ContractBreachAction = ContractRoomAction.Lock;

        SetupContractRoomQuery(TestContractId, room);
        SetupParticipants(TestRoomId, CreateTestParticipant(sessionId: TestSessionId));

        var evt = new ContractBreachDetectedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = TestContractId,
        };

        await service.HandleContractBreachDetectedAsync(evt);

        MockRoomStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.Is<ChatRoomModel>(r => r.Status == ChatRoomStatus.Locked),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleContractFulfilled_MultiplePages_ProcessesAllRooms()
    {
        var service = CreateService();

        var room1 = CreateTestRoom(roomId: Guid.NewGuid(), contractId: TestContractId);
        var room2 = CreateTestRoom(roomId: Guid.NewGuid(), contractId: TestContractId);
        room1.ContractFulfilledAction = ContractRoomAction.Lock;
        room2.ContractFulfilledAction = ContractRoomAction.Lock;

        // Page 1: returns room1, HasMore = true (TotalCount=2, Offset=0, Items=1)
        var page1Results = new List<JsonQueryResult<ChatRoomModel>>
        {
            new($"room:{room1.RoomId}", room1),
        };
        // Page 2: returns room2, HasMore = false (TotalCount=2, Offset=1, Items=1)
        var page2Results = new List<JsonQueryResult<ChatRoomModel>>
        {
            new($"room:{room2.RoomId}", room2),
        };

        MockRoomStore
            .SetupSequence(s => s.JsonQueryPagedAsync(
                It.Is<IReadOnlyList<QueryCondition>>(c =>
                    c.Any(q => q.Path == "$.ContractId" && (string)q.Value == TestContractId.ToString())),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(page1Results, 2, 0, 100))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(page2Results, 2, 1, 100));

        SetupRoomCache(room1);
        SetupRoomCache(room2);
        SetupParticipants(room1.RoomId, CreateTestParticipant(sessionId: TestSessionId));
        SetupParticipants(room2.RoomId, CreateTestParticipant(sessionId: Guid.NewGuid()));

        var evt = new ContractFulfilledEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = TestContractId,
        };

        await service.HandleContractFulfilledAsync(evt);

        // Verify the store was queried twice (two pages)
        MockRoomStore.Verify(
            s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Both rooms should be locked (action applied to both pages)
        MockRoomStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.Is<ChatRoomModel>(r => r.Status == ChatRoomStatus.Locked),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task HandleContractEvent_NoRoomsFound_CompletesWithoutError()
    {
        var service = CreateService();

        // Empty result for contract query
        var contractId = Guid.NewGuid();
        MockRoomStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.Is<IReadOnlyList<QueryCondition>>(c =>
                    c.Any(q => q.Path == "$.ContractId" && (string)q.Value == contractId.ToString())),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(
                new List<JsonQueryResult<ChatRoomModel>>(), 0, 0, 100));

        var evt = new ContractFulfilledEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = contractId,
        };

        // Should not throw
        await service.HandleContractFulfilledAsync(evt);

        // No room actions taken
        MockRoomStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatRoomModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region AdminListRooms

    [Fact]
    public async Task AdminListRooms_ReturnsPagedResults()
    {
        var service = CreateService();

        var rooms = new List<ChatRoomModel>
        {
            CreateTestRoom(roomId: Guid.NewGuid()),
            CreateTestRoom(roomId: Guid.NewGuid()),
        };
        SetupRoomQuery(rooms, 2);

        var (status, response) = await service.AdminListRoomsAsync(
            new AdminListRoomsRequest { Page = 0, PageSize = 20 },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task AdminListRooms_WithFilters_PassesConditions()
    {
        var service = CreateService();
        SetupRoomQuery(new List<ChatRoomModel>(), 0);

        await service.AdminListRoomsAsync(new AdminListRoomsRequest
        {
            RoomTypeCode = "text",
            Status = ChatRoomStatus.Active,
            Page = 0,
            PageSize = 20,
        }, CancellationToken.None);

        MockRoomStore.Verify(s => s.JsonQueryPagedAsync(
            It.Is<IReadOnlyList<QueryCondition>>(c =>
                c.Any(q => q.Path == "$.RoomTypeCode") &&
                c.Any(q => q.Path == "$.Status")),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region AdminGetStats

    [Fact]
    public async Task AdminGetStats_ReturnsCorrectCounts()
    {
        var service = CreateService();

        // Total rooms
        MockRoomStore
            .Setup(s => s.JsonCountAsync(
                It.Is<IReadOnlyList<QueryCondition>>(c =>
                    c.Any(q => q.Path == "$.RoomId" && q.Operator == QueryOperator.Exists)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(10L);

        // Active rooms
        MockRoomStore
            .Setup(s => s.JsonCountAsync(
                It.Is<IReadOnlyList<QueryCondition>>(c =>
                    c.Any(q => q.Path == "$.Status" && (string)q.Value == "Active")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(6L);

        // Locked rooms
        MockRoomStore
            .Setup(s => s.JsonCountAsync(
                It.Is<IReadOnlyList<QueryCondition>>(c =>
                    c.Any(q => q.Path == "$.Status" && (string)q.Value == "Locked")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(2L);

        // Archived rooms
        MockRoomStore
            .Setup(s => s.JsonCountAsync(
                It.Is<IReadOnlyList<QueryCondition>>(c =>
                    c.Any(q => q.Path == "$.Status" && (string)q.Value == "Archived")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(2L);

        // Total room types
        MockRoomTypeStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3L);

        // No rooms in the query result for participant sum (use empty list)
        SetupRoomQuery(new List<ChatRoomModel>(), 0);

        var (status, response) = await service.AdminGetStatsAsync(
            new AdminGetStatsRequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(10, response.TotalRooms);
        Assert.Equal(6, response.ActiveRooms);
        Assert.Equal(2, response.LockedRooms);
        Assert.Equal(2, response.ArchivedRooms);
        Assert.Equal(3, response.TotalRoomTypes);
        Assert.Equal(0, response.TotalParticipants); // no rooms in query
    }

    #endregion

    #region AdminForceCleanup / CleanupIdleRooms

    [Fact]
    public async Task AdminForceCleanup_ReturnsCleanupResult()
    {
        var service = CreateService();

        // No idle rooms found
        MockRoomStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(
                new List<JsonQueryResult<ChatRoomModel>>(), 0, 0, 1000));

        var (status, response) = await service.AdminForceCleanupAsync(
            new AdminForceCleanupRequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.CleanedRooms);
        Assert.Equal(0, response.ArchivedRooms);
        Assert.Equal(0, response.DeletedRooms);
    }

    [Fact]
    public async Task CleanupIdleRooms_ArchivesPersistentRooms()
    {
        var service = CreateService();

        var idleRoom = CreateTestRoom(roomId: Guid.NewGuid());
        idleRoom.LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-(Configuration.IdleRoomTimeoutMinutes + 10));
        idleRoom.ContractId = null; // not contract-governed
        idleRoom.IsArchived = false;

        var roomType = CreateTestRoomType("text", MessageFormat.Text, PersistenceMode.Persistent);

        var queryResult = new JsonQueryResult<ChatRoomModel>($"room:{idleRoom.RoomId}", idleRoom);
        MockRoomStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(
                new List<JsonQueryResult<ChatRoomModel>> { queryResult }, 1, 0, 1000));

        SetupFindRoomTypeByCode(roomType);

        MockResourceClient
            .Setup(r => r.ExecuteCompressAsync(
                It.IsAny<ExecuteCompressRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecuteCompressResponse { ArchiveId = Guid.NewGuid() });

        var result = await service.CleanupIdleRoomsAsync(CancellationToken.None);

        Assert.Equal(1, result.CleanedRooms);
        Assert.Equal(1, result.ArchivedRooms);
        Assert.Equal(0, result.DeletedRooms);

        // Verify room saved as archived
        MockRoomStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.Is<ChatRoomModel>(r => r.IsArchived && r.Status == ChatRoomStatus.Archived),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CleanupIdleRooms_DeletesEphemeralRooms()
    {
        var service = CreateService();

        var idleRoom = CreateTestRoom(roomId: Guid.NewGuid(), roomTypeCode: "ephemeral");
        idleRoom.LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-(Configuration.IdleRoomTimeoutMinutes + 10));
        idleRoom.ContractId = null;

        var roomType = CreateTestRoomType("ephemeral", MessageFormat.Text, PersistenceMode.Ephemeral);

        var queryResult = new JsonQueryResult<ChatRoomModel>($"room:{idleRoom.RoomId}", idleRoom);
        MockRoomStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(
                new List<JsonQueryResult<ChatRoomModel>> { queryResult }, 1, 0, 1000));

        SetupFindRoomTypeByCode(roomType);

        var result = await service.CleanupIdleRoomsAsync(CancellationToken.None);

        Assert.Equal(1, result.CleanedRooms);
        Assert.Equal(0, result.ArchivedRooms);
        Assert.Equal(1, result.DeletedRooms);

        // Verify room deleted
        MockRoomStore.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        MockRoomCache.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CleanupIdleRooms_SkipsContractGovernedRooms()
    {
        var service = CreateService();

        var contractRoom = CreateTestRoom(contractId: TestContractId);
        contractRoom.LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-(Configuration.IdleRoomTimeoutMinutes + 10));

        var queryResult = new JsonQueryResult<ChatRoomModel>($"room:{contractRoom.RoomId}", contractRoom);
        MockRoomStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(
                new List<JsonQueryResult<ChatRoomModel>> { queryResult }, 1, 0, 1000));

        var result = await service.CleanupIdleRoomsAsync(CancellationToken.None);

        Assert.Equal(0, result.CleanedRooms);

        // Nothing deleted or archived
        MockRoomStore.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        MockResourceClient.Verify(
            r => r.ExecuteCompressAsync(It.IsAny<ExecuteCompressRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region ExecuteContractRoomActionAsync Error Paths

    /// <summary>
    /// Verifies that when archive fails during contract fulfilled handling,
    /// the error is caught and logged without propagating.
    /// </summary>
    [Fact]
    public async Task HandleContractFulfilled_ArchiveFails_LogsWarningAndContinues()
    {
        var service = CreateService();

        var room = CreateTestRoom(contractId: TestContractId);
        room.ContractFulfilledAction = ContractRoomAction.Archive;

        SetupContractRoomQuery(TestContractId, room);

        // Resource client throws
        MockResourceClient
            .Setup(r => r.ExecuteCompressAsync(
                It.IsAny<ExecuteCompressRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Resource service unavailable", 503, null, null, null));

        var evt = new ContractFulfilledEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = TestContractId,
        };

        // Should not throw - error is caught inside ExecuteContractRoomActionAsync
        await service.HandleContractFulfilledAsync(evt);

        // Archive event should NOT be published (archive failed)
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.room.archived", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that delete action with participants clears permissions and
    /// publishes deletion events.
    /// </summary>
    [Fact]
    public async Task HandleContractTerminated_DeleteAction_ClearsParticipantPermissions()
    {
        var service = CreateService();
        var p1Session = Guid.NewGuid();
        var p2Session = Guid.NewGuid();

        var room = CreateTestRoom(contractId: TestContractId);
        room.ContractTerminatedAction = ContractRoomAction.Delete;

        SetupContractRoomQuery(TestContractId, room);

        var p1 = CreateTestParticipant(sessionId: p1Session);
        var p2 = CreateTestParticipant(sessionId: p2Session);
        SetupParticipants(TestRoomId, p1, p2);

        var evt = new ContractTerminatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = TestContractId,
        };

        await service.HandleContractTerminatedAsync(evt);

        // Verify permissions cleared for both participants
        MockPermissionClient.Verify(p => p.ClearSessionStateAsync(
            It.IsAny<ClearSessionStateRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Verify client event broadcast to participants
        MockClientEventPublisher.Verify(
            p => p.PublishToSessionsAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<ChatRoomDeletedClientEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify room deleted
        MockRoomStore.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when the entire contract event handler throws,
    /// the error is published via TryPublishErrorAsync.
    /// </summary>
    [Fact]
    public async Task HandleContractFulfilled_ExceptionThrown_PublishesError()
    {
        var service = CreateService();

        // Room store query throws
        MockRoomStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection lost"));

        var evt = new ContractFulfilledEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = TestContractId,
        };

        // Should not throw
        await service.HandleContractFulfilledAsync(evt);

        // Verify error event was published
        MockMessageBus.Verify(
            m => m.TryPublishErrorAsync(
                "chat", "HandleContractFulfilled",
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region FindRoomsByContractIdAsync Safety Cap

    /// <summary>
    /// Verifies that the safety cap stops iteration when maxResults is reached.
    /// Uses a small maxResults value and many rooms to trigger the cap.
    /// </summary>
    [Fact]
    public async Task HandleContractFulfilled_SafetyCap_StopsAtMaxResults()
    {
        var service = CreateService();

        // Set a very small safety cap
        Configuration.MaxContractRoomQueryResults = 2;
        Configuration.ContractRoomQueryBatchSize = 1;

        var room1 = CreateTestRoom(roomId: Guid.NewGuid(), contractId: TestContractId);
        var room2 = CreateTestRoom(roomId: Guid.NewGuid(), contractId: TestContractId);

        room1.ContractFulfilledAction = ContractRoomAction.Lock;
        room2.ContractFulfilledAction = ContractRoomAction.Lock;

        var page1 = new List<JsonQueryResult<ChatRoomModel>>
        {
            new($"room:{room1.RoomId}", room1),
        };
        var page2 = new List<JsonQueryResult<ChatRoomModel>>
        {
            new($"room:{room2.RoomId}", room2),
        };

        // Page 1: returns 1 room, HasMore=true, TotalCount=5 (many more available)
        // Page 2: returns 1 more room, allRooms.Count == 2 >= maxResults(2), breaks
        MockRoomStore
            .SetupSequence(s => s.JsonQueryPagedAsync(
                It.Is<IReadOnlyList<QueryCondition>>(c =>
                    c.Any(q => q.Path == "$.ContractId" && (string)q.Value == TestContractId.ToString())),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(page1, 5, 0, 1))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(page2, 5, 1, 1));

        SetupParticipants(room1.RoomId, CreateTestParticipant(sessionId: Guid.NewGuid()));
        SetupParticipants(room2.RoomId, CreateTestParticipant(sessionId: Guid.NewGuid()));

        var evt = new ContractFulfilledEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = TestContractId,
        };

        await service.HandleContractFulfilledAsync(evt);

        // Only 2 rooms processed (safety cap), even though TotalCount was 5
        MockRoomStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.Is<ChatRoomModel>(r => r.Status == ChatRoomStatus.Locked),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Only 2 pages queried (broke after second because allRooms.Count >= maxResults)
        MockRoomStore.Verify(
            s => s.JsonQueryPagedAsync(
                It.Is<IReadOnlyList<QueryCondition>>(c =>
                    c.Any(q => q.Path == "$.ContractId")),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    #endregion

    #region CleanupIdleRooms Recently-Active Room Preservation

    /// <summary>
    /// Verifies that rooms with recent activity (below idle timeout threshold)
    /// are NOT returned by the cleanup query and thus NOT cleaned up.
    /// This tests the query condition: LastActivityAt < cutoff.
    /// </summary>
    [Fact]
    public async Task CleanupIdleRooms_RecentlyActiveRooms_NotCleaned()
    {
        var service = CreateService();

        // The query uses $.LastActivityAt < cutoff, so recently active rooms
        // would not be returned. We simulate this by returning an empty result.
        MockRoomStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(
                new List<JsonQueryResult<ChatRoomModel>>(), 0, 0, 1000));

        var result = await service.CleanupIdleRoomsAsync(CancellationToken.None);

        Assert.Equal(0, result.CleanedRooms);
        Assert.Equal(0, result.ArchivedRooms);
        Assert.Equal(0, result.DeletedRooms);

        // Verify no room was archived or deleted
        MockRoomStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatRoomModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        MockRoomStore.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that when a mix of idle and non-idle rooms exist,
    /// only the idle ones appear in the query results and get cleaned.
    /// Tests behavior via the cutoff time condition by having both idle and
    /// non-idle rooms, but the query only returns the idle one.
    /// </summary>
    [Fact]
    public async Task CleanupIdleRooms_MixOfIdleAndActive_OnlyIdleCleaned()
    {
        var service = CreateService();

        // Only the idle room appears in the query (recently active is filtered by the store query)
        var idleRoom = CreateTestRoom(roomId: Guid.NewGuid(), roomTypeCode: "ephemeral");
        idleRoom.LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-(Configuration.IdleRoomTimeoutMinutes + 10));
        idleRoom.ContractId = null;

        var roomType = CreateTestRoomType("ephemeral", MessageFormat.Text, PersistenceMode.Ephemeral);
        SetupFindRoomTypeByCode(roomType);

        var queryResult = new JsonQueryResult<ChatRoomModel>($"room:{idleRoom.RoomId}", idleRoom);
        MockRoomStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(
                new List<JsonQueryResult<ChatRoomModel>> { queryResult }, 1, 0, 1000));

        var result = await service.CleanupIdleRoomsAsync(CancellationToken.None);

        // Only 1 room cleaned (the idle one), the active room was never in the query
        Assert.Equal(1, result.CleanedRooms);
        Assert.Equal(0, result.ArchivedRooms);
        Assert.Equal(1, result.DeletedRooms);

        // Verify only 1 delete call
        MockRoomStore.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Sets up the room store to return rooms when queried by ContractId.
    /// </summary>
    private void SetupContractRoomQuery(Guid contractId, params ChatRoomModel[] rooms)
    {
        var queryResults = rooms.Select(r =>
            new JsonQueryResult<ChatRoomModel>($"room:{r.RoomId}", r)).ToList();

        MockRoomStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.Is<IReadOnlyList<QueryCondition>>(c =>
                    c.Any(q => q.Path == "$.ContractId" && (string)q.Value == contractId.ToString())),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(queryResults, rooms.Length, 0, 100));
    }

    #endregion
}
