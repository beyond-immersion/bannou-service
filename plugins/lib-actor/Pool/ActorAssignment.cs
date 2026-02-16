// =============================================================================
// Actor Assignment Model
// Tracks which pool node is running a specific actor.
// =============================================================================

using BeyondImmersion.BannouService.Actor;

namespace BeyondImmersion.BannouService.Actor.Pool;

/// <summary>
/// Assignment of an actor to a pool node.
/// Stored in Redis for routing messages and tracking actor locations.
/// </summary>
public class ActorAssignment
{
    /// <summary>
    /// Unique identifier for the actor.
    /// </summary>
    public required string ActorId { get; set; }

    /// <summary>
    /// Pool node ID where this actor is running.
    /// </summary>
    public required string NodeId { get; set; }

    /// <summary>
    /// Mesh app-id for routing to the node.
    /// Cached here to avoid lookup on every message.
    /// </summary>
    public required string NodeAppId { get; set; }

    /// <summary>
    /// Template ID this actor was created from.
    /// </summary>
    public required Guid TemplateId { get; set; }

    /// <summary>
    /// Actor category (from template).
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Actor status (running, stopped, etc.).
    /// </summary>
    public ActorStatus Status { get; set; } = ActorStatus.Pending;

    /// <summary>
    /// When the actor was assigned to this node.
    /// </summary>
    public DateTimeOffset AssignedAt { get; set; }

    /// <summary>
    /// When the actor started running (set when status changes to running).
    /// </summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Associated character ID for NPC brain actors.
    /// </summary>
    public Guid? CharacterId { get; set; }

    /// <summary>
    /// Realm this actor operates in.
    /// Resolved at spawn time from request or character lookup.
    /// </summary>
    public Guid? RealmId { get; set; }
}

/// <summary>
/// Capacity summary across all pool nodes.
/// </summary>
public class PoolCapacitySummary
{
    /// <summary>
    /// Total number of pool nodes.
    /// </summary>
    public int TotalNodes { get; set; }

    /// <summary>
    /// Number of healthy nodes.
    /// </summary>
    public int HealthyNodes { get; set; }

    /// <summary>
    /// Number of draining nodes.
    /// </summary>
    public int DrainingNodes { get; set; }

    /// <summary>
    /// Number of unhealthy nodes.
    /// </summary>
    public int UnhealthyNodes { get; set; }

    /// <summary>
    /// Total capacity across all healthy nodes.
    /// </summary>
    public int TotalCapacity { get; set; }

    /// <summary>
    /// Current total load across all healthy nodes.
    /// </summary>
    public int TotalLoad { get; set; }

    /// <summary>
    /// Available capacity (TotalCapacity - TotalLoad).
    /// </summary>
    public int AvailableCapacity => TotalCapacity - TotalLoad;

    /// <summary>
    /// Capacity breakdown by pool type.
    /// </summary>
    public Dictionary<string, PoolTypeCapacity> ByPoolType { get; set; } = new();
}

/// <summary>
/// Capacity for a specific pool type.
/// </summary>
public class PoolTypeCapacity
{
    /// <summary>
    /// Pool type name.
    /// </summary>
    public required string PoolType { get; set; }

    /// <summary>
    /// Number of nodes for this pool type.
    /// </summary>
    public int NodeCount { get; set; }

    /// <summary>
    /// Total capacity for this pool type.
    /// </summary>
    public int TotalCapacity { get; set; }

    /// <summary>
    /// Current load for this pool type.
    /// </summary>
    public int CurrentLoad { get; set; }

    /// <summary>
    /// Available capacity.
    /// </summary>
    public int AvailableCapacity => TotalCapacity - CurrentLoad;
}
