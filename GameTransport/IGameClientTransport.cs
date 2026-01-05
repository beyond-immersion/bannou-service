using BeyondImmersion.Bannou.GameProtocol;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.Bannou.GameTransport;

/// <summary>
/// Delegate for handling messages received from the server.
/// </summary>
/// <param name="version">Protocol version from the envelope.</param>
/// <param name="messageType">Type of message received.</param>
/// <param name="payload">Raw MessagePack payload (without envelope).</param>
public delegate void ClientMessageReceived(byte version, GameMessageType messageType, ReadOnlyMemory<byte> payload);

/// <summary>
/// Abstraction for a game client transport (e.g., LiteNetLib client).
/// </summary>
public interface IGameClientTransport : IAsyncDisposable
{
    /// <summary>
    /// Connects to the game server.
    /// </summary>
    Task ConnectAsync(string host, int port, byte protocolVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the game server.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to the server.
    /// </summary>
    Task SendAsync(GameMessageType messageType, byte[] payload, bool reliable, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when connected.
    /// </summary>
    event Action? OnConnected;

    /// <summary>
    /// Event raised when disconnected.
    /// </summary>
    event Action<string?>? OnDisconnected;

    /// <summary>
    /// Event raised when a message is received from the server.
    /// </summary>
    event ClientMessageReceived? OnServerMessage;
}
