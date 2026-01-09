// =============================================================================
// Pool Node State Model
// Represents the current state of a pool node for control plane tracking.
// =============================================================================

namespace BeyondImmersion.BannouService.Actor.Pool;

/// <summary>
/// State of a pool node as tracked by the control plane.
/// Stored in Redis with TTL for automatic cleanup of dead nodes.
/// </summary>
public class PoolNodeState
{
    /// <summary>
    /// Unique identifier for this pool node.
    /// </summary>
    public required string NodeId { get; set; }

    /// <summary>
    /// Mesh app-id for routing commands to this node.
    /// </summary>
    public required string AppId { get; set; }

    /// <summary>
    /// Pool type this node belongs to (shared, npc-brain, event-coordinator, etc.).
    /// </summary>
    public required string PoolType { get; set; }

    /// <summary>
    /// Maximum number of actors this node can run.
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>
    /// Number of actors currently running on this node.
    /// </summary>
    public int CurrentLoad { get; set; }

    /// <summary>
    /// Node status: healthy, draining, unhealthy.
    /// </summary>
    public PoolNodeStatus Status { get; set; } = PoolNodeStatus.Healthy;

    /// <summary>
    /// When the node registered with the control plane.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>
    /// When the last heartbeat was received.
    /// </summary>
    public DateTimeOffset LastHeartbeat { get; set; }

    /// <summary>
    /// Number of remaining actors if node is draining.
    /// </summary>
    public int? DrainingActorsRemaining { get; set; }

    /// <summary>
    /// Checks if this node has capacity for additional actors.
    /// </summary>
    public bool HasCapacity => Status == PoolNodeStatus.Healthy && CurrentLoad < Capacity;

    /// <summary>
    /// Returns the available capacity (remaining slots).
    /// </summary>
    public int AvailableCapacity => Math.Max(0, Capacity - CurrentLoad);
}

/// <summary>
/// Pool node status enumeration.
/// </summary>
public enum PoolNodeStatus
{
    /// <summary>Node is healthy and accepting new actors.</summary>
    Healthy,

    /// <summary>Node is draining (shutting down gracefully, not accepting new actors).</summary>
    Draining,

    /// <summary>Node is unhealthy (missed heartbeats, connection lost).</summary>
    Unhealthy
}
