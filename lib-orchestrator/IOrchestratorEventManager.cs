using BeyondImmersion.BannouService.Orchestrator;

namespace LibOrchestrator;

/// <summary>
/// Interface for managing direct RabbitMQ connections for orchestrator service.
/// Enables unit testing through mocking.
/// </summary>
public interface IOrchestratorEventManager : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Event raised when a heartbeat is received from RabbitMQ.
    /// </summary>
    event Action<ServiceHeartbeatEvent>? HeartbeatReceived;

    /// <summary>
    /// Event raised when a service mapping event is received from RabbitMQ.
    /// Used to update NGINX routing when topology changes.
    /// </summary>
    event Action<ServiceMappingEvent>? ServiceMappingReceived;

    /// <summary>
    /// Initialize RabbitMQ connection with wait-on-startup retry logic.
    /// Uses exponential backoff to handle infrastructure startup delays.
    /// </summary>
    /// <returns>True if connection successful, false otherwise.</returns>
    Task<bool> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Publish service restart event to RabbitMQ.
    /// </summary>
    Task PublishServiceRestartEventAsync(ServiceRestartEvent restartEvent);

    /// <summary>
    /// Publish service mapping event to RabbitMQ.
    /// Used when topology changes to notify all bannou instances of new service-to-app-id mappings.
    /// </summary>
    Task PublishServiceMappingEventAsync(ServiceMappingEvent mappingEvent);

    /// <summary>
    /// Publish deployment event to RabbitMQ.
    /// Used to broadcast deployment lifecycle events (started, completed, failed, topology-changed).
    /// Topic: bannou-deployment-events
    /// </summary>
    Task PublishDeploymentEventAsync(DeploymentEvent deploymentEvent);

    /// <summary>
    /// Check if RabbitMQ connection is healthy.
    /// </summary>
    (bool IsHealthy, string? Message) CheckHealth();
}

/// <summary>
/// Event published when service-to-app-id mappings change.
/// All bannou instances consume this to update their ServiceAppMappingResolver.
/// </summary>
public class ServiceMappingEvent
{
    /// <summary>Unique event identifier.</summary>
    public required string EventId { get; set; }

    /// <summary>Timestamp of the event.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Service name (e.g., "auth", "accounts", "behavior").</summary>
    public required string ServiceName { get; set; }

    /// <summary>Target Dapr app-id for the service (e.g., "bannou-auth", "npc-omega").</summary>
    public required string AppId { get; set; }

    /// <summary>Action being performed.</summary>
    public required ServiceMappingAction Action { get; set; }

    /// <summary>Optional geographic or functional region for routing.</summary>
    public string? Region { get; set; }

    /// <summary>Optional priority weight for load balancing.</summary>
    public int? Priority { get; set; }
}

/// <summary>
/// Action for a service mapping event.
/// </summary>
public enum ServiceMappingAction
{
    /// <summary>Register a new service mapping.</summary>
    Register,

    /// <summary>Update an existing service mapping.</summary>
    Update,

    /// <summary>Unregister a service mapping (fall back to default).</summary>
    Unregister
}
