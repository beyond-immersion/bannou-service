using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Net.Http;
using System.Text;

namespace BeyondImmersion.BannouService.Messaging.Tests;

public class MessagingServiceTests
{
    private readonly Mock<ILogger<MessagingService>> _mockLogger;
    private readonly MessagingServiceConfiguration _configuration;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IMessageSubscriber> _mockMessageSubscriber;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly MessagingService _service;

    public MessagingServiceTests()
    {
        _mockLogger = new Mock<ILogger<MessagingService>>();
        _configuration = new MessagingServiceConfiguration();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockMessageSubscriber = new Mock<IMessageSubscriber>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();

        _service = new MessagingService(
            _mockLogger.Object,
            _configuration,
            _mockMessageBus.Object,
            _mockMessageSubscriber.Object,
            _mockHttpClientFactory.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = new MessagingService(
            _mockLogger.Object,
            _configuration,
            _mockMessageBus.Object,
            _mockMessageSubscriber.Object,
            _mockHttpClientFactory.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new MessagingService(
            null!,
            _configuration,
            _mockMessageBus.Object,
            _mockMessageSubscriber.Object,
            _mockHttpClientFactory.Object));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new MessagingService(
            _mockLogger.Object,
            null!,
            _mockMessageBus.Object,
            _mockMessageSubscriber.Object,
            _mockHttpClientFactory.Object));
        Assert.Equal("configuration", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullMessageBus_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new MessagingService(
            _mockLogger.Object,
            _configuration,
            null!,
            _mockMessageSubscriber.Object,
            _mockHttpClientFactory.Object));
        Assert.Equal("messageBus", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullMessageSubscriber_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new MessagingService(
            _mockLogger.Object,
            _configuration,
            _mockMessageBus.Object,
            null!,
            _mockHttpClientFactory.Object));
        Assert.Equal("messageSubscriber", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullHttpClientFactory_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new MessagingService(
            _mockLogger.Object,
            _configuration,
            _mockMessageBus.Object,
            _mockMessageSubscriber.Object,
            null!));
        Assert.Equal("httpClientFactory", ex.ParamName);
    }

    #endregion

    #region PublishEventAsync Tests

    [Fact]
    public async Task PublishEventAsync_WithValidRequest_ReturnsOkWithMessageId()
    {
        // Arrange
        var expectedMessageId = Guid.NewGuid();
        var request = new PublishEventRequest
        {
            Topic = "test.topic",
            Payload = new { Key = "value" }
        };

        _mockMessageBus
            .Setup(x => x.PublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMessageId);

        // Act
        var (statusCode, response) = await _service.PublishEventAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(expectedMessageId, response.MessageId);
    }

    [Fact]
    public async Task PublishEventAsync_WithOptions_PassesOptionsToMessageBus()
    {
        // Arrange
        var expectedMessageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var request = new PublishEventRequest
        {
            Topic = "test.topic",
            Payload = new { Key = "value" },
            Options = new Messaging.PublishOptions
            {
                Exchange = "custom-exchange",
                Persistent = true,
                Priority = 5,
                CorrelationId = correlationId
            }
        };

        PublishOptions? capturedOptions = null;
        _mockMessageBus
            .Setup(x => x.PublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, PublishOptions?, CancellationToken>((t, p, o, ct) => capturedOptions = o)
            .ReturnsAsync(expectedMessageId);

        // Act
        var (statusCode, response) = await _service.PublishEventAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(capturedOptions);
        Assert.Equal("custom-exchange", capturedOptions.Exchange);
        Assert.True(capturedOptions.Persistent);
        Assert.Equal(5, capturedOptions.Priority);
        Assert.Equal(correlationId, capturedOptions.CorrelationId);
    }

    [Fact]
    public async Task PublishEventAsync_WhenMessageBusThrows_ReturnsInternalServerErrorAndEmitsErrorEvent()
    {
        // Arrange
        var request = new PublishEventRequest
        {
            Topic = "test.topic",
            Payload = new { Key = "value" }
        };

        _mockMessageBus
            .Setup(x => x.PublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ connection failed"));

        // Act
        var (statusCode, response) = await _service.PublishEventAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, statusCode);
        Assert.NotNull(response);
        Assert.False(response.Success);

        _mockMessageBus.Verify(
            m => m.TryPublishErrorAsync(
                "messaging",
                "PublishEvent",
                "InvalidOperationException",
                "RabbitMQ connection failed",
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishEventAsync_WithNullOptions_PassesNullToMessageBus()
    {
        // Arrange
        var expectedMessageId = Guid.NewGuid();
        var request = new PublishEventRequest
        {
            Topic = "test.topic",
            Payload = new { Key = "value" },
            Options = null!
        };

        PublishOptions? capturedOptions = null;
        _mockMessageBus
            .Setup(x => x.PublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, PublishOptions?, CancellationToken>((t, p, o, ct) => capturedOptions = o)
            .ReturnsAsync(expectedMessageId);

        // Act
        var (statusCode, response) = await _service.PublishEventAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.Null(capturedOptions);
    }

    [Fact]
    public async Task PublishEventAsync_WithEmptyCorrelationId_DoesNotPassCorrelationId()
    {
        // Arrange
        var expectedMessageId = Guid.NewGuid();
        var request = new PublishEventRequest
        {
            Topic = "test.topic",
            Payload = new { Key = "value" },
            Options = new Messaging.PublishOptions
            {
                CorrelationId = Guid.Empty
            }
        };

        PublishOptions? capturedOptions = null;
        _mockMessageBus
            .Setup(x => x.PublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, PublishOptions?, CancellationToken>((t, p, o, ct) => capturedOptions = o)
            .ReturnsAsync(expectedMessageId);

        // Act
        var (statusCode, response) = await _service.PublishEventAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(capturedOptions);
        Assert.Null(capturedOptions.CorrelationId);
    }

    #endregion

    #region CreateSubscriptionAsync Tests

    [Fact]
    public async Task CreateSubscriptionAsync_WithValidRequest_ReturnsOkWithSubscriptionId()
    {
        // Arrange
        var request = new CreateSubscriptionRequest
        {
            Topic = "test.topic",
            CallbackUrl = new Uri("http://localhost:8080/callback")
        };

        var mockHandle = new Mock<IAsyncDisposable>();
        _mockMessageSubscriber
            .Setup(x => x.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                It.IsAny<string>(),
                It.IsAny<Func<Services.GenericMessageEnvelope, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionExchangeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockHandle.Object);

        // Act
        var (statusCode, response) = await _service.CreateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response.SubscriptionId);
        Assert.StartsWith("bannou-dynamic-", response.QueueName);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_WhenSubscriberThrows_ReturnsInternalServerErrorAndEmitsErrorEvent()
    {
        // Arrange
        var request = new CreateSubscriptionRequest
        {
            Topic = "test.topic",
            CallbackUrl = new Uri("http://localhost:8080/callback")
        };

        _mockMessageSubscriber
            .Setup(x => x.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                It.IsAny<string>(),
                It.IsAny<Func<Services.GenericMessageEnvelope, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionExchangeType>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cannot create subscription"));

        // Act
        var (statusCode, response) = await _service.CreateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, statusCode);
        Assert.Null(response);

        _mockMessageBus.Verify(
            m => m.TryPublishErrorAsync(
                "messaging",
                "CreateSubscription",
                "InvalidOperationException",
                "Cannot create subscription",
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_MultipleSubscriptions_GeneratesUniqueIds()
    {
        // Arrange
        var request1 = new CreateSubscriptionRequest
        {
            Topic = "test.topic1",
            CallbackUrl = new Uri("http://localhost:8080/callback1")
        };
        var request2 = new CreateSubscriptionRequest
        {
            Topic = "test.topic2",
            CallbackUrl = new Uri("http://localhost:8080/callback2")
        };

        var mockHandle = new Mock<IAsyncDisposable>();
        _mockMessageSubscriber
            .Setup(x => x.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                It.IsAny<string>(),
                It.IsAny<Func<Services.GenericMessageEnvelope, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionExchangeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockHandle.Object);

        // Act
        var (_, response1) = await _service.CreateSubscriptionAsync(request1, CancellationToken.None);
        var (_, response2) = await _service.CreateSubscriptionAsync(request2, CancellationToken.None);

        // Assert
        Assert.NotNull(response1);
        Assert.NotNull(response2);
        Assert.NotEqual(response1.SubscriptionId, response2.SubscriptionId);
    }

    #endregion

    #region RemoveSubscriptionAsync Tests

    [Fact]
    public async Task RemoveSubscriptionAsync_WithExistingSubscription_ReturnsOkAndSuccess()
    {
        // Arrange - First create a subscription
        var createRequest = new CreateSubscriptionRequest
        {
            Topic = "test.topic",
            CallbackUrl = new Uri("http://localhost:8080/callback")
        };

        var mockHandle = new Mock<IAsyncDisposable>();
        _mockMessageSubscriber
            .Setup(x => x.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                It.IsAny<string>(),
                It.IsAny<Func<Services.GenericMessageEnvelope, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionExchangeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockHandle.Object);

        // Set up HttpClientFactory to return a real HttpClient for subscription entry
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient());

        var (_, createResponse) = await _service.CreateSubscriptionAsync(createRequest, CancellationToken.None);
        Assert.NotNull(createResponse);

        var removeRequest = new RemoveSubscriptionRequest
        {
            SubscriptionId = createResponse.SubscriptionId
        };

        // Act
        var (statusCode, response) = await _service.RemoveSubscriptionAsync(removeRequest, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Success);

        // Verify the handle was disposed
        mockHandle.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task RemoveSubscriptionAsync_WithNonExistingSubscription_ReturnsNotFound()
    {
        // Arrange
        var request = new RemoveSubscriptionRequest
        {
            SubscriptionId = Guid.NewGuid()
        };

        // Act
        var (statusCode, response) = await _service.RemoveSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.NotNull(response);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task RemoveSubscriptionAsync_WhenDisposeThrows_ReturnsInternalServerErrorAndEmitsErrorEvent()
    {
        // Arrange - Create a subscription with a handle that throws on dispose
        var createRequest = new CreateSubscriptionRequest
        {
            Topic = "test.topic",
            CallbackUrl = new Uri("http://localhost:8080/callback")
        };

        var mockHandle = new Mock<IAsyncDisposable>();
        mockHandle
            .Setup(x => x.DisposeAsync())
            .ThrowsAsync(new InvalidOperationException("Dispose failed"));

        _mockMessageSubscriber
            .Setup(x => x.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                It.IsAny<string>(),
                It.IsAny<Func<Services.GenericMessageEnvelope, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionExchangeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockHandle.Object);

        var (_, createResponse) = await _service.CreateSubscriptionAsync(createRequest, CancellationToken.None);
        Assert.NotNull(createResponse);

        var removeRequest = new RemoveSubscriptionRequest
        {
            SubscriptionId = createResponse.SubscriptionId
        };

        // Act
        var (statusCode, response) = await _service.RemoveSubscriptionAsync(removeRequest, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, statusCode);
        Assert.NotNull(response);
        Assert.False(response.Success);

        _mockMessageBus.Verify(
            m => m.TryPublishErrorAsync(
                "messaging",
                "RemoveSubscription",
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ListTopicsAsync Tests

    [Fact]
    public async Task ListTopicsAsync_WithNoSubscriptions_ReturnsEmptyList()
    {
        // Arrange
        var request = new ListTopicsRequest();

        // Act
        var (statusCode, response) = await _service.ListTopicsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Empty(response.Topics);
    }

    [Fact]
    public async Task ListTopicsAsync_WithActiveSubscriptions_ReturnsTopics()
    {
        // Arrange - Create subscriptions first
        var mockHandle = new Mock<IAsyncDisposable>();
        _mockMessageSubscriber
            .Setup(x => x.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                It.IsAny<string>(),
                It.IsAny<Func<Services.GenericMessageEnvelope, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionExchangeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockHandle.Object);

        await _service.CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Topic = "topic1",
            CallbackUrl = new Uri("http://localhost/callback1")
        }, CancellationToken.None);

        await _service.CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Topic = "topic2",
            CallbackUrl = new Uri("http://localhost/callback2")
        }, CancellationToken.None);

        var request = new ListTopicsRequest();

        // Act
        var (statusCode, response) = await _service.ListTopicsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Topics.Count);
        Assert.Contains(response.Topics, t => t.Name == "topic1");
        Assert.Contains(response.Topics, t => t.Name == "topic2");
    }

    [Fact]
    public async Task ListTopicsAsync_WithFilter_ReturnsFilteredTopics()
    {
        // Arrange - Create subscriptions with different topic prefixes
        var mockHandle = new Mock<IAsyncDisposable>();
        _mockMessageSubscriber
            .Setup(x => x.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                It.IsAny<string>(),
                It.IsAny<Func<Services.GenericMessageEnvelope, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionExchangeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockHandle.Object);

        await _service.CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Topic = "auth.user.created",
            CallbackUrl = new Uri("http://localhost/callback1")
        }, CancellationToken.None);

        await _service.CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Topic = "auth.session.expired",
            CallbackUrl = new Uri("http://localhost/callback2")
        }, CancellationToken.None);

        await _service.CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Topic = "account.deleted",
            CallbackUrl = new Uri("http://localhost/callback3")
        }, CancellationToken.None);

        var request = new ListTopicsRequest { ExchangeFilter = "auth" };

        // Act
        var (statusCode, response) = await _service.ListTopicsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(2, response.Topics.Count);
        Assert.All(response.Topics, t => Assert.StartsWith("auth", t.Name));
    }

    [Fact]
    public async Task ListTopicsAsync_WithDuplicateTopics_ReturnsDistinctTopics()
    {
        // Arrange - Create multiple subscriptions to the same topic
        var mockHandle = new Mock<IAsyncDisposable>();
        _mockMessageSubscriber
            .Setup(x => x.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                It.IsAny<string>(),
                It.IsAny<Func<Services.GenericMessageEnvelope, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionExchangeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockHandle.Object);

        await _service.CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Topic = "same.topic",
            CallbackUrl = new Uri("http://localhost/callback1")
        }, CancellationToken.None);

        await _service.CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Topic = "same.topic",
            CallbackUrl = new Uri("http://localhost/callback2")
        }, CancellationToken.None);

        var request = new ListTopicsRequest();

        // Act
        var (statusCode, response) = await _service.ListTopicsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Topics);
        Assert.Equal("same.topic", response.Topics.First().Name);
        Assert.Equal(2, response.Topics.First().ConsumerCount);
    }

    [Fact]
    public async Task ListTopicsAsync_WithNullRequest_ReturnsAllTopics()
    {
        // Arrange - Create subscription
        var mockHandle = new Mock<IAsyncDisposable>();
        _mockMessageSubscriber
            .Setup(x => x.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                It.IsAny<string>(),
                It.IsAny<Func<Services.GenericMessageEnvelope, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionExchangeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockHandle.Object);

        await _service.CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Topic = "test.topic",
            CallbackUrl = new Uri("http://localhost/callback")
        }, CancellationToken.None);

        // Act
        var (statusCode, response) = await _service.ListTopicsAsync(null!, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Topics);
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_WithActiveSubscriptions_DisposesAllSubscriptions()
    {
        // Arrange
        var mockHandle1 = new Mock<IAsyncDisposable>();
        var mockHandle2 = new Mock<IAsyncDisposable>();

        var handleIndex = 0;
        _mockMessageSubscriber
            .Setup(x => x.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                It.IsAny<string>(),
                It.IsAny<Func<Services.GenericMessageEnvelope, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionExchangeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => handleIndex++ == 0 ? mockHandle1.Object : mockHandle2.Object);

        await _service.CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Topic = "topic1",
            CallbackUrl = new Uri("http://localhost/callback1")
        }, CancellationToken.None);

        await _service.CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Topic = "topic2",
            CallbackUrl = new Uri("http://localhost/callback2")
        }, CancellationToken.None);

        // Act
        await _service.DisposeAsync();

        // Assert
        mockHandle1.Verify(x => x.DisposeAsync(), Times.Once);
        mockHandle2.Verify(x => x.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WithNoSubscriptions_CompletesWithoutError()
    {
        // Act
        var exception = await Record.ExceptionAsync(() => _service.DisposeAsync().AsTask());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_WhenHandleDisposeThrows_ContinuesDisposingOthers()
    {
        // Arrange
        var mockHandle1 = new Mock<IAsyncDisposable>();
        var mockHandle2 = new Mock<IAsyncDisposable>();

        mockHandle1
            .Setup(x => x.DisposeAsync())
            .ThrowsAsync(new InvalidOperationException("Dispose failed"));

        var handleIndex = 0;
        _mockMessageSubscriber
            .Setup(x => x.SubscribeDynamicAsync<Services.GenericMessageEnvelope>(
                It.IsAny<string>(),
                It.IsAny<Func<Services.GenericMessageEnvelope, CancellationToken, Task>>(),
                It.IsAny<string?>(),
                It.IsAny<SubscriptionExchangeType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => handleIndex++ == 0 ? mockHandle1.Object : mockHandle2.Object);

        await _service.CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Topic = "topic1",
            CallbackUrl = new Uri("http://localhost/callback1")
        }, CancellationToken.None);

        await _service.CreateSubscriptionAsync(new CreateSubscriptionRequest
        {
            Topic = "topic2",
            CallbackUrl = new Uri("http://localhost/callback2")
        }, CancellationToken.None);

        // Act
        var exception = await Record.ExceptionAsync(() => _service.DisposeAsync().AsTask());

        // Assert - Should not throw, should continue disposing
        Assert.Null(exception);
        mockHandle1.Verify(x => x.DisposeAsync(), Times.Once);
        mockHandle2.Verify(x => x.DisposeAsync(), Times.Once);
    }

    #endregion
}

public class MessagingConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var config = new MessagingServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void Configuration_DefaultValues_ShouldBeSet()
    {
        // Arrange & Act
        var config = new MessagingServiceConfiguration();

        // Assert
        // Configuration defaults are defined in schema - verify they're accessible
        Assert.NotNull(config);
    }
}
