// =============================================================================
// Processor Node State Model
// Represents the current state of an asset processor node.
// =============================================================================

namespace BeyondImmersion.BannouService.Asset.Pool;

/// <summary>
/// State of an asset processor node.
/// Stored in Redis with TTL for automatic cleanup of crashed nodes.
/// </summary>
public class ProcessorNodeState
{
    /// <summary>
    /// Unique identifier for this processor node.
    /// </summary>
    public required string NodeId { get; set; }

    /// <summary>
    /// Mesh app-id for routing requests to this node.
    /// </summary>
    public required string AppId { get; set; }

    /// <summary>
    /// Pool type this node handles (texture-processor, model-processor, audio-processor, asset-processor).
    /// </summary>
    public required string PoolType { get; set; }

    /// <summary>
    /// Maximum number of concurrent jobs this node can process.
    /// </summary>
    public int Capacity { get; set; } = 10;

    /// <summary>
    /// Number of jobs currently being processed.
    /// </summary>
    public int CurrentLoad { get; set; }

    /// <summary>
    /// Node status: Healthy or Draining.
    /// </summary>
    public ProcessorNodeStatus Status { get; set; } = ProcessorNodeStatus.Healthy;

    /// <summary>
    /// When the node registered.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; }

    /// <summary>
    /// When the last heartbeat was sent.
    /// </summary>
    public DateTimeOffset LastHeartbeat { get; set; }

    /// <summary>
    /// Consecutive heartbeats with zero load (for idle timeout detection).
    /// </summary>
    public int IdleHeartbeatCount { get; set; }

    /// <summary>
    /// Whether this node has capacity for new jobs.
    /// </summary>
    public bool HasCapacity => Status == ProcessorNodeStatus.Healthy && CurrentLoad < Capacity;

    /// <summary>
    /// Returns the available capacity (remaining job slots).
    /// </summary>
    public int AvailableCapacity => Math.Max(0, Capacity - CurrentLoad);
}

/// <summary>
/// Processor node status enumeration.
/// </summary>
public enum ProcessorNodeStatus
{
    /// <summary>Node is healthy and accepting new jobs.</summary>
    Healthy,

    /// <summary>Node is draining (shutting down gracefully, not accepting new jobs).</summary>
    Draining
}
