using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Development-only email service that logs email content to console.
/// Replace with a concrete provider (SendGrid, AWS SES, etc.) for production.
/// </summary>
public class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _logger;

    /// <summary>
    /// Initializes a new instance of ConsoleEmailService.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public ConsoleEmailService(ILogger<ConsoleEmailService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // IMPLEMENTATION TENETS: async methods must use await

        // LogDebug to prevent sensitive content (reset tokens, etc.) from appearing in production log aggregation
        _logger.LogDebug(
            "=== EMAIL (CONSOLE) ===\n" +
            "To: {To}\n" +
            "Subject: {Subject}\n" +
            "Body:\n{Body}\n" +
            "=== END EMAIL ===",
            to, subject, body);
    }
}
