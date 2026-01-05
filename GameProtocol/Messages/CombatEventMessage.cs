using MessagePack;
using System.Collections.Generic;

namespace BeyondImmersion.Bannou.GameProtocol.Messages;

/// <summary>
/// Combat events batched per tick (server -> client).
/// </summary>
[MessagePackObject]
public class CombatEventMessage
{
    [Key(0)]
    public uint Tick { get; set; }

    [Key(1)]
    public List<CombatEvent> Events { get; set; } = new();
}

[MessagePackObject]
public class CombatEvent
{
    [Key(0)]
    public CombatEventType Type { get; set; }

    [Key(1)]
    public int SourceEntityId { get; set; }

    [Key(2)]
    public int TargetEntityId { get; set; }

    [Key(3)]
    public float Amount { get; set; }

    [Key(4)]
    public float RemainingHp { get; set; }

    [Key(5)]
    public int DurationMs { get; set; }
}

public enum CombatEventType : byte
{
    Hit = 0,
    Stagger = 1,
    Heal = 2,
    Block = 3
}
