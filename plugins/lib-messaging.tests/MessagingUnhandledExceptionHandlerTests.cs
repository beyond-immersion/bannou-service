using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Messaging.Tests;

/// <summary>
/// Unit tests for MessagingUnhandledExceptionHandler.
/// Tests verify error event publishing and AsyncLocal-based cycle prevention.
/// </summary>
public class MessagingUnhandledExceptionHandlerTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<MessagingUnhandledExceptionHandler>> _mockLogger;

    public MessagingUnhandledExceptionHandlerTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<MessagingUnhandledExceptionHandler>>();
    }

    private MessagingUnhandledExceptionHandler CreateHandler()
    {
        return new MessagingUnhandledExceptionHandler(
            _mockMessageBus.Object,
            _mockLogger.Object);
    }

    #region HandleAsync Tests

    /// <summary>
    /// Verifies that HandleAsync publishes an error event via TryPublishErrorAsync.
    /// </summary>
    [Fact]
    public async Task HandleAsync_PublishesErrorEvent()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new InvalidOperationException("test error");
        var context = new UnhandledExceptionContext(
            ServiceName: "account",
            Operation: "GetAccount",
            Endpoint: "post:account/get",
            CorrelationId: Guid.NewGuid());

        _mockMessageBus
            .Setup(m => m.TryPublishErrorAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await handler.HandleAsync(exception, context, CancellationToken.None);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "account",
            "GetAccount",
            "InvalidOperationException",
            "test error",
            It.IsAny<string?>(),
            "post:account/get",
            ServiceErrorEventSeverity.Error,
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            context.CorrelationId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that HandleAsync resets the publishing flag after completion,
    /// allowing subsequent calls to publish normally.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ResetsFlag_AllowsSubsequentCalls()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new Exception("error 1");
        var context = new UnhandledExceptionContext("svc", "op");

        _mockMessageBus
            .Setup(m => m.TryPublishErrorAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(), It.IsAny<object?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act — two sequential calls
        await handler.HandleAsync(exception, context, CancellationToken.None);
        await handler.HandleAsync(new Exception("error 2"), context, CancellationToken.None);

        // Assert — both should publish (flag was reset after first call)
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(), It.IsAny<object?>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// Verifies that if TryPublishErrorAsync throws, the exception is caught
    /// and logged as a warning (defensive catch).
    /// </summary>
    [Fact]
    public async Task HandleAsync_PublishThrows_LogsWarningAndDoesNotPropagate()
    {
        // Arrange
        var handler = CreateHandler();
        var exception = new Exception("original error");
        var context = new UnhandledExceptionContext("account", "GetAccount");

        _mockMessageBus
            .Setup(m => m.TryPublishErrorAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(), It.IsAny<object?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("publish failed"));

        // Act — should not throw
        await handler.HandleAsync(exception, context, CancellationToken.None);

        // Assert — publish was attempted
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(), It.IsAny<object?>(),
            It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that the flag is reset even when TryPublishErrorAsync throws,
    /// so subsequent calls can publish normally.
    /// </summary>
    [Fact]
    public async Task HandleAsync_PublishThrows_StillResetsFlag()
    {
        // Arrange
        var handler = CreateHandler();
        var context = new UnhandledExceptionContext("svc", "op");
        var callCount = 0;

        _mockMessageBus
            .Setup(m => m.TryPublishErrorAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(), It.IsAny<object?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, string, string, string, string?, string?,
                    ServiceErrorEventSeverity, object?, string?, Guid?, CancellationToken>(
                (_, _, _, _, _, _, _, _, _, _, _) =>
                {
                    callCount++;
                    if (callCount == 1) throw new Exception("first publish fails");
                    return Task.FromResult(true);
                });

        // Act — first call fails, second should still work
        await handler.HandleAsync(new Exception("error 1"), context, CancellationToken.None);
        await handler.HandleAsync(new Exception("error 2"), context, CancellationToken.None);

        // Assert — both publish attempts were made (flag was reset despite first failure)
        Assert.Equal(2, callCount);
    }

    /// <summary>
    /// Verifies that HandleAsync passes the exception stack trace to TryPublishErrorAsync.
    /// </summary>
    [Fact]
    public async Task HandleAsync_PassesStackTrace()
    {
        // Arrange
        var handler = CreateHandler();
        string? capturedStack = null;

        _mockMessageBus
            .Setup(m => m.TryPublishErrorAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<ServiceErrorEventSeverity>(), It.IsAny<object?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string?, string?,
                    ServiceErrorEventSeverity, object?, string?, Guid?, CancellationToken>(
                (_, _, _, _, _, _, _, _, stack, _, _) => capturedStack = stack)
            .ReturnsAsync(true);

        // Create exception with a stack trace by throwing and catching
        Exception thrownException;
        try
        {
            throw new InvalidOperationException("with stack");
        }
        catch (Exception ex)
        {
            thrownException = ex;
        }

        var context = new UnhandledExceptionContext("svc", "op");

        // Act
        await handler.HandleAsync(thrownException, context, CancellationToken.None);

        // Assert — stack trace contains method/class names, not the exception type
        Assert.NotNull(capturedStack);
        Assert.Contains("HandleAsync_PassesStackTrace", capturedStack);
    }

    #endregion
}
