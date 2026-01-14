using MessagePack;
using System.Collections.Generic;

namespace BeyondImmersion.Bannou.Protocol.Messages;

/// <summary>
/// State delta (server -> client) using per-entity bitmask for changed fields.
/// </summary>
[MessagePackObject]
public class ArenaStateDelta
{
    /// <summary>Server tick when this delta was created.</summary>
    [Key(0)]
    public uint Tick { get; set; }

    /// <summary>Entities with changed state since last snapshot/delta.</summary>
    [Key(1)]
    public List<EntityDelta> Entities { get; set; } = new();
}

/// <summary>
/// Per-entity delta with bitmask indicating which fields changed.
/// </summary>
[MessagePackObject]
public class EntityDelta
{
    /// <summary>Unique identifier of the entity.</summary>
    [Key(0)]
    public int EntityId { get; set; }

    /// <summary>
    /// Bitmask of changed fields (flags below).
    /// </summary>
    [Key(1)]
    public EntityDeltaFlags Flags { get; set; }

    /// <summary>X position (only valid if Position flag set).</summary>
    [Key(2)]
    public float X { get; set; }

    /// <summary>Y position (only valid if Position flag set).</summary>
    [Key(3)]
    public float Y { get; set; }

    /// <summary>Z position (only valid if Position flag set).</summary>
    [Key(4)]
    public float Z { get; set; }

    /// <summary>Y-axis rotation in radians (only valid if Rotation flag set).</summary>
    [Key(5)]
    public float RotationY { get; set; }

    /// <summary>Current health value (only valid if Health flag set).</summary>
    [Key(6)]
    public float Health { get; set; }

    /// <summary>Current action state identifier (only valid if ActionState flag set).</summary>
    [Key(7)]
    public int ActionState { get; set; }
}

/// <summary>
/// Bitmask flags indicating which fields in an <see cref="EntityDelta"/> have changed.
/// </summary>
[System.Flags]
public enum EntityDeltaFlags : byte
{
    /// <summary>No fields changed.</summary>
    None = 0,
    /// <summary>Position (X, Y, Z) changed.</summary>
    Position = 1 << 0,
    /// <summary>Rotation changed.</summary>
    Rotation = 1 << 1,
    /// <summary>Health changed.</summary>
    Health = 1 << 2,
    /// <summary>Action state changed.</summary>
    ActionState = 1 << 3
}
