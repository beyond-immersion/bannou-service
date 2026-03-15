using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Messaging.Tests;

/// <summary>
/// Unit tests for DirectDispatchMessageBus.
/// Tests zero-overhead event delivery for embedded and sidecar deployments.
/// </summary>
public class DirectDispatchMessageBusTests
{
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceProvider> _mockScopeServiceProvider;
    private readonly Mock<ILogger<DirectDispatchMessageBus>> _mockLogger;
    private readonly MessagingServiceConfiguration _configuration;
    private readonly DirectDispatchMessageBus _messageBus;

    public DirectDispatchMessageBusTests()
    {
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockScope = new Mock<IServiceScope>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScopeServiceProvider = new Mock<IServiceProvider>();
        _mockLogger = new Mock<ILogger<DirectDispatchMessageBus>>();
        _configuration = new MessagingServiceConfiguration();

        // Wire up scope creation
        _mockScope.Setup(s => s.ServiceProvider).Returns(_mockScopeServiceProvider.Object);
        _mockScopeFactory.Setup(f => f.CreateScope()).Returns(_mockScope.Object);
        _mockServiceProvider
            .Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(_mockScopeFactory.Object);

        _messageBus = new DirectDispatchMessageBus(
            _mockEventConsumer.Object,
            _mockServiceProvider.Object,
            _mockLogger.Object,
            _configuration);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        Assert.NotNull(_messageBus);
    }

    #endregion

    #region TryPublishAsync Tests

    [Fact]
    public async Task TryPublishAsync_WithValidParameters_ReturnsTrue()
    {
        // Arrange
        var topic = "test.topic";
        var eventData = new TestEvent { Message = "Hello" };

        // Act
        var result = await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TryPublishAsync_DispatchesToEventConsumer()
    {
        // Arrange
        var topic = "test.topic";
        var eventData = new TestEvent { Message = "Hello" };
        string? capturedTopic = null;
        object? capturedEvent = null;

        _mockEventConsumer
            .Setup(ec => ec.DispatchAsync(
                It.IsAny<string>(), It.IsAny<TestEvent>(), It.IsAny<IServiceProvider>()))
            .Callback<string, object, IServiceProvider>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .Returns(Task.CompletedTask);

        // Act
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: TestContext.Current.CancellationToken);

        // Allow fire-and-forget to complete
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("test.topic", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<TestEvent>(capturedEvent);
        Assert.Equal("Hello", typedEvent.Message);
    }

    [Fact]
    public async Task TryPublishAsync_CreatesNewScope()
    {
        // Arrange
        var topic = "test.topic";
        var eventData = new TestEvent { Message = "Hello" };

        _mockEventConsumer
            .Setup(ec => ec.DispatchAsync(
                It.IsAny<string>(), It.IsAny<TestEvent>(), It.IsAny<IServiceProvider>()))
            .Returns(Task.CompletedTask);

        // Act
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: TestContext.Current.CancellationToken);

        // Allow fire-and-forget to complete
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — scope was created for IEventConsumer dispatch
        _mockScopeFactory.Verify(f => f.CreateScope(), Times.Once);
    }

    [Fact]
    public async Task TryPublishAsync_NoSubscribers_CompletesWithoutError()
    {
        // Arrange
        var topic = "test.topic";
        var eventData = new TestEvent { Message = "Hello" };

        // Act
        var result = await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TryPublishAsync_WithOptions_CompletesSuccessfully()
    {
        // Arrange
        var topic = "test.topic";
        var eventData = new TestEvent { Message = "Hello" };
        var options = new PublishOptions
        {
            Exchange = "custom-exchange",
            Persistent = true
        };

        // Act
        var result = await _messageBus.TryPublishAsync(topic, eventData, options, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TryPublishAsync_DeliversToDirectSubscribers()
    {
        // Arrange
        var topic = "test.topic";
        var receivedEvent = default(TestEvent);
        var eventData = new TestEvent { Message = "DirectSub" };

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            receivedEvent = evt;
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: TestContext.Current.CancellationToken);

        // Allow fire-and-forget to complete
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(receivedEvent);
        Assert.Equal("DirectSub", receivedEvent.Message);
    }

    [Fact]
    public async Task TryPublishAsync_DeliversToMultipleDirectSubscribers()
    {
        // Arrange
        var topic = "test.topic";
        var receivedCount = 0;
        var eventData = new TestEvent { Message = "Hello" };

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: TestContext.Current.CancellationToken);

        // Allow fire-and-forget to complete
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, receivedCount);
    }

    [Fact]
    public async Task TryPublishAsync_SubscriberThrows_DoesNotAffectOtherSubscribers()
    {
        // Arrange
        var topic = "test.topic";
        var secondSubscriberCalled = false;
        var eventData = new TestEvent { Message = "Hello" };

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            throw new InvalidOperationException("First subscriber fails");
        }, cancellationToken: TestContext.Current.CancellationToken);

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            secondSubscriberCalled = true;
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: TestContext.Current.CancellationToken);

        // Allow fire-and-forget to complete
        await Task.Delay(100, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(secondSubscriberCalled);
    }

    [Fact]
    public async Task TryPublishAsync_EventConsumerThrows_DoesNotCrash()
    {
        // Arrange
        var topic = "test.topic";
        var eventData = new TestEvent { Message = "Hello" };

        _mockEventConsumer
            .Setup(ec => ec.DispatchAsync(
                It.IsAny<string>(), It.IsAny<TestEvent>(), It.IsAny<IServiceProvider>()))
            .ThrowsAsync(new InvalidOperationException("EventConsumer failure"));

        // Act — should not throw (fire-and-forget catches internally)
        var result = await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: TestContext.Current.CancellationToken);

        // Allow fire-and-forget to complete
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region SkipUnhandledTopics Tests

    [Fact]
    public async Task TryPublishAsync_SkipUnhandledTopics_SkipsScopeWhenNoHandlers()
    {
        // Arrange
        var config = new MessagingServiceConfiguration { SkipUnhandledTopics = true };
        var bus = new DirectDispatchMessageBus(
            _mockEventConsumer.Object,
            _mockServiceProvider.Object,
            _mockLogger.Object,
            config);

        _mockEventConsumer.Setup(ec => ec.GetHandlerCount("test.topic")).Returns(0);

        // Act
        await bus.TryPublishAsync("test.topic", new TestEvent { Message = "Hello" }, cancellationToken: TestContext.Current.CancellationToken);

        // Allow fire-and-forget to complete
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — no scope created, no dispatch
        _mockScopeFactory.Verify(f => f.CreateScope(), Times.Never);
        _mockEventConsumer.Verify(
            ec => ec.DispatchAsync(It.IsAny<string>(), It.IsAny<TestEvent>(), It.IsAny<IServiceProvider>()),
            Times.Never);
    }

    [Fact]
    public async Task TryPublishAsync_SkipUnhandledTopics_DispatchesWhenHandlersExist()
    {
        // Arrange
        var config = new MessagingServiceConfiguration { SkipUnhandledTopics = true };
        var bus = new DirectDispatchMessageBus(
            _mockEventConsumer.Object,
            _mockServiceProvider.Object,
            _mockLogger.Object,
            config);

        _mockEventConsumer.Setup(ec => ec.GetHandlerCount("test.topic")).Returns(1);
        _mockEventConsumer
            .Setup(ec => ec.DispatchAsync(
                It.IsAny<string>(), It.IsAny<TestEvent>(), It.IsAny<IServiceProvider>()))
            .Returns(Task.CompletedTask);

        // Act
        await bus.TryPublishAsync("test.topic", new TestEvent { Message = "Hello" }, cancellationToken: TestContext.Current.CancellationToken);

        // Allow fire-and-forget to complete
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — dispatch occurred
        _mockEventConsumer.Verify(
            ec => ec.DispatchAsync("test.topic", It.IsAny<TestEvent>(), It.IsAny<IServiceProvider>()),
            Times.Once);
    }

    [Fact]
    public async Task TryPublishAsync_DefaultConfig_AlwaysDispatches()
    {
        // Arrange — default config has SkipUnhandledTopics=false
        _mockEventConsumer.Setup(ec => ec.GetHandlerCount("test.topic")).Returns(0);
        _mockEventConsumer
            .Setup(ec => ec.DispatchAsync(
                It.IsAny<string>(), It.IsAny<TestEvent>(), It.IsAny<IServiceProvider>()))
            .Returns(Task.CompletedTask);

        // Act
        await _messageBus.TryPublishAsync("test.topic", new TestEvent { Message = "Hello" }, cancellationToken: TestContext.Current.CancellationToken);

        // Allow fire-and-forget to complete
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — dispatch occurred even with 0 handlers (default behavior)
        _mockEventConsumer.Verify(
            ec => ec.DispatchAsync("test.topic", It.IsAny<TestEvent>(), It.IsAny<IServiceProvider>()),
            Times.Once);
    }

    #endregion

    #region TryPublishRawAsync Tests

    [Fact]
    public async Task TryPublishRawAsync_WithValidParameters_ReturnsTrue()
    {
        // Arrange
        var topic = "test.topic";
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var contentType = "application/octet-stream";

        // Act
        var result = await _messageBus.TryPublishRawAsync(topic, payload, contentType, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TryPublishRawAsync_WithEmptyPayload_CompletesSuccessfully()
    {
        // Arrange
        var topic = "test.topic";
        var payload = ReadOnlyMemory<byte>.Empty;
        var contentType = "application/octet-stream";

        // Act
        var result = await _messageBus.TryPublishRawAsync(topic, payload, contentType, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region TryPublishErrorAsync Tests

    [Fact]
    public async Task TryPublishErrorAsync_WithValidParameters_ReturnsTrue()
    {
        // Arrange & Act
        var result = await _messageBus.TryPublishErrorAsync(
            "test-service", "TestOperation", "InvalidOperationException", "Something went wrong",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TryPublishErrorAsync_WithAllParameters_ReturnsTrue()
    {
        // Act
        var result = await _messageBus.TryPublishErrorAsync(
            "test-service", "TestOperation", "InvalidOperationException", "Something went wrong",
            "database", "/api/test", ServiceErrorEventSeverity.Warning,
            new { key = "value" }, "at Method()", Guid.NewGuid(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region SubscribeAsync Tests

    [Fact]
    public async Task SubscribeAsync_WithValidParameters_CompletesSuccessfully()
    {
        // Arrange
        var topic = "test.topic";
        var handler = new Func<TestEvent, CancellationToken, Task>((evt, ct) => Task.CompletedTask);

        // Act & Assert — should complete without exception
        var exception = await Record.ExceptionAsync(() => _messageBus.SubscribeAsync(topic, handler, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }

    #endregion

    #region SubscribeDynamicAsync Tests

    [Fact]
    public async Task SubscribeDynamicAsync_WithValidParameters_ReturnsDisposable()
    {
        // Arrange
        var topic = "test.topic";
        var handler = new Func<TestEvent, CancellationToken, Task>((evt, ct) => Task.CompletedTask);

        // Act
        var disposable = await _messageBus.SubscribeDynamicAsync(topic, handler, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(disposable);
    }

    [Fact]
    public async Task SubscribeDynamicAsync_ReceivesPublishedEvents()
    {
        // Arrange
        var topic = "test.topic";
        var receivedEvent = default(TestEvent);
        var eventData = new TestEvent { Message = "Hello" };

        var disposable = await _messageBus.SubscribeDynamicAsync<TestEvent>(topic, (evt, ct) =>
        {
            receivedEvent = evt;
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: TestContext.Current.CancellationToken);

        // Allow fire-and-forget to complete
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(receivedEvent);
        Assert.Equal("Hello", receivedEvent.Message);

        // Cleanup
        await disposable.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeDynamicAsync_AfterDispose_NoLongerReceivesEvents()
    {
        // Arrange
        var topic = "test.topic";
        var receivedCount = 0;
        var eventData = new TestEvent { Message = "Hello" };

        var disposable = await _messageBus.SubscribeDynamicAsync<TestEvent>(topic, (evt, ct) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        // First publish — should receive
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Act — Dispose
        await disposable.DisposeAsync();

        // Second publish — should NOT receive
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, receivedCount);
    }

    #endregion

    #region SubscribeDynamicRawAsync Tests

    [Fact]
    public async Task SubscribeDynamicRawAsync_WithValidParameters_ReturnsDisposable()
    {
        // Arrange
        var topic = "test.topic";
        var handler = new Func<byte[], CancellationToken, Task>((bytes, ct) => Task.CompletedTask);

        // Act
        var disposable = await _messageBus.SubscribeDynamicRawAsync(topic, handler, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(disposable);
        await disposable.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeDynamicRawAsync_ReceivesSerializedBytes()
    {
        // Arrange
        var topic = "test.topic";
        byte[]? receivedBytes = null;
        var receivedEvent = new TaskCompletionSource<bool>();

        var disposable = await _messageBus.SubscribeDynamicRawAsync(topic, (bytes, ct) =>
        {
            receivedBytes = bytes;
            receivedEvent.TrySetResult(true);
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        var eventData = new TestEvent { Message = "RawTest" };

        // Act
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: TestContext.Current.CancellationToken);
        await Task.WhenAny(receivedEvent.Task, Task.Delay(1000, cancellationToken: TestContext.Current.CancellationToken));

        // Assert — should receive bytes containing the serialized message
        Assert.NotNull(receivedBytes);
        Assert.True(receivedBytes.Length > 0);
        var json = System.Text.Encoding.UTF8.GetString(receivedBytes);
        Assert.Contains("RawTest", json);

        // Cleanup
        await disposable.DisposeAsync();
    }

    [Fact]
    public async Task SubscribeDynamicRawAsync_AfterDispose_NoLongerReceives()
    {
        // Arrange
        var topic = "test.topic";
        var receivedCount = 0;

        var disposable = await _messageBus.SubscribeDynamicRawAsync(topic, (bytes, ct) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        // First publish — should receive
        await _messageBus.TryPublishAsync(topic, new TestEvent { Message = "First" }, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, receivedCount);

        // Act — Dispose
        await disposable.DisposeAsync();

        // Second publish — should NOT receive
        await _messageBus.TryPublishAsync(topic, new TestEvent { Message = "Second" }, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, receivedCount);
    }

    #endregion

    #region UnsubscribeAsync Tests

    [Fact]
    public async Task UnsubscribeAsync_WithValidTopic_CompletesSuccessfully()
    {
        // Arrange
        var topic = "test.topic";
        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) => Task.CompletedTask, cancellationToken: TestContext.Current.CancellationToken);

        // Act & Assert
        var exception = await Record.ExceptionAsync(() => _messageBus.UnsubscribeAsync(topic));
        Assert.Null(exception);
    }

    [Fact]
    public async Task UnsubscribeAsync_RemovesAllHandlers()
    {
        // Arrange
        var topic = "test.topic";
        var receivedCount = 0;

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        // First publish — should receive
        await _messageBus.TryPublishAsync(topic, new TestEvent { Message = "Hello" }, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, receivedCount);

        // Act — Unsubscribe
        await _messageBus.UnsubscribeAsync(topic);

        // Second publish — should NOT receive
        await _messageBus.TryPublishAsync(topic, new TestEvent { Message = "Hello" }, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, receivedCount);
    }

    [Fact]
    public async Task UnsubscribeAsync_NonExistentTopic_CompletesWithoutError()
    {
        // Act & Assert
        var exception = await Record.ExceptionAsync(() => _messageBus.UnsubscribeAsync("nonexistent.topic"));
        Assert.Null(exception);
    }

    #endregion

    #region Multiple Topic Tests

    [Fact]
    public async Task Subscribe_MultipleDifferentTopics_OnlyReceivesMatchingTopic()
    {
        // Arrange
        var topic1 = "test.topic1";
        var topic2 = "test.topic2";
        var receivedOnTopic1 = 0;
        var receivedOnTopic2 = 0;

        await _messageBus.SubscribeAsync<TestEvent>(topic1, (evt, ct) =>
        {
            Interlocked.Increment(ref receivedOnTopic1);
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        await _messageBus.SubscribeAsync<TestEvent>(topic2, (evt, ct) =>
        {
            Interlocked.Increment(ref receivedOnTopic2);
            return Task.CompletedTask;
        }, cancellationToken: TestContext.Current.CancellationToken);

        // Act — Publish only to topic1
        await _messageBus.TryPublishAsync(topic1, new TestEvent { Message = "OnlyTopic1" }, cancellationToken: TestContext.Current.CancellationToken);
        await Task.Delay(50, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, receivedOnTopic1);
        Assert.Equal(0, receivedOnTopic2);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentPublish_WithMultipleThreads_AllComplete()
    {
        // Arrange
        var topic = "test.topic";
        var publishCount = 100;

        // Act — Publish from multiple tasks concurrently
        var tasks = Enumerable.Range(0, publishCount)
            .Select(i => _messageBus.TryPublishAsync(topic, new TestEvent { Message = $"Message {i}" }))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert — All publishes should succeed
        Assert.All(results, result => Assert.True(result));
    }

    #endregion

    #region Interface Implementation Tests

    [Fact]
    public void DirectDispatchMessageBus_ImplementsIMessageBus()
    {
        Assert.True(typeof(IMessageBus).IsAssignableFrom(typeof(DirectDispatchMessageBus)));
    }

    [Fact]
    public void DirectDispatchMessageBus_ImplementsIMessageSubscriber()
    {
        Assert.True(typeof(IMessageSubscriber).IsAssignableFrom(typeof(DirectDispatchMessageBus)));
    }

    [Fact]
    public void DirectDispatchMessageBus_IsSealed()
    {
        Assert.True(typeof(DirectDispatchMessageBus).IsSealed,
            "DirectDispatchMessageBus should be sealed to prevent inheritance.");
    }

    #endregion

    #region Test Event Classes

    private class TestEvent
    {
        public string Message { get; set; } = string.Empty;
    }

    #endregion
}
