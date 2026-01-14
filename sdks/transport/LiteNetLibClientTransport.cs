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
/// LiteNetLib-based client transport implementation.
/// </summary>
public sealed class LiteNetLibClientTransport : IGameClientTransport, INetEventListener
{
    private NetManager? _netManager;
    private NetPeer? _serverPeer;
    private readonly NetPacketProcessor _packetProcessor = new();
    private readonly Random _random = new();

    /// <summary>
    /// Optional fuzz settings for testing packet loss/reordering/delay on client side.
    /// </summary>
    public TransportFuzzOptions FuzzOptions { get; } = new();

    /// <inheritdoc />
    public event Action? OnConnected;

    /// <inheritdoc />
    public event Action<string?>? OnDisconnected;

    /// <inheritdoc />
    public event ClientMessageReceived? OnServerMessage;

    /// <inheritdoc />
    public async Task ConnectAsync(string host, int port, byte protocolVersion, CancellationToken cancellationToken = default)
    {
        _netManager = new NetManager(this)
        {
            IPv6Enabled = true,
            UnconnectedMessagesEnabled = false,
            DisconnectTimeout = 5000
        };

        _netManager.Start();
        _serverPeer = _netManager.Connect(host, port, "GameTransport");

        // Yield to ensure proper async behavior per IMPLEMENTATION TENETS (T23)
        await Task.Yield();

        // Fire-and-forget: background task runs until cancellation or disconnect.
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
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _serverPeer?.Disconnect();
        _netManager?.Stop();
        _serverPeer = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SendAsync(GameMessageType messageType, byte[] payload, bool reliable, CancellationToken cancellationToken = default)
    {
        if (_serverPeer == null) return;
        var writer = BuildPacket(messageType, payload);
        var method = reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced;
        _serverPeer.Send(writer, method);
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
    /// Called when connected to the server. Raises <see cref="OnConnected"/>.
    /// </summary>
    /// <param name="peer">The server peer.</param>
    public void OnPeerConnected(NetPeer peer)
    {
        if (peer == _serverPeer)
        {
            OnConnected?.Invoke();
        }
    }

    /// <summary>
    /// Called when disconnected from the server. Raises <see cref="OnDisconnected"/>.
    /// </summary>
    /// <param name="peer">The server peer.</param>
    /// <param name="disconnectInfo">Details about the disconnection.</param>
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (peer == _serverPeer)
        {
            OnDisconnected?.Invoke(disconnectInfo.Reason.ToString());
        }
    }

    /// <summary>
    /// Called when data is received from the server. Parses envelope and raises <see cref="OnServerMessage"/>.
    /// </summary>
    /// <param name="peer">The server peer.</param>
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
                OnServerMessage?.Invoke(version, messageType, payload);
            });
            return;
        }

        OnServerMessage?.Invoke(version, messageType, payload);
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

    /// <summary>Called when a connection request is received. Not applicable for client.</summary>
    /// <param name="request">The connection request.</param>
    public void OnConnectionRequest(ConnectionRequest request) { }

    #endregion

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _serverPeer?.Disconnect();
        _netManager?.Stop();
        _serverPeer = null;
        _netManager = null;
        return ValueTask.CompletedTask;
    }
}
