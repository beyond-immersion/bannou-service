using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Messaging.Tests;

/// <summary>
/// Unit tests for InMemoryMessageBus.
/// Tests in-memory message bus for testing and minimal infrastructure scenarios.
/// </summary>
public class InMemoryMessageBusTests
{
    private readonly Mock<ILogger<InMemoryMessageBus>> _mockLogger;
    private readonly InMemoryMessageBus _messageBus;

    public InMemoryMessageBusTests()
    {
        _mockLogger = new Mock<ILogger<InMemoryMessageBus>>();
        _messageBus = new InMemoryMessageBus(_mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<InMemoryMessageBus>();
        Assert.NotNull(_messageBus);
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishAsync_WithValidParameters_ReturnsMessageId()
    {
        // Arrange
        var topic = "test.topic";
        var eventData = new TestEvent { Message = "Hello" };

        // Act
        var result = await _messageBus.TryPublishAsync(topic, eventData);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task PublishAsync_DeliversToSubscribers()
    {
        // Arrange
        var topic = "test.topic";
        var receivedEvent = default(TestEvent);
        var eventData = new TestEvent { Message = "Hello" };

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            receivedEvent = evt;
            return Task.CompletedTask;
        });

        // Act
        await _messageBus.TryPublishAsync(topic, eventData);

        // Allow delivery to complete
        await Task.Delay(50);

        // Assert
        Assert.NotNull(receivedEvent);
        Assert.Equal("Hello", receivedEvent.Message);
    }

    [Fact]
    public async Task PublishAsync_DeliversToMultipleSubscribers()
    {
        // Arrange
        var topic = "test.topic";
        var receivedCount = 0;
        var eventData = new TestEvent { Message = "Hello" };

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        });

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        });

        // Act
        await _messageBus.TryPublishAsync(topic, eventData);

        // Allow delivery to complete
        await Task.Delay(50);

        // Assert
        Assert.Equal(2, receivedCount);
    }

    [Fact]
    public async Task TryPublishAsync_NoSubscribers_CompletesWithoutError()
    {
        // Arrange
        var topic = "test.topic";
        var eventData = new TestEvent { Message = "Hello" };

        // Act
        var result = await _messageBus.TryPublishAsync(topic, eventData);

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
        var result = await _messageBus.TryPublishAsync(topic, eventData, options);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task PublishAsync_SubscriberThrows_DoesNotAffectOtherSubscribers()
    {
        // Arrange
        var topic = "test.topic";
        var secondSubscriberCalled = false;
        var eventData = new TestEvent { Message = "Hello" };

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            throw new InvalidOperationException("First subscriber fails");
        });

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            secondSubscriberCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await _messageBus.TryPublishAsync(topic, eventData);

        // Allow delivery to complete
        await Task.Delay(50);

        // Assert
        Assert.True(secondSubscriberCalled);
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
        var result = await _messageBus.TryPublishRawAsync(topic, payload, contentType);

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
        var result = await _messageBus.TryPublishRawAsync(topic, payload, contentType);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region TryPublishErrorAsync Tests

    [Fact]
    public async Task TryPublishErrorAsync_WithValidParameters_ReturnsTrue()
    {
        // Arrange
        var serviceId = "test-service";
        var operation = "TestOperation";
        var errorType = "InvalidOperationException";
        var message = "Something went wrong";

        // Act
        var result = await _messageBus.TryPublishErrorAsync(
            serviceId, operation, errorType, message);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TryPublishErrorAsync_WithAllParameters_ReturnsTrue()
    {
        // Arrange
        var serviceId = "test-service";
        var operation = "TestOperation";
        var errorType = "InvalidOperationException";
        var message = "Something went wrong";
        var dependency = "database";
        var endpoint = "/api/test";
        var severity = ServiceErrorEventSeverity.Warning;
        var details = new { key = "value" };
        var stack = "at Method()";
        var correlationId = "correlation-123";

        // Act
        var result = await _messageBus.TryPublishErrorAsync(
            serviceId, operation, errorType, message,
            dependency, endpoint, severity, details, stack, correlationId);

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

        // Act & Assert - should complete without exception
        var exception = await Record.ExceptionAsync(() => _messageBus.SubscribeAsync(topic, handler));
        Assert.Null(exception);
    }

    [Fact]
    public async Task SubscribeAsync_WithOptions_CompletesSuccessfully()
    {
        // Arrange
        var topic = "test.topic";
        var handler = new Func<TestEvent, CancellationToken, Task>((evt, ct) => Task.CompletedTask);
        var options = new SubscriptionOptions();

        // Act & Assert - should complete without exception
        var exception = await Record.ExceptionAsync(() =>
            _messageBus.SubscribeAsync(topic, handler, exchange: null, options: options));
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
        var disposable = await _messageBus.SubscribeDynamicAsync(topic, handler);

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
        });

        // Act
        await _messageBus.TryPublishAsync(topic, eventData);

        // Allow delivery to complete
        await Task.Delay(50);

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
        });

        // First publish - should receive
        await _messageBus.TryPublishAsync(topic, eventData);
        await Task.Delay(50);

        // Act - Dispose
        await disposable.DisposeAsync();

        // Second publish - should NOT receive
        await _messageBus.TryPublishAsync(topic, eventData);
        await Task.Delay(50);

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
        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) => Task.CompletedTask);

        // Act & Assert - should complete without exception
        var exception = await Record.ExceptionAsync(() => _messageBus.UnsubscribeAsync(topic));
        Assert.Null(exception);
    }

    [Fact]
    public async Task UnsubscribeAsync_RemovesAllHandlers()
    {
        // Arrange
        var topic = "test.topic";
        var receivedCount = 0;
        var eventData = new TestEvent { Message = "Hello" };

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        });

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        });

        // First publish - should receive twice
        await _messageBus.TryPublishAsync(topic, eventData);
        await Task.Delay(50);
        Assert.Equal(2, receivedCount);

        // Act - Unsubscribe
        await _messageBus.UnsubscribeAsync(topic);

        // Second publish - should NOT receive
        await _messageBus.TryPublishAsync(topic, eventData);
        await Task.Delay(50);

        // Assert
        Assert.Equal(2, receivedCount);
    }

    [Fact]
    public async Task UnsubscribeAsync_NonExistentTopic_CompletesWithoutError()
    {
        // Arrange
        var topic = "nonexistent.topic";

        // Act & Assert - should complete without exception
        var exception = await Record.ExceptionAsync(() => _messageBus.UnsubscribeAsync(topic));
        Assert.Null(exception);
    }

    #endregion

    #region Type Filtering Tests

    [Fact]
    public async Task Subscribe_WrongEventType_DoesNotReceiveEvent()
    {
        // Arrange
        var topic = "test.topic";
        var receivedCount = 0;
        var eventData = new TestEvent { Message = "Hello" };

        // Subscribe for OtherEvent but publish TestEvent
        await _messageBus.SubscribeAsync<OtherEvent>(topic, (evt, ct) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        });

        // Act
        await _messageBus.TryPublishAsync(topic, eventData);

        // Allow delivery to complete
        await Task.Delay(50);

        // Assert - Should not receive because types don't match
        Assert.Equal(0, receivedCount);
    }

    [Fact]
    public async Task Subscribe_CorrectEventType_ReceivesEvent()
    {
        // Arrange
        var topic = "test.topic";
        var receivedCount = 0;
        var eventData = new TestEvent { Message = "Hello" };

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        });

        // Act
        await _messageBus.TryPublishAsync(topic, eventData);

        // Allow delivery to complete
        await Task.Delay(50);

        // Assert
        Assert.Equal(1, receivedCount);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentPublish_WithMultipleThreads_AllMessagesDelivered()
    {
        // Arrange
        var topic = "test.topic";
        var receivedCount = 0;
        var publishCount = 100;

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            Interlocked.Increment(ref receivedCount);
            return Task.CompletedTask;
        });

        // Act - Publish from multiple tasks concurrently
        var tasks = Enumerable.Range(0, publishCount)
            .Select(i => _messageBus.TryPublishAsync(topic, new TestEvent { Message = $"Message {i}" }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Allow delivery to complete
        await Task.Delay(200);

        // Assert - All messages should be delivered
        Assert.Equal(publishCount, receivedCount);
    }

    [Fact]
    public async Task ConcurrentSubscribeUnsubscribe_WithMultipleThreads_NoExceptions()
    {
        // Arrange
        var topic = "test.topic";
        var exceptions = new List<Exception>();

        // Act - Subscribe and unsubscribe from multiple tasks concurrently
        var tasks = Enumerable.Range(0, 50)
            .Select(async i =>
            {
                try
                {
                    if (i % 2 == 0)
                    {
                        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) => Task.CompletedTask);
                    }
                    else
                    {
                        await _messageBus.UnsubscribeAsync(topic);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            })
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert - No exceptions should occur
        Assert.Empty(exceptions);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task PublishAsync_WithCancellationToken_PassesToHandler()
    {
        // Arrange
        var topic = "test.topic";
        var receivedCancellationToken = default(CancellationToken);
        var eventData = new TestEvent { Message = "Hello" };
        using var cts = new CancellationTokenSource();

        await _messageBus.SubscribeAsync<TestEvent>(topic, (evt, ct) =>
        {
            receivedCancellationToken = ct;
            return Task.CompletedTask;
        });

        // Act
        await _messageBus.TryPublishAsync(topic, eventData, cancellationToken: cts.Token);

        // Allow delivery to complete
        await Task.Delay(50);

        // Assert - The handler receives the cancellation token
        Assert.Equal(cts.Token, receivedCancellationToken);
    }

    #endregion

    #region Test Event Classes

    private class TestEvent
    {
        public string Message { get; set; } = string.Empty;
    }

    private class OtherEvent
    {
        public int Value { get; set; }
    }

    #endregion
}
