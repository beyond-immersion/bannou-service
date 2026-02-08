namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Abstraction for sending emails. Implementations may use SendGrid, AWS SES, SMTP, etc.
/// Default implementation (ConsoleEmailService) logs to console for development.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email to the specified recipient.
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Email subject line.</param>
    /// <param name="body">Email body content (plain text).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
