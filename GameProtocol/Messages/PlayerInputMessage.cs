using MessagePack;

namespace BeyondImmersion.Bannou.GameProtocol.Messages;

/// <summary>
/// Player input (client -> server).
/// </summary>
[MessagePackObject]
public class PlayerInputMessage
{
    [Key(0)]
    public uint Tick { get; set; }

    [Key(1)]
    public float MoveX { get; set; }

    [Key(2)]
    public float MoveY { get; set; }

    [Key(3)]
    public bool Action1 { get; set; }

    [Key(4)]
    public bool Action2 { get; set; }

    [Key(5)]
    public bool Action3 { get; set; }

    [Key(6)]
    public bool Dodge { get; set; }
}
