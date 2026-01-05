using MessagePack;
using System.Collections.Generic;

namespace BeyondImmersion.Bannou.GameProtocol.Messages;

/// <summary>
/// Full state snapshot (server -> client).
/// </summary>
[MessagePackObject]
public class ArenaStateSnapshot
{
    [Key(0)]
    public uint Tick { get; set; }

    [Key(1)]
    public List<EntityState> Entities { get; set; } = new();
}

/// <summary>
/// Basic entity state for snapshots.
/// </summary>
[MessagePackObject]
public class EntityState
{
    [Key(0)]
    public int EntityId { get; set; }

    [Key(1)]
    public float X { get; set; }

    [Key(2)]
    public float Y { get; set; }

    [Key(3)]
    public float Z { get; set; }

    [Key(4)]
    public float RotationY { get; set; }

    [Key(5)]
    public float Health { get; set; }

    [Key(6)]
    public int ActionState { get; set; }
}
