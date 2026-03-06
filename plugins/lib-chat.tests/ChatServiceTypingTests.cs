using BeyondImmersion.Bannou.Chat.ClientEvents;
using BeyondImmersion.BannouService.Chat;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.State;
using Moq;

namespace BeyondImmersion.BannouService.Chat.Tests;

/// <summary>
/// Tests for ChatService typing indicator operations:
/// TypingAsync, EndTypingAsync, ClearTypingStateAsync, and related shortcut methods.
/// </summary>
public class ChatServiceTypingTests : ChatServiceTestBase
{
    #region TypingAsync

    [Fact]
    public async Task Typing_NewTyping_PublishesStartedEventAndReturnsOK()
    {
        var service = CreateService();

        // Sorted set score returns null => new typing (not heartbeat)
        MockParticipantStore
            .Setup(s => s.SortedSetScoreAsync("typing:active", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((double?)null);

        // Set up participant for display name resolution
        var participant = CreateTestParticipant(roomId: TestRoomId, sessionId: TestSessionId);
        SetupParticipants(TestRoomId, participant);

        var status = await service.TypingAsync(new TypingRequest
        {
            RoomId = TestRoomId,
            SessionId = TestSessionId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);

        // Verify timestamp upserted into sorted set
        MockParticipantStore.Verify(s => s.SortedSetAddAsync(
            "typing:active",
            It.Is<string>(m => m.Contains(TestRoomId.ToString("N")) && m.Contains(TestSessionId.ToString("N"))),
            It.IsAny<double>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify typing started event published to entity sessions
        MockEntitySessionRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            "chat-room", TestRoomId,
            It.Is<ChatTypingStartedClientEvent>(e =>
                e.RoomId == TestRoomId &&
                e.ParticipantSessionId == TestSessionId &&
                e.DisplayName == "TestPlayer"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Typing_HeartbeatRefresh_DoesNotPublishStartEvent()
    {
        var service = CreateService();

        // Sorted set score returns a value => heartbeat refresh (already typing)
        MockParticipantStore
            .Setup(s => s.SortedSetScoreAsync("typing:active", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1234567890.0);

        var status = await service.TypingAsync(new TypingRequest
        {
            RoomId = TestRoomId,
            SessionId = TestSessionId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);

        // Verify timestamp still upserted (refreshed)
        MockParticipantStore.Verify(s => s.SortedSetAddAsync(
            "typing:active", It.IsAny<string>(), It.IsAny<double>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify NO start event published (heartbeat only)
        MockEntitySessionRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<ChatTypingStartedClientEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region EndTypingAsync

    [Fact]
    public async Task EndTyping_ActivelyTyping_ClearsStateAndPublishesStoppedEvent()
    {
        var service = CreateService();

        // SortedSetRemoveAsync returns true => was typing
        MockParticipantStore
            .Setup(s => s.SortedSetRemoveAsync("typing:active", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var status = await service.EndTypingAsync(new EndTypingRequest
        {
            RoomId = TestRoomId,
            SessionId = TestSessionId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);

        // Verify stopped event published
        MockEntitySessionRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            "chat-room", TestRoomId,
            It.Is<ChatTypingStoppedClientEvent>(e =>
                e.RoomId == TestRoomId &&
                e.ParticipantSessionId == TestSessionId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EndTyping_NotTyping_ReturnsOKWithoutPublishing()
    {
        var service = CreateService();

        // SortedSetRemoveAsync returns false => was not typing
        MockParticipantStore
            .Setup(s => s.SortedSetRemoveAsync("typing:active", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var status = await service.EndTypingAsync(new EndTypingRequest
        {
            RoomId = TestRoomId,
            SessionId = TestSessionId,
        }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);

        // Verify NO stopped event published
        MockEntitySessionRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<ChatTypingStoppedClientEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Typing Sorted Set Member Format

    [Fact]
    public async Task Typing_MemberKey_UsesCorrectFormat()
    {
        var service = CreateService();

        MockParticipantStore
            .Setup(s => s.SortedSetScoreAsync("typing:active", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1234567890.0);

        await service.TypingAsync(new TypingRequest
        {
            RoomId = TestRoomId,
            SessionId = TestSessionId,
        }, CancellationToken.None);

        // Member format should be "{roomId:N}:{sessionId:N}" (no hyphens)
        var expectedMember = $"{TestRoomId:N}:{TestSessionId:N}";

        MockParticipantStore.Verify(s => s.SortedSetScoreAsync(
            "typing:active", expectedMember, It.IsAny<CancellationToken>()), Times.Once);

        MockParticipantStore.Verify(s => s.SortedSetAddAsync(
            "typing:active", expectedMember, It.IsAny<double>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
