using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Messaging.Tests;

/// <summary>
/// Unit tests for NativeEventConsumerBackend.
/// Tests the hosted service that bridges RabbitMQ subscriptions to IEventConsumer fan-out.
/// </summary>
public class NativeEventConsumerBackendTests
{
    private readonly Mock<IMessageSubscriber> _mockSubscriber;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<ILogger<NativeEventConsumerBackend>> _mockLogger;

    // Use unique topic names per test to avoid static registry collisions
    private readonly string _testId = Guid.NewGuid().ToString("N")[..8];

    public NativeEventConsumerBackendTests()
    {
        _mockSubscriber = new Mock<IMessageSubscriber>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockLogger = new Mock<ILogger<NativeEventConsumerBackend>>();
    }

    private string UniqueTopic(string baseName) => $"{baseName}.{_testId}";

    private NativeEventConsumerBackend CreateBackend()
    {
        return new NativeEventConsumerBackend(
            _mockSubscriber.Object,
            _mockEventConsumer.Object,
            _mockServiceProvider.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var backend = CreateBackend();

        // Assert
        Assert.NotNull(backend);
    }

    [Fact]
    public void Constructor_WithNullSubscriber_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new NativeEventConsumerBackend(
            null!,
            _mockEventConsumer.Object,
            _mockServiceProvider.Object,
            _mockLogger.Object));
        Assert.Equal("subscriber", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullEventConsumer_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new NativeEventConsumerBackend(
            _mockSubscriber.Object,
            null!,
            _mockServiceProvider.Object,
            _mockLogger.Object));
        Assert.Equal("eventConsumer", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullServiceProvider_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new NativeEventConsumerBackend(
            _mockSubscriber.Object,
            _mockEventConsumer.Object,
            null!,
            _mockLogger.Object));
        Assert.Equal("serviceProvider", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new NativeEventConsumerBackend(
            _mockSubscriber.Object,
            _mockEventConsumer.Object,
            _mockServiceProvider.Object,
            null!));
        Assert.Equal("logger", ex.ParamName);
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_WithNoRegisteredTopics_ShouldCompleteSuccessfully()
    {
        // Arrange
        _mockEventConsumer.Setup(x => x.GetRegisteredTopics())
            .Returns(Enumerable.Empty<string>());

        var backend = CreateBackend();

        // Act
        await backend.StartAsync(CancellationToken.None);

        // Assert
        _mockSubscriber.Verify(
            x => x.SubscribeDynamicAsync<It.IsAnyType>(
                It.IsAny<string>(),
                It.IsAny<Func<It.IsAnyType, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StartAsync_WithRegisteredTopicAndEventType_ShouldSubscribe()
    {
        // Arrange
        var topic = UniqueTopic("test.topic");
        EventSubscriptionRegistry.Register<TestEvent>(topic);

        _mockEventConsumer.Setup(x => x.GetRegisteredTopics())
            .Returns(new[] { topic });

        var mockSubscription = new Mock<IAsyncDisposable>();
        _mockSubscriber.Setup(x => x.SubscribeDynamicAsync<TestEvent>(
                topic,
                It.IsAny<Func<TestEvent, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSubscription.Object);

        var backend = CreateBackend();

        // Act
        await backend.StartAsync(CancellationToken.None);

        // Assert
        _mockSubscriber.Verify(
            x => x.SubscribeDynamicAsync<TestEvent>(
                topic,
                It.IsAny<Func<TestEvent, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_WithMultipleTopics_ShouldSubscribeToAll()
    {
        // Arrange
        var topic1 = UniqueTopic("test.topic1");
        var topic2 = UniqueTopic("test.topic2");
        EventSubscriptionRegistry.Register<TestEvent>(topic1);
        EventSubscriptionRegistry.Register<TestEvent2>(topic2);

        _mockEventConsumer.Setup(x => x.GetRegisteredTopics())
            .Returns(new[] { topic1, topic2 });

        var mockSubscription = new Mock<IAsyncDisposable>();
        _mockSubscriber.Setup(x => x.SubscribeDynamicAsync<TestEvent>(
                topic1,
                It.IsAny<Func<TestEvent, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSubscription.Object);
        _mockSubscriber.Setup(x => x.SubscribeDynamicAsync<TestEvent2>(
                topic2,
                It.IsAny<Func<TestEvent2, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSubscription.Object);

        var backend = CreateBackend();

        // Act
        await backend.StartAsync(CancellationToken.None);

        // Assert
        _mockSubscriber.Verify(
            x => x.SubscribeDynamicAsync<TestEvent>(
                topic1,
                It.IsAny<Func<TestEvent, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockSubscriber.Verify(
            x => x.SubscribeDynamicAsync<TestEvent2>(
                topic2,
                It.IsAny<Func<TestEvent2, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_WithTopicButNoEventType_ShouldSkipAndContinue()
    {
        // Arrange
        var registeredTopic = UniqueTopic("registered.topic");
        var unregisteredTopic = UniqueTopic("unregistered.topic");
        EventSubscriptionRegistry.Register<TestEvent>(registeredTopic);
        // Note: unregisteredTopic is NOT registered in EventSubscriptionRegistry

        _mockEventConsumer.Setup(x => x.GetRegisteredTopics())
            .Returns(new[] { unregisteredTopic, registeredTopic });

        var mockSubscription = new Mock<IAsyncDisposable>();
        _mockSubscriber.Setup(x => x.SubscribeDynamicAsync<TestEvent>(
                registeredTopic,
                It.IsAny<Func<TestEvent, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSubscription.Object);

        var backend = CreateBackend();

        // Act
        await backend.StartAsync(CancellationToken.None);

        // Assert - should still subscribe to the registered topic
        _mockSubscriber.Verify(
            x => x.SubscribeDynamicAsync<TestEvent>(
                registeredTopic,
                It.IsAny<Func<TestEvent, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenSubscriptionThrows_ShouldContinueWithOtherTopics()
    {
        // Arrange
        var failingTopic = UniqueTopic("failing.topic");
        var successTopic = UniqueTopic("success.topic");
        EventSubscriptionRegistry.Register<TestEvent>(failingTopic);
        EventSubscriptionRegistry.Register<TestEvent2>(successTopic);

        _mockEventConsumer.Setup(x => x.GetRegisteredTopics())
            .Returns(new[] { failingTopic, successTopic });

        _mockSubscriber.Setup(x => x.SubscribeDynamicAsync<TestEvent>(
                failingTopic,
                It.IsAny<Func<TestEvent, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Subscription failed"));

        var mockSubscription = new Mock<IAsyncDisposable>();
        _mockSubscriber.Setup(x => x.SubscribeDynamicAsync<TestEvent2>(
                successTopic,
                It.IsAny<Func<TestEvent2, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSubscription.Object);

        var backend = CreateBackend();

        // Act - should not throw despite one subscription failing
        await backend.StartAsync(CancellationToken.None);

        // Assert - should still try to subscribe to the success topic
        _mockSubscriber.Verify(
            x => x.SubscribeDynamicAsync<TestEvent2>(
                successTopic,
                It.IsAny<Func<TestEvent2, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region StopAsync Tests

    [Fact]
    public async Task StopAsync_WithNoSubscriptions_ShouldCompleteSuccessfully()
    {
        // Arrange
        _mockEventConsumer.Setup(x => x.GetRegisteredTopics())
            .Returns(Enumerable.Empty<string>());

        var backend = CreateBackend();
        await backend.StartAsync(CancellationToken.None);

        // Act
        await backend.StopAsync(CancellationToken.None);

        // Assert - no exception means success
        Assert.True(true);
    }

    [Fact]
    public async Task StopAsync_WithActiveSubscriptions_ShouldDisposeAll()
    {
        // Arrange
        var topic = UniqueTopic("test.topic");
        EventSubscriptionRegistry.Register<TestEvent>(topic);

        _mockEventConsumer.Setup(x => x.GetRegisteredTopics())
            .Returns(new[] { topic });

        var mockSubscription = new Mock<IAsyncDisposable>();
        _mockSubscriber.Setup(x => x.SubscribeDynamicAsync<TestEvent>(
                topic,
                It.IsAny<Func<TestEvent, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSubscription.Object);

        var backend = CreateBackend();
        await backend.StartAsync(CancellationToken.None);

        // Act
        await backend.StopAsync(CancellationToken.None);

        // Assert
        mockSubscription.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task StopAsync_WhenDisposeThrows_ShouldContinueWithOtherSubscriptions()
    {
        // Arrange
        var topic1 = UniqueTopic("test.topic1");
        var topic2 = UniqueTopic("test.topic2");
        EventSubscriptionRegistry.Register<TestEvent>(topic1);
        EventSubscriptionRegistry.Register<TestEvent2>(topic2);

        _mockEventConsumer.Setup(x => x.GetRegisteredTopics())
            .Returns(new[] { topic1, topic2 });

        var mockSubscription1 = new Mock<IAsyncDisposable>();
        mockSubscription1.Setup(x => x.DisposeAsync())
            .ThrowsAsync(new Exception("Dispose failed"));

        var mockSubscription2 = new Mock<IAsyncDisposable>();

        _mockSubscriber.Setup(x => x.SubscribeDynamicAsync<TestEvent>(
                topic1,
                It.IsAny<Func<TestEvent, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSubscription1.Object);
        _mockSubscriber.Setup(x => x.SubscribeDynamicAsync<TestEvent2>(
                topic2,
                It.IsAny<Func<TestEvent2, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSubscription2.Object);

        var backend = CreateBackend();
        await backend.StartAsync(CancellationToken.None);

        // Act - should not throw despite one dispose failing
        await backend.StopAsync(CancellationToken.None);

        // Assert - both should have been attempted
        mockSubscription1.Verify(x => x.DisposeAsync(), Times.Once);
        mockSubscription2.Verify(x => x.DisposeAsync(), Times.Once);
    }

    #endregion

    #region Event Dispatch Tests

    [Fact]
    public async Task EventHandler_WhenEventReceived_ShouldDispatchToEventConsumer()
    {
        // Arrange
        var topic = UniqueTopic("test.topic");
        EventSubscriptionRegistry.Register<TestEvent>(topic);

        _mockEventConsumer.Setup(x => x.GetRegisteredTopics())
            .Returns(new[] { topic });

        Func<TestEvent, CancellationToken, Task>? capturedHandler = null;
        var mockSubscription = new Mock<IAsyncDisposable>();

        _mockSubscriber.Setup(x => x.SubscribeDynamicAsync<TestEvent>(
                topic,
                It.IsAny<Func<TestEvent, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Func<TestEvent, CancellationToken, Task>, CancellationToken>(
                (t, handler, ct) => capturedHandler = handler)
            .ReturnsAsync(mockSubscription.Object);

        // Setup service provider to return a scope
        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(x => x.CreateScope()).Returns(mockScope.Object);
        mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);

        var backend = CreateBackend();
        await backend.StartAsync(CancellationToken.None);

        // Act - simulate receiving an event
        Assert.NotNull(capturedHandler);
        var testEvent = new TestEvent { Message = "Hello" };
        await capturedHandler(testEvent, CancellationToken.None);

        // Assert - should have dispatched to event consumer
        _mockEventConsumer.Verify(
            x => x.DispatchAsync(topic, testEvent, It.IsAny<IServiceProvider>()),
            Times.Once);
    }

    [Fact]
    public async Task EventHandler_WhenDispatchThrows_ShouldRethrow()
    {
        // Arrange
        var topic = UniqueTopic("test.topic");
        EventSubscriptionRegistry.Register<TestEvent>(topic);

        _mockEventConsumer.Setup(x => x.GetRegisteredTopics())
            .Returns(new[] { topic });

        Func<TestEvent, CancellationToken, Task>? capturedHandler = null;
        var mockSubscription = new Mock<IAsyncDisposable>();

        _mockSubscriber.Setup(x => x.SubscribeDynamicAsync<TestEvent>(
                topic,
                It.IsAny<Func<TestEvent, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Func<TestEvent, CancellationToken, Task>, CancellationToken>(
                (t, handler, ct) => capturedHandler = handler)
            .ReturnsAsync(mockSubscription.Object);

        // Setup service provider to return a scope
        var mockScope = new Mock<IServiceScope>();
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(x => x.CreateScope()).Returns(mockScope.Object);
        mockScope.Setup(x => x.ServiceProvider).Returns(_mockServiceProvider.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);

        _mockEventConsumer.Setup(x => x.DispatchAsync(topic, It.IsAny<TestEvent>(), It.IsAny<IServiceProvider>()))
            .ThrowsAsync(new Exception("Dispatch failed"));

        var backend = CreateBackend();
        await backend.StartAsync(CancellationToken.None);

        // Act & Assert - should rethrow the exception
        Assert.NotNull(capturedHandler);
        var testEvent = new TestEvent { Message = "Hello" };
        await Assert.ThrowsAsync<Exception>(() => capturedHandler(testEvent, CancellationToken.None));
    }

    #endregion

    #region Test Event Classes

    private class TestEvent
    {
        public string Message { get; set; } = string.Empty;
    }

    private class TestEvent2
    {
        public int Value { get; set; }
    }

    #endregion
}
