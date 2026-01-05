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

    public event Action<long>? OnClientConnected;
    public event Action<long>? OnClientDisconnected;
    public event ServerMessageReceived? OnClientMessage;

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

        // Run a lightweight poll loop; caller is expected to call PollEvents externally if desired.
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested && _netManager.IsRunning)
            {
                _netManager.PollEvents();
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _netManager?.Stop();
        return Task.CompletedTask;
    }

    public async Task BroadcastAsync(GameMessageType messageType, byte[] payload, bool reliable, CancellationToken cancellationToken = default)
    {
        if (_netManager == null) return;
        var writer = BuildPacket(messageType, payload);
        var method = reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced;
        _netManager.SendToAll(writer, method);
        await Task.CompletedTask;
    }

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

    public void OnPeerConnected(NetPeer peer)
    {
        OnClientConnected?.Invoke(peer.Id);
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        OnClientDisconnected?.Invoke(peer.Id);
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

    // Unused callbacks
    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    public void OnConnectionRequest(ConnectionRequest request)
    {
        request.AcceptIfKey("GameTransport");
    }

    #endregion

    public ValueTask DisposeAsync()
    {
        _netManager?.Stop();
        _netManager = null;
        return ValueTask.CompletedTask;
    }
}
