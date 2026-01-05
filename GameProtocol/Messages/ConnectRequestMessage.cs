using MessagePack;

namespace BeyondImmersion.Bannou.GameProtocol.Messages;

/// <summary>
/// Client connect/join request (client -> server).
/// </summary>
[MessagePackObject]
public class ConnectRequestMessage
{
    /// <summary>Unique identifier of the connecting player.</summary>
    [Key(0)]
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>Session identifier from the authentication system.</summary>
    [Key(1)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Match or game instance the player wants to join.</summary>
    [Key(2)]
    public string MatchId { get; set; } = string.Empty;
}
