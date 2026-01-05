// =============================================================================
// Asset Processor Pool Manager Interface
// Manages processor node state in distributed storage.
// =============================================================================

namespace BeyondImmersion.BannouService.Asset.Pool;

/// <summary>
/// Manages asset processor pool state in distributed storage.
/// Used by both API nodes (to query availability) and worker nodes (to register/heartbeat).
/// </summary>
public interface IAssetProcessorPoolManager
{
    /// <summary>
    /// Registers a processor node in the pool.
    /// Called by worker nodes on startup.
    /// </summary>
    /// <param name="nodeId">Unique node identifier.</param>
    /// <param name="appId">Mesh app-id for routing.</param>
    /// <param name="poolType">Pool type (texture-processor, model-processor, etc.).</param>
    /// <param name="capacity">Maximum concurrent jobs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if registration succeeded.</returns>
    Task<bool> RegisterNodeAsync(
        string nodeId,
        string appId,
        string poolType,
        int capacity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates heartbeat and current load for a node.
    /// Called periodically by worker nodes.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="poolType">Pool type.</param>
    /// <param name="currentLoad">Current job count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated node state, or null if node not found.</returns>
    Task<ProcessorNodeState?> UpdateHeartbeatAsync(
        string nodeId,
        string poolType,
        int currentLoad,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a processor node from the pool.
    /// Called by worker nodes during graceful shutdown.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="poolType">Pool type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if removal succeeded.</returns>
    Task<bool> RemoveNodeAsync(
        string nodeId,
        string poolType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the state of a specific node.
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="poolType">Pool type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Node state, or null if not found.</returns>
    Task<ProcessorNodeState?> GetNodeAsync(
        string nodeId,
        string poolType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of available (healthy with capacity) processors for a pool type.
    /// </summary>
    /// <param name="poolType">Pool type to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of available processors.</returns>
    Task<int> GetAvailableCountAsync(
        string poolType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all available (healthy with capacity) processors for a pool type.
    /// </summary>
    /// <param name="poolType">Pool type to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of available node states.</returns>
    Task<IReadOnlyList<ProcessorNodeState>> GetAvailableNodesAsync(
        string poolType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total node count for a pool type (includes all statuses).
    /// Used when determining target instance count for scaling.
    /// </summary>
    /// <param name="poolType">Pool type to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total number of registered nodes.</returns>
    Task<int> GetTotalNodeCountAsync(
        string poolType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a node as draining (shutting down).
    /// </summary>
    /// <param name="nodeId">Node identifier.</param>
    /// <param name="poolType">Pool type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if status was updated.</returns>
    Task<bool> SetDrainingAsync(
        string nodeId,
        string poolType,
        CancellationToken cancellationToken = default);
}
