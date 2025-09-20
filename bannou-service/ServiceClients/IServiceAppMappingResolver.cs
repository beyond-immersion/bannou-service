namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Resolves service names to Dapr app-ids for distributed service routing.
/// Supports dynamic mapping via RabbitMQ events for production scaling.
/// </summary>
public interface IServiceAppMappingResolver
{
    /// <summary>
    /// Gets the Dapr app-id for the specified service name.
    /// Defaults to "bannou" (omnipotent local node) but can be overridden
    /// by service mapping events from RabbitMQ.
    /// </summary>
    /// <param name="serviceName">The service name (e.g., "accounts", "character-agent"). Can be null.</param>
    /// <returns>The Dapr app-id to route requests to</returns>
    string GetAppIdForService(string? serviceName);

    /// <summary>
    /// Updates the service mapping from RabbitMQ service discovery events.
    /// </summary>
    /// <param name="serviceName">The service name</param>
    /// <param name="appId">The Dapr app-id where this service is running</param>
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
}
