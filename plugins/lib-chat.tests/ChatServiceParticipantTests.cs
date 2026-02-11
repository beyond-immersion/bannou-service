using BeyondImmersion.BannouService.Chat;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.State;
using Moq;

namespace BeyondImmersion.BannouService.Chat.Tests;

/// <summary>
/// Tests for ChatService participant operations:
/// JoinRoom, LeaveRoom, ListParticipants, KickParticipant, BanParticipant, UnbanParticipant, MuteParticipant.
/// </summary>
public class ChatServiceParticipantTests : ChatServiceTestBase
{
    #region JoinRoom

    [Fact]
    public async Task JoinRoom_ValidRequest_AddsParticipantAndReturnsOK()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        SetupRoom(room);

        var roomType = CreateTestRoomType("text");
        SetupFindRoomTypeByCode(roomType);

        var (status, response) = await service.JoinRoomAsync(new JoinRoomRequest
        {
            RoomId = TestRoomId,
            SenderType = SenderType.Player,
            SenderId = Guid.NewGuid(),
            DisplayName = "Player1",
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify participant saved to hash
        MockParticipantStore.Verify(s => s.HashSetAsync(
            TestRoomId.ToString(), TestSessionId.ToString(),
            It.IsAny<ChatParticipantModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify permission state set
        MockPermissionClient.Verify(p => p.UpdateSessionStateAsync(
            It.Is<SessionStateUpdate>(u => u.SessionId == TestSessionId && u.ServiceId == "chat" && u.NewState == "in_room"),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify service event
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.participant.joined", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);

        ClearCallerSession();
    }

    [Fact]
    public async Task JoinRoom_NoSession_ReturnsUnauthorized()
    {
        var service = CreateService();
        ClearCallerSession();

        var (status, _) = await service.JoinRoomAsync(new JoinRoomRequest
        {
            RoomId = TestRoomId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.Unauthorized, status);
    }

    [Fact]
    public async Task JoinRoom_AlreadyJoined_ReturnsOKWithExistingState()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        SetupRoom(room);

        var roomType = CreateTestRoomType("text");
        SetupFindRoomTypeByCode(roomType);

        var existingParticipant = CreateTestParticipant(sessionId: TestSessionId);
        SetupParticipants(TestRoomId, existingParticipant);

        var (status, response) = await service.JoinRoomAsync(new JoinRoomRequest
        {
            RoomId = TestRoomId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Should NOT save a new participant
        MockParticipantStore.Verify(s => s.HashSetAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<ChatParticipantModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);

        ClearCallerSession();
    }

    [Fact]
    public async Task JoinRoom_LockedRoom_ReturnsForbidden()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom(status: ChatRoomStatus.Locked);
        SetupRoom(room);

        var (status, _) = await service.JoinRoomAsync(new JoinRoomRequest
        {
            RoomId = TestRoomId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.Forbidden, status);
        ClearCallerSession();
    }

    [Fact]
    public async Task JoinRoom_BannedUser_ReturnsForbidden()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        SetupRoom(room);

        var roomType = CreateTestRoomType("text");
        SetupFindRoomTypeByCode(roomType);

        // Active ban (no expiry)
        var ban = new ChatBanModel
        {
            BanId = Guid.NewGuid(),
            RoomId = TestRoomId,
            TargetSessionId = TestSessionId,
            BannedBySessionId = Guid.NewGuid(),
            BannedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
        SetupBan(TestRoomId, TestSessionId, ban);

        var (status, _) = await service.JoinRoomAsync(new JoinRoomRequest
        {
            RoomId = TestRoomId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.Forbidden, status);
        ClearCallerSession();
    }

    [Fact]
    public async Task JoinRoom_RoomFull_ReturnsConflict()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom(maxParticipants: 2);
        SetupRoom(room);

        var roomType = CreateTestRoomType("text");
        SetupFindRoomTypeByCode(roomType);

        // No ban
        MockBanStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatBanModel?)null);

        // Room is full (2 participants, max 2)
        var p1 = CreateTestParticipant(sessionId: Guid.NewGuid());
        var p2 = CreateTestParticipant(sessionId: Guid.NewGuid());
        SetupParticipants(TestRoomId, p1, p2);

        var (status, _) = await service.JoinRoomAsync(new JoinRoomRequest
        {
            RoomId = TestRoomId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
        ClearCallerSession();
    }

    #endregion

    #region LeaveRoom

    [Fact]
    public async Task LeaveRoom_ValidParticipant_RemovesAndReturnsOK()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        SetupRoom(room);

        var participant = CreateTestParticipant(sessionId: TestSessionId);
        SetupParticipants(TestRoomId, participant);

        var (status, response) = await service.LeaveRoomAsync(new LeaveRoomRequest
        {
            RoomId = TestRoomId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify participant removed from hash
        MockParticipantStore.Verify(s => s.HashDeleteAsync(
            TestRoomId.ToString(), TestSessionId.ToString(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify permission cleared
        MockPermissionClient.Verify(p => p.ClearSessionStateAsync(
            It.IsAny<ClearSessionStateRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify service event
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.participant.left", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);

        ClearCallerSession();
    }

    [Fact]
    public async Task LeaveRoom_NotAParticipant_ReturnsNotFound()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        SetupRoom(room);

        // No participants in room - HashGetAsync returns null
        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), TestSessionId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatParticipantModel?)null);

        var (status, _) = await service.LeaveRoomAsync(new LeaveRoomRequest
        {
            RoomId = TestRoomId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        ClearCallerSession();
    }

    #endregion

    #region KickParticipant

    [Fact]
    public async Task KickParticipant_OwnerKicksMember_Succeeds()
    {
        var service = CreateService();
        var ownerSession = Guid.NewGuid();
        var targetSession = Guid.NewGuid();
        SetCallerSession(ownerSession);

        var room = CreateTestRoom();
        SetupRoom(room);

        var owner = CreateTestParticipant(sessionId: ownerSession, role: ChatParticipantRole.Owner);
        var target = CreateTestParticipant(sessionId: targetSession, role: ChatParticipantRole.Member);
        SetupParticipants(TestRoomId, owner, target);

        var (status, response) = await service.KickParticipantAsync(new KickParticipantRequest
        {
            RoomId = TestRoomId,
            TargetSessionId = targetSession,
            Reason = "Testing",
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);

        // Verify target removed
        MockParticipantStore.Verify(s => s.HashDeleteAsync(
            TestRoomId.ToString(), targetSession.ToString(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify permission cleared for target
        MockPermissionClient.Verify(p => p.ClearSessionStateAsync(
            It.IsAny<ClearSessionStateRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        ClearCallerSession();
    }

    [Fact]
    public async Task KickParticipant_MemberTriesToKick_ReturnsForbidden()
    {
        var service = CreateService();
        var memberSession = Guid.NewGuid();
        SetCallerSession(memberSession);

        var room = CreateTestRoom();
        SetupRoom(room);

        var caller = CreateTestParticipant(sessionId: memberSession, role: ChatParticipantRole.Member);
        var target = CreateTestParticipant(sessionId: Guid.NewGuid(), role: ChatParticipantRole.Member);
        SetupParticipants(TestRoomId, caller, target);

        var (status, _) = await service.KickParticipantAsync(new KickParticipantRequest
        {
            RoomId = TestRoomId,
            TargetSessionId = target.SessionId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.Forbidden, status);
        ClearCallerSession();
    }

    #endregion

    #region BanParticipant

    [Fact]
    public async Task BanParticipant_ValidRequest_CreatesBanAndKicks()
    {
        var service = CreateService();
        var modSession = Guid.NewGuid();
        var targetSession = Guid.NewGuid();
        SetCallerSession(modSession);

        var room = CreateTestRoom();
        SetupRoom(room);

        var moderator = CreateTestParticipant(sessionId: modSession, role: ChatParticipantRole.Moderator);
        SetupParticipants(TestRoomId, moderator);

        // Target is currently in room
        var targetParticipant = CreateTestParticipant(sessionId: targetSession);
        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), targetSession.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetParticipant);

        var (status, _) = await service.BanParticipantAsync(new BanParticipantRequest
        {
            RoomId = TestRoomId,
            TargetSessionId = targetSession,
            Reason = "Bad behavior",
            DurationMinutes = 60,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);

        // Verify ban saved
        MockBanStore.Verify(s => s.SaveAsync(
            $"ban:{TestRoomId}:{targetSession}",
            It.IsAny<ChatBanModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify target kicked (removed from hash)
        MockParticipantStore.Verify(s => s.HashDeleteAsync(
            TestRoomId.ToString(), targetSession.ToString(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify ban event published
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.participant.banned", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);

        ClearCallerSession();
    }

    #endregion

    #region UnbanParticipant

    [Fact]
    public async Task UnbanParticipant_BanExists_DeletesBanAndReturnsOK()
    {
        var service = CreateService();
        var targetSession = Guid.NewGuid();

        var ban = new ChatBanModel
        {
            BanId = Guid.NewGuid(),
            RoomId = TestRoomId,
            TargetSessionId = targetSession,
            BannedBySessionId = Guid.NewGuid(),
            BannedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
        SetupBan(TestRoomId, targetSession, ban);

        var room = CreateTestRoom();
        SetupRoomCache(room);

        var (status, response) = await service.UnbanParticipantAsync(new UnbanParticipantRequest
        {
            RoomId = TestRoomId,
            TargetSessionId = targetSession,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);

        // Verify ban deleted
        MockBanStore.Verify(s => s.DeleteAsync(
            $"ban:{TestRoomId}:{targetSession}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnbanParticipant_NoBan_ReturnsNotFound()
    {
        var service = CreateService();
        MockBanStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatBanModel?)null);

        var (status, _) = await service.UnbanParticipantAsync(new UnbanParticipantRequest
        {
            RoomId = TestRoomId,
            TargetSessionId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region MuteParticipant

    [Fact]
    public async Task MuteParticipant_ValidRequest_MutesAndReturnsOK()
    {
        var service = CreateService();
        var modSession = Guid.NewGuid();
        var targetSession = Guid.NewGuid();
        SetCallerSession(modSession);

        var room = CreateTestRoom();
        SetupRoom(room);

        var moderator = CreateTestParticipant(sessionId: modSession, role: ChatParticipantRole.Moderator);
        SetupParticipants(TestRoomId, moderator);

        var target = CreateTestParticipant(sessionId: targetSession);
        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), targetSession.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        var (status, _) = await service.MuteParticipantAsync(new MuteParticipantRequest
        {
            RoomId = TestRoomId,
            TargetSessionId = targetSession,
            DurationMinutes = 30,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);

        // Verify participant saved with muted state
        MockParticipantStore.Verify(s => s.HashSetAsync(
            TestRoomId.ToString(), targetSession.ToString(),
            It.Is<ChatParticipantModel>(p => p.IsMuted),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify mute event
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.participant.muted", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);

        ClearCallerSession();
    }

    [Fact]
    public async Task MuteParticipant_TargetNotInRoom_ReturnsNotFound()
    {
        var service = CreateService();
        var modSession = Guid.NewGuid();
        SetCallerSession(modSession);

        var room = CreateTestRoom();
        SetupRoom(room);

        var moderator = CreateTestParticipant(sessionId: modSession, role: ChatParticipantRole.Owner);
        SetupParticipants(TestRoomId, moderator);

        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), It.Is<string>(k => k != modSession.ToString()), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatParticipantModel?)null);

        var (status, _) = await service.MuteParticipantAsync(new MuteParticipantRequest
        {
            RoomId = TestRoomId,
            TargetSessionId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
        ClearCallerSession();
    }

    #endregion

    #region ListParticipants

    [Fact]
    public async Task ListParticipants_ReturnsAllParticipants()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        SetupRoomCache(room);

        var p1 = CreateTestParticipant(sessionId: TestSessionId);
        var p2 = CreateTestParticipant(sessionId: Guid.NewGuid());
        SetupParticipants(TestRoomId, p1, p2);

        var (status, response) = await service.ListParticipantsAsync(new ListParticipantsRequest
        {
            RoomId = TestRoomId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Participants.Count);

        ClearCallerSession();
    }

    #endregion
}
