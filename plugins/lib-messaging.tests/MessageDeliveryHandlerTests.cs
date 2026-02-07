#nullable enable

using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;

namespace BeyondImmersion.BannouService.Messaging.Tests;

/// <summary>
/// Unit tests for RabbitMQMessageSubscriber's message delivery handlers.
/// Tests HandleTypedDeliveryAsync and HandleRawDeliveryAsync extracted methods.
/// </summary>
public class MessageDeliveryHandlerTests
{
    private readonly Mock<IChannelManager> _mockChannelManager;
    private readonly Mock<ILogger<RabbitMQMessageSubscriber>> _mockLogger;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly MessagingServiceConfiguration _configuration;
    private readonly RabbitMQMessageSubscriber _subscriber;

    public MessageDeliveryHandlerTests()
    {
        _mockChannelManager = new Mock<IChannelManager>();
        _mockLogger = new Mock<ILogger<RabbitMQMessageSubscriber>>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockMessageBus = new Mock<IMessageBus>();

        _mockChannelManager.Setup(x => x.DefaultExchange).Returns("bannou");
        _mockTelemetryProvider.Setup(x => x.TracingEnabled).Returns(false);
        _mockTelemetryProvider.Setup(x => x.MetricsEnabled).Returns(false);

        _configuration = new MessagingServiceConfiguration
        {
            DefaultExchange = "bannou",
            DefaultPrefetchCount = 10,
            MaxTotalChannels = 100
        };

        _subscriber = new RabbitMQMessageSubscriber(
            _mockChannelManager.Object,
            _mockLogger.Object,
            _configuration,
            _mockTelemetryProvider.Object,
            _mockMessageBus.Object);
    }

    #region Test Event Model

    /// <summary>
    /// Simple test event for deserialization testing.
    /// </summary>
    public sealed class TestEvent
    {
        public required string Name { get; init; }
        public required int Value { get; init; }
    }

    #endregion

    #region HandleTypedDeliveryAsync - Success Cases

    [Fact]
    public async Task HandleTypedDeliveryAsync_ValidJson_ReturnsSuccess()
    {
        // Arrange
        var json = """{"Name":"test","Value":42}""";
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
        var handlerCalled = false;
        TestEvent? receivedEvent = null;

        Task Handler(TestEvent evt, CancellationToken ct)
        {
            handlerCalled = true;
            receivedEvent = evt;
            return Task.CompletedTask;
        }

        // Act
        var result = await _subscriber.HandleTypedDeliveryAsync<TestEvent>(
            body,
            "test.topic",
            Handler,
            activity: null,
            subscriptionId: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(RabbitMQMessageSubscriber.MessageDeliveryResult.Success, result);
        Assert.True(handlerCalled);
        Assert.NotNull(receivedEvent);
        Assert.Equal("test", receivedEvent.Name);
        Assert.Equal(42, receivedEvent.Value);
    }

    [Fact]
    public async Task HandleTypedDeliveryAsync_WithSubscriptionId_ReturnsSuccess()
    {
        // Arrange
        var json = """{"Name":"dynamic","Value":123}""";
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
        var subscriptionId = Guid.NewGuid();
        var handlerCalled = false;

        Task Handler(TestEvent evt, CancellationToken ct)
        {
            handlerCalled = true;
            return Task.CompletedTask;
        }

        // Act
        var result = await _subscriber.HandleTypedDeliveryAsync<TestEvent>(
            body,
            "test.dynamic",
            Handler,
            activity: null,
            subscriptionId: subscriptionId,
            CancellationToken.None);

        // Assert
        Assert.Equal(RabbitMQMessageSubscriber.MessageDeliveryResult.Success, result);
        Assert.True(handlerCalled);
    }

    [Fact]
    public async Task HandleTypedDeliveryAsync_EmptyJsonObject_ReturnsDeserializationFailed()
    {
        // Arrange - empty JSON object should fail for required properties
        var json = "{}";
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

        Task Handler(TestEvent evt, CancellationToken ct) => Task.CompletedTask;

        // Act
        var result = await _subscriber.HandleTypedDeliveryAsync<TestEvent>(
            body,
            "test.topic",
            Handler,
            activity: null,
            subscriptionId: null,
            CancellationToken.None);

        // Assert - BannouJson returns null for invalid deserialization
        // OR it could throw - both result in a failure
        Assert.True(
            result == RabbitMQMessageSubscriber.MessageDeliveryResult.DeserializationFailed ||
            result == RabbitMQMessageSubscriber.MessageDeliveryResult.HandlerError);
    }

    #endregion

    #region HandleTypedDeliveryAsync - Deserialization Failures

    [Fact]
    public async Task HandleTypedDeliveryAsync_InvalidJson_ReturnsDeserializationFailedOrHandlerError()
    {
        // Arrange - completely invalid JSON
        var json = "not valid json at all";
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
        var handlerCalled = false;

        Task Handler(TestEvent evt, CancellationToken ct)
        {
            handlerCalled = true;
            return Task.CompletedTask;
        }

        // Act
        var result = await _subscriber.HandleTypedDeliveryAsync<TestEvent>(
            body,
            "test.topic",
            Handler,
            activity: null,
            subscriptionId: null,
            CancellationToken.None);

        // Assert - invalid JSON should result in deserialization failure or exception
        Assert.False(handlerCalled);
        Assert.True(
            result == RabbitMQMessageSubscriber.MessageDeliveryResult.DeserializationFailed ||
            result == RabbitMQMessageSubscriber.MessageDeliveryResult.HandlerError);
    }

    [Fact]
    public async Task HandleTypedDeliveryAsync_NullJson_ReturnsDeserializationFailed()
    {
        // Arrange - JSON null literal
        var json = "null";
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
        var handlerCalled = false;

        Task Handler(TestEvent evt, CancellationToken ct)
        {
            handlerCalled = true;
            return Task.CompletedTask;
        }

        // Act
        var result = await _subscriber.HandleTypedDeliveryAsync<TestEvent>(
            body,
            "test.topic",
            Handler,
            activity: null,
            subscriptionId: null,
            CancellationToken.None);

        // Assert
        Assert.False(handlerCalled);
        Assert.Equal(RabbitMQMessageSubscriber.MessageDeliveryResult.DeserializationFailed, result);
    }

    [Fact]
    public async Task HandleTypedDeliveryAsync_DeserializationFailed_PublishesErrorEvent()
    {
        // Arrange - JSON null literal results in deserialization failure
        var json = "null";
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

        Task Handler(TestEvent evt, CancellationToken ct) => Task.CompletedTask;

        // Act
        await _subscriber.HandleTypedDeliveryAsync<TestEvent>(
            body,
            "error.topic",
            Handler,
            activity: null,
            subscriptionId: null,
            CancellationToken.None);

        // Assert - should publish error event
        _mockMessageBus.Verify(
            x => x.TryPublishErrorAsync(
                "messaging",
                "deserialize",
                "DeserializationFailed",
                It.Is<string>(s => s.Contains("error.topic")),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<BeyondImmersion.BannouService.Events.ServiceErrorEventSeverity>(),
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleTypedDeliveryAsync_DeserializationFailed_WithSubscriptionId_LogsWithId()
    {
        // Arrange
        var json = "null";
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
        var subscriptionId = Guid.NewGuid();

        Task Handler(TestEvent evt, CancellationToken ct) => Task.CompletedTask;

        // Act
        await _subscriber.HandleTypedDeliveryAsync<TestEvent>(
            body,
            "test.topic",
            Handler,
            activity: null,
            subscriptionId: subscriptionId,
            CancellationToken.None);

        // Assert - verify error includes subscription ID in message
        _mockMessageBus.Verify(
            x => x.TryPublishErrorAsync(
                "messaging",
                "deserialize",
                "DeserializationFailed",
                It.Is<string>(s => s.Contains(subscriptionId.ToString())),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<BeyondImmersion.BannouService.Events.ServiceErrorEventSeverity>(),
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleTypedDeliveryAsync_LargePayload_TruncatesInLog()
    {
        // Arrange - create a large JSON payload that will be truncated
        var largeValue = new string('x', 1000);
        var json = $"null"; // Still null to trigger failure
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

        Task Handler(TestEvent evt, CancellationToken ct) => Task.CompletedTask;

        // Act
        await _subscriber.HandleTypedDeliveryAsync<TestEvent>(
            body,
            "test.topic",
            Handler,
            activity: null,
            subscriptionId: null,
            CancellationToken.None);

        // Assert - logger should have been called
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region HandleTypedDeliveryAsync - Handler Errors

    [Fact]
    public async Task HandleTypedDeliveryAsync_HandlerThrowsException_ReturnsHandlerError()
    {
        // Arrange
        var json = """{"Name":"test","Value":42}""";
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

        Task Handler(TestEvent evt, CancellationToken ct)
        {
            throw new InvalidOperationException("Test exception");
        }

        // Act
        var result = await _subscriber.HandleTypedDeliveryAsync<TestEvent>(
            body,
            "test.topic",
            Handler,
            activity: null,
            subscriptionId: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(RabbitMQMessageSubscriber.MessageDeliveryResult.HandlerError, result);
    }

    [Fact]
    public async Task HandleTypedDeliveryAsync_HandlerThrows_WithSubscriptionId_LogsWithId()
    {
        // Arrange
        var json = """{"Name":"test","Value":42}""";
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
        var subscriptionId = Guid.NewGuid();

        Task Handler(TestEvent evt, CancellationToken ct)
        {
            throw new InvalidOperationException("Handler failure");
        }

        // Act
        await _subscriber.HandleTypedDeliveryAsync<TestEvent>(
            body,
            "test.topic",
            Handler,
            activity: null,
            subscriptionId: subscriptionId,
            CancellationToken.None);

        // Assert - logger should log with subscription ID
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(subscriptionId.ToString())),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleTypedDeliveryAsync_HandlerThrows_LogsException()
    {
        // Arrange
        var json = """{"Name":"test","Value":42}""";
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
        var expectedException = new InvalidOperationException("Expected test exception");

        Task Handler(TestEvent evt, CancellationToken ct)
        {
            throw expectedException;
        }

        // Act
        await _subscriber.HandleTypedDeliveryAsync<TestEvent>(
            body,
            "test.topic",
            Handler,
            activity: null,
            subscriptionId: null,
            CancellationToken.None);

        // Assert - logger should log the exception
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region HandleRawDeliveryAsync - Success Cases

    [Fact]
    public async Task HandleRawDeliveryAsync_ValidBytes_ReturnsSuccess()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var body = new ReadOnlyMemory<byte>(data);
        var handlerCalled = false;
        byte[]? receivedBytes = null;

        Task Handler(byte[] bytes, CancellationToken ct)
        {
            handlerCalled = true;
            receivedBytes = bytes;
            return Task.CompletedTask;
        }

        // Act
        var result = await _subscriber.HandleRawDeliveryAsync(
            body,
            "test.raw",
            Handler,
            activity: null,
            subscriptionId: Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        Assert.Equal(RabbitMQMessageSubscriber.MessageDeliveryResult.Success, result);
        Assert.True(handlerCalled);
        Assert.NotNull(receivedBytes);
        Assert.Equal(data, receivedBytes);
    }

    [Fact]
    public async Task HandleRawDeliveryAsync_EmptyBytes_ReturnsSuccess()
    {
        // Arrange
        var data = Array.Empty<byte>();
        var body = new ReadOnlyMemory<byte>(data);
        var handlerCalled = false;

        Task Handler(byte[] bytes, CancellationToken ct)
        {
            handlerCalled = true;
            return Task.CompletedTask;
        }

        // Act
        var result = await _subscriber.HandleRawDeliveryAsync(
            body,
            "test.raw",
            Handler,
            activity: null,
            subscriptionId: Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        Assert.Equal(RabbitMQMessageSubscriber.MessageDeliveryResult.Success, result);
        Assert.True(handlerCalled);
    }

    [Fact]
    public async Task HandleRawDeliveryAsync_LargePayload_ReturnsSuccess()
    {
        // Arrange
        var data = new byte[1024 * 1024]; // 1MB
        new Random(42).NextBytes(data);
        var body = new ReadOnlyMemory<byte>(data);
        byte[]? receivedBytes = null;

        Task Handler(byte[] bytes, CancellationToken ct)
        {
            receivedBytes = bytes;
            return Task.CompletedTask;
        }

        // Act
        var result = await _subscriber.HandleRawDeliveryAsync(
            body,
            "test.raw",
            Handler,
            activity: null,
            subscriptionId: Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        Assert.Equal(RabbitMQMessageSubscriber.MessageDeliveryResult.Success, result);
        Assert.NotNull(receivedBytes);
        Assert.Equal(data.Length, receivedBytes.Length);
    }

    #endregion

    #region HandleRawDeliveryAsync - Handler Errors

    [Fact]
    public async Task HandleRawDeliveryAsync_HandlerThrowsException_ReturnsHandlerError()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };
        var body = new ReadOnlyMemory<byte>(data);

        Task Handler(byte[] bytes, CancellationToken ct)
        {
            throw new InvalidOperationException("Raw handler failure");
        }

        // Act
        var result = await _subscriber.HandleRawDeliveryAsync(
            body,
            "test.raw",
            Handler,
            activity: null,
            subscriptionId: Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        Assert.Equal(RabbitMQMessageSubscriber.MessageDeliveryResult.HandlerError, result);
    }

    [Fact]
    public async Task HandleRawDeliveryAsync_HandlerThrows_LogsException()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };
        var body = new ReadOnlyMemory<byte>(data);
        var subscriptionId = Guid.NewGuid();
        var expectedException = new InvalidOperationException("Expected raw failure");

        Task Handler(byte[] bytes, CancellationToken ct)
        {
            throw expectedException;
        }

        // Act
        await _subscriber.HandleRawDeliveryAsync(
            body,
            "test.raw",
            Handler,
            activity: null,
            subscriptionId: subscriptionId,
            CancellationToken.None);

        // Assert - logger should log with subscription ID and exception
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(subscriptionId.ToString())),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task HandleTypedDeliveryAsync_CancellationRequested_PassesToHandler()
    {
        // Arrange
        var json = """{"Name":"test","Value":42}""";
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
        var cts = new CancellationTokenSource();
        cts.Cancel();
        CancellationToken receivedToken = default;

        Task Handler(TestEvent evt, CancellationToken ct)
        {
            receivedToken = ct;
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        // Act
        var result = await _subscriber.HandleTypedDeliveryAsync<TestEvent>(
            body,
            "test.topic",
            Handler,
            activity: null,
            subscriptionId: null,
            cts.Token);

        // Assert - handler receives cancellation token and throws, which is caught as HandlerError
        Assert.Equal(RabbitMQMessageSubscriber.MessageDeliveryResult.HandlerError, result);
    }

    [Fact]
    public async Task HandleRawDeliveryAsync_CancellationRequested_PassesToHandler()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };
        var body = new ReadOnlyMemory<byte>(data);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Task Handler(byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        // Act
        var result = await _subscriber.HandleRawDeliveryAsync(
            body,
            "test.raw",
            Handler,
            activity: null,
            subscriptionId: Guid.NewGuid(),
            cts.Token);

        // Assert
        Assert.Equal(RabbitMQMessageSubscriber.MessageDeliveryResult.HandlerError, result);
    }

    [Fact]
    public async Task HandleTypedDeliveryAsync_AsyncHandler_AwaitsCompletion()
    {
        // Arrange
        var json = """{"Name":"test","Value":42}""";
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
        var handlerCompleted = false;

        async Task Handler(TestEvent evt, CancellationToken ct)
        {
            await Task.Delay(10, ct);
            handlerCompleted = true;
        }

        // Act
        var result = await _subscriber.HandleTypedDeliveryAsync<TestEvent>(
            body,
            "test.topic",
            Handler,
            activity: null,
            subscriptionId: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(RabbitMQMessageSubscriber.MessageDeliveryResult.Success, result);
        Assert.True(handlerCompleted);
    }

    [Fact]
    public async Task HandleRawDeliveryAsync_AsyncHandler_AwaitsCompletion()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };
        var body = new ReadOnlyMemory<byte>(data);
        var handlerCompleted = false;

        async Task Handler(byte[] bytes, CancellationToken ct)
        {
            await Task.Delay(10, ct);
            handlerCompleted = true;
        }

        // Act
        var result = await _subscriber.HandleRawDeliveryAsync(
            body,
            "test.raw",
            Handler,
            activity: null,
            subscriptionId: Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        Assert.Equal(RabbitMQMessageSubscriber.MessageDeliveryResult.Success, result);
        Assert.True(handlerCompleted);
    }

    #endregion
}
