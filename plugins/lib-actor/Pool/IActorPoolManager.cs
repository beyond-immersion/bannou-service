// =============================================================================
// Actor Pool Manager Interface
// Manages pool nodes and actor assignments for distributed actor execution.
// =============================================================================

using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.Actor.Pool;

/// <summary>
/// Interface for managing actor pool nodes and assignments.
/// Used by control plane to track pool nodes and route actors.
/// </summary>
public interface IActorPoolManager
{
    #region Pool Node Management

    /// <summary>
    /// Registers a new pool node with the control plane.
    /// Called when a pool node starts and publishes PoolNodeRegisteredEvent.
    /// </summary>
    /// <param name="registration">Registration event from pool node.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if registration successful, false if node already exists.</returns>
    Task<bool> RegisterNodeAsync(PoolNodeRegisteredEvent registration, CancellationToken ct = default);

    /// <summary>
    /// Updates pool node state from a heartbeat event.
    /// Refreshes last seen timestamp and current load.
    /// </summary>
    /// <param name="heartbeat">Heartbeat event from pool node.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateNodeHeartbeatAsync(PoolNodeHeartbeatEvent heartbeat, CancellationToken ct = default);

    /// <summary>
    /// Marks a pool node as draining (no new actors, graceful shutdown).
    /// Called when a pool node publishes PoolNodeDrainingEvent.
    /// </summary>
    /// <param name="nodeId">ID of the node to drain.</param>
    /// <param name="remainingActors">Number of actors still running.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of actors that need to complete before node shuts down.</returns>
    Task<int> DrainNodeAsync(string nodeId, int remainingActors, CancellationToken ct = default);

    /// <summary>
    /// Removes a node from the pool (unhealthy or drained).
    /// Clears all assignments for the node.
    /// </summary>
    /// <param name="nodeId">ID of the node to remove.</param>
    /// <param name="reason">Reason for removal.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of actors that were assigned to this node.</returns>
    Task<int> RemoveNodeAsync(string nodeId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Gets the state of a specific pool node.
    /// </summary>
    /// <param name="nodeId">Node ID to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Pool node state, or null if not found.</returns>
    Task<PoolNodeState?> GetNodeAsync(string nodeId, CancellationToken ct = default);

    /// <summary>
    /// Lists all registered pool nodes.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of all pool node states.</returns>
    Task<IReadOnlyList<PoolNodeState>> ListNodesAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists pool nodes by pool type.
    /// </summary>
    /// <param name="poolType">Pool type to filter by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of pool node states for the specified type.</returns>
    Task<IReadOnlyList<PoolNodeState>> ListNodesByTypeAsync(string poolType, CancellationToken ct = default);

    #endregion

    #region Actor Assignment

    /// <summary>
    /// Acquires a pool node with capacity for a new actor.
    /// Uses least-loaded selection within the appropriate pool type.
    /// </summary>
    /// <param name="category">Actor category (maps to pool type).</param>
    /// <param name="estimatedLoad">Estimated load units for this actor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Selected pool node, or null if no capacity available.</returns>
    Task<PoolNodeState?> AcquireNodeForActorAsync(string category, int estimatedLoad = 1, CancellationToken ct = default);

    /// <summary>
    /// Records an actor assignment to a pool node.
    /// Called after spawning an actor on a node.
    /// </summary>
    /// <param name="assignment">Assignment details.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordActorAssignmentAsync(ActorAssignment assignment, CancellationToken ct = default);

    /// <summary>
    /// Gets the current assignment for an actor.
    /// Used for routing messages to the correct pool node.
    /// </summary>
    /// <param name="actorId">Actor ID to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Assignment details, or null if actor not found.</returns>
    Task<ActorAssignment?> GetActorAssignmentAsync(string actorId, CancellationToken ct = default);

    /// <summary>
    /// Removes an actor assignment (actor stopped or completed).
    /// </summary>
    /// <param name="actorId">Actor ID to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if assignment was removed, false if not found.</returns>
    Task<bool> RemoveActorAssignmentAsync(string actorId, CancellationToken ct = default);

    /// <summary>
    /// Lists all actor assignments for a specific pool node.
    /// Used for recovery and monitoring.
    /// </summary>
    /// <param name="nodeId">Node ID to list assignments for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of actor assignments on the node.</returns>
    Task<IReadOnlyList<ActorAssignment>> ListActorsByNodeAsync(string nodeId, CancellationToken ct = default);

    /// <summary>
    /// Lists all actor assignments for a specific template.
    /// Used for auto-spawn max instance checking.
    /// </summary>
    /// <param name="templateId">Template ID to list assignments for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of actor assignments from this template.</returns>
    Task<IReadOnlyList<ActorAssignment>> GetAssignmentsByTemplateAsync(string templateId, CancellationToken ct = default);

    /// <summary>
    /// Updates the status of an actor assignment.
    /// Called when ActorStatusChangedEvent is received.
    /// </summary>
    /// <param name="actorId">Actor ID.</param>
    /// <param name="newStatus">New status value.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateActorStatusAsync(string actorId, ActorStatus newStatus, CancellationToken ct = default);

    #endregion

    #region Capacity & Monitoring

    /// <summary>
    /// Gets a summary of pool capacity across all nodes.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Capacity summary with breakdowns.</returns>
    Task<PoolCapacitySummary> GetCapacitySummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks for nodes that have missed heartbeats.
    /// Returns nodes that should be marked unhealthy.
    /// </summary>
    /// <param name="heartbeatTimeout">Maximum time since last heartbeat.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of nodes that missed heartbeats.</returns>
    Task<IReadOnlyList<PoolNodeState>> GetUnhealthyNodesAsync(TimeSpan heartbeatTimeout, CancellationToken ct = default);

    #endregion
}
