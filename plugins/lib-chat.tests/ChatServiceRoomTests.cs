using BeyondImmersion.Bannou.Chat.ClientEvents;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Chat;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Moq;

namespace BeyondImmersion.BannouService.Chat.Tests;

/// <summary>
/// Tests for ChatService room CRUD operations:
/// CreateRoom, GetRoom, ListRooms, UpdateRoom, DeleteRoom, ArchiveRoom.
/// </summary>
public class ChatServiceRoomTests : ChatServiceTestBase
{
    #region CreateRoom

    [Fact]
    public async Task CreateRoom_ValidRequest_SavesRoomAndReturnsOK()
    {
        var service = CreateService();
        var roomType = CreateTestRoomType("text");
        SetupFindRoomTypeByCode(roomType);

        ChatRoomModel? savedRoom = null;
        MockRoomStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatRoomModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ChatRoomModel, StateOptions?, CancellationToken>(
                (_, m, _, _) => savedRoom = m)
            .ReturnsAsync("etag");

        var (status, response) = await service.CreateRoomAsync(new CreateRoomRequest
        {
            RoomTypeCode = "text",
            DisplayName = "My Room",
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("text", response.RoomTypeCode);
        Assert.Equal("My Room", response.DisplayName);
        Assert.Equal(ChatRoomStatus.Active, response.Status);
        Assert.Equal(0, response.ParticipantCount);
        Assert.False(response.IsArchived);

        // Verify saved to both MySQL and cache
        MockRoomStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<ChatRoomModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        MockRoomCache.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<ChatRoomModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify event published
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.room.created", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateRoom_RoomTypeNotFound_ReturnsNotFound()
    {
        var service = CreateService();

        // FindRoomTypeByCodeAsync returns null
        MockRoomTypeStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomTypeModel>(
                new List<JsonQueryResult<ChatRoomTypeModel>>(), 0, 0, 1));

        var (status, _) = await service.CreateRoomAsync(new CreateRoomRequest
        {
            RoomTypeCode = "nonexistent",
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task CreateRoom_DeprecatedRoomType_ReturnsBadRequest()
    {
        var service = CreateService();
        var roomType = CreateTestRoomType("old", status: RoomTypeStatus.Deprecated);
        SetupFindRoomTypeByCode(roomType);

        var (status, _) = await service.CreateRoomAsync(new CreateRoomRequest
        {
            RoomTypeCode = "old",
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task CreateRoom_WithContractId_ValidatesContract()
    {
        var service = CreateService();
        var roomType = CreateTestRoomType("text");
        SetupFindRoomTypeByCode(roomType);

        MockContractClient
            .Setup(c => c.GetContractInstanceAsync(
                It.IsAny<GetContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = TestContractId });

        var (status, response) = await service.CreateRoomAsync(new CreateRoomRequest
        {
            RoomTypeCode = "text",
            ContractId = TestContractId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TestContractId, response.ContractId);
    }

    [Fact]
    public async Task CreateRoom_ContractNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        var roomType = CreateTestRoomType("text");
        SetupFindRoomTypeByCode(roomType);

        MockContractClient
            .Setup(c => c.GetContractInstanceAsync(
                It.IsAny<GetContractInstanceRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404));

        var (status, _) = await service.CreateRoomAsync(new CreateRoomRequest
        {
            RoomTypeCode = "text",
            ContractId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region GetRoom

    [Fact]
    public async Task GetRoom_CacheHit_ReturnsCachedRoom()
    {
        var service = CreateService();
        var room = CreateTestRoom();
        SetupRoomCache(room);

        MockParticipantStore
            .Setup(s => s.HashCountAsync(TestRoomId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3L);

        var (status, response) = await service.GetRoomAsync(
            new GetRoomRequest { RoomId = TestRoomId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(TestRoomId, response.RoomId);
        Assert.Equal(3, response.ParticipantCount);

        // Should NOT hit MySQL store
        MockRoomStore.Verify(
            s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRoom_CacheMiss_FallsBackToStore()
    {
        var service = CreateService();
        var room = CreateTestRoom();

        // Cache miss
        MockRoomCache
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomModel?)null);

        // MySQL hit
        SetupRoom(room);

        var (status, response) = await service.GetRoomAsync(
            new GetRoomRequest { RoomId = TestRoomId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Should write back to cache
        MockRoomCache.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatRoomModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetRoom_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        MockRoomCache
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomModel?)null);
        MockRoomStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomModel?)null);

        var (status, _) = await service.GetRoomAsync(
            new GetRoomRequest { RoomId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region ListRooms

    [Fact]
    public async Task ListRooms_ReturnsPagedResults()
    {
        var service = CreateService();
        var rooms = new List<ChatRoomModel>
        {
            CreateTestRoom(roomId: Guid.NewGuid()),
            CreateTestRoom(roomId: Guid.NewGuid()),
        };
        SetupRoomQuery(rooms, 2);

        var (status, response) = await service.ListRoomsAsync(
            new ListRoomsRequest { Page = 0, PageSize = 20 }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task ListRooms_WithFilters_PassesConditions()
    {
        var service = CreateService();
        SetupRoomQuery(new List<ChatRoomModel>(), 0);

        await service.ListRoomsAsync(new ListRoomsRequest
        {
            RoomTypeCode = "text",
            SessionId = TestSessionId,
            Status = ChatRoomStatus.Active,
            Page = 0,
            PageSize = 20,
        }, CancellationToken.None);

        MockRoomStore.Verify(s => s.JsonQueryPagedAsync(
            It.Is<IReadOnlyList<QueryCondition>>(c =>
                c.Any(q => q.Path == "$.RoomTypeCode") &&
                c.Any(q => q.Path == "$.SessionId") &&
                c.Any(q => q.Path == "$.Status")),
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region UpdateRoom

    [Fact]
    public async Task UpdateRoom_ValidRequest_UpdatesAndReturnsOK()
    {
        var service = CreateService();
        var room = CreateTestRoom();
        SetupRoom(room);

        var (status, response) = await service.UpdateRoomAsync(new UpdateRoomRequest
        {
            RoomId = TestRoomId,
            DisplayName = "Updated Name",
            MaxParticipants = 50,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify saved to both stores
        MockRoomStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<ChatRoomModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        MockRoomCache.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<ChatRoomModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify event
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.room.updated", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateRoom_LockFails_ReturnsConflict()
    {
        var service = CreateService();
        SetupLockFailure();

        var (status, _) = await service.UpdateRoomAsync(
            new UpdateRoomRequest { RoomId = TestRoomId }, CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task UpdateRoom_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        MockRoomStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomModel?)null);

        var (status, _) = await service.UpdateRoomAsync(
            new UpdateRoomRequest { RoomId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region DeleteRoom

    [Fact]
    public async Task DeleteRoom_ValidRoom_DeletesAndCleanup()
    {
        var service = CreateService();
        var room = CreateTestRoom();
        SetupRoom(room);

        var (status, response) = await service.DeleteRoomAsync(
            new DeleteRoomRequest { RoomId = TestRoomId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify both stores deleted
        MockRoomStore.Verify(s => s.DeleteAsync(
            $"room:{TestRoomId}", It.IsAny<CancellationToken>()), Times.Once);
        MockRoomCache.Verify(s => s.DeleteAsync(
            $"room:{TestRoomId}", It.IsAny<CancellationToken>()), Times.Once);

        // Verify participants deleted
        MockParticipantStore.Verify(s => s.DeleteAsync(
            TestRoomId.ToString(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify event
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.room.deleted", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteRoom_WithParticipants_NotifiesAndClearsPermissions()
    {
        var service = CreateService();
        var room = CreateTestRoom();
        SetupRoom(room);

        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var participantA = CreateTestParticipant(sessionId: sessionA);
        var participantB = CreateTestParticipant(sessionId: sessionB);
        SetupParticipants(TestRoomId, participantA, participantB);

        var (status, _) = await service.DeleteRoomAsync(
            new DeleteRoomRequest { RoomId = TestRoomId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);

        // Verify client event published to participants
        MockClientEventPublisher.Verify(p => p.PublishToSessionsAsync(
            It.IsAny<IEnumerable<string>>(),
            It.IsAny<ChatRoomDeletedClientEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify permission state cleared for both participants
        MockPermissionClient.Verify(p => p.ClearSessionStateAsync(
            It.IsAny<ClearSessionStateRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task DeleteRoom_NotFound_ReturnsNotFound()
    {
        var service = CreateService();
        MockRoomStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomModel?)null);

        var (status, _) = await service.DeleteRoomAsync(
            new DeleteRoomRequest { RoomId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region ArchiveRoom

    [Fact]
    public async Task ArchiveRoom_ValidRoom_ArchivesViaResourceService()
    {
        var service = CreateService();
        var room = CreateTestRoom();
        SetupRoom(room);

        var archiveId = Guid.NewGuid();
        MockResourceClient
            .Setup(c => c.ExecuteCompressAsync(
                It.IsAny<ExecuteCompressRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecuteCompressResponse { ArchiveId = archiveId });

        var (status, response) = await service.ArchiveRoomAsync(
            new ArchiveRoomRequest { RoomId = TestRoomId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsArchived);
        Assert.Equal(ChatRoomStatus.Archived, response.Status);

        // Verify saved with archived state
        MockRoomStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(),
            It.Is<ChatRoomModel>(m => m.IsArchived && m.Status == ChatRoomStatus.Archived),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify archive event
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.room.archived", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ArchiveRoom_AlreadyArchived_ReturnsOKWithoutAction()
    {
        var service = CreateService();
        var room = CreateTestRoom(isArchived: true);
        SetupRoom(room);

        var (status, response) = await service.ArchiveRoomAsync(
            new ArchiveRoomRequest { RoomId = TestRoomId }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Should NOT call resource service
        MockResourceClient.Verify(c => c.ExecuteCompressAsync(
            It.IsAny<ExecuteCompressRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArchiveRoom_ResourceServiceError_ReturnsInternalServerError()
    {
        var service = CreateService();
        var room = CreateTestRoom();
        SetupRoom(room);

        MockResourceClient
            .Setup(c => c.ExecuteCompressAsync(
                It.IsAny<ExecuteCompressRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Resource error", 500));

        var (status, _) = await service.ArchiveRoomAsync(
            new ArchiveRoomRequest { RoomId = TestRoomId }, CancellationToken.None);

        Assert.Equal(StatusCodes.InternalServerError, status);
    }

    #endregion
}
