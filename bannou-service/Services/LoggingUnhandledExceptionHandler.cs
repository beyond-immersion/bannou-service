#nullable enable

using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Default unhandled exception handler that logs the exception with structured context.
/// Always registered before any plugin handlers, ensuring exceptions are logged
/// even if all other handlers fail.
/// </summary>
public sealed class LoggingUnhandledExceptionHandler : IUnhandledExceptionHandler
{
    private readonly ILogger<LoggingUnhandledExceptionHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="LoggingUnhandledExceptionHandler"/>.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public LoggingUnhandledExceptionHandler(ILogger<LoggingUnhandledExceptionHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        Exception exception,
        UnhandledExceptionContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogError(
            exception,
            "Unhandled exception in {ServiceName}.{Operation} (endpoint: {Endpoint}, correlationId: {CorrelationId})",
            context.ServiceName,
            context.Operation,
            context.Endpoint ?? "none",
            context.CorrelationId?.ToString() ?? "none");

        return Task.CompletedTask;
    }
}
