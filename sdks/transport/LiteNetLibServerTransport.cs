using BeyondImmersion.Bannou.Protocol;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.Bannou.Transport;

/// <summary>
/// LiteNetLib-based server transport implementation.
/// </summary>
public sealed class LiteNetLibServerTransport : IGameServerTransport, INetEventListener
{
    private NetManager? _netManager;
    private readonly NetPacketProcessor _packetProcessor = new();
    private GameTransportConfig? _config;
    private readonly Random _random = new();

    /// <summary>
    /// Optional fuzz settings for testing packet loss/reorder/delay.
    /// </summary>
    public TransportFuzzOptions FuzzOptions { get; } = new();

    /// <inheritdoc />
    public event Action<long>? OnClientConnected;

    /// <inheritdoc />
    public event Action<long>? OnClientDisconnected;

    /// <inheritdoc />
    public event ServerMessageReceived? OnClientMessage;

    /// <inheritdoc />
    public async Task StartAsync(GameTransportConfig config, CancellationToken cancellationToken = default)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _netManager = new NetManager(this)
        {
            IPv6Enabled = true,
            UnconnectedMessagesEnabled = false,
            DisconnectTimeout = 5000
        };
        if (!_netManager.Start(_config.Port))
        {
            throw new InvalidOperationException($"Failed to start LiteNetLib server on port {_config.Port}");
        }

        // Yield to ensure proper async behavior per IMPLEMENTATION TENETS (T23)
        await Task.Yield();

        // Run a lightweight poll loop; caller is expected to call PollEvents externally if desired.
        // Fire-and-forget: background task runs until cancellation or stop.
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested && _netManager.IsRunning)
            {
                _netManager.PollEvents();
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _netManager?.Stop();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task BroadcastAsync(GameMessageType messageType, byte[] payload, bool reliable, CancellationToken cancellationToken = default)
    {
        if (_netManager == null) return;
        var writer = BuildPacket(messageType, payload);
        var method = reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced;
        _netManager.SendToAll(writer, method);
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SendAsync(long clientId, GameMessageType messageType, byte[] payload, bool reliable, CancellationToken cancellationToken = default)
    {
        if (_netManager == null) return;
        var peer = _netManager.GetPeerById((int)clientId);
        if (peer == null) return;
        var writer = BuildPacket(messageType, payload);
        var method = reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced;
        peer.Send(writer, method);
        await Task.CompletedTask;
    }

    private NetDataWriter BuildPacket(GameMessageType messageType, byte[] payload)
    {
        var writer = new NetDataWriter(false, 2 + payload.Length);
        writer.Put(GameProtocolEnvelope.CurrentVersion);
        writer.Put((byte)messageType);
        writer.Put(payload);
        return writer;
    }

    #region INetEventListener

    /// <summary>
    /// Called when a peer connects. Raises <see cref="OnClientConnected"/>.
    /// </summary>
    /// <param name="peer">The connected peer.</param>
    public void OnPeerConnected(NetPeer peer)
    {
        OnClientConnected?.Invoke(peer.Id);
    }

    /// <summary>
    /// Called when a peer disconnects. Raises <see cref="OnClientDisconnected"/>.
    /// </summary>
    /// <param name="peer">The disconnected peer.</param>
    /// <param name="disconnectInfo">Details about the disconnection.</param>
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        OnClientDisconnected?.Invoke(peer.Id);
    }

    /// <summary>
    /// Called when data is received from a peer. Parses envelope and raises <see cref="OnClientMessage"/>.
    /// </summary>
    /// <param name="peer">The peer that sent the data.</param>
    /// <param name="reader">Packet reader containing the data.</param>
    /// <param name="channelNumber">Channel number the data was received on.</param>
    /// <param name="deliveryMethod">Delivery method used.</param>
    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (reader.AvailableBytes < 2)
        {
            return;
        }

        var version = reader.GetByte();
        var messageType = (GameMessageType)reader.GetByte();
        var payload = reader.GetRemainingBytes();

        if (FuzzOptions.ShouldDrop(_random))
        {
            return;
        }

        if (FuzzOptions.ShouldDelay(out var delayMs, _random))
        {
            Task.Delay(delayMs).ContinueWith(_ =>
            {
                OnClientMessage?.Invoke(peer.Id, version, messageType, payload);
            });
            return;
        }

        OnClientMessage?.Invoke(peer.Id, version, messageType, payload);
    }

    /// <summary>Called when a network error occurs. Currently unused.</summary>
    /// <param name="endPoint">The endpoint where the error occurred.</param>
    /// <param name="socketError">The socket error code.</param>
    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }

    /// <summary>Called when peer latency is updated. Currently unused.</summary>
    /// <param name="peer">The peer whose latency was measured.</param>
    /// <param name="latency">The measured latency in milliseconds.</param>
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

    /// <summary>Called when an unconnected message is received. Currently unused.</summary>
    /// <param name="remoteEndPoint">The sender's endpoint.</param>
    /// <param name="reader">Packet reader containing the message.</param>
    /// <param name="messageType">The unconnected message type.</param>
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }

    /// <summary>
    /// Called when a connection request is received. Accepts if key matches "GameTransport".
    /// </summary>
    /// <param name="request">The connection request.</param>
    public void OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey("GameTransport");
    }

    #endregion

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _netManager?.Stop();
        _netManager = null;
        return ValueTask.CompletedTask;
    }
}
