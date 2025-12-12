using BeyondImmersion.BannouService.Events;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Centralized emitter for ServiceErrorEvent with guards against cascading failures.
/// </summary>
public class ErrorEventEmitter : IErrorEventEmitter
{
    private readonly ServiceErrorPublisher _publisher;
    private readonly ILogger<ErrorEventEmitter> _logger;

    /// <inheritdoc/>
    public ErrorEventEmitter(DaprClient daprClient, ILoggerFactory loggerFactory)
    {
        _publisher = new ServiceErrorPublisher(daprClient, loggerFactory.CreateLogger<ServiceErrorPublisher>());
        _logger = loggerFactory.CreateLogger<ErrorEventEmitter>();
    }

    /// <inheritdoc/>
    public async Task<bool> TryPublishAsync(
        string serviceId,
        string operation,
        string errorType,
        string message,
        string? dependency = null,
        string? endpoint = null,
        ServiceErrorEventSeverity severity = ServiceErrorEventSeverity.Error,
        object? details = null,
        string? stack = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _publisher.PublishErrorAsync(
                serviceId: serviceId,
                operation: operation,
                errorType: errorType,
                message: message,
                dependency: dependency,
                endpoint: endpoint,
                severity: severity,
                details: details,
                stack: stack,
                correlationId: correlationId,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Avoid cascading failures when pub/sub or Dapr is the culprit.
            _logger.LogWarning(ex, "Failed to publish ServiceErrorEvent for {ServiceId}/{Operation}", serviceId, operation);
            return false;
        }
    }
}
