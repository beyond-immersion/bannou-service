using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BeyondImmersion.Bannou.GameProtocol;
using LiteNetLib;
using LiteNetLib.Utils;

namespace BeyondImmersion.Bannou.GameTransport;

/// <summary>
/// LiteNetLib-based client transport implementation.
/// </summary>
public sealed class LiteNetLibClientTransport : IGameClientTransport, INetEventListener
{
    private NetManager? _netManager;
    private NetPeer? _serverPeer;
    private readonly NetPacketProcessor _packetProcessor = new();

    public event Action? OnConnected;
    public event Action<string?>? OnDisconnected;
    public event ClientMessageReceived? OnServerMessage;

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

        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested && _netManager.IsRunning)
            {
                _netManager.PollEvents();
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _serverPeer?.Disconnect();
        _netManager?.Stop();
        _serverPeer = null;
        return Task.CompletedTask;
    }

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

    public void OnPeerConnected(NetPeer peer)
    {
        if (peer == _serverPeer)
        {
            OnConnected?.Invoke();
        }
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (peer == _serverPeer)
        {
            OnDisconnected?.Invoke(disconnectInfo.Reason.ToString());
        }
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        if (reader.AvailableBytes < 2)
        {
            return;
        }

        var version = reader.GetByte();
        var messageType = (GameMessageType)reader.GetByte();
        var payload = reader.GetRemainingBytes();
        OnServerMessage?.Invoke(version, messageType, payload);
    }

    // Unused callbacks
    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    public void OnConnectionRequest(ConnectionRequest request) { }

    #endregion

    public ValueTask DisposeAsync()
    {
        _serverPeer?.Disconnect();
        _netManager?.Stop();
        _serverPeer = null;
        _netManager = null;
        return ValueTask.CompletedTask;
    }
}
