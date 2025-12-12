using Dapr.Client;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// Helper publisher for emitting ServiceErrorEvent to the shared error topic.
/// Use for unexpected/internal failures only (not user/input errors).
/// </summary>
public class ServiceErrorPublisher : EventPublisherBase
{
    private const string SERVICE_ERROR_TOPIC = "service.error";

    /// <inheritdoc/>
    public ServiceErrorPublisher(DaprClient daprClient, ILogger<ServiceErrorPublisher> logger)
        : base(daprClient, logger)
    {
    }

    /// <summary>
    /// Publishes a structured service error event.
    /// </summary>
    public Task<bool> PublishErrorAsync(
        string serviceId,
        string operation,
        string errorType,
        string message,
        string? appId = null,
        string? correlationId = null,
        ServiceErrorEventSeverity severity = ServiceErrorEventSeverity.Error,
        object? details = null,
        string? dependency = null,
        string? endpoint = null,
        string? stack = null,
        float cpuUsage = 0,
        float memoryUsage = 0,
        CancellationToken cancellationToken = default)
    {
        var evt = new ServiceErrorEvent
        {
            EventId = NewEventId(),
            Timestamp = CurrentTimestamp(),
            ServiceId = serviceId,
            AppId = appId ?? Environment.GetEnvironmentVariable("DAPR_APP_ID") ?? "bannou",
            Operation = operation,
            ErrorType = errorType,
            Message = message,
            CorrelationId = correlationId,
            Severity = severity,
            Details = details,
            Dependency = dependency,
            Endpoint = endpoint,
            Stack = stack,
            CpuUsage = cpuUsage,
            MemoryUsage = memoryUsage
        };

        return PublishEventAsync(SERVICE_ERROR_TOPIC, evt, cancellationToken);
    }
}
