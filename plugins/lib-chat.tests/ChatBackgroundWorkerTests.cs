using BeyondImmersion.Bannou.Chat.ClientEvents;
using BeyondImmersion.BannouService.Chat;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Chat.Tests;

/// <summary>
/// Unit tests for all four chat background workers:
/// BanExpiryWorker, IdleRoomCleanupWorker, MessageRetentionWorker, TypingExpiryWorker.
/// Tests the processing logic through ExecuteAsync / StartAsync lifecycle.
/// </summary>
public class ChatBackgroundWorkerTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceProvider> _mockScopedProvider;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly ChatServiceConfiguration _configuration;
    private readonly NullTelemetryProvider _telemetry;

    public ChatBackgroundWorkerTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScope = new Mock<IServiceScope>();
        _mockScopedProvider = new Mock<IServiceProvider>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockMessageBus = new Mock<IMessageBus>();
        _configuration = new ChatServiceConfiguration();
        _telemetry = new NullTelemetryProvider();

        // Wire up the DI scope chain
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(_mockScopeFactory.Object);
        _mockScopeFactory.Setup(f => f.CreateScope())
            .Returns(_mockScope.Object);
        _mockScope.Setup(s => s.ServiceProvider)
            .Returns(_mockScopedProvider.Object);

        // Common scoped service registrations
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IStateStoreFactory)))
            .Returns(_mockStateStoreFactory.Object);
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IDistributedLockProvider)))
            .Returns(_mockLockProvider.Object);
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IMessageBus)))
            .Returns(_mockMessageBus.Object);
    }

    private static Mock<ILockResponse> CreateLockResponse(bool success)
    {
        var mock = new Mock<ILockResponse>();
        mock.Setup(l => l.Success).Returns(success);
        mock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return mock;
    }

    // ============================================================================
    // BanExpiryWorker
    // ============================================================================

    #region BanExpiryWorker

    [Fact]
    public async Task BanExpiryWorker_WhenCancelledDuringStartup_StopsGracefully()
    {
        // Arrange
        _configuration.BanExpiryStartupDelaySeconds = 60;
        using var worker = new BanExpiryWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<BanExpiryWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - should not throw
        await worker.StartAsync(cts.Token);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task BanExpiryWorker_WhenLockNotAcquired_SkipsCycle()
    {
        // Arrange
        _configuration.BanExpiryStartupDelaySeconds = 0;
        _configuration.BanExpiryIntervalMinutes = 60;

        _mockLockProvider.Setup(l => l.LockAsync(
                StateStoreDefinitions.ChatLock,
                "ban-expiry-cycle",
                It.IsAny<string>(),
                _configuration.BanExpiryLockExpirySeconds,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLockResponse(false).Object);

        var mockBanStore = new Mock<IJsonQueryableStateStore<ChatBanModel>>();
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<ChatBanModel>(StateStoreDefinitions.ChatBans))
            .Returns(mockBanStore.Object);

        using var worker = new BanExpiryWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<BanExpiryWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - ban store should never be queried because lock was not acquired
        mockBanStore.Verify(s => s.JsonQueryPagedAsync(
            It.IsAny<IReadOnlyList<QueryCondition>>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BanExpiryWorker_WhenNoExpiredBans_CompletesWithoutDeletion()
    {
        // Arrange
        _configuration.BanExpiryStartupDelaySeconds = 0;
        _configuration.BanExpiryIntervalMinutes = 60;

        _mockLockProvider.Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLockResponse(true).Object);

        var mockBanStore = new Mock<IJsonQueryableStateStore<ChatBanModel>>();
        mockBanStore.Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatBanModel>(
                new List<JsonQueryResult<ChatBanModel>>(), 0, 0, _configuration.BanExpiryBatchSize));

        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<ChatBanModel>(StateStoreDefinitions.ChatBans))
            .Returns(mockBanStore.Object);

        using var worker = new BanExpiryWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<BanExpiryWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - query happened but no deletions
        mockBanStore.Verify(s => s.JsonQueryPagedAsync(
            It.IsAny<IReadOnlyList<QueryCondition>>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        mockBanStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BanExpiryWorker_WithExpiredBans_DeletesEach()
    {
        // Arrange
        _configuration.BanExpiryStartupDelaySeconds = 0;
        _configuration.BanExpiryIntervalMinutes = 60;

        _mockLockProvider.Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLockResponse(true).Object);

        var expiredBans = new List<JsonQueryResult<ChatBanModel>>
        {
            new("ban:room1:session1", new ChatBanModel
            {
                BanId = Guid.NewGuid(),
                RoomId = Guid.NewGuid(),
                TargetSessionId = Guid.NewGuid(),
                BannedBySessionId = Guid.NewGuid(),
            }),
            new("ban:room1:session2", new ChatBanModel
            {
                BanId = Guid.NewGuid(),
                RoomId = Guid.NewGuid(),
                TargetSessionId = Guid.NewGuid(),
                BannedBySessionId = Guid.NewGuid(),
            }),
        };

        var mockBanStore = new Mock<IJsonQueryableStateStore<ChatBanModel>>();
        mockBanStore.Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatBanModel>(
                expiredBans, 2, 0, _configuration.BanExpiryBatchSize));

        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<ChatBanModel>(StateStoreDefinitions.ChatBans))
            .Returns(mockBanStore.Object);

        using var worker = new BanExpiryWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<BanExpiryWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - both bans deleted
        mockBanStore.Verify(s => s.DeleteAsync("ban:room1:session1", It.IsAny<CancellationToken>()), Times.Once);
        mockBanStore.Verify(s => s.DeleteAsync("ban:room1:session2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BanExpiryWorker_WhenDeleteFails_ContinuesWithRemainingBans()
    {
        // Arrange
        _configuration.BanExpiryStartupDelaySeconds = 0;
        _configuration.BanExpiryIntervalMinutes = 60;

        _mockLockProvider.Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLockResponse(true).Object);

        var expiredBans = new List<JsonQueryResult<ChatBanModel>>
        {
            new("ban:fail", new ChatBanModel
            {
                BanId = Guid.NewGuid(), RoomId = Guid.NewGuid(),
                TargetSessionId = Guid.NewGuid(), BannedBySessionId = Guid.NewGuid(),
            }),
            new("ban:succeed", new ChatBanModel
            {
                BanId = Guid.NewGuid(), RoomId = Guid.NewGuid(),
                TargetSessionId = Guid.NewGuid(), BannedBySessionId = Guid.NewGuid(),
            }),
        };

        var mockBanStore = new Mock<IJsonQueryableStateStore<ChatBanModel>>();
        mockBanStore.Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatBanModel>(
                expiredBans, 2, 0, _configuration.BanExpiryBatchSize));

        // First delete fails, second succeeds
        mockBanStore.Setup(s => s.DeleteAsync("ban:fail", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delete failed"));

        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<ChatBanModel>(StateStoreDefinitions.ChatBans))
            .Returns(mockBanStore.Object);

        using var worker = new BanExpiryWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<BanExpiryWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - second ban still attempted after first failed
        mockBanStore.Verify(s => s.DeleteAsync("ban:succeed", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    // ============================================================================
    // IdleRoomCleanupWorker
    // ============================================================================

    #region IdleRoomCleanupWorker

    [Fact]
    public async Task IdleRoomCleanupWorker_WhenCancelledDuringStartup_StopsGracefully()
    {
        // Arrange
        _configuration.IdleRoomCleanupStartupDelaySeconds = 60;
        using var worker = new IdleRoomCleanupWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<IdleRoomCleanupWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await worker.StartAsync(cts.Token);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task IdleRoomCleanupWorker_WhenLockNotAcquired_SkipsCycle()
    {
        // Arrange
        _configuration.IdleRoomCleanupStartupDelaySeconds = 0;
        _configuration.IdleRoomCleanupIntervalMinutes = 60;

        _mockLockProvider.Setup(l => l.LockAsync(
                StateStoreDefinitions.ChatLock,
                "idle-room-cleanup",
                It.IsAny<string>(),
                _configuration.IdleRoomCleanupLockExpirySeconds,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLockResponse(false).Object);

        // IChatService should never be resolved
        var mockChatService = new Mock<IChatService>();
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IChatService)))
            .Returns(mockChatService.Object);

        using var worker = new IdleRoomCleanupWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<IdleRoomCleanupWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - ChatService should not be resolved when lock fails
        _mockScopedProvider.Verify(sp => sp.GetService(typeof(IChatService)), Times.Never);
    }

    [Fact]
    public async Task IdleRoomCleanupWorker_WhenChatServiceNotResolvable_LogsErrorAndReturns()
    {
        // Arrange: IChatService resolves to something that's NOT a ChatService instance
        _configuration.IdleRoomCleanupStartupDelaySeconds = 0;
        _configuration.IdleRoomCleanupIntervalMinutes = 60;

        _mockLockProvider.Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLockResponse(true).Object);

        // Return a mock IChatService (not a real ChatService instance)
        var mockChatService = new Mock<IChatService>();
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IChatService)))
            .Returns(mockChatService.Object);

        using var worker = new IdleRoomCleanupWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<IdleRoomCleanupWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act & Assert - should not throw, just log error and return
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);
    }

    #endregion

    // ============================================================================
    // MessageRetentionWorker
    // ============================================================================

    #region MessageRetentionWorker

    [Fact]
    public async Task MessageRetentionWorker_WhenCancelledDuringStartup_StopsGracefully()
    {
        // Arrange
        _configuration.MessageRetentionStartupDelaySeconds = 60;
        using var worker = new MessageRetentionWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<MessageRetentionWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await worker.StartAsync(cts.Token);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MessageRetentionWorker_WhenLockNotAcquired_SkipsCycle()
    {
        // Arrange
        _configuration.MessageRetentionStartupDelaySeconds = 0;
        _configuration.MessageRetentionCleanupIntervalMinutes = 60;

        _mockLockProvider.Setup(l => l.LockAsync(
                StateStoreDefinitions.ChatLock,
                "message-retention-cleanup",
                It.IsAny<string>(),
                _configuration.MessageRetentionLockExpirySeconds,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLockResponse(false).Object);

        var mockRoomTypeStore = new Mock<IJsonQueryableStateStore<ChatRoomTypeModel>>();
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<ChatRoomTypeModel>(StateStoreDefinitions.ChatRoomTypes))
            .Returns(mockRoomTypeStore.Object);

        using var worker = new MessageRetentionWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<MessageRetentionWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - room type store should never be queried when lock fails
        mockRoomTypeStore.Verify(s => s.JsonQueryPagedAsync(
            It.IsAny<IReadOnlyList<QueryCondition>>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MessageRetentionWorker_WhenNoRoomTypesWithRetention_CompletesWithoutDeletion()
    {
        // Arrange
        _configuration.MessageRetentionStartupDelaySeconds = 0;
        _configuration.MessageRetentionCleanupIntervalMinutes = 60;

        _mockLockProvider.Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLockResponse(true).Object);

        var mockRoomTypeStore = new Mock<IJsonQueryableStateStore<ChatRoomTypeModel>>();
        mockRoomTypeStore.Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomTypeModel>(
                new List<JsonQueryResult<ChatRoomTypeModel>>(), 0, 0, 1000));

        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<ChatRoomTypeModel>(StateStoreDefinitions.ChatRoomTypes))
            .Returns(mockRoomTypeStore.Object);

        var mockMessageStore = new Mock<IJsonQueryableStateStore<ChatMessageModel>>();
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<ChatMessageModel>(StateStoreDefinitions.ChatMessages))
            .Returns(mockMessageStore.Object);

        using var worker = new MessageRetentionWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<MessageRetentionWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - no message deletion
        mockMessageStore.Verify(s => s.JsonQueryPagedAsync(
            It.IsAny<IReadOnlyList<QueryCondition>>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MessageRetentionWorker_WithExpiredMessages_DeletesThem()
    {
        // Arrange
        _configuration.MessageRetentionStartupDelaySeconds = 0;
        _configuration.MessageRetentionCleanupIntervalMinutes = 60;

        _mockLockProvider.Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLockResponse(true).Object);

        var roomId = Guid.NewGuid();
        var roomType = new ChatRoomTypeModel
        {
            Code = "persistent-type",
            DisplayName = "Persistent",
            PersistenceMode = PersistenceMode.Persistent,
            RetentionDays = 30,
            Status = RoomTypeStatus.Active,
        };

        var mockRoomTypeStore = new Mock<IJsonQueryableStateStore<ChatRoomTypeModel>>();
        mockRoomTypeStore.Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomTypeModel>(
                new List<JsonQueryResult<ChatRoomTypeModel>>
                {
                    new("roomtype:persistent-type", roomType),
                }, 1, 0, 1000));

        var mockRoomStore = new Mock<IJsonQueryableStateStore<ChatRoomModel>>();
        mockRoomStore.Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(
                new List<JsonQueryResult<ChatRoomModel>>
                {
                    new($"room:{roomId:N}", new ChatRoomModel { RoomId = roomId, RoomTypeCode = "persistent-type" }),
                }, 1, 0, 1000));

        var expiredMessages = new List<JsonQueryResult<ChatMessageModel>>
        {
            new($"{roomId:N}:msg1", new ChatMessageModel
            {
                MessageId = Guid.NewGuid(), RoomId = roomId,
                Timestamp = DateTimeOffset.UtcNow.AddDays(-60),
            }),
            new($"{roomId:N}:msg2", new ChatMessageModel
            {
                MessageId = Guid.NewGuid(), RoomId = roomId,
                Timestamp = DateTimeOffset.UtcNow.AddDays(-45),
            }),
        };

        var mockMessageStore = new Mock<IJsonQueryableStateStore<ChatMessageModel>>();
        mockMessageStore.Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatMessageModel>(
                expiredMessages, 2, 0, _configuration.MessageRetentionBatchSize));

        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<ChatRoomTypeModel>(StateStoreDefinitions.ChatRoomTypes))
            .Returns(mockRoomTypeStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<ChatRoomModel>(StateStoreDefinitions.ChatRooms))
            .Returns(mockRoomStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<ChatMessageModel>(StateStoreDefinitions.ChatMessages))
            .Returns(mockMessageStore.Object);

        using var worker = new MessageRetentionWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<MessageRetentionWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // Assert - both expired messages deleted
        mockMessageStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.EndsWith(":msg1")),
            It.IsAny<CancellationToken>()), Times.Once);
        mockMessageStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.EndsWith(":msg2")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MessageRetentionWorker_WhenMessageDeleteFails_ContinuesWithRemaining()
    {
        // Arrange
        _configuration.MessageRetentionStartupDelaySeconds = 0;
        _configuration.MessageRetentionCleanupIntervalMinutes = 60;

        _mockLockProvider.Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLockResponse(true).Object);

        var roomId = Guid.NewGuid();
        var roomType = new ChatRoomTypeModel
        {
            Code = "persistent",
            DisplayName = "Persistent",
            PersistenceMode = PersistenceMode.Persistent,
            RetentionDays = 7,
            Status = RoomTypeStatus.Active,
        };

        var mockRoomTypeStore = new Mock<IJsonQueryableStateStore<ChatRoomTypeModel>>();
        mockRoomTypeStore.Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomTypeModel>(
                new List<JsonQueryResult<ChatRoomTypeModel>>
                {
                    new("roomtype:persistent", roomType),
                }, 1, 0, 1000));

        var mockRoomStore = new Mock<IJsonQueryableStateStore<ChatRoomModel>>();
        mockRoomStore.Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatRoomModel>(
                new List<JsonQueryResult<ChatRoomModel>>
                {
                    new($"room:{roomId:N}", new ChatRoomModel { RoomId = roomId, RoomTypeCode = "persistent" }),
                }, 1, 0, 1000));

        var messages = new List<JsonQueryResult<ChatMessageModel>>
        {
            new("msg:fail", new ChatMessageModel
            {
                MessageId = Guid.NewGuid(), RoomId = roomId,
                Timestamp = DateTimeOffset.UtcNow.AddDays(-30),
            }),
            new("msg:ok", new ChatMessageModel
            {
                MessageId = Guid.NewGuid(), RoomId = roomId,
                Timestamp = DateTimeOffset.UtcNow.AddDays(-20),
            }),
        };

        var mockMessageStore = new Mock<IJsonQueryableStateStore<ChatMessageModel>>();
        mockMessageStore.Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<ChatMessageModel>(
                messages, 2, 0, _configuration.MessageRetentionBatchSize));

        mockMessageStore.Setup(s => s.DeleteAsync("msg:fail", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated failure"));

        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<ChatRoomTypeModel>(StateStoreDefinitions.ChatRoomTypes))
            .Returns(mockRoomTypeStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<ChatRoomModel>(StateStoreDefinitions.ChatRooms))
            .Returns(mockRoomStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<ChatMessageModel>(StateStoreDefinitions.ChatMessages))
            .Returns(mockMessageStore.Object);

        using var worker = new MessageRetentionWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<MessageRetentionWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);

        // Assert - second message still deleted after first failure
        mockMessageStore.Verify(s => s.DeleteAsync("msg:ok", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    // ============================================================================
    // TypingExpiryWorker
    // ============================================================================

    #region TypingExpiryWorker

    [Fact]
    public async Task TypingExpiryWorker_WhenCancelledImmediately_StopsGracefully()
    {
        // Arrange
        using var worker = new TypingExpiryWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<TypingExpiryWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await worker.StartAsync(cts.Token);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TypingExpiryWorker_WhenNoExpiredEntries_CompletesWithoutPublishing()
    {
        // Arrange
        _configuration.TypingWorkerIntervalMilliseconds = 5000;

        var mockParticipantStore = new Mock<ICacheableStateStore<ChatParticipantModel>>();
        mockParticipantStore.Setup(s => s.SortedSetRangeByScoreAsync(
                "typing:active", double.NegativeInfinity, It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string, double)>());

        _mockStateStoreFactory.Setup(f => f.GetCacheableStore<ChatParticipantModel>(StateStoreDefinitions.ChatParticipants))
            .Returns(mockParticipantStore.Object);

        var mockEntityRegistry = new Mock<IEntitySessionRegistry>();
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IEntitySessionRegistry)))
            .Returns(mockEntityRegistry.Object);

        using var worker = new TypingExpiryWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<TypingExpiryWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - no stop events published
        mockEntityRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<ChatTypingStoppedClientEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TypingExpiryWorker_WithExpiredEntries_RemovesAndPublishesStopEvents()
    {
        // Arrange
        _configuration.TypingWorkerIntervalMilliseconds = 5000;

        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var member = $"{roomId:N}:{sessionId:N}";

        var mockParticipantStore = new Mock<ICacheableStateStore<ChatParticipantModel>>();
        mockParticipantStore.Setup(s => s.SortedSetRangeByScoreAsync(
                "typing:active", double.NegativeInfinity, It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string, double)> { (member, 1000.0) });

        _mockStateStoreFactory.Setup(f => f.GetCacheableStore<ChatParticipantModel>(StateStoreDefinitions.ChatParticipants))
            .Returns(mockParticipantStore.Object);

        var mockEntityRegistry = new Mock<IEntitySessionRegistry>();
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IEntitySessionRegistry)))
            .Returns(mockEntityRegistry.Object);

        using var worker = new TypingExpiryWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<TypingExpiryWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - entry removed from sorted set
        mockParticipantStore.Verify(s => s.SortedSetRemoveAsync(
            "typing:active", member, It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // Assert - stop event published to room participants
        mockEntityRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            "chat-room",
            roomId,
            It.Is<ChatTypingStoppedClientEvent>(e =>
                e.RoomId == roomId && e.ParticipantSessionId == sessionId),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task TypingExpiryWorker_WithInvalidMemberLength_SkipsEntry()
    {
        // Arrange: member is not 65 chars (32 + 1 + 32)
        _configuration.TypingWorkerIntervalMilliseconds = 5000;

        var mockParticipantStore = new Mock<ICacheableStateStore<ChatParticipantModel>>();
        mockParticipantStore.Setup(s => s.SortedSetRangeByScoreAsync(
                "typing:active", double.NegativeInfinity, It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string, double)>
            {
                ("short-invalid-member", 1000.0),
            });

        _mockStateStoreFactory.Setup(f => f.GetCacheableStore<ChatParticipantModel>(StateStoreDefinitions.ChatParticipants))
            .Returns(mockParticipantStore.Object);

        var mockEntityRegistry = new Mock<IEntitySessionRegistry>();
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IEntitySessionRegistry)))
            .Returns(mockEntityRegistry.Object);

        using var worker = new TypingExpiryWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<TypingExpiryWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - invalid entry skipped entirely (no remove, no publish)
        mockParticipantStore.Verify(s => s.SortedSetRemoveAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mockEntityRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<ChatTypingStoppedClientEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TypingExpiryWorker_WithInvalidGuidInMember_SkipsEntry()
    {
        // Arrange: 65 chars but not valid GUIDs
        _configuration.TypingWorkerIntervalMilliseconds = 5000;

        var invalidMember = new string('x', 32) + ":" + new string('y', 32);

        var mockParticipantStore = new Mock<ICacheableStateStore<ChatParticipantModel>>();
        mockParticipantStore.Setup(s => s.SortedSetRangeByScoreAsync(
                "typing:active", double.NegativeInfinity, It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(string, double)>
            {
                (invalidMember, 1000.0),
            });

        _mockStateStoreFactory.Setup(f => f.GetCacheableStore<ChatParticipantModel>(StateStoreDefinitions.ChatParticipants))
            .Returns(mockParticipantStore.Object);

        var mockEntityRegistry = new Mock<IEntitySessionRegistry>();
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IEntitySessionRegistry)))
            .Returns(mockEntityRegistry.Object);

        using var worker = new TypingExpiryWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<TypingExpiryWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - invalid GUID entry skipped (no remove, no publish)
        mockParticipantStore.Verify(s => s.SortedSetRemoveAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mockEntityRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<ChatTypingStoppedClientEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TypingExpiryWorker_WithMultipleExpiredEntries_ProcessesAll()
    {
        // Arrange
        _configuration.TypingWorkerIntervalMilliseconds = 5000;

        var room1 = Guid.NewGuid();
        var session1 = Guid.NewGuid();
        var room2 = Guid.NewGuid();
        var session2 = Guid.NewGuid();

        var entries = new List<(string, double)>
        {
            ($"{room1:N}:{session1:N}", 900.0),
            ($"{room2:N}:{session2:N}", 950.0),
        };

        var mockParticipantStore = new Mock<ICacheableStateStore<ChatParticipantModel>>();
        mockParticipantStore.Setup(s => s.SortedSetRangeByScoreAsync(
                "typing:active", double.NegativeInfinity, It.IsAny<double>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        _mockStateStoreFactory.Setup(f => f.GetCacheableStore<ChatParticipantModel>(StateStoreDefinitions.ChatParticipants))
            .Returns(mockParticipantStore.Object);

        var mockEntityRegistry = new Mock<IEntitySessionRegistry>();
        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IEntitySessionRegistry)))
            .Returns(mockEntityRegistry.Object);

        using var worker = new TypingExpiryWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<TypingExpiryWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Assert - both entries removed and stop events published
        mockParticipantStore.Verify(s => s.SortedSetRemoveAsync(
            "typing:active",
            $"{room1:N}:{session1:N}",
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        mockParticipantStore.Verify(s => s.SortedSetRemoveAsync(
            "typing:active",
            $"{room2:N}:{session2:N}",
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        mockEntityRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            "chat-room", room1,
            It.Is<ChatTypingStoppedClientEvent>(e => e.RoomId == room1),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        mockEntityRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            "chat-room", room2,
            It.Is<ChatTypingStoppedClientEvent>(e => e.RoomId == room2),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    #endregion

    // ============================================================================
    // Error Handling (shared pattern across all workers)
    // ============================================================================

    #region Error Handling

    [Fact]
    public async Task BanExpiryWorker_WhenProcessingThrows_ContinuesNextCycle()
    {
        // Arrange: lock acquisition throws to simulate transient infrastructure failure
        _configuration.BanExpiryStartupDelaySeconds = 0;
        _configuration.BanExpiryIntervalMinutes = 60;

        _mockLockProvider.Setup(l => l.LockAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable"));

        using var worker = new BanExpiryWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<BanExpiryWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act & Assert - worker should not crash; it logs error and continues
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Verify error event was published
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "chat", "BanExpiryWorker",
            "InvalidOperationException",
            "Redis unavailable",
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            ServiceErrorEventSeverity.Error,
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task TypingExpiryWorker_WhenProcessingThrows_PublishesErrorAndContinues()
    {
        // Arrange: state store factory throws
        _configuration.TypingWorkerIntervalMilliseconds = 5000;

        _mockScopedProvider.Setup(sp => sp.GetService(typeof(IStateStoreFactory)))
            .Throws(new InvalidOperationException("State store unavailable"));

        using var worker = new TypingExpiryWorker(
            _mockServiceProvider.Object,
            new Mock<ILogger<TypingExpiryWorker>>().Object,
            _configuration,
            _telemetry);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act & Assert - should not crash
        await worker.StartAsync(cts.Token);
        await Task.Delay(100);
        await worker.StopAsync(CancellationToken.None);

        // Verify error event published
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "chat", "TypingExpiryWorker",
            "InvalidOperationException",
            "State store unavailable",
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            ServiceErrorEventSeverity.Error,
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    #endregion
}
