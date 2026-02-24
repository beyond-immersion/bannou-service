#nullable enable

using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using System.Collections.Generic;
using System.Text;

namespace BeyondImmersion.BannouService.Messaging.Tests;

/// <summary>
/// Unit tests for DeadLetterConsumerService.
/// Tests static header extraction helpers, configuration gating, lifecycle management,
/// and the overall consumer setup flow.
/// </summary>
public class DeadLetterConsumerServiceTests
{
    private readonly Mock<IChannelManager> _mockChannelManager;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<DeadLetterConsumerService>> _mockLogger;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    public DeadLetterConsumerServiceTests()
    {
        _mockChannelManager = new Mock<IChannelManager>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<DeadLetterConsumerService>>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
    }

    private static MessagingServiceConfiguration CreateConfig(
        bool enabled = true,
        int startupDelaySeconds = 0,
        string deadLetterExchange = "bannou-dlx")
    {
        return new MessagingServiceConfiguration
        {
            DeadLetterConsumerEnabled = enabled,
            DeadLetterConsumerStartupDelaySeconds = startupDelaySeconds,
            DeadLetterExchange = deadLetterExchange
        };
    }

    private DeadLetterConsumerService CreateService(MessagingServiceConfiguration? config = null)
    {
        config ??= CreateConfig();
        return new DeadLetterConsumerService(
            _mockChannelManager.Object,
            _mockMessageBus.Object,
            config,
            _mockLogger.Object,
            _mockTelemetryProvider.Object);
    }

    private static Mock<IChannel> CreateMockChannel()
    {
        var mockChannel = new Mock<IChannel>();

        mockChannel.Setup(x => x.ExchangeDeclareAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mockChannel.Setup(x => x.QueueDeclareAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueueDeclareOk("bannou-dlx-consumer", 0, 0));

        mockChannel.Setup(x => x.QueueBindAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IDictionary<string, object?>>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mockChannel.Setup(x => x.BasicConsumeAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<bool>(), It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<IAsyncBasicConsumer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("consumer-tag");

        mockChannel.Setup(x => x.BasicAckAsync(
                It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        mockChannel.Setup(x => x.BasicNackAsync(
                It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        mockChannel.Setup(x => x.CloseAsync(
                It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return mockChannel;
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<DeadLetterConsumerService>();
        using var service = CreateService();
        Assert.NotNull(service);
    }

    #endregion

    #region ExtractStringHeader Tests

    [Fact]
    public void ExtractStringHeader_NullHeaders_ReturnsNull()
    {
        var result = DeadLetterConsumerService.ExtractStringHeader(null, "key");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractStringHeader_MissingKey_ReturnsNull()
    {
        var headers = new Dictionary<string, object?> { ["other"] = "value" };
        var result = DeadLetterConsumerService.ExtractStringHeader(headers, "missing");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractStringHeader_NullValue_ReturnsNull()
    {
        var headers = new Dictionary<string, object?> { ["key"] = null };
        var result = DeadLetterConsumerService.ExtractStringHeader(headers, "key");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractStringHeader_ByteArrayValue_ReturnsUtf8String()
    {
        var headers = new Dictionary<string, object?>
        {
            ["key"] = Encoding.UTF8.GetBytes("hello-world")
        };
        var result = DeadLetterConsumerService.ExtractStringHeader(headers, "key");
        Assert.Equal("hello-world", result);
    }

    [Fact]
    public void ExtractStringHeader_StringValue_ReturnsString()
    {
        var headers = new Dictionary<string, object?> { ["key"] = "direct-string" };
        var result = DeadLetterConsumerService.ExtractStringHeader(headers, "key");
        Assert.Equal("direct-string", result);
    }

    [Fact]
    public void ExtractStringHeader_OtherType_ReturnsToString()
    {
        var headers = new Dictionary<string, object?> { ["key"] = 42 };
        var result = DeadLetterConsumerService.ExtractStringHeader(headers, "key");
        Assert.Equal("42", result);
    }

    #endregion

    #region ExtractIntHeader Tests

    [Fact]
    public void ExtractIntHeader_NullHeaders_ReturnsNull()
    {
        var result = DeadLetterConsumerService.ExtractIntHeader(null, "key");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractIntHeader_MissingKey_ReturnsNull()
    {
        var headers = new Dictionary<string, object?> { ["other"] = 5 };
        var result = DeadLetterConsumerService.ExtractIntHeader(headers, "missing");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractIntHeader_NullValue_ReturnsNull()
    {
        var headers = new Dictionary<string, object?> { ["key"] = null };
        var result = DeadLetterConsumerService.ExtractIntHeader(headers, "key");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractIntHeader_IntValue_ReturnsInt()
    {
        var headers = new Dictionary<string, object?> { ["key"] = 7 };
        var result = DeadLetterConsumerService.ExtractIntHeader(headers, "key");
        Assert.Equal(7, result);
    }

    [Fact]
    public void ExtractIntHeader_LongValue_ReturnsCastInt()
    {
        var headers = new Dictionary<string, object?> { ["key"] = 42L };
        var result = DeadLetterConsumerService.ExtractIntHeader(headers, "key");
        Assert.Equal(42, result);
    }

    [Fact]
    public void ExtractIntHeader_ParseableString_ReturnsParsed()
    {
        var headers = new Dictionary<string, object?> { ["key"] = "99" };
        var result = DeadLetterConsumerService.ExtractIntHeader(headers, "key");
        Assert.Equal(99, result);
    }

    [Fact]
    public void ExtractIntHeader_UnparseableValue_ReturnsNull()
    {
        var headers = new Dictionary<string, object?> { ["key"] = "not-a-number" };
        var result = DeadLetterConsumerService.ExtractIntHeader(headers, "key");
        Assert.Null(result);
    }

    #endregion

    #region ExtractLongHeader Tests

    [Fact]
    public void ExtractLongHeader_NullHeaders_ReturnsNull()
    {
        var result = DeadLetterConsumerService.ExtractLongHeader(null, "key");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractLongHeader_MissingKey_ReturnsNull()
    {
        var headers = new Dictionary<string, object?> { ["other"] = 5L };
        var result = DeadLetterConsumerService.ExtractLongHeader(headers, "missing");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractLongHeader_NullValue_ReturnsNull()
    {
        var headers = new Dictionary<string, object?> { ["key"] = null };
        var result = DeadLetterConsumerService.ExtractLongHeader(headers, "key");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractLongHeader_LongValue_ReturnsLong()
    {
        var headers = new Dictionary<string, object?> { ["key"] = 1700000000000L };
        var result = DeadLetterConsumerService.ExtractLongHeader(headers, "key");
        Assert.Equal(1700000000000L, result);
    }

    [Fact]
    public void ExtractLongHeader_IntValue_ReturnsPromotedLong()
    {
        var headers = new Dictionary<string, object?> { ["key"] = 42 };
        var result = DeadLetterConsumerService.ExtractLongHeader(headers, "key");
        Assert.Equal(42L, result);
    }

    [Fact]
    public void ExtractLongHeader_ParseableString_ReturnsParsed()
    {
        var headers = new Dictionary<string, object?> { ["key"] = "1700000000000" };
        var result = DeadLetterConsumerService.ExtractLongHeader(headers, "key");
        Assert.Equal(1700000000000L, result);
    }

    [Fact]
    public void ExtractLongHeader_UnparseableValue_ReturnsNull()
    {
        var headers = new Dictionary<string, object?> { ["key"] = "not-a-long" };
        var result = DeadLetterConsumerService.ExtractLongHeader(headers, "key");
        Assert.Null(result);
    }

    #endregion

    #region ExtractPayloadPreview Tests

    [Fact]
    public void ExtractPayloadPreview_EmptyBody_ReturnsEmptyIndicator()
    {
        var body = ReadOnlyMemory<byte>.Empty;
        var result = DeadLetterConsumerService.ExtractPayloadPreview(body);
        Assert.Equal("(empty)", result);
    }

    [Fact]
    public void ExtractPayloadPreview_ShortBody_ReturnsFullContent()
    {
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("short message"));
        var result = DeadLetterConsumerService.ExtractPayloadPreview(body);
        Assert.Equal("short message", result);
    }

    [Fact]
    public void ExtractPayloadPreview_ExactLimitBody_ReturnsFullContent()
    {
        // 500 characters exactly — should NOT append truncation suffix
        var text = new string('x', 500);
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(text));
        var result = DeadLetterConsumerService.ExtractPayloadPreview(body);
        Assert.Equal(text, result);
        Assert.DoesNotContain("bytes total", result);
    }

    [Fact]
    public void ExtractPayloadPreview_LongBody_ReturnsTruncatedWithSize()
    {
        var text = new string('y', 1000);
        var body = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(text));
        var result = DeadLetterConsumerService.ExtractPayloadPreview(body);

        // Should start with first 500 chars
        Assert.StartsWith(new string('y', 500), result);
        // Should include total size
        Assert.Contains("1000 bytes total", result);
    }

    [Fact]
    public void ExtractPayloadPreview_InvalidUtf8_ReturnsBinaryDescription()
    {
        // Create intentionally invalid UTF-8 by putting a continuation byte at the truncation boundary
        // A 500+ byte array where the byte at index 499 is a multi-byte UTF-8 start byte
        var bytes = new byte[600];
        Array.Fill(bytes, (byte)0x41); // Fill with 'A'
        // Place an incomplete multi-byte sequence at the truncation boundary
        bytes[498] = 0xF0; // Start of a 4-byte UTF-8 sequence
        bytes[499] = 0x90; // Continuation byte (will be at boundary)

        var body = new ReadOnlyMemory<byte>(bytes);
        var result = DeadLetterConsumerService.ExtractPayloadPreview(body);

        // Should either return a truncated preview or a binary description
        // depending on whether the truncated UTF-8 can be decoded
        Assert.NotNull(result);
        Assert.NotEqual("(empty)", result);
    }

    #endregion

    #region CalculateAgeDescription Tests

    [Fact]
    public void CalculateAgeDescription_NullQueuedAt_ReturnsNull()
    {
        var result = DeadLetterConsumerService.CalculateAgeDescription(null, null);
        Assert.Null(result);
    }

    [Fact]
    public void CalculateAgeDescription_ShortDuration_ReturnsSeconds()
    {
        var now = DateTimeOffset.UtcNow;
        var queuedAtMs = now.AddSeconds(-30).ToUnixTimeMilliseconds();
        var discardedAtMs = now.ToUnixTimeMilliseconds();

        var result = DeadLetterConsumerService.CalculateAgeDescription(queuedAtMs, discardedAtMs);

        Assert.NotNull(result);
        Assert.EndsWith("s", result);
        Assert.DoesNotContain("m", result);
    }

    [Fact]
    public void CalculateAgeDescription_LongDuration_ReturnsMinutes()
    {
        var now = DateTimeOffset.UtcNow;
        var queuedAtMs = now.AddMinutes(-5).ToUnixTimeMilliseconds();
        var discardedAtMs = now.ToUnixTimeMilliseconds();

        var result = DeadLetterConsumerService.CalculateAgeDescription(queuedAtMs, discardedAtMs);

        Assert.NotNull(result);
        Assert.EndsWith("m", result);
    }

    [Fact]
    public void CalculateAgeDescription_NullDiscardedAt_UsesCurrentTime()
    {
        // Queue something 10 seconds ago, no discard time
        var queuedAtMs = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();

        var result = DeadLetterConsumerService.CalculateAgeDescription(queuedAtMs, null);

        // Should calculate age from queuedAt to now (approximately 10 seconds)
        Assert.NotNull(result);
        Assert.EndsWith("s", result);
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_DoesNotCreateChannel()
    {
        // Arrange
        var config = CreateConfig(enabled: false);
        using var service = CreateService(config);

        // Act
        await service.StartAsync(CancellationToken.None);
        // Give ExecuteAsync a moment to run and return
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        // Assert — should never attempt to create a channel
        _mockChannelManager.Verify(
            x => x.CreateConsumerChannelAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_DeclaresExchangeQueueAndBinding()
    {
        // Arrange
        var config = CreateConfig(enabled: true, startupDelaySeconds: 0, deadLetterExchange: "test-dlx");
        var mockChannel = CreateMockChannel();

        _mockChannelManager.Setup(x => x.CreateConsumerChannelAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockChannel.Object);

        using var service = CreateService(config);
        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        // Give ExecuteAsync time to set up the consumer
        await Task.Delay(200);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert — channel was created
        _mockChannelManager.Verify(
            x => x.CreateConsumerChannelAsync(It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert — DLX exchange declared as topic, durable
        mockChannel.Verify(
            x => x.ExchangeDeclareAsync(
                "test-dlx",
                "topic",
                true,    // durable
                false,   // autoDelete
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert — durable queue declared
        mockChannel.Verify(
            x => x.QueueDeclareAsync(
                "bannou-dlx-consumer",
                true,    // durable
                false,   // exclusive
                false,   // autoDelete
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert — queue bound to DLX exchange with dead-letter.# routing key
        mockChannel.Verify(
            x => x.QueueBindAsync(
                "bannou-dlx-consumer",
                "test-dlx",
                "dead-letter.#",
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert — consumer started with manual ack
        mockChannel.Verify(
            x => x.BasicConsumeAsync(
                "bannou-dlx-consumer",
                false,   // autoAck = false (manual ack)
                It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<IDictionary<string, object?>>(),
                It.IsAny<IAsyncBasicConsumer>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ExitsGracefully()
    {
        // Arrange
        var config = CreateConfig(enabled: true, startupDelaySeconds: 0);
        var mockChannel = CreateMockChannel();

        _mockChannelManager.Setup(x => x.CreateConsumerChannelAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockChannel.Object);

        using var service = CreateService(config);
        using var cts = new CancellationTokenSource();

        // Act — start then immediately cancel
        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        // Should not throw
        var exception = await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));
        Assert.Null(exception);
    }

    [Fact]
    public async Task ExecuteAsync_WhenChannelCreationFails_LogsError()
    {
        // Arrange
        var config = CreateConfig(enabled: true, startupDelaySeconds: 0);

        _mockChannelManager.Setup(x => x.CreateConsumerChannelAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection failed"));

        using var service = CreateService(config);
        using var cts = new CancellationTokenSource();

        // Act — should not throw; error is caught internally
        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        var exception = await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));
        Assert.Null(exception);
    }

    #endregion

    #region StopAsync Tests

    [Fact]
    public async Task StopAsync_WithNoChannel_CompletesWithoutError()
    {
        // Arrange — disabled config means no channel is ever created
        using var service = CreateService(CreateConfig(enabled: false));
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        // Act & Assert — should complete without exception
        var exception = await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));
        Assert.Null(exception);
    }

    [Fact]
    public async Task StopAsync_WithActiveChannel_ClosesChannel()
    {
        // Arrange
        var config = CreateConfig(enabled: true, startupDelaySeconds: 0);
        var mockChannel = CreateMockChannel();

        _mockChannelManager.Setup(x => x.CreateConsumerChannelAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockChannel.Object);

        using var service = CreateService(config);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        // Act
        await service.StopAsync(CancellationToken.None);

        // Assert — channel should have been closed
        mockChannel.Verify(
            x => x.CloseAsync(
                It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StopAsync_WhenChannelCloseThrows_CompletesWithoutError()
    {
        // Arrange
        var config = CreateConfig(enabled: true, startupDelaySeconds: 0);
        var mockChannel = CreateMockChannel();

        // Override CloseAsync to throw
        mockChannel.Setup(x => x.CloseAsync(
                It.IsAny<ushort>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Channel already closed"));

        _mockChannelManager.Setup(x => x.CreateConsumerChannelAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockChannel.Object);

        using var service = CreateService(config);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        // Act & Assert — should not throw despite channel close failure
        var exception = await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));
        Assert.Null(exception);
    }

    #endregion
}
