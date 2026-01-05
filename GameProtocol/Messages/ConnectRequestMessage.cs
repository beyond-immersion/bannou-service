using MessagePack;

namespace BeyondImmersion.Bannou.GameProtocol.Messages;

/// <summary>
/// Client connect/join request (client -> server).
/// </summary>
[MessagePackObject]
public class ConnectRequestMessage
{
    [Key(0)]
    public string PlayerId { get; set; } = string.Empty;

    [Key(1)]
    public string SessionId { get; set; } = string.Empty;

    [Key(2)]
    public string MatchId { get; set; } = string.Empty;
}
