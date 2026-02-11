using BeyondImmersion.Bannou.Chat.ClientEvents;
using BeyondImmersion.BannouService.Chat;
using BeyondImmersion.BannouService.Common;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Moq;

namespace BeyondImmersion.BannouService.Chat.Tests;

/// <summary>
/// Tests for ChatService message operations:
/// SendMessage, SendMessageBatch, GetMessageHistory, DeleteMessage, PinMessage, UnpinMessage, SearchMessages.
/// </summary>
public class ChatServiceMessageTests : ChatServiceTestBase
{
    #region SendMessage

    [Fact]
    public async Task SendMessage_ValidTextMessage_SavesAndBroadcasts()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        var roomType = CreateTestRoomType("text", MessageFormat.Text, PersistenceMode.Persistent);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var sender = CreateTestParticipant(sessionId: TestSessionId);
        SetupParticipants(TestRoomId, sender);

        // Rate limit: first message (returns 1, under default limit)
        MockParticipantStore
            .Setup(s => s.IncrementAsync(
                It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        ChatMessageModel? savedMessage = null;
        MockMessageStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatMessageModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ChatMessageModel, StateOptions?, CancellationToken>(
                (_, m, _, _) => savedMessage = m)
            .ReturnsAsync("etag");

        var request = new SendMessageRequest
        {
            RoomId = TestRoomId,
            Content = new SendMessageContent { Text = "Hello world" },
        };

        var (status, response) = await service.SendMessageAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedMessage);
        Assert.Equal("Hello world", savedMessage.TextContent);
        Assert.Equal(MessageFormat.Text, savedMessage.MessageFormat);
        Assert.Equal(TestRoomId, savedMessage.RoomId);
        Assert.False(savedMessage.IsPinned);

        // Verify persistent store was used (not ephemeral buffer)
        MockMessageStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatMessageModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        MockMessageBuffer.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatMessageModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify event published
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.message.sent", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify client broadcast
        MockClientEventPublisher.Verify(
            p => p.PublishToSessionsAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<ChatMessageReceivedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendMessage_EphemeralRoom_UsesBufferWithTtl()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom(roomTypeCode: "ephemeral");
        var roomType = CreateTestRoomType("ephemeral", MessageFormat.Text, PersistenceMode.Ephemeral);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var sender = CreateTestParticipant(sessionId: TestSessionId);
        SetupParticipants(TestRoomId, sender);

        MockParticipantStore
            .Setup(s => s.IncrementAsync(
                It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        StateOptions? capturedOptions = null;
        MockMessageBuffer
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatMessageModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ChatMessageModel, StateOptions?, CancellationToken>(
                (_, _, opts, _) => capturedOptions = opts)
            .ReturnsAsync("etag");

        var request = new SendMessageRequest
        {
            RoomId = TestRoomId,
            Content = new SendMessageContent { Text = "Ephemeral message" },
        };

        var (status, _) = await service.SendMessageAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);

        // Verify ephemeral buffer was used with TTL
        MockMessageBuffer.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatMessageModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        MockMessageStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatMessageModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // TTL should be EphemeralMessageTtlMinutes * 60
        Assert.NotNull(capturedOptions);
        Assert.Equal(Configuration.EphemeralMessageTtlMinutes * 60, capturedOptions.Ttl);
    }

    [Fact]
    public async Task SendMessage_NoSession_ReturnsUnauthorized()
    {
        var service = CreateService();
        ClearCallerSession();

        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest { RoomId = TestRoomId, Content = new SendMessageContent { Text = "test" } },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Unauthorized, status);
    }

    [Fact]
    public async Task SendMessage_RoomNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        // Cache miss + store miss = not found
        MockRoomCache
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomModel?)null);
        MockRoomStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomModel?)null);

        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest { RoomId = TestRoomId, Content = new SendMessageContent { Text = "test" } },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task SendMessage_LockedRoom_ReturnsForbidden()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom(status: ChatRoomStatus.Locked);
        SetupRoomCache(room);

        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest { RoomId = TestRoomId, Content = new SendMessageContent { Text = "test" } },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Forbidden, status);
    }

    [Fact]
    public async Task SendMessage_ArchivedRoom_ReturnsForbidden()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom(status: ChatRoomStatus.Archived, isArchived: true);
        SetupRoomCache(room);

        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest { RoomId = TestRoomId, Content = new SendMessageContent { Text = "test" } },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Forbidden, status);
    }

    [Fact]
    public async Task SendMessage_NotParticipant_ReturnsForbidden()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        SetupRoomCache(room);

        // Participant store returns null for this session
        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), TestSessionId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatParticipantModel?)null);

        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest { RoomId = TestRoomId, Content = new SendMessageContent { Text = "test" } },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Forbidden, status);
    }

    [Fact]
    public async Task SendMessage_ReadOnlyParticipant_ReturnsForbidden()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        SetupRoomCache(room);

        var sender = CreateTestParticipant(sessionId: TestSessionId, role: ChatParticipantRole.ReadOnly);
        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), TestSessionId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sender);

        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest { RoomId = TestRoomId, Content = new SendMessageContent { Text = "test" } },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Forbidden, status);
    }

    [Fact]
    public async Task SendMessage_MutedParticipant_ReturnsForbidden()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        SetupRoomCache(room);

        // Muted indefinitely
        var sender = CreateTestParticipant(sessionId: TestSessionId, isMuted: true);
        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), TestSessionId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sender);

        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest { RoomId = TestRoomId, Content = new SendMessageContent { Text = "test" } },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Forbidden, status);
    }

    [Fact]
    public async Task SendMessage_ExpiredMute_AutoUnmutesAndSends()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        var roomType = CreateTestRoomType("text", MessageFormat.Text, PersistenceMode.Persistent);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        // Mute expired 5 minutes ago
        var sender = CreateTestParticipant(sessionId: TestSessionId, isMuted: true);
        sender.MutedUntil = DateTimeOffset.UtcNow.AddMinutes(-5);
        SetupParticipants(TestRoomId, sender);

        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), TestSessionId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sender);

        MockParticipantStore
            .Setup(s => s.IncrementAsync(
                It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest { RoomId = TestRoomId, Content = new SendMessageContent { Text = "I'm back" } },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task SendMessage_RateLimited_ReturnsBadRequest()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        var roomType = CreateTestRoomType("text", MessageFormat.Text, PersistenceMode.Persistent);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var sender = CreateTestParticipant(sessionId: TestSessionId);
        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), TestSessionId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sender);

        // Rate limit exceeded: returns count above the limit
        var effectiveLimit = Configuration.DefaultRateLimitPerMinute;
        MockParticipantStore
            .Setup(s => s.IncrementAsync(
                It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(effectiveLimit + 1);

        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest { RoomId = TestRoomId, Content = new SendMessageContent { Text = "spam" } },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task SendMessage_CustomRateLimit_UsesRoomTypeLimit()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom(roomTypeCode: "limited");
        var roomType = CreateTestRoomType("limited", MessageFormat.Text, PersistenceMode.Persistent, rateLimitPerMinute: 5);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var sender = CreateTestParticipant(sessionId: TestSessionId);
        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), TestSessionId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sender);

        // Count is 6, which exceeds room type's custom limit of 5
        MockParticipantStore
            .Setup(s => s.IncrementAsync(
                It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(6L);

        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest { RoomId = TestRoomId, Content = new SendMessageContent { Text = "too fast" } },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task SendMessage_SentimentFormat_ValidatesContent()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom(roomTypeCode: "sentiment");
        var roomType = CreateTestRoomType("sentiment", MessageFormat.Sentiment, PersistenceMode.Persistent);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var sender = CreateTestParticipant(sessionId: TestSessionId);
        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), TestSessionId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sender);

        MockParticipantStore
            .Setup(s => s.IncrementAsync(
                It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        // Missing sentiment fields
        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest
            {
                RoomId = TestRoomId,
                Content = new SendMessageContent { Text = "wrong format" },
            }, CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task SendMessage_SentimentFormat_ValidContent_Succeeds()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom(roomTypeCode: "sentiment");
        var roomType = CreateTestRoomType("sentiment", MessageFormat.Sentiment, PersistenceMode.Persistent);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var sender = CreateTestParticipant(sessionId: TestSessionId);
        SetupParticipants(TestRoomId, sender);

        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), TestSessionId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sender);

        MockParticipantStore
            .Setup(s => s.IncrementAsync(
                It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        var (status, response) = await service.SendMessageAsync(
            new SendMessageRequest
            {
                RoomId = TestRoomId,
                Content = new SendMessageContent
                {
                    SentimentCategory = SentimentCategory.Excited,
                    SentimentIntensity = 0.8f,
                },
            }, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task SendMessage_EmojiFormat_MissingEmojiCode_ReturnsBadRequest()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom(roomTypeCode: "emoji");
        var roomType = CreateTestRoomType("emoji", MessageFormat.Emoji, PersistenceMode.Persistent);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var sender = CreateTestParticipant(sessionId: TestSessionId);
        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), TestSessionId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sender);

        MockParticipantStore
            .Setup(s => s.IncrementAsync(
                It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest
            {
                RoomId = TestRoomId,
                Content = new SendMessageContent { Text = "not an emoji" },
            }, CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task SendMessage_TextFormat_ValidatorConfig_MaxLength_ReturnsBadRequest()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        var roomType = CreateTestRoomType("text", MessageFormat.Text, PersistenceMode.Persistent);
        roomType.ValidatorConfig = new ValidatorConfigModel { MaxMessageLength = 10 };
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var sender = CreateTestParticipant(sessionId: TestSessionId);
        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), TestSessionId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sender);

        MockParticipantStore
            .Setup(s => s.IncrementAsync(
                It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest
            {
                RoomId = TestRoomId,
                Content = new SendMessageContent { Text = "This text is way too long for the validator" },
            }, CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task SendMessage_TextFormat_MissingText_ReturnsBadRequest()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var room = CreateTestRoom();
        var roomType = CreateTestRoomType("text", MessageFormat.Text, PersistenceMode.Persistent);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var sender = CreateTestParticipant(sessionId: TestSessionId);
        MockParticipantStore
            .Setup(s => s.HashGetAsync<ChatParticipantModel>(
                TestRoomId.ToString(), TestSessionId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sender);

        MockParticipantStore
            .Setup(s => s.IncrementAsync(
                It.IsAny<string>(), It.IsAny<long>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        var (status, _) = await service.SendMessageAsync(
            new SendMessageRequest
            {
                RoomId = TestRoomId,
                Content = new SendMessageContent { }, // no text
            }, CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region SendMessageBatch

    [Fact]
    public async Task SendMessageBatch_ValidMessages_ReturnsCount()
    {
        var service = CreateService();

        var room = CreateTestRoom();
        var roomType = CreateTestRoomType("text", MessageFormat.Text, PersistenceMode.Persistent);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var participants = new[] { CreateTestParticipant(sessionId: TestSessionId) };
        SetupParticipants(TestRoomId, participants);

        var request = new SendMessageBatchRequest
        {
            RoomId = TestRoomId,
            Messages = new List<BatchMessageEntry>
            {
                new() { SenderType = "npc", SenderId = Guid.NewGuid(), DisplayName = "NPC1", Content = new SendMessageContent { Text = "Hello" } },
                new() { SenderType = "npc", SenderId = Guid.NewGuid(), DisplayName = "NPC2", Content = new SendMessageContent { Text = "World" } },
            },
        };

        var (status, response) = await service.SendMessageBatchAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.MessageCount);

        // Two messages saved to persistent store
        MockMessageStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatMessageModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SendMessageBatch_RoomNotFound_ReturnsNotFound()
    {
        var service = CreateService();

        MockRoomCache
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomModel?)null);
        MockRoomStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomModel?)null);

        var request = new SendMessageBatchRequest
        {
            RoomId = TestRoomId,
            Messages = new List<BatchMessageEntry>
            {
                new() { SenderType = "system", DisplayName = "Sys", Content = new SendMessageContent { Text = "test" } },
            },
        };

        var (status, _) = await service.SendMessageBatchAsync(request, CancellationToken.None);
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task SendMessageBatch_LockedRoom_ReturnsForbidden()
    {
        var service = CreateService();
        var room = CreateTestRoom(status: ChatRoomStatus.Locked);
        SetupRoomCache(room);

        var request = new SendMessageBatchRequest
        {
            RoomId = TestRoomId,
            Messages = new List<BatchMessageEntry>
            {
                new() { SenderType = "system", DisplayName = "Sys", Content = new SendMessageContent { Text = "test" } },
            },
        };

        var (status, _) = await service.SendMessageBatchAsync(request, CancellationToken.None);
        Assert.Equal(StatusCodes.Forbidden, status);
    }

    [Fact]
    public async Task SendMessageBatch_SkipsInvalidMessages()
    {
        var service = CreateService();

        var room = CreateTestRoom(roomTypeCode: "emoji");
        var roomType = CreateTestRoomType("emoji", MessageFormat.Emoji, PersistenceMode.Persistent);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var participants = new[] { CreateTestParticipant(sessionId: TestSessionId) };
        SetupParticipants(TestRoomId, participants);

        var request = new SendMessageBatchRequest
        {
            RoomId = TestRoomId,
            Messages = new List<BatchMessageEntry>
            {
                // Valid emoji
                new() { SenderType = "npc", SenderId = Guid.NewGuid(), DisplayName = "NPC", Content = new SendMessageContent { EmojiCode = "smile" } },
                // Invalid: emoji room needs EmojiCode, not text
                new() { SenderType = "npc", SenderId = Guid.NewGuid(), DisplayName = "NPC", Content = new SendMessageContent { Text = "wrong" } },
            },
        };

        var (status, response) = await service.SendMessageBatchAsync(request, CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.MessageCount); // only the valid one
    }

    #endregion

    #region GetMessageHistory

    [Fact]
    public async Task GetMessageHistory_PersistentRoom_ReturnsMessages()
    {
        var service = CreateService();

        var room = CreateTestRoom();
        var roomType = CreateTestRoomType("text", MessageFormat.Text, PersistenceMode.Persistent);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var messages = new List<ChatMessageModel>
        {
            CreateTestMessage(roomId: TestRoomId, textContent: "First"),
            CreateTestMessage(roomId: TestRoomId, textContent: "Second"),
        };
        SetupMessageQuery(messages, 2);

        var (status, response) = await service.GetMessageHistoryAsync(
            new MessageHistoryRequest { RoomId = TestRoomId, Limit = 50 },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Messages.Count);
        Assert.False(response.HasMore);
    }

    [Fact]
    public async Task GetMessageHistory_EphemeralRoom_ReturnsEmptyList()
    {
        var service = CreateService();

        var room = CreateTestRoom(roomTypeCode: "ephemeral");
        var roomType = CreateTestRoomType("ephemeral", MessageFormat.Text, PersistenceMode.Ephemeral);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var (status, response) = await service.GetMessageHistoryAsync(
            new MessageHistoryRequest { RoomId = TestRoomId, Limit = 50 },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Messages);
        Assert.False(response.HasMore);

        // Verify message store was NOT queried
        MockMessageStore.Verify(
            s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetMessageHistory_RoomNotFound_ReturnsNotFound()
    {
        var service = CreateService();

        MockRoomCache
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomModel?)null);
        MockRoomStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomModel?)null);

        var (status, _) = await service.GetMessageHistoryAsync(
            new MessageHistoryRequest { RoomId = TestRoomId, Limit = 50 },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task GetMessageHistory_LimitClampedToPageSize()
    {
        var service = CreateService();

        var room = CreateTestRoom();
        var roomType = CreateTestRoomType("text", MessageFormat.Text, PersistenceMode.Persistent);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);
        SetupMessageQuery(new List<ChatMessageModel>(), 0);

        // Request with limit larger than config page size
        await service.GetMessageHistoryAsync(
            new MessageHistoryRequest { RoomId = TestRoomId, Limit = 999 },
            CancellationToken.None);

        // Verify the query was called with page size + 1 (for hasMore detection)
        MockMessageStore.Verify(s => s.JsonQueryPagedAsync(
            It.IsAny<IReadOnlyList<QueryCondition>>(),
            0,
            Configuration.MessageHistoryPageSize + 1,
            It.IsAny<JsonSortSpec?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region DeleteMessage

    [Fact]
    public async Task DeleteMessage_ValidRequest_DeletesAndBroadcasts()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        var messageId = Guid.NewGuid();
        var message = CreateTestMessage(roomId: TestRoomId, messageId: messageId);
        var room = CreateTestRoom();
        var msgKey = $"{TestRoomId}:{messageId}";

        MockMessageStore
            .Setup(s => s.GetAsync(msgKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        SetupRoomCache(room);

        var participants = new[] { CreateTestParticipant(sessionId: TestSessionId) };
        SetupParticipants(TestRoomId, participants);

        var (status, response) = await service.DeleteMessageAsync(
            new DeleteMessageRequest { RoomId = TestRoomId, MessageId = messageId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify deletion
        MockMessageStore.Verify(
            s => s.DeleteAsync(msgKey, It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify event published
        MockMessageBus.Verify(
            m => m.TryPublishAsync("chat.message.deleted", It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify client broadcast
        MockClientEventPublisher.Verify(
            p => p.PublishToSessionsAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<ChatMessageDeletedClientEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteMessage_NoSession_ReturnsUnauthorized()
    {
        var service = CreateService();
        ClearCallerSession();

        var (status, _) = await service.DeleteMessageAsync(
            new DeleteMessageRequest { RoomId = TestRoomId, MessageId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Unauthorized, status);
    }

    [Fact]
    public async Task DeleteMessage_MessageNotFound_ReturnsNotFound()
    {
        var service = CreateService();
        SetCallerSession(TestSessionId);

        MockMessageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessageModel?)null);

        var (status, _) = await service.DeleteMessageAsync(
            new DeleteMessageRequest { RoomId = TestRoomId, MessageId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region PinMessage

    [Fact]
    public async Task PinMessage_ValidRequest_SetsIsPinnedTrue()
    {
        var service = CreateService();

        var messageId = Guid.NewGuid();
        var message = CreateTestMessage(roomId: TestRoomId, messageId: messageId);
        var msgKey = $"{TestRoomId}:{messageId}";
        var room = CreateTestRoom();

        MockMessageStore
            .Setup(s => s.GetAsync(msgKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);

        // Zero pinned messages currently
        MockMessageStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        ChatMessageModel? savedMessage = null;
        MockMessageStore
            .Setup(s => s.SaveAsync(msgKey, It.IsAny<ChatMessageModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ChatMessageModel, StateOptions?, CancellationToken>(
                (_, m, _, _) => savedMessage = m)
            .ReturnsAsync("etag");

        SetupRoomCache(room);
        SetupParticipants(TestRoomId, CreateTestParticipant(sessionId: TestSessionId));

        var (status, response) = await service.PinMessageAsync(
            new PinMessageRequest { RoomId = TestRoomId, MessageId = messageId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedMessage);
        Assert.True(savedMessage.IsPinned);
    }

    [Fact]
    public async Task PinMessage_AlreadyPinned_ReturnsOKWithoutSave()
    {
        var service = CreateService();

        var messageId = Guid.NewGuid();
        var message = CreateTestMessage(roomId: TestRoomId, messageId: messageId, isPinned: true);
        var msgKey = $"{TestRoomId}:{messageId}";
        var room = CreateTestRoom();

        MockMessageStore
            .Setup(s => s.GetAsync(msgKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);
        SetupRoomCache(room);

        var (status, response) = await service.PinMessageAsync(
            new PinMessageRequest { RoomId = TestRoomId, MessageId = messageId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Should NOT save again
        MockMessageStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ChatMessageModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PinMessage_ExceedsMaxPins_ReturnsConflict()
    {
        var service = CreateService();

        var messageId = Guid.NewGuid();
        var message = CreateTestMessage(roomId: TestRoomId, messageId: messageId);
        var msgKey = $"{TestRoomId}:{messageId}";

        MockMessageStore
            .Setup(s => s.GetAsync(msgKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);

        // Already at max pinned
        MockMessageStore
            .Setup(s => s.JsonCountAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long)Configuration.MaxPinnedMessagesPerRoom);

        var (status, _) = await service.PinMessageAsync(
            new PinMessageRequest { RoomId = TestRoomId, MessageId = messageId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task PinMessage_MessageNotFound_ReturnsNotFound()
    {
        var service = CreateService();

        MockMessageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessageModel?)null);

        var (status, _) = await service.PinMessageAsync(
            new PinMessageRequest { RoomId = TestRoomId, MessageId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task PinMessage_LockFails_ReturnsConflict()
    {
        var service = CreateService();
        SetupLockFailure();

        var (status, _) = await service.PinMessageAsync(
            new PinMessageRequest { RoomId = TestRoomId, MessageId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Conflict, status);
    }

    #endregion

    #region UnpinMessage

    [Fact]
    public async Task UnpinMessage_ValidRequest_SetsIsPinnedFalse()
    {
        var service = CreateService();

        var messageId = Guid.NewGuid();
        var message = CreateTestMessage(roomId: TestRoomId, messageId: messageId, isPinned: true);
        var msgKey = $"{TestRoomId}:{messageId}";
        var room = CreateTestRoom();

        MockMessageStore
            .Setup(s => s.GetAsync(msgKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);

        ChatMessageModel? savedMessage = null;
        MockMessageStore
            .Setup(s => s.SaveAsync(msgKey, It.IsAny<ChatMessageModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ChatMessageModel, StateOptions?, CancellationToken>(
                (_, m, _, _) => savedMessage = m)
            .ReturnsAsync("etag");

        SetupRoomCache(room);
        SetupParticipants(TestRoomId, CreateTestParticipant(sessionId: TestSessionId));

        var (status, response) = await service.UnpinMessageAsync(
            new UnpinMessageRequest { RoomId = TestRoomId, MessageId = messageId },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedMessage);
        Assert.False(savedMessage.IsPinned);
    }

    [Fact]
    public async Task UnpinMessage_MessageNotFound_ReturnsNotFound()
    {
        var service = CreateService();

        MockMessageStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessageModel?)null);

        var (status, _) = await service.UnpinMessageAsync(
            new UnpinMessageRequest { RoomId = TestRoomId, MessageId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region SearchMessages

    [Fact]
    public async Task SearchMessages_ValidQuery_ReturnsResults()
    {
        var service = CreateService();

        var room = CreateTestRoom();
        var roomType = CreateTestRoomType("text", MessageFormat.Text, PersistenceMode.Persistent);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var messages = new List<ChatMessageModel>
        {
            CreateTestMessage(roomId: TestRoomId, textContent: "Hello there"),
        };
        SetupMessageQuery(messages, 1);

        var (status, response) = await service.SearchMessagesAsync(
            new SearchMessagesRequest { RoomId = TestRoomId, Query = "Hello", Limit = 20 },
            CancellationToken.None);

        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Messages);
        Assert.Equal(1, response.TotalMatches);
    }

    [Fact]
    public async Task SearchMessages_EphemeralRoom_ReturnsBadRequest()
    {
        var service = CreateService();

        var room = CreateTestRoom(roomTypeCode: "ephemeral");
        var roomType = CreateTestRoomType("ephemeral", MessageFormat.Text, PersistenceMode.Ephemeral);
        SetupRoomCache(room);
        SetupFindRoomTypeByCode(roomType);

        var (status, _) = await service.SearchMessagesAsync(
            new SearchMessagesRequest { RoomId = TestRoomId, Query = "Hello", Limit = 20 },
            CancellationToken.None);

        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task SearchMessages_RoomNotFound_ReturnsNotFound()
    {
        var service = CreateService();

        MockRoomCache
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomModel?)null);
        MockRoomStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatRoomModel?)null);

        var (status, _) = await service.SearchMessagesAsync(
            new SearchMessagesRequest { RoomId = TestRoomId, Query = "Hello", Limit = 20 },
            CancellationToken.None);

        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion
}
