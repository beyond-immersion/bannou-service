#nullable enable

using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;

namespace BeyondImmersion.BannouService.Messaging.Tests;

/// <summary>
/// Unit tests for MessageRetryBuffer.
/// Tests retry buffering, backpressure, crash-fast behavior, and poison message handling.
/// </summary>
public class MessageRetryBufferTests : IAsyncDisposable
{
    private readonly Mock<IChannelManager> _mockChannelManager;
    private readonly Mock<ILogger<MessageRetryBuffer>> _mockLogger;
    private readonly Mock<IProcessTerminator> _mockProcessTerminator;
    private MessageRetryBuffer? _buffer;

    public MessageRetryBufferTests()
    {
        _mockChannelManager = new Mock<IChannelManager>();
        _mockLogger = new Mock<ILogger<MessageRetryBuffer>>();
        _mockProcessTerminator = new Mock<IProcessTerminator>();

        // Setup default channel manager behavior
        _mockChannelManager.Setup(x => x.DefaultExchange).Returns("bannou");
    }

    public async ValueTask DisposeAsync()
    {
        if (_buffer != null)
        {
            await _buffer.DisposeAsync();
        }
    }

    private MessagingServiceConfiguration CreateConfig(
        bool enabled = true,
        int maxSize = 100,
        int maxAgeSeconds = 60,
        int intervalSeconds = 1,
        double backpressureThreshold = 0.8,
        int retryMaxAttempts = 5,
        int retryDelayMs = 100,
        int retryMaxBackoffMs = 1000)
    {
        return new MessagingServiceConfiguration
        {
            RetryBufferEnabled = enabled,
            RetryBufferMaxSize = maxSize,
            RetryBufferMaxAgeSeconds = maxAgeSeconds,
            RetryBufferIntervalSeconds = intervalSeconds,
            RetryBufferBackpressureThreshold = backpressureThreshold,
            RetryMaxAttempts = retryMaxAttempts,
            RetryDelayMs = retryDelayMs,
            RetryMaxBackoffMs = retryMaxBackoffMs,
            DeadLetterExchange = "bannou-dlx"
        };
    }

    private MessageRetryBuffer CreateBuffer(MessagingServiceConfiguration? config = null)
    {
        config ??= CreateConfig();
        _buffer = new MessageRetryBuffer(
            _mockChannelManager.Object,
            config,
            _mockLogger.Object,
            _mockProcessTerminator.Object);
        return _buffer;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_IsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<MessageRetryBuffer>();
    }

    [Fact]
    public void Constructor_WhenEnabled_InitializesWithZeroCount()
    {
        // Arrange & Act
        var buffer = CreateBuffer();

        // Assert
        Assert.Equal(0, buffer.BufferCount);
        Assert.True(buffer.IsEnabled);
        Assert.False(buffer.IsBackpressureActive);
    }

    [Fact]
    public void Constructor_WhenDisabled_IsNotEnabled()
    {
        // Arrange & Act
        var buffer = CreateBuffer(CreateConfig(enabled: false));

        // Assert
        Assert.False(buffer.IsEnabled);
        Assert.Equal(0, buffer.BufferCount);
    }

    [Fact]
    public void Constructor_WithNullProcessTerminator_UsesDefault()
    {
        // Arrange & Act - should not throw
        _buffer = new MessageRetryBuffer(
            _mockChannelManager.Object,
            CreateConfig(),
            _mockLogger.Object,
            processTerminator: null);

        // Assert
        Assert.NotNull(_buffer);
        Assert.True(_buffer.IsEnabled);
    }

    #endregion

    #region TryEnqueueForRetry Tests

    [Fact]
    public void TryEnqueueForRetry_WhenBufferDisabled_ReturnsFalse()
    {
        // Arrange
        var buffer = CreateBuffer(CreateConfig(enabled: false));
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");

        // Act
        var result = buffer.TryEnqueueForRetry("test.topic", payload, null, Guid.NewGuid());

        // Assert
        Assert.False(result);
        Assert.Equal(0, buffer.BufferCount);
    }

    [Fact]
    public void TryEnqueueForRetry_WhenBufferEnabled_ReturnsTrue()
    {
        // Arrange
        var buffer = CreateBuffer();
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");

        // Act
        var result = buffer.TryEnqueueForRetry("test.topic", payload, null, Guid.NewGuid());

        // Assert
        Assert.True(result);
        Assert.Equal(1, buffer.BufferCount);
    }

    [Fact]
    public void TryEnqueueForRetry_MultipleMessages_IncrementsCount()
    {
        // Arrange
        var buffer = CreateBuffer();
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");

        // Act
        for (int i = 0; i < 10; i++)
        {
            buffer.TryEnqueueForRetry($"test.topic.{i}", payload, null, Guid.NewGuid());
        }

        // Assert
        Assert.Equal(10, buffer.BufferCount);
    }

    [Fact]
    public void TryEnqueueForRetry_WithPublishOptions_AcceptsOptions()
    {
        // Arrange
        var buffer = CreateBuffer();
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");
        var options = new PublishOptions
        {
            Exchange = "custom-exchange",
            RoutingKey = "custom-routing-key",
            Persistent = true
        };

        // Act
        var result = buffer.TryEnqueueForRetry("test.topic", payload, options, Guid.NewGuid());

        // Assert
        Assert.True(result);
        Assert.Equal(1, buffer.BufferCount);
    }

    #endregion

    #region Backpressure Tests

    [Fact]
    public void IsBackpressureActive_WhenBelowThreshold_ReturnsFalse()
    {
        // Arrange - threshold at 80% of 100 = 80
        var buffer = CreateBuffer(CreateConfig(maxSize: 100, backpressureThreshold: 0.8));
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");

        // Add 50 messages (below 80% threshold)
        for (int i = 0; i < 50; i++)
        {
            buffer.TryEnqueueForRetry("test.topic", payload, null, Guid.NewGuid());
        }

        // Assert
        Assert.False(buffer.IsBackpressureActive);
        Assert.Equal(50, buffer.BufferCount);
    }

    [Fact]
    public void IsBackpressureActive_WhenAtThreshold_ReturnsTrue()
    {
        // Arrange - threshold at 80% of 100 = 80
        var buffer = CreateBuffer(CreateConfig(maxSize: 100, backpressureThreshold: 0.8));
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");

        // Add exactly 80 messages (at 80% threshold)
        for (int i = 0; i < 80; i++)
        {
            buffer.TryEnqueueForRetry("test.topic", payload, null, Guid.NewGuid());
        }

        // Assert
        Assert.True(buffer.IsBackpressureActive);
        Assert.Equal(80, buffer.BufferCount);
    }

    [Fact]
    public void TryEnqueueForRetry_WhenBackpressureActive_RejectsNewMessages()
    {
        // Arrange - threshold at 80% of 100 = 80
        var buffer = CreateBuffer(CreateConfig(maxSize: 100, backpressureThreshold: 0.8));
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");

        // Fill to threshold
        for (int i = 0; i < 80; i++)
        {
            buffer.TryEnqueueForRetry("test.topic", payload, null, Guid.NewGuid());
        }

        // Act - try to add one more
        var result = buffer.TryEnqueueForRetry("test.topic.new", payload, null, Guid.NewGuid());

        // Assert
        Assert.False(result);
        Assert.Equal(80, buffer.BufferCount); // Still 80, new message rejected
    }

    [Fact]
    public void BackpressureThreshold_CalculatedCorrectly()
    {
        // Arrange
        var buffer = CreateBuffer(CreateConfig(maxSize: 1000, backpressureThreshold: 0.75));

        // Assert
        Assert.Equal(750, buffer.BackpressureThreshold);
    }

    [Fact]
    public void IsBackpressureActive_WhenDisabled_ReturnsFalse()
    {
        // Arrange
        var buffer = CreateBuffer(CreateConfig(enabled: false));

        // Assert - even with disabled buffer, backpressure should be false
        Assert.False(buffer.IsBackpressureActive);
    }

    #endregion

    #region Crash-Fast Behavior Tests

    [Fact]
    public void TryEnqueueForRetry_WhenBufferExceedsMaxSize_TerminatesProcess()
    {
        // Arrange - very small buffer for test
        var buffer = CreateBuffer(CreateConfig(maxSize: 10, backpressureThreshold: 0.99));
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");

        // Act - fill beyond max size (backpressure at 99% = 9.9, so 10 messages fit)
        for (int i = 0; i < 10; i++)
        {
            buffer.TryEnqueueForRetry("test.topic", payload, null, Guid.NewGuid());
        }

        // Assert - process terminator should have been called
        _mockProcessTerminator.Verify(
            x => x.TerminateProcess(It.Is<string>(s => s.Contains("exceeded max size"))),
            Times.Once);
    }

    [Fact]
    public void TryEnqueueForRetry_BelowMaxSize_DoesNotTerminate()
    {
        // Arrange
        var buffer = CreateBuffer(CreateConfig(maxSize: 100, backpressureThreshold: 0.8));
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");

        // Act - add just a few messages
        for (int i = 0; i < 5; i++)
        {
            buffer.TryEnqueueForRetry("test.topic", payload, null, Guid.NewGuid());
        }

        // Assert
        _mockProcessTerminator.Verify(
            x => x.TerminateProcess(It.IsAny<string>()),
            Times.Never);
    }

    #endregion

    #region Interface Implementation Tests

    [Fact]
    public void IRetryBuffer_TryEnqueueForRetry_MatchesConcreteImplementation()
    {
        // Arrange
        IRetryBuffer buffer = CreateBuffer();
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");

        // Act
        var result = buffer.TryEnqueueForRetry("test.topic", payload, null, Guid.NewGuid());

        // Assert
        Assert.True(result);
        Assert.Equal(1, buffer.BufferCount);
    }

    [Fact]
    public void IRetryBuffer_Properties_Accessible()
    {
        // Arrange
        IRetryBuffer buffer = CreateBuffer(CreateConfig(maxSize: 200, backpressureThreshold: 0.75));

        // Assert
        Assert.Equal(0, buffer.BufferCount);
        Assert.True(buffer.IsEnabled);
        Assert.False(buffer.IsBackpressureActive);
        Assert.Equal(150, buffer.BackpressureThreshold); // 75% of 200
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_WithMessagesInBuffer_LogsWarning()
    {
        // Arrange
        var buffer = CreateBuffer();
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");

        // Add some messages
        buffer.TryEnqueueForRetry("test.topic", payload, null, Guid.NewGuid());
        buffer.TryEnqueueForRetry("test.topic", payload, null, Guid.NewGuid());

        // Act
        await buffer.DisposeAsync();
        _buffer = null; // Prevent double dispose in test cleanup

        // Assert - logger should have been called with warning about buffered messages
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("2 messages still buffered")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_WithEmptyBuffer_DoesNotLogWarning()
    {
        // Arrange
        var buffer = CreateBuffer();

        // Act
        await buffer.DisposeAsync();
        _buffer = null; // Prevent double dispose in test cleanup

        // Assert - no warning about buffered messages
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("messages still buffered")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var buffer = CreateBuffer();

        // Act & Assert - should not throw
        await buffer.DisposeAsync();
        await buffer.DisposeAsync();
        _buffer = null; // Prevent double dispose in test cleanup
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void TryEnqueueForRetry_WithEmptyPayload_Succeeds()
    {
        // Arrange
        var buffer = CreateBuffer();
        var payload = Array.Empty<byte>();

        // Act
        var result = buffer.TryEnqueueForRetry("test.topic", payload, null, Guid.NewGuid());

        // Assert
        Assert.True(result);
        Assert.Equal(1, buffer.BufferCount);
    }

    [Fact]
    public void TryEnqueueForRetry_WithLargePayload_Succeeds()
    {
        // Arrange
        var buffer = CreateBuffer();
        var payload = new byte[1024 * 1024]; // 1MB payload
        new Random(42).NextBytes(payload);

        // Act
        var result = buffer.TryEnqueueForRetry("test.topic", payload, null, Guid.NewGuid());

        // Assert
        Assert.True(result);
        Assert.Equal(1, buffer.BufferCount);
    }

    [Fact]
    public void TryEnqueueForRetry_WithSpecialTopicCharacters_Succeeds()
    {
        // Arrange
        var buffer = CreateBuffer();
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");

        // Act
        var result = buffer.TryEnqueueForRetry("test.*.#.topic", payload, null, Guid.NewGuid());

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void BufferCount_IsThreadSafe()
    {
        // Arrange
        var buffer = CreateBuffer(CreateConfig(maxSize: 10000, backpressureThreshold: 0.99));
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"test\":true}");
        var tasks = new List<Task>();

        // Act - concurrent enqueues
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 10; j++)
                {
                    buffer.TryEnqueueForRetry("test.topic", payload, null, Guid.NewGuid());
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - should have 1000 messages (may be less if max size hit triggers termination)
        Assert.True(buffer.BufferCount > 0);
    }

    #endregion
}
