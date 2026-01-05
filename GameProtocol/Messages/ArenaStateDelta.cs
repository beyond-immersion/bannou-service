using MessagePack;
using System.Collections.Generic;

namespace BeyondImmersion.Bannou.GameProtocol.Messages;

/// <summary>
/// State delta (server -> client) using per-entity bitmask for changed fields.
/// </summary>
[MessagePackObject]
public class ArenaStateDelta
{
    [Key(0)]
    public uint Tick { get; set; }

    [Key(1)]
    public List<EntityDelta> Entities { get; set; } = new();
}

[MessagePackObject]
public class EntityDelta
{
    [Key(0)]
    public int EntityId { get; set; }

    /// <summary>
    /// Bitmask of changed fields (flags below).
    /// </summary>
    [Key(1)]
    public EntityDeltaFlags Flags { get; set; }

    [Key(2)]
    public float X { get; set; }

    [Key(3)]
    public float Y { get; set; }

    [Key(4)]
    public float Z { get; set; }

    [Key(5)]
    public float RotationY { get; set; }

    [Key(6)]
    public float Health { get; set; }

    [Key(7)]
    public int ActionState { get; set; }
}

[System.Flags]
public enum EntityDeltaFlags : byte
{
    None = 0,
    Position = 1 << 0,
    Rotation = 1 << 1,
    Health = 1 << 2,
    ActionState = 1 << 3
}
