using BeyondImmersion.Bannou.Protocol;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.Bannou.Transport;

/// <summary>
/// Delegate for handling messages received from clients.
/// </summary>
/// <param name="clientId">The client that sent the message.</param>
/// <param name="version">Protocol version from the envelope.</param>
/// <param name="messageType">Type of message received.</param>
/// <param name="payload">Raw MessagePack payload (without envelope).</param>
public delegate void ServerMessageReceived(long clientId, byte version, GameMessageType messageType, ReadOnlyMemory<byte> payload);

/// <summary>
/// Abstraction for a game server transport host (e.g., LiteNetLib).
/// </summary>
public interface IGameServerTransport : IAsyncDisposable
{
    /// <summary>
    /// Starts listening for clients.
    /// </summary>
    Task StartAsync(GameTransportConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the transport and disconnects clients.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Broadcast a message to all connected clients.
    /// </summary>
    Task BroadcastAsync(GameMessageType messageType, byte[] payload, bool reliable, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a message to a specific client.
    /// </summary>
    Task SendAsync(long clientId, GameMessageType messageType, byte[] payload, bool reliable, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a client connects.
    /// </summary>
    event Action<long>? OnClientConnected;

    /// <summary>
    /// Event raised when a client disconnects.
    /// </summary>
    event Action<long>? OnClientDisconnected;

    /// <summary>
    /// Event raised when a message is received from a client.
    /// Provides envelope info and raw payload for higher layers to parse.
    /// </summary>
    event ServerMessageReceived? OnClientMessage;
}
