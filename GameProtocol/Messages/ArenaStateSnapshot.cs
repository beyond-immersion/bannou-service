using MessagePack;
using System.Collections.Generic;

namespace BeyondImmersion.Bannou.GameProtocol.Messages;

/// <summary>
/// Full state snapshot (server -> client).
/// </summary>
[MessagePackObject]
public class ArenaStateSnapshot
{
    /// <summary>Server tick when this snapshot was created.</summary>
    [Key(0)]
    public uint Tick { get; set; }

    /// <summary>All entity states at this tick.</summary>
    [Key(1)]
    public List<EntityState> Entities { get; set; } = new();

    /// <summary>Current combat phase (0=Waiting, 1=Countdown, 2=Fighting, 3=Opportunity, 4=Cinematic, 5=Finished).</summary>
    [Key(2)]
    public int Phase { get; set; }

    /// <summary>Cinematic ID when in cinematic phase (null otherwise).</summary>
    [Key(3)]
    public string? CinematicId { get; set; }

    /// <summary>Cinematic attacker entity slot (0=MonsterA, 1=MonsterB).</summary>
    [Key(4)]
    public int CinematicAttacker { get; set; }

    /// <summary>Cinematic defender entity slot (0=MonsterA, 1=MonsterB).</summary>
    [Key(5)]
    public int CinematicDefender { get; set; }

    /// <summary>Cinematic target object ID (e.g., boulder ID).</summary>
    [Key(6)]
    public string? CinematicTargetId { get; set; }
}

/// <summary>
/// Basic entity state for snapshots.
/// </summary>
[MessagePackObject]
public class EntityState
{
    /// <summary>Unique identifier of the entity.</summary>
    [Key(0)]
    public int EntityId { get; set; }

    /// <summary>X position in world space.</summary>
    [Key(1)]
    public float X { get; set; }

    /// <summary>Y position in world space.</summary>
    [Key(2)]
    public float Y { get; set; }

    /// <summary>Z position in world space.</summary>
    [Key(3)]
    public float Z { get; set; }

    /// <summary>Y-axis rotation in radians.</summary>
    [Key(4)]
    public float RotationY { get; set; }

    /// <summary>Current health value.</summary>
    [Key(5)]
    public float Health { get; set; }

    /// <summary>Current action state identifier.</summary>
    [Key(6)]
    public int ActionState { get; set; }
}
