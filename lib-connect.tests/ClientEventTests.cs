using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Connect.ClientEvents;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for client event delivery system components.
/// Tests ClientEventWhitelist and ClientEventRabbitMQSubscriber functionality.
/// </summary>
public class ClientEventTests
{
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

    private const string TestConnectionString = "amqp://guest:guest@localhost:5672/";

    [Fact]
    public void RabbitMQSubscriber_Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ClientEventRabbitMQSubscriber>>();
        Func<string, byte[], Task> handler = (sessionId, payload) => Task.CompletedTask;

        // Act & Assert
        var exception = Record.Exception(() =>
            new ClientEventRabbitMQSubscriber(TestConnectionString, mockLogger.Object, handler));
        Assert.Null(exception);
    }

    [Fact]
    public void RabbitMQSubscriber_Constructor_WithNullConnectionString_ShouldThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ClientEventRabbitMQSubscriber>>();
        Func<string, byte[], Task> handler = (sessionId, payload) => Task.CompletedTask;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ClientEventRabbitMQSubscriber(null!, mockLogger.Object, handler));
    }

    [Fact]
    public void RabbitMQSubscriber_Constructor_WithNullLogger_ShouldThrow()
    {
        // Arrange
        Func<string, byte[], Task> handler = (sessionId, payload) => Task.CompletedTask;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ClientEventRabbitMQSubscriber(TestConnectionString, null!, handler));
    }

    [Fact]
    public void RabbitMQSubscriber_Constructor_WithNullHandler_ShouldThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ClientEventRabbitMQSubscriber>>();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ClientEventRabbitMQSubscriber(TestConnectionString, mockLogger.Object, null!));
    }

    [Fact]
    public void RabbitMQSubscriber_ActiveSubscriptionCount_Initially_ShouldBeZero()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ClientEventRabbitMQSubscriber>>();
        Func<string, byte[], Task> handler = (sessionId, payload) => Task.CompletedTask;
        var subscriber = new ClientEventRabbitMQSubscriber(TestConnectionString, mockLogger.Object, handler);

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
        var subscriber = new ClientEventRabbitMQSubscriber(TestConnectionString, mockLogger.Object, handler);

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
        var subscriber = new ClientEventRabbitMQSubscriber(TestConnectionString, mockLogger.Object, handler);

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
        var subscriber = new ClientEventRabbitMQSubscriber(TestConnectionString, mockLogger.Object, handler);

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
        var subscriber = new ClientEventRabbitMQSubscriber(TestConnectionString, mockLogger.Object, handler);

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
        var subscriber = new ClientEventRabbitMQSubscriber(TestConnectionString, mockLogger.Object, handler);

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
        var subscriber = new ClientEventRabbitMQSubscriber(TestConnectionString, mockLogger.Object, handler);

        // Act & Assert - should dispose cleanly without throwing
        var exception = await Record.ExceptionAsync(async () =>
        {
            await subscriber.DisposeAsync();
        });
        Assert.Null(exception);
    }

    #endregion
}
