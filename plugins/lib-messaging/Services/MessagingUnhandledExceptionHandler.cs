#nullable enable

using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Messaging.Services;

/// <summary>
/// Unhandled exception handler that publishes <see cref="ServiceErrorEvent"/> via <see cref="IMessageBus"/>.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="AsyncLocal{T}"/> to detect and prevent infinite recursion: if publishing
/// the error event itself triggers an unhandled exception, the handler detects re-entry
/// and bails out with a log message instead of recursing.
/// </para>
/// <para>
/// Registered by lib-messaging as part of the composite <see cref="IUnhandledExceptionHandler"/>
/// pattern. Fires after <c>LoggingUnhandledExceptionHandler</c> (which is registered in
/// bannou-service before any plugins load).
/// </para>
/// </remarks>
public sealed class MessagingUnhandledExceptionHandler : IUnhandledExceptionHandler
{
    private static readonly AsyncLocal<bool> _isPublishing = new();

    private readonly IMessageBus _messageBus;
    private readonly ILogger<MessagingUnhandledExceptionHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="MessagingUnhandledExceptionHandler"/>.
    /// </summary>
    /// <param name="messageBus">Message bus for publishing error events.</param>
    /// <param name="logger">Logger for recording publishing failures and cycle detection.</param>
    public MessagingUnhandledExceptionHandler(
        IMessageBus messageBus,
        ILogger<MessagingUnhandledExceptionHandler> logger)
    {
        _messageBus = messageBus;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        Exception exception,
        UnhandledExceptionContext context,
        CancellationToken cancellationToken)
    {
        // Cycle protection: if we're already in an error-publishing path, don't recurse
        if (_isPublishing.Value)
        {
            _logger.LogWarning(
                "Skipping error event publish for {ServiceName}.{Operation} — already in error-publishing path (cycle prevention)",
                context.ServiceName,
                context.Operation);
            return;
        }

        _isPublishing.Value = true;
        try
        {
            await _messageBus.TryPublishErrorAsync(
                context.ServiceName,
                context.Operation,
                exception.GetType().Name,
                exception.Message,
                endpoint: context.Endpoint,
                severity: ServiceErrorEventSeverity.Error,
                stack: exception.StackTrace,
                correlationId: context.CorrelationId,
                cancellationToken: cancellationToken);
        }
        catch (Exception publishException)
        {
            // TryPublishErrorAsync already has its own try/catch, but defend against the unexpected
            _logger.LogWarning(
                publishException,
                "Failed to publish error event for {ServiceName}.{Operation}",
                context.ServiceName,
                context.Operation);
        }
        finally
        {
            _isPublishing.Value = false;
        }
    }
}
