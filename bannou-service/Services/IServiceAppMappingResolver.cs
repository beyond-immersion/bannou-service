namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Event arguments for service mapping changes.
/// </summary>
public class ServiceMappingChangedEventArgs : EventArgs
{
    /// <summary>The service name that changed.</summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>The new app-id for the service (null if removed).</summary>
    public string? NewAppId { get; init; }

    /// <summary>The previous app-id (null if new mapping).</summary>
    public string? PreviousAppId { get; init; }
}

/// <summary>
/// Resolves service names to app-ids for distributed service routing.
/// Supports dynamic mapping via RabbitMQ events for production scaling.
/// </summary>
public interface IServiceAppMappingResolver
{
    /// <summary>
    /// Event raised when a service mapping changes.
    /// Used by ServiceHeartbeatManager to stop heartbeating services routed elsewhere.
    /// </summary>
    event EventHandler<ServiceMappingChangedEventArgs>? MappingChanged;
    /// <summary>
    /// Gets the app-id for the specified service name.
    /// Defaults to "bannou" (omnipotent local node) but can be overridden
    /// by service mapping events from RabbitMQ.
    /// </summary>
    /// <param name="serviceName">The service name (e.g., "account", "character-agent"). Can be null.</param>
    /// <returns>The app-id to route requests to</returns>
    string GetAppIdForService(string? serviceName);

    /// <summary>
    /// Updates the service mapping from RabbitMQ service discovery events.
    /// </summary>
    /// <param name="serviceName">The service name</param>
    /// <param name="appId">The app-id where this service is running</param>
    void UpdateServiceMapping(string serviceName, string appId);

    /// <summary>
    /// Removes a service mapping (when service goes offline).
    /// </summary>
    /// <param name="serviceName">The service name to remove</param>
    void RemoveServiceMapping(string serviceName);

    /// <summary>
    /// Gets all current service mappings for debugging/monitoring.
    /// </summary>
    /// <returns>Dictionary of service name to app-id mappings</returns>
    IReadOnlyDictionary<string, string> GetAllMappings();

    /// <summary>
    /// Imports multiple service mappings at once, typically from a source container.
    /// Used during startup of containers deployed by orchestrator.
    /// </summary>
    /// <param name="mappings">The mappings to import</param>
    void ImportMappings(IReadOnlyDictionary<string, string> mappings);

    /// <summary>
    /// Atomically replaces all service mappings with a new full state from Orchestrator.
    /// Implements version checking to reject out-of-order events.
    /// </summary>
    /// <param name="mappings">Complete dictionary of serviceName -> appId mappings</param>
    /// <param name="defaultAppId">Default app-id for unmapped services</param>
    /// <param name="version">Monotonically increasing version number</param>
    /// <returns>True if mappings were applied, false if version was stale</returns>
    bool ReplaceAllMappings(IReadOnlyDictionary<string, string> mappings, string defaultAppId, long version);

    /// <summary>
    /// Gets the current version of the service mappings.
    /// </summary>
    long CurrentVersion { get; }
}
