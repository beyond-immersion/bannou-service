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
            SenderType = "player",
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
            m => m.TryPublishAsync("chat-participant.joined", It.IsAny<object>(), It.IsAny<CancellationToken>()),
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
            m => m.TryPublishAsync("chat-participant.left", It.IsAny<object>(), It.IsAny<CancellationToken>()),
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
            m => m.TryPublishAsync("chat-participant.banned", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);

        ClearCallerSession();
    }

    #endregion

    #region UnbanParticipant

    [Fact]
    public async Task UnbanParticipant_BanExists_DeletesBanAndReturnsOK()
    {
        var service = CreateService();
        var modSession = Guid.NewGuid();
        var targetSession = Guid.NewGuid();
        SetCallerSession(modSession);

        var room = CreateTestRoom();
        SetupRoom(room);

        var moderator = CreateTestParticipant(sessionId: modSession, role: ChatParticipantRole.Moderator);
        SetupParticipants(TestRoomId, moderator);

        var ban = new ChatBanModel
        {
            BanId = Guid.NewGuid(),
            RoomId = TestRoomId,
            TargetSessionId = targetSession,
            BannedBySessionId = Guid.NewGuid(),
            BannedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
        SetupBan(TestRoomId, targetSession, ban);

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
        var modSession = Guid.NewGuid();
        SetCallerSession(modSession);

        var room = CreateTestRoom();
        SetupRoom(room);

        var moderator = CreateTestParticipant(sessionId: modSession, role: ChatParticipantRole.Moderator);
        SetupParticipants(TestRoomId, moderator);

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
            m => m.TryPublishAsync("chat-participant.muted", It.IsAny<object>(), It.IsAny<CancellationToken>()),
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

    #region LeaveRoom Owner Promotion

    /// <summary>
    /// Verifies that when the owner leaves, the oldest remaining member is promoted to owner.
    /// </summary>
    [Fact]
    public async Task LeaveRoom_OwnerLeaves_PromotesOldestMemberToOwner()
    {
        var service = CreateService();
        var ownerSessionId = Guid.NewGuid();
        SetCallerSession(ownerSessionId);

        var room = CreateTestRoom();
        SetupRoom(room);

        var member1SessionId = Guid.NewGuid();
        var member2SessionId = Guid.NewGuid();

        var owner = CreateTestParticipant(sessionId: ownerSessionId, role: ChatParticipantRole.Owner);
        owner.JoinedAt = DateTimeOffset.UtcNow.AddMinutes(-60);

        var member1 = CreateTestParticipant(sessionId: member1SessionId, role: ChatParticipantRole.Member);
        member1.JoinedAt = DateTimeOffset.UtcNow.AddMinutes(-30); // oldest remaining

        var member2 = CreateTestParticipant(sessionId: member2SessionId, role: ChatParticipantRole.Member);
        member2.JoinedAt = DateTimeOffset.UtcNow.AddMinutes(-10); // newest

        var allParticipants = new Dictionary<string, ChatParticipantModel>
        {
            { ownerSessionId.ToString(), owner },
            { member1SessionId.ToString(), member1 },
            { member2SessionId.ToString(), member2 }
        };

        var remainingParticipants = new Dictionary<string, ChatParticipantModel>
        {
            { member1SessionId.ToString(), member1 },
            { member2SessionId.ToString(), member2 }
        };

        // First call: initial lookup (all 3). Subsequent calls: after removal (2 remaining).
        MockParticipantStore
            .SetupSequence(s => s.HashGetAllAsync<ChatParticipantModel>(
                TestRoomId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allParticipants)
            .ReturnsAsync(remainingParticipants)
            .ReturnsAsync(remainingParticipants);

        MockParticipantStore
            .Setup(s => s.HashCountAsync(TestRoomId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2L);

        // Act
        var (status, _) = await service.LeaveRoomAsync(new LeaveRoomRequest
        {
            RoomId = TestRoomId,
        }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify member1 (oldest) was promoted by saving with Owner role
        MockParticipantStore.Verify(s => s.HashSetAsync(
            TestRoomId.ToString(),
            member1SessionId.ToString(),
            It.Is<ChatParticipantModel>(p => p.Role == ChatParticipantRole.Owner),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        ClearCallerSession();
    }

    /// <summary>
    /// Verifies that when the owner leaves, a moderator is promoted before regular members.
    /// </summary>
    [Fact]
    public async Task LeaveRoom_OwnerLeaves_PromotesModeratorOverOlderMember()
    {
        var service = CreateService();
        var ownerSessionId = Guid.NewGuid();
        SetCallerSession(ownerSessionId);

        var room = CreateTestRoom();
        SetupRoom(room);

        var memberSessionId = Guid.NewGuid();
        var modSessionId = Guid.NewGuid();

        var owner = CreateTestParticipant(sessionId: ownerSessionId, role: ChatParticipantRole.Owner);

        var oldMember = CreateTestParticipant(sessionId: memberSessionId, role: ChatParticipantRole.Member);
        oldMember.JoinedAt = DateTimeOffset.UtcNow.AddHours(-2); // older than moderator

        var moderator = CreateTestParticipant(sessionId: modSessionId, role: ChatParticipantRole.Moderator);
        moderator.JoinedAt = DateTimeOffset.UtcNow.AddMinutes(-10); // newer but higher role

        var allParticipants = new Dictionary<string, ChatParticipantModel>
        {
            { ownerSessionId.ToString(), owner },
            { memberSessionId.ToString(), oldMember },
            { modSessionId.ToString(), moderator }
        };

        var remaining = new Dictionary<string, ChatParticipantModel>
        {
            { memberSessionId.ToString(), oldMember },
            { modSessionId.ToString(), moderator }
        };

        MockParticipantStore
            .SetupSequence(s => s.HashGetAllAsync<ChatParticipantModel>(
                TestRoomId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allParticipants)
            .ReturnsAsync(remaining)
            .ReturnsAsync(remaining);

        MockParticipantStore
            .Setup(s => s.HashCountAsync(TestRoomId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2L);

        // Act
        var (status, _) = await service.LeaveRoomAsync(new LeaveRoomRequest
        {
            RoomId = TestRoomId,
        }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Moderator promoted over older member
        MockParticipantStore.Verify(s => s.HashSetAsync(
            TestRoomId.ToString(),
            modSessionId.ToString(),
            It.Is<ChatParticipantModel>(p => p.Role == ChatParticipantRole.Owner),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        ClearCallerSession();
    }

    /// <summary>
    /// Verifies that when the owner leaves and no other participants remain,
    /// no promotion occurs (no HashSet call for promotion).
    /// </summary>
    [Fact]
    public async Task LeaveRoom_OwnerLeaves_NoRemainingParticipants_NoPromotion()
    {
        var service = CreateService();
        var ownerSessionId = Guid.NewGuid();
        SetCallerSession(ownerSessionId);

        var room = CreateTestRoom();
        SetupRoom(room);

        var owner = CreateTestParticipant(sessionId: ownerSessionId, role: ChatParticipantRole.Owner);

        var allParticipants = new Dictionary<string, ChatParticipantModel>
        {
            { ownerSessionId.ToString(), owner }
        };

        var empty = new Dictionary<string, ChatParticipantModel>();

        MockParticipantStore
            .SetupSequence(s => s.HashGetAllAsync<ChatParticipantModel>(
                TestRoomId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allParticipants)
            .ReturnsAsync(empty)
            .ReturnsAsync(empty);

        MockParticipantStore
            .Setup(s => s.HashCountAsync(TestRoomId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        // Act
        var (status, _) = await service.LeaveRoomAsync(new LeaveRoomRequest
        {
            RoomId = TestRoomId,
        }, CancellationToken.None);

        // Assert - no promotion save (only the delete for removal)
        Assert.Equal(StatusCodes.OK, status);
        MockParticipantStore.Verify(s => s.HashSetAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<ChatParticipantModel>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);

        ClearCallerSession();
    }

    #endregion

    #region GetEffectiveMaxParticipants Tests (via JoinRoom)

    /// <summary>
    /// Verifies that room-level MaxParticipants override takes precedence over room type default.
    /// </summary>
    [Fact]
    public async Task JoinRoom_RoomMaxOverride_UsesRoomMaxOverRoomTypeDefault()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var roomType = CreateTestRoomType(defaultMaxParticipants: 100);
        SetupRoomType(roomType);
        SetupFindRoomTypeByCode(roomType);

        // Room has explicit max of 5, overriding room type's 100
        var room = CreateTestRoom(maxParticipants: 5);
        SetupRoom(room);
        SetupRoomCache(room);

        // 5 participants already in room → room is full
        var existing = Enumerable.Range(0, 5).Select(i =>
        {
            var p = CreateTestParticipant(sessionId: Guid.NewGuid());
            return p;
        }).ToArray();
        SetupParticipants(TestRoomId, existing);

        MockParticipantStore
            .Setup(s => s.HashCountAsync(TestRoomId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5L);

        // Act - try to join full room
        var (status, _) = await service.JoinRoomAsync(new JoinRoomRequest
        {
            RoomId = TestRoomId,
            SenderType = "player",
            SenderId = Guid.NewGuid(),
            DisplayName = "NewPlayer",
        }, CancellationToken.None);

        // Assert - should be rejected because room is full at max 5 (room override)
        Assert.Equal(StatusCodes.Conflict, status);

        ClearCallerSession();
    }

    /// <summary>
    /// Verifies that when room has no override, room type default is used.
    /// </summary>
    [Fact]
    public async Task JoinRoom_NoRoomOverride_UsesRoomTypeDefault()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var roomType = CreateTestRoomType(defaultMaxParticipants: 2);
        SetupRoomType(roomType);
        SetupFindRoomTypeByCode(roomType);

        // Room has no MaxParticipants override → falls back to room type's 2
        var room = CreateTestRoom(maxParticipants: null);
        SetupRoom(room);
        SetupRoomCache(room);

        // 2 participants already in room → full per room type default
        var existing = Enumerable.Range(0, 2).Select(i =>
        {
            var p = CreateTestParticipant(sessionId: Guid.NewGuid());
            return p;
        }).ToArray();
        SetupParticipants(TestRoomId, existing);

        MockParticipantStore
            .Setup(s => s.HashCountAsync(TestRoomId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2L);

        // Act
        var (status, _) = await service.JoinRoomAsync(new JoinRoomRequest
        {
            RoomId = TestRoomId,
            SenderType = "player",
            SenderId = Guid.NewGuid(),
            DisplayName = "NewPlayer",
        }, CancellationToken.None);

        // Assert - full at room type default
        Assert.Equal(StatusCodes.Conflict, status);

        ClearCallerSession();
    }

    /// <summary>
    /// Verifies that when both room and room type have no max, the global config default is used.
    /// </summary>
    [Fact]
    public async Task JoinRoom_NoOverrides_UsesGlobalConfigDefault()
    {
        // Set global default to 3
        Configuration.DefaultMaxParticipantsPerRoom = 3;

        var service = CreateService();
        SetCallerSession(TestSessionId);

        var roomType = CreateTestRoomType(defaultMaxParticipants: null);
        SetupRoomType(roomType);
        SetupFindRoomTypeByCode(roomType);

        // Room has no override, room type has no default → uses global config (3)
        var room = CreateTestRoom(maxParticipants: null);
        SetupRoom(room);
        SetupRoomCache(room);

        // 3 participants already → full
        var existing = Enumerable.Range(0, 3).Select(i =>
        {
            var p = CreateTestParticipant(sessionId: Guid.NewGuid());
            return p;
        }).ToArray();
        SetupParticipants(TestRoomId, existing);

        MockParticipantStore
            .Setup(s => s.HashCountAsync(TestRoomId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3L);

        // Act
        var (status, _) = await service.JoinRoomAsync(new JoinRoomRequest
        {
            RoomId = TestRoomId,
            SenderType = "player",
            SenderId = Guid.NewGuid(),
            DisplayName = "NewPlayer",
        }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);

        ClearCallerSession();
    }

    #endregion
}
