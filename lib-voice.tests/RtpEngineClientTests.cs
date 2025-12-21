using BeyondImmersion.BannouService.Voice.Clients;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Voice.Tests;

/// <summary>
/// Unit tests for RtpEngineClient.
/// Note: Full protocol testing requires actual RTPEngine infrastructure (integration tests).
/// </summary>
public class RtpEngineClientTests : IDisposable
{
    private readonly Mock<ILogger<RtpEngineClient>> _mockLogger;

    public RtpEngineClientTests()
    {
        _mockLogger = new Mock<ILogger<RtpEngineClient>>();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        using var client = new RtpEngineClient("127.0.0.1", 22222, _mockLogger.Object);

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_WithNullHost_ShouldThrowArgumentException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() => new RtpEngineClient(
            null!,
            22222,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithEmptyHost_ShouldThrowArgumentException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() => new RtpEngineClient(
            string.Empty,
            22222,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RtpEngineClient(
            "127.0.0.1",
            22222,
            null!));
    }

    [Fact]
    public void Constructor_WithCustomTimeout_ShouldNotThrow()
    {
        // Arrange & Act
        using var client = new RtpEngineClient("127.0.0.1", 22222, _mockLogger.Object, timeoutSeconds: 10);

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_WithZeroPort_ShouldNotThrow()
    {
        // Arrange & Act - Port 0 is technically valid (OS assigns)
        using var client = new RtpEngineClient("127.0.0.1", 0, _mockLogger.Object);

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_WithMaxPort_ShouldNotThrow()
    {
        // Arrange & Act
        using var client = new RtpEngineClient("127.0.0.1", 65535, _mockLogger.Object);

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_WithUnresolvableHostname_ShouldThrowArgumentException()
    {
        // Arrange, Act & Assert
        // Unresolvable hostnames throw ArgumentException after DNS lookup fails
        Assert.Throws<ArgumentException>(() => new RtpEngineClient(
            "this-hostname-definitely-does-not-exist.invalid",
            22222,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithLocalhostHostname_ShouldResolveSuccessfully()
    {
        // Arrange & Act - "localhost" should resolve to 127.0.0.1
        using var client = new RtpEngineClient("localhost", 22222, _mockLogger.Object);

        // Assert
        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_WithIPv6Localhost_ShouldNotThrow()
    {
        // Arrange & Act - IPv6 loopback address
        using var client = new RtpEngineClient("::1", 22222, _mockLogger.Object);

        // Assert
        Assert.NotNull(client);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var client = new RtpEngineClient("127.0.0.1", 22222, _mockLogger.Object);

        // Act & Assert - Should not throw
        client.Dispose();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var client = new RtpEngineClient("127.0.0.1", 22222, _mockLogger.Object);

        // Act & Assert - Multiple disposals should be safe
        client.Dispose();
        client.Dispose();
        client.Dispose();
    }

    [Fact]
    public async Task OperationAfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var client = new RtpEngineClient("127.0.0.1", 22222, _mockLogger.Object);
        client.Dispose();

        // Act & Assert
        // Note: IsHealthyAsync catches exceptions and returns false,
        // so we test with QueryAsync which propagates the exception
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            client.QueryAsync("test-call", CancellationToken.None));
    }

    [Fact]
    public async Task IsHealthyAsync_AfterDispose_ReturnsFalse()
    {
        // Arrange
        var client = new RtpEngineClient("127.0.0.1", 22222, _mockLogger.Object);
        client.Dispose();

        // Act - IsHealthyAsync catches ObjectDisposedException and returns false
        var result = await client.IsHealthyAsync(CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Response Model Tests

    [Fact]
    public void RtpEngineBaseResponse_IsSuccess_ReturnsTrue_WhenResultIsOk()
    {
        // Arrange
        var response = new RtpEngineOfferResponse { Result = "ok" };

        // Act & Assert
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public void RtpEngineBaseResponse_IsSuccess_ReturnsFalse_WhenResultIsError()
    {
        // Arrange
        var response = new RtpEngineOfferResponse { Result = "error" };

        // Act & Assert
        Assert.False(response.IsSuccess);
    }

    [Fact]
    public void RtpEngineQueryResponse_StreamCount_ReturnsZero_WhenStreamsNull()
    {
        // Arrange
        var response = new RtpEngineQueryResponse { Streams = null };

        // Act & Assert
        Assert.Equal(0, response.StreamCount);
    }

    [Fact]
    public void RtpEngineQueryResponse_StreamCount_ReturnsCorrectCount()
    {
        // Arrange
        var response = new RtpEngineQueryResponse
        {
            Streams = new Dictionary<string, object>
            {
                { "stream-1", new object() },
                { "stream-2", new object() }
            }
        };

        // Act & Assert
        Assert.Equal(2, response.StreamCount);
    }

    [Fact]
    public void RtpEngineOfferResponse_CanSetSdp()
    {
        // Arrange
        var sdp = "v=0\r\no=- 123 456 IN IP4 127.0.0.1\r\n";
        var response = new RtpEngineOfferResponse { Sdp = sdp };

        // Act & Assert
        Assert.Equal(sdp, response.Sdp);
    }

    [Fact]
    public void RtpEngineAnswerResponse_CanSetSdp()
    {
        // Arrange
        var sdp = "v=0\r\no=- 123 456 IN IP4 127.0.0.1\r\n";
        var response = new RtpEngineAnswerResponse { Sdp = sdp };

        // Act & Assert
        Assert.Equal(sdp, response.Sdp);
    }

    [Fact]
    public void RtpEnginePublishResponse_CanSetSdp()
    {
        // Arrange
        var sdp = "v=0\r\no=- 123 456 IN IP4 127.0.0.1\r\n";
        var response = new RtpEnginePublishResponse { Sdp = sdp };

        // Act & Assert
        Assert.Equal(sdp, response.Sdp);
    }

    [Fact]
    public void RtpEngineSubscribeResponse_CanSetSdp()
    {
        // Arrange
        var sdp = "v=0\r\no=- 123 456 IN IP4 127.0.0.1\r\n";
        var response = new RtpEngineSubscribeResponse { Sdp = sdp };

        // Act & Assert
        Assert.Equal(sdp, response.Sdp);
    }

    [Fact]
    public void RtpEngineBaseResponse_ErrorReason_CanBeSet()
    {
        // Arrange
        var response = new RtpEngineDeleteResponse
        {
            Result = "error",
            ErrorReason = "Call not found"
        };

        // Act & Assert
        Assert.Equal("Call not found", response.ErrorReason);
        Assert.False(response.IsSuccess);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task OfferAsync_WithAlreadyCancelledToken_ShouldThrowOperationCanceledException()
    {
        // Arrange
        using var client = new RtpEngineClient("127.0.0.1", 22222, _mockLogger.Object, timeoutSeconds: 1);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.OfferAsync("call-1", "from-1", "v=0\r\n", cancellationToken: cts.Token));
    }

    [Fact]
    public async Task QueryAsync_WithAlreadyCancelledToken_ShouldThrowOperationCanceledException()
    {
        // Arrange
        using var client = new RtpEngineClient("127.0.0.1", 22222, _mockLogger.Object, timeoutSeconds: 1);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.QueryAsync("call-1", cts.Token));
    }

    #endregion
}
