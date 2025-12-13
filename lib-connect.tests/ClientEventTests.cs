using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Connect.ClientEvents;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for client event delivery system components.
/// Tests ClientEventQueueManager and ClientEventWhitelist functionality.
/// </summary>
public class ClientEventTests
{
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<ILogger<ClientEventQueueManager>> _mockLogger;

    public ClientEventTests()
    {
        _mockDaprClient = new Mock<DaprClient>();
        _mockLogger = new Mock<ILogger<ClientEventQueueManager>>();
    }

    #region ClientEventQueueManager Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var exception = Record.Exception(() =>
            new ClientEventQueueManager(_mockDaprClient.Object, _mockLogger.Object));
        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WithNullDaprClient_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ClientEventQueueManager(null!, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ClientEventQueueManager(_mockDaprClient.Object, null!));
    }

    [Fact]
    public async Task QueueEventAsync_WithNullSessionId_ShouldReturnFalse()
    {
        // Arrange
        var manager = new ClientEventQueueManager(_mockDaprClient.Object, _mockLogger.Object);
        var payload = new byte[] { 1, 2, 3, 4 };

        // Act
        var result = await manager.QueueEventAsync(null!, payload);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task QueueEventAsync_WithEmptySessionId_ShouldReturnFalse()
    {
        // Arrange
        var manager = new ClientEventQueueManager(_mockDaprClient.Object, _mockLogger.Object);
        var payload = new byte[] { 1, 2, 3, 4 };

        // Act
        var result = await manager.QueueEventAsync("", payload);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task QueueEventAsync_WithValidInput_ShouldSaveToStateStore()
    {
        // Arrange
        var manager = new ClientEventQueueManager(_mockDaprClient.Object, _mockLogger.Object);
        var sessionId = "test-session-123";
        var payload = new byte[] { 1, 2, 3, 4 };

        _mockDaprClient.Setup(x => x.GetStateAsync<List<object>?>(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<object>?)null);

        // Act
        var result = await manager.QueueEventAsync(sessionId, payload);

        // Assert
        Assert.True(result);
        _mockDaprClient.Verify(x => x.SaveStateAsync(
            "connect-statestore",
            $"event-queue:{sessionId}",
            It.IsAny<object>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DequeueEventsAsync_WithNullSessionId_ShouldReturnEmptyList()
    {
        // Arrange
        var manager = new ClientEventQueueManager(_mockDaprClient.Object, _mockLogger.Object);

        // Act
        var result = await manager.DequeueEventsAsync(null!);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task DequeueEventsAsync_WithEmptySessionId_ShouldReturnEmptyList()
    {
        // Arrange
        var manager = new ClientEventQueueManager(_mockDaprClient.Object, _mockLogger.Object);

        // Act
        var result = await manager.DequeueEventsAsync("");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task DequeueEventsAsync_WithNoQueuedEvents_ShouldReturnEmptyList()
    {
        // Arrange
        var manager = new ClientEventQueueManager(_mockDaprClient.Object, _mockLogger.Object);
        var sessionId = "test-session";

        _mockDaprClient.Setup(x => x.GetStateAsync<List<object>?>(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<ConsistencyMode?>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<object>?)null);

        // Act
        var result = await manager.DequeueEventsAsync(sessionId);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetQueuedEventCountAsync_WithNullSessionId_ShouldReturnZero()
    {
        // Arrange
        var manager = new ClientEventQueueManager(_mockDaprClient.Object, _mockLogger.Object);

        // Act
        var result = await manager.GetQueuedEventCountAsync(null!);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task GetQueuedEventCountAsync_WithEmptySessionId_ShouldReturnZero()
    {
        // Arrange
        var manager = new ClientEventQueueManager(_mockDaprClient.Object, _mockLogger.Object);

        // Act
        var result = await manager.GetQueuedEventCountAsync("");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task ClearQueueAsync_WithNullSessionId_ShouldNotThrow()
    {
        // Arrange
        var manager = new ClientEventQueueManager(_mockDaprClient.Object, _mockLogger.Object);

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => manager.ClearQueueAsync(null!));
        Assert.Null(exception);
    }

    [Fact]
    public async Task ClearQueueAsync_WithValidSessionId_ShouldDeleteFromStateStore()
    {
        // Arrange
        var manager = new ClientEventQueueManager(_mockDaprClient.Object, _mockLogger.Object);
        var sessionId = "test-session-456";

        // Act
        await manager.ClearQueueAsync(sessionId);

        // Assert
        _mockDaprClient.Verify(x => x.DeleteStateAsync(
            "connect-statestore",
            $"event-queue:{sessionId}",
            It.IsAny<StateOptions?>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ClientEventWhitelist Tests

    [Fact]
    public void IsValidEventName_WithNullInput_ShouldReturnFalse()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName(null);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEventName_WithEmptyString_ShouldReturnFalse()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName("");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEventName_WithWhitespace_ShouldReturnFalse()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName("   ");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEventName_WithValidConnectEvent_ShouldReturnTrue()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName("connect.capability_manifest");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidEventName_WithValidGameSessionEvent_ShouldReturnTrue()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName("game_session.state_changed");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidEventName_WithValidSystemEvent_ShouldReturnTrue()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName("system.error");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidEventName_WithUnknownEvent_ShouldReturnFalse()
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName("unknown.event.type");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValidEventName_WithCaseVariation_ShouldReturnTrue()
    {
        // The whitelist uses OrdinalIgnoreCase comparison
        // Act
        var result = ClientEventWhitelist.IsValidEventName("CONNECT.CAPABILITY_MANIFEST");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GetAllValidEventNames_ShouldReturnNonEmptySet()
    {
        // Act
        var eventNames = ClientEventWhitelist.GetAllValidEventNames();

        // Assert
        Assert.NotEmpty(eventNames);
        Assert.Contains("connect.capability_manifest", eventNames);
        Assert.Contains("game_session.state_changed", eventNames);
        Assert.Contains("system.error", eventNames);
    }

    [Fact]
    public void Count_ShouldReturnPositiveNumber()
    {
        // Act
        var count = ClientEventWhitelist.Count;

        // Assert
        Assert.True(count > 0);
    }

    [Theory]
    [InlineData("connect.capability_manifest")]
    [InlineData("connect.disconnect_notification")]
    [InlineData("game_session.action_result")]
    [InlineData("game_session.chat_received")]
    [InlineData("game_session.player_joined")]
    [InlineData("game_session.player_kicked")]
    [InlineData("game_session.player_left")]
    [InlineData("game_session.state_changed")]
    [InlineData("game_session.state_updated")]
    [InlineData("system.error")]
    [InlineData("system.notification")]
    public void IsValidEventName_AllRegisteredEvents_ShouldReturnTrue(string eventName)
    {
        // Act
        var result = ClientEventWhitelist.IsValidEventName(eventName);

        // Assert
        Assert.True(result, $"Expected event '{eventName}' to be valid");
    }

    #endregion

    #region ClientEventRabbitMQSubscriber Tests

    [Fact]
    public void RabbitMQSubscriber_Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ClientEventRabbitMQSubscriber>>();
        Func<string, byte[], Task> handler = (sessionId, payload) => Task.CompletedTask;

        // Act & Assert
        var exception = Record.Exception(() =>
            new ClientEventRabbitMQSubscriber(mockLogger.Object, handler));
        Assert.Null(exception);
    }

    [Fact]
    public void RabbitMQSubscriber_Constructor_WithNullLogger_ShouldThrow()
    {
        // Arrange
        Func<string, byte[], Task> handler = (sessionId, payload) => Task.CompletedTask;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ClientEventRabbitMQSubscriber(null!, handler));
    }

    [Fact]
    public void RabbitMQSubscriber_Constructor_WithNullHandler_ShouldThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ClientEventRabbitMQSubscriber>>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ClientEventRabbitMQSubscriber(mockLogger.Object, null!));
    }

    [Fact]
    public void RabbitMQSubscriber_ActiveSubscriptionCount_Initially_ShouldBeZero()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ClientEventRabbitMQSubscriber>>();
        Func<string, byte[], Task> handler = (sessionId, payload) => Task.CompletedTask;
        var subscriber = new ClientEventRabbitMQSubscriber(mockLogger.Object, handler);

        // Act
        var count = subscriber.ActiveSubscriptionCount;

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public void RabbitMQSubscriber_IsSessionSubscribed_WithNonExistentSession_ShouldReturnFalse()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ClientEventRabbitMQSubscriber>>();
        Func<string, byte[], Task> handler = (sessionId, payload) => Task.CompletedTask;
        var subscriber = new ClientEventRabbitMQSubscriber(mockLogger.Object, handler);

        // Act
        var result = subscriber.IsSessionSubscribed("non-existent-session");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RabbitMQSubscriber_SubscribeToSessionAsync_WithoutConnection_ShouldReturnFalse()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ClientEventRabbitMQSubscriber>>();
        Func<string, byte[], Task> handler = (sessionId, payload) => Task.CompletedTask;
        var subscriber = new ClientEventRabbitMQSubscriber(mockLogger.Object, handler);

        // Act - try to subscribe without initializing connection
        var result = await subscriber.SubscribeToSessionAsync("test-session");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RabbitMQSubscriber_SubscribeToSessionAsync_WithNullSessionId_ShouldReturnFalse()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ClientEventRabbitMQSubscriber>>();
        Func<string, byte[], Task> handler = (sessionId, payload) => Task.CompletedTask;
        var subscriber = new ClientEventRabbitMQSubscriber(mockLogger.Object, handler);

        // Act
        var result = await subscriber.SubscribeToSessionAsync(null!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RabbitMQSubscriber_SubscribeToSessionAsync_WithEmptySessionId_ShouldReturnFalse()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ClientEventRabbitMQSubscriber>>();
        Func<string, byte[], Task> handler = (sessionId, payload) => Task.CompletedTask;
        var subscriber = new ClientEventRabbitMQSubscriber(mockLogger.Object, handler);

        // Act
        var result = await subscriber.SubscribeToSessionAsync("");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RabbitMQSubscriber_UnsubscribeFromSessionAsync_WithNonExistentSession_ShouldNotThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ClientEventRabbitMQSubscriber>>();
        Func<string, byte[], Task> handler = (sessionId, payload) => Task.CompletedTask;
        var subscriber = new ClientEventRabbitMQSubscriber(mockLogger.Object, handler);

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() =>
            subscriber.UnsubscribeFromSessionAsync("non-existent-session"));
        Assert.Null(exception);
    }

    [Fact]
    public async Task RabbitMQSubscriber_DisposeAsync_ShouldNotThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ClientEventRabbitMQSubscriber>>();
        Func<string, byte[], Task> handler = (sessionId, payload) => Task.CompletedTask;
        var subscriber = new ClientEventRabbitMQSubscriber(mockLogger.Object, handler);

        // Act & Assert - should dispose cleanly without throwing
        var exception = await Record.ExceptionAsync(async () =>
        {
            await subscriber.DisposeAsync();
        });
        Assert.Null(exception);
    }

    #endregion
}
