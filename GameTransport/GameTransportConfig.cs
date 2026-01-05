using System;

namespace BeyondImmersion.Bannou.GameTransport;

/// <summary>
/// Base configuration for game transport.
/// Does not bind to a specific networking stack (LiteNetLib to be wired later).
/// </summary>
public sealed class GameTransportConfig
{
    /// <summary>
    /// UDP port for the game server.
    /// </summary>
    public int Port { get; init; } = 9000;

    /// <summary>
    /// Interval (in ticks) between full snapshots (server → client).
    /// </summary>
    public int SnapshotIntervalTicks { get; init; } = 60;

    /// <summary>
    /// Target broadcast tick rate (Hz).
    /// </summary>
    public int BroadcastHz { get; init; } = 60;

    /// <summary>
    /// Protocol version expected by this server/client.
    /// </summary>
    public byte ProtocolVersion { get; init; } = GameProtocol.GameProtocolEnvelope.CurrentVersion;
}
