namespace BeyondImmersion.Bannou.GameProtocol;

/// <summary>
/// Message type prefix for game transport messages (server/client).
/// The byte sits after the protocol version in the envelope.
/// </summary>
public enum GameMessageType : byte
{
    /// <summary>Unknown or uninitialized message.</summary>
    Unknown = 0,

    // Server -> Client
    /// <summary>Full state snapshot.</summary>
    ArenaStateSnapshot = 1,
    /// <summary>State delta update.</summary>
    ArenaStateDelta = 2,
    /// <summary>Combat event batch.</summary>
    CombatEvent = 3,
    /// <summary>Match-level state update.</summary>
    MatchState = 4,
    /// <summary>Opportunity/QTE data for clients.</summary>
    OpportunityData = 5,
    /// <summary>Cinematic extension payload reference.</summary>
    CinematicExtension = 6,

    // Client -> Server
    /// <summary>Client requests to join/connect.</summary>
    ConnectRequest = 64,
    /// <summary>Player input from client.</summary>
    PlayerInput = 65,
    /// <summary>Response to an opportunity/QTE.</summary>
    OpportunityResponse = 66,
    /// <summary>Latency/keepalive ping.</summary>
    Ping = 67
}
