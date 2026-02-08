using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Email service implementation using the SendGrid API.
/// Requires a valid SendGrid API key configured via AUTH_SENDGRID_API_KEY.
/// </summary>
public class SendGridEmailService : IEmailService
{
    private readonly ISendGridClient _client;
    private readonly EmailAddress _from;
    private readonly ILogger<SendGridEmailService> _logger;

    /// <summary>
    /// Initializes a new instance of SendGridEmailService.
    /// </summary>
    /// <param name="client">SendGrid client instance.</param>
    /// <param name="from">Sender email address.</param>
    /// <param name="logger">Logger instance.</param>
    public SendGridEmailService(
        ISendGridClient client,
        EmailAddress from,
        ILogger<SendGridEmailService> logger)
    {
        _client = client;
        _from = from;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        var msg = new SendGridMessage
        {
            From = _from,
            Subject = subject,
            PlainTextContent = body
        };
        msg.AddTo(new EmailAddress(to));

        var response = await _client.SendEmailAsync(msg, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Body.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "SendGrid email delivery failed with status {StatusCode} to {Recipient}: {ResponseBody}",
                response.StatusCode, to, responseBody);
            throw new InvalidOperationException(
                $"SendGrid email delivery failed with status {response.StatusCode}");
        }

        _logger.LogInformation("Email sent via SendGrid to {Recipient}, subject: {Subject}", to, subject);
    }
}
