using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Email service implementation using SMTP via MailKit.
/// Requires a valid SMTP host configured via AUTH_SMTP_HOST.
/// </summary>
public class SmtpEmailService : IEmailService
{
    private readonly string _host;
    private readonly int _port;
    private readonly string? _username;
    private readonly string? _password;
    private readonly bool _useSsl;
    private readonly MailboxAddress _from;
    private readonly ILogger<SmtpEmailService> _logger;

    /// <summary>
    /// Initializes a new instance of SmtpEmailService.
    /// </summary>
    /// <param name="host">SMTP server hostname.</param>
    /// <param name="port">SMTP server port.</param>
    /// <param name="username">SMTP authentication username (null for anonymous).</param>
    /// <param name="password">SMTP authentication password (null for anonymous).</param>
    /// <param name="useSsl">Whether to use SSL/TLS.</param>
    /// <param name="from">Sender mailbox address.</param>
    /// <param name="logger">Logger instance.</param>
    public SmtpEmailService(
        string host,
        int port,
        string? username,
        string? password,
        bool useSsl,
        MailboxAddress from,
        ILogger<SmtpEmailService> logger)
    {
        _host = host;
        _port = port;
        _username = username;
        _password = password;
        _useSsl = useSsl;
        _from = from;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        using var message = new MimeMessage();
        message.From.Add(_from);
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(_host, _port, _useSsl, cancellationToken);

        if (!string.IsNullOrEmpty(_username))
        {
            await client.AuthenticateAsync(_username, _password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        _logger.LogInformation("Email sent via SMTP to {Recipient}, subject: {Subject}", to, subject);
    }
}
