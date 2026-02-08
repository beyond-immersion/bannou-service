using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Email service implementation using AWS Simple Email Service (SES) v2 API.
/// Requires valid AWS credentials configured via AUTH_SES_ACCESS_KEY_ID and AUTH_SES_SECRET_ACCESS_KEY.
/// </summary>
public class SesEmailService : IEmailService
{
    private readonly IAmazonSimpleEmailServiceV2 _client;
    private readonly string _fromAddress;
    private readonly ILogger<SesEmailService> _logger;

    /// <summary>
    /// Initializes a new instance of SesEmailService.
    /// </summary>
    /// <param name="client">AWS SES v2 client instance.</param>
    /// <param name="fromAddress">Sender email address (must be verified in SES).</param>
    /// <param name="logger">Logger instance.</param>
    public SesEmailService(
        IAmazonSimpleEmailServiceV2 client,
        string fromAddress,
        ILogger<SesEmailService> logger)
    {
        _client = client;
        _fromAddress = fromAddress;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        var request = new SendEmailRequest
        {
            FromEmailAddress = _fromAddress,
            Destination = new Destination
            {
                ToAddresses = [to]
            },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = subject },
                    Body = new Body
                    {
                        Text = new Content { Data = body }
                    }
                }
            }
        };

        var response = await _client.SendEmailAsync(request, cancellationToken);

        if ((int)response.HttpStatusCode < 200 || (int)response.HttpStatusCode >= 300)
        {
            _logger.LogError(
                "AWS SES email delivery failed with status {StatusCode} to {Recipient}, MessageId: {MessageId}",
                response.HttpStatusCode, to, response.MessageId);
            throw new InvalidOperationException(
                $"AWS SES email delivery failed with status {response.HttpStatusCode}");
        }

        _logger.LogInformation(
            "Email sent via AWS SES to {Recipient}, subject: {Subject}, MessageId: {MessageId}",
            to, subject, response.MessageId);
    }
}
