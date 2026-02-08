using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Net;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Unit tests for email service implementations.
/// SendGridEmailService is fully testable via the ISendGridClient interface.
/// SmtpEmailService creates internal SmtpClient connections and is tested via integration tests.
/// </summary>
public class EmailServiceTests
{
    #region ConsoleEmailService Tests

    [Fact]
    public async Task ConsoleEmailService_SendAsync_ShouldLogAndNotThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ConsoleEmailService>>();
        var service = new ConsoleEmailService(mockLogger.Object);

        // Act
        await service.SendAsync("user@example.com", "Test Subject", "Test Body");

        // Assert - verify logging occurred (LogDebug is invoked once)
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("user@example.com")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region SendGridEmailService Tests

    [Fact]
    public async Task SendGridEmailService_SendAsync_WithSuccessResponse_ShouldSucceed()
    {
        // Arrange
        var mockClient = new Mock<ISendGridClient>();
        var from = new EmailAddress("noreply@bannou.test", "Bannou Test");
        var mockLogger = new Mock<ILogger<SendGridEmailService>>();

        mockClient
            .Setup(c => c.SendEmailAsync(It.IsAny<SendGridMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Response(HttpStatusCode.Accepted, new StringContent(""), null));

        var service = new SendGridEmailService(mockClient.Object, from, mockLogger.Object);

        // Act
        await service.SendAsync("user@example.com", "Password Reset", "Click here to reset");

        // Assert
        mockClient.Verify(
            c => c.SendEmailAsync(
                It.Is<SendGridMessage>(msg =>
                    msg.From.Email == "noreply@bannou.test" &&
                    msg.Subject == "Password Reset" &&
                    msg.PlainTextContent == "Click here to reset"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendGridEmailService_SendAsync_WithFailureResponse_ShouldThrow()
    {
        // Arrange
        var mockClient = new Mock<ISendGridClient>();
        var from = new EmailAddress("noreply@bannou.test", "Bannou Test");
        var mockLogger = new Mock<ILogger<SendGridEmailService>>();

        mockClient
            .Setup(c => c.SendEmailAsync(It.IsAny<SendGridMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Response(HttpStatusCode.Unauthorized, new StringContent("Invalid API key"), null));

        var service = new SendGridEmailService(mockClient.Object, from, mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync("user@example.com", "Subject", "Body"));

        Assert.Contains("Unauthorized", exception.Message);
    }

    [Fact]
    public async Task SendGridEmailService_SendAsync_WithFailureResponse_ShouldLogError()
    {
        // Arrange
        var mockClient = new Mock<ISendGridClient>();
        var from = new EmailAddress("noreply@bannou.test", "Bannou Test");
        var mockLogger = new Mock<ILogger<SendGridEmailService>>();

        mockClient
            .Setup(c => c.SendEmailAsync(It.IsAny<SendGridMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Response(HttpStatusCode.Forbidden, new StringContent("Forbidden"), null));

        var service = new SendGridEmailService(mockClient.Object, from, mockLogger.Object);

        // Act - swallow the expected exception
        try
        {
            await service.SendAsync("user@example.com", "Subject", "Body");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }

        // Assert - error was logged before throwing
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SendGrid email delivery failed")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendGridEmailService_SendAsync_WithSuccessResponse_ShouldLogInformation()
    {
        // Arrange
        var mockClient = new Mock<ISendGridClient>();
        var from = new EmailAddress("noreply@bannou.test");
        var mockLogger = new Mock<ILogger<SendGridEmailService>>();

        mockClient
            .Setup(c => c.SendEmailAsync(It.IsAny<SendGridMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Response(HttpStatusCode.OK, new StringContent(""), null));

        var service = new SendGridEmailService(mockClient.Object, from, mockLogger.Object);

        // Act
        await service.SendAsync("user@example.com", "Test Subject", "Test Body");

        // Assert
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Email sent via SendGrid")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendGridEmailService_SendAsync_ShouldPassCancellationToken()
    {
        // Arrange
        var mockClient = new Mock<ISendGridClient>();
        var from = new EmailAddress("noreply@bannou.test");
        var mockLogger = new Mock<ILogger<SendGridEmailService>>();
        using var cts = new CancellationTokenSource();

        mockClient
            .Setup(c => c.SendEmailAsync(It.IsAny<SendGridMessage>(), cts.Token))
            .ReturnsAsync(new Response(HttpStatusCode.Accepted, new StringContent(""), null));

        var service = new SendGridEmailService(mockClient.Object, from, mockLogger.Object);

        // Act
        await service.SendAsync("user@example.com", "Subject", "Body", cts.Token);

        // Assert - verify the cancellation token was forwarded
        mockClient.Verify(
            c => c.SendEmailAsync(It.IsAny<SendGridMessage>(), cts.Token),
            Times.Once);
    }

    [Fact]
    public async Task SendGridEmailService_SendAsync_ShouldSetFromAddress()
    {
        // Arrange
        var mockClient = new Mock<ISendGridClient>();
        var from = new EmailAddress("custom-sender@bannou.test", "Custom Sender");
        var mockLogger = new Mock<ILogger<SendGridEmailService>>();

        mockClient
            .Setup(c => c.SendEmailAsync(It.IsAny<SendGridMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Response(HttpStatusCode.Accepted, new StringContent(""), null));

        var service = new SendGridEmailService(mockClient.Object, from, mockLogger.Object);

        // Act
        await service.SendAsync("recipient@example.com", "Subject", "Body");

        // Assert - verify the sender address was used
        mockClient.Verify(
            c => c.SendEmailAsync(
                It.Is<SendGridMessage>(msg =>
                    msg.From.Email == "custom-sender@bannou.test" &&
                    msg.From.Name == "Custom Sender"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
