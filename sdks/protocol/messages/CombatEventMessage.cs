using MessagePack;
using System.Collections.Generic;

namespace BeyondImmersion.Bannou.Protocol.Messages;

/// <summary>
/// Combat events batched per tick (server -> client).
/// </summary>
[MessagePackObject]
public class CombatEventMessage
{
    /// <summary>Server tick when these events occurred.</summary>
    [Key(0)]
    public uint Tick { get; set; }

    /// <summary>Combat events that occurred this tick.</summary>
    [Key(1)]
    public List<CombatEvent> Events { get; set; } = new();
}

/// <summary>
/// Individual combat event within a batch.
/// </summary>
[MessagePackObject]
public class CombatEvent
{
    /// <summary>Type of combat event.</summary>
    [Key(0)]
    public CombatEventType Type { get; set; }

    /// <summary>Entity that initiated the combat action.</summary>
    [Key(1)]
    public int SourceEntityId { get; set; }

    /// <summary>Entity that received the combat action.</summary>
    [Key(2)]
    public int TargetEntityId { get; set; }

    /// <summary>Amount of damage, healing, or effect value.</summary>
    [Key(3)]
    public float Amount { get; set; }

    /// <summary>Target's remaining HP after this event.</summary>
    [Key(4)]
    public float RemainingHp { get; set; }

    /// <summary>Duration of effect in milliseconds (for staggers, etc.).</summary>
    [Key(5)]
    public int DurationMs { get; set; }
}

/// <summary>
/// Types of combat events that can occur.
/// </summary>
public enum CombatEventType : byte
{
    /// <summary>Damage dealt to target.</summary>
    Hit = 0,
    /// <summary>Target staggered/interrupted.</summary>
    Stagger = 1,
    /// <summary>Health restored to target.</summary>
    Heal = 2,
    /// <summary>Attack blocked by target.</summary>
    Block = 3
}
