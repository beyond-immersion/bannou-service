using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Extension methods for publishing error events from BackgroundService workers.
/// Workers cannot inject scoped services (like IMessageBus) directly because they
/// are singletons. This helper creates a scope, resolves IMessageBus, publishes
/// the error event, and swallows any publish failures to protect the worker loop.
/// </summary>
public static class WorkerErrorPublisher
{
    /// <summary>
    /// Publishes an error event from a background worker, handling scope creation
    /// and failure isolation. Safe to call from any BackgroundService catch block.
    /// </summary>
    /// <param name="serviceProvider">The root service provider for scope creation.</param>
    /// <param name="serviceName">The service name (e.g., "subscription", "matchmaking").</param>
    /// <param name="operationName">The operation that failed (e.g., "ExpirationCheck", "ProcessCycle").</param>
    /// <param name="exception">The exception that occurred.</param>
    /// <param name="logger">Logger for recording publish failures (at Debug level).</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task TryPublishWorkerErrorAsync(
        this IServiceProvider serviceProvider,
        string serviceName,
        string operationName,
        Exception exception,
        ILogger logger,
        CancellationToken ct = default)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await messageBus.TryPublishErrorAsync(
                serviceName,
                operationName,
                exception.GetType().Name,
                exception.Message,
                severity: ServiceErrorEventSeverity.Error,
                stack: exception.StackTrace,
                cancellationToken: ct);
        }
        catch (Exception pubEx)
        {
            logger.LogDebug(pubEx, "Failed to publish worker error event for {Service}.{Operation}",
                serviceName, operationName);
        }
    }
}
