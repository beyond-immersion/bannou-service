using BeyondImmersion.BannouService.Mesh;

namespace BeyondImmersion.BannouService.Mesh.Services;

/// <summary>
/// Interface for Mesh Redis operations.
/// Manages service endpoint registration, health status, and routing information.
/// Uses direct Redis connection (NOT via mesh) to avoid circular dependencies.
/// </summary>
public interface IMeshRedisManager : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Initialize Redis connection with wait-on-startup retry logic.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if connection was established successfully.</returns>
    Task<bool> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if Redis is currently connected and healthy.
    /// </summary>
    /// <returns>Health status tuple with connectivity state, message, and ping time.</returns>
    Task<(bool IsHealthy, string? Message, TimeSpan? PingTime)> CheckHealthAsync();

    /// <summary>
    /// Register a new endpoint in the mesh.
    /// </summary>
    /// <param name="endpoint">The endpoint to register.</param>
    /// <param name="ttlSeconds">TTL in seconds for the registration.</param>
    /// <returns>True if registration was successful.</returns>
    Task<bool> RegisterEndpointAsync(MeshEndpoint endpoint, int ttlSeconds);

    /// <summary>
    /// Deregister an endpoint from the mesh.
    /// </summary>
    /// <param name="instanceId">Instance ID to deregister.</param>
    /// <param name="appId">App ID of the instance.</param>
    /// <returns>True if deregistration was successful.</returns>
    Task<bool> DeregisterEndpointAsync(Guid instanceId, string appId);

    /// <summary>
    /// Update endpoint heartbeat and metrics.
    /// </summary>
    /// <param name="instanceId">Instance ID sending heartbeat.</param>
    /// <param name="appId">App ID of the instance.</param>
    /// <param name="status">Current health status.</param>
    /// <param name="loadPercent">Current load percentage.</param>
    /// <param name="currentConnections">Current connection count.</param>
    /// <param name="ttlSeconds">TTL in seconds for the heartbeat.</param>
    /// <returns>True if heartbeat was recorded.</returns>
    Task<bool> UpdateHeartbeatAsync(
        Guid instanceId,
        string appId,
        EndpointStatus status,
        float loadPercent,
        int currentConnections,
        int ttlSeconds);

    /// <summary>
    /// Get all endpoints for a specific app-id.
    /// </summary>
    /// <param name="appId">The app-id to query.</param>
    /// <param name="includeUnhealthy">Whether to include unhealthy endpoints.</param>
    /// <returns>List of endpoints for the app-id.</returns>
    Task<List<MeshEndpoint>> GetEndpointsForAppIdAsync(string appId, bool includeUnhealthy = false);

    /// <summary>
    /// Get all registered endpoints in the mesh.
    /// </summary>
    /// <param name="appIdPrefix">Optional prefix filter for app-id.</param>
    /// <returns>List of all endpoints.</returns>
    Task<List<MeshEndpoint>> GetAllEndpointsAsync(string? appIdPrefix = null);

    /// <summary>
    /// Get a specific endpoint by instance ID.
    /// </summary>
    /// <param name="instanceId">Instance ID to look up.</param>
    /// <returns>The endpoint if found, null otherwise.</returns>
    Task<MeshEndpoint?> GetEndpointByInstanceIdAsync(Guid instanceId);
}
