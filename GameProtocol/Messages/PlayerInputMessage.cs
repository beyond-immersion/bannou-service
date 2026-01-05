using MessagePack;

namespace BeyondImmersion.Bannou.GameProtocol.Messages;

/// <summary>
/// Player input (client -> server).
/// </summary>
[MessagePackObject]
public class PlayerInputMessage
{
    /// <summary>Client tick when this input was captured.</summary>
    [Key(0)]
    public uint Tick { get; set; }

    /// <summary>Horizontal movement input (-1 to 1).</summary>
    [Key(1)]
    public float MoveX { get; set; }

    /// <summary>Vertical movement input (-1 to 1).</summary>
    [Key(2)]
    public float MoveY { get; set; }

    /// <summary>Primary action button pressed.</summary>
    [Key(3)]
    public bool Action1 { get; set; }

    /// <summary>Secondary action button pressed.</summary>
    [Key(4)]
    public bool Action2 { get; set; }

    /// <summary>Tertiary action button pressed.</summary>
    [Key(5)]
    public bool Action3 { get; set; }

    /// <summary>Dodge/evade button pressed.</summary>
    [Key(6)]
    public bool Dodge { get; set; }
}
