using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Connect.Protocol;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Manages the inter-node WebSocket broadcast mesh for multi-instance Connect deployments.
/// Each Connect instance registers in a Redis sorted set, discovers compatible peers on startup,
/// and establishes WebSocket connections for broadcast relay.
/// </summary>
/// <remarks>
/// Broadcast is active only when BOTH conditions are met:
/// <list type="bullet">
/// <item><description>MultiNodeBroadcastMode is not None</description></item>
/// <item><description>BroadcastInternalUrl is not null/empty</description></item>
/// </list>
/// When inactive, no registration, discovery, connections, or timers are created.
/// </remarks>
public sealed class InterNodeBroadcastManager : IDisposable
{
    private const string BROADCAST_REGISTRY_KEY = "broadcast-registry";

    private readonly ConcurrentDictionary<Guid, WebSocket> _nodeConnections = new();
    private Timer? _maintenanceTimer;
    private bool _disposed;

    private readonly BroadcastMode _broadcastMode;
    private readonly string? _broadcastInternalUrl;
    private readonly Guid _instanceId;
    private readonly string _registryMember;
    private readonly bool _isActive;

    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ConnectServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<InterNodeBroadcastManager> _logger;

    /// <summary>
    /// Callback invoked when a broadcast message is received from another node.
    /// Wired by ConnectService to BinaryMessage.Parse + BroadcastMessageAsync.
    /// Parameters: (byte[] buffer, int messageLength).
    /// </summary>
    public Func<byte[], int, Task>? OnBroadcastReceived { get; set; }

    /// <summary>
    /// Number of active inter-node connections.
    /// </summary>
    public int ActiveConnectionCount => _nodeConnections.Count;

    /// <summary>
    /// Creates a new InterNodeBroadcastManager.
    /// </summary>
    /// <param name="stateStoreFactory">Factory for Redis state store access.</param>
    /// <param name="configuration">Connect service configuration.</param>
    /// <param name="meshInstanceIdentifier">Process-stable instance identity from lib-mesh.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger instance.</param>
    public InterNodeBroadcastManager(
        IStateStoreFactory stateStoreFactory,
        ConnectServiceConfiguration configuration,
        IMeshInstanceIdentifier meshInstanceIdentifier,
        ITelemetryProvider telemetryProvider,
        ILogger<InterNodeBroadcastManager> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
        _instanceId = meshInstanceIdentifier.InstanceId;

        _broadcastMode = configuration.MultiNodeBroadcastMode;
        _broadcastInternalUrl = string.IsNullOrEmpty(configuration.BroadcastInternalUrl)
            ? null
            : configuration.BroadcastInternalUrl;

        _isActive = _broadcastMode != BroadcastMode.None && _broadcastInternalUrl != null;

        // Pre-serialize registry entry for consistent sorted set member identity
        var entry = new BroadcastRegistryEntry(_instanceId, _broadcastInternalUrl ?? string.Empty, _broadcastMode);
        _registryMember = BannouJson.Serialize(entry);

        _logger.LogInformation(
            "InterNodeBroadcastManager initialized: Mode={BroadcastMode}, Active={IsActive}, InstanceId={InstanceId}",
            _broadcastMode, _isActive, _instanceId);
    }

    /// <summary>
    /// Registers this instance in the broadcast registry, discovers and connects to compatible peers,
    /// and starts the maintenance timer. Call after DI is ready, before accepting client connections.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task InitializeAsync(CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "InterNodeBroadcastManager.InitializeAsync");

        if (!_isActive) return;

        var store = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Connect);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Register self in sorted set
        await store.SortedSetAddAsync(BROADCAST_REGISTRY_KEY, _registryMember, now, cancellationToken: ct);
        _logger.LogInformation("Registered in broadcast registry at score {Timestamp}", now);

        // Discover compatible peers
        var cutoff = now - _configuration.BroadcastStaleThresholdSeconds;
        var entries = await store.SortedSetRangeByScoreAsync(
            BROADCAST_REGISTRY_KEY, cutoff, double.PositiveInfinity, cancellationToken: ct);

        var connectedCount = 0;
        var peerCount = 0;
        foreach (var (member, _) in entries)
        {
            var peer = BannouJson.Deserialize<BroadcastRegistryEntry>(member);
            if (peer == null || peer.InstanceId == _instanceId) continue;

            peerCount++;
            if (!IsCompatible(_broadcastMode, peer.BroadcastMode)) continue;

            if (await ConnectToPeerAsync(peer, ct))
            {
                connectedCount++;
            }
        }

        _logger.LogInformation("Discovered {PeerCount} peers, connected to {ConnectedCount}",
            peerCount, connectedCount);

        // Start maintenance timer (heartbeat + stale cleanup)
        _maintenanceTimer = new Timer(
            MaintenanceCallback,
            null,
            TimeSpan.FromSeconds(_configuration.BroadcastHeartbeatIntervalSeconds),
            TimeSpan.FromSeconds(_configuration.BroadcastHeartbeatIntervalSeconds));
    }

    /// <summary>
    /// Handles an incoming WebSocket connection from another Connect instance.
    /// Called by the /connect/broadcast endpoint handler. Blocks until the connection closes.
    /// </summary>
    /// <param name="webSocket">The accepted WebSocket connection.</param>
    /// <param name="remoteInstanceId">Instance ID of the connecting peer.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task HandleIncomingConnectionAsync(WebSocket webSocket, Guid remoteInstanceId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "InterNodeBroadcastManager.HandleIncomingConnectionAsync");

        if (!_isActive)
        {
            _logger.LogWarning("Rejecting incoming broadcast connection from {RemoteInstanceId}: broadcast not active",
                remoteInstanceId);
            return;
        }

        _nodeConnections.TryAdd(remoteInstanceId, webSocket);
        _logger.LogInformation("Accepted incoming broadcast connection from peer {RemoteInstanceId}", remoteInstanceId);

        try
        {
            await ReadLoopAsync(webSocket, remoteInstanceId);
        }
        finally
        {
            _nodeConnections.TryRemove(remoteInstanceId, out _);
            _logger.LogInformation("Incoming broadcast peer {RemoteInstanceId} disconnected", remoteInstanceId);
        }
    }

    /// <summary>
    /// Relays a broadcast message to all connected inter-node peers. Fire-and-forget.
    /// Only sends if broadcast mode is Send or Both.
    /// </summary>
    /// <param name="messageBytes">Raw binary message to relay.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RelayBroadcastAsync(byte[] messageBytes, CancellationToken ct)
    {
        if (_broadcastMode != BroadcastMode.Send && _broadcastMode != BroadcastMode.Both) return;

        foreach (var (instanceId, ws) in _nodeConnections)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.SendAsync(
                        new ArraySegment<byte>(messageBytes),
                        WebSocketMessageType.Binary,
                        true,
                        ct);
                }
                else
                {
                    _nodeConnections.TryRemove(instanceId, out _);
                }
            }
            catch (Exception ex)
            {
                _nodeConnections.TryRemove(instanceId, out _);
                _logger.LogDebug(ex, "Error relaying broadcast to peer {PeerInstanceId}", instanceId);
            }
        }
    }

    /// <summary>
    /// Determines if two broadcast modes are compatible for establishing a connection.
    /// At least one side must send and the other must receive.
    /// </summary>
    /// <param name="myMode">This instance's broadcast mode.</param>
    /// <param name="peerMode">The peer instance's broadcast mode.</param>
    /// <returns>True if the two modes are compatible for a broadcast connection.</returns>
    internal static bool IsCompatible(BroadcastMode myMode, BroadcastMode peerMode)
    {
        if (myMode == BroadcastMode.None || peerMode == BroadcastMode.None) return false;
        if (myMode == BroadcastMode.Send && peerMode == BroadcastMode.Send) return false;
        if (myMode == BroadcastMode.Receive && peerMode == BroadcastMode.Receive) return false;
        return true;
    }

    private async Task<bool> ConnectToPeerAsync(BroadcastRegistryEntry peer, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "InterNodeBroadcastManager.ConnectToPeerAsync");

        ClientWebSocket? ws = null;
        try
        {
            ws = new ClientWebSocket();

            // Same auth as Internal mode
            if (!string.IsNullOrEmpty(_configuration.InternalServiceToken))
            {
                ws.Options.SetRequestHeader("X-Service-Token", _configuration.InternalServiceToken);
            }

            var uri = new Uri($"{peer.InternalUrl}?instanceId={_instanceId}");
            await ws.ConnectAsync(uri, ct);

            _nodeConnections.TryAdd(peer.InstanceId, ws);
            ws = null; // Ownership transferred to _nodeConnections

            // Start background read loop for this connection (use the stored reference)
            if (_nodeConnections.TryGetValue(peer.InstanceId, out var storedWs))
            {
                _ = Task.Run(async () => await ReadLoopAsync(storedWs, peer.InstanceId), CancellationToken.None);
            }

            _logger.LogInformation("Connected to broadcast peer {PeerInstanceId} at {PeerUrl}",
                peer.InstanceId, peer.InternalUrl);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to broadcast peer {PeerInstanceId} at {PeerUrl}",
                peer.InstanceId, peer.InternalUrl);
            return false;
        }
        finally
        {
            ws?.Dispose(); // Disposes only if ownership was NOT transferred
        }
    }

    private async Task ReadLoopAsync(WebSocket ws, Guid remoteInstanceId)
    {
        var buffer = new byte[_configuration.BufferSize];

        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                // Only deliver received broadcasts if mode allows receiving
                if ((_broadcastMode == BroadcastMode.Receive || _broadcastMode == BroadcastMode.Both)
                    && OnBroadcastReceived != null)
                {
                    try
                    {
                        // Copy the received bytes before passing to callback — the buffer
                        // is reused on the next ReceiveAsync iteration
                        var messageCopy = buffer[..result.Count];
                        await OnBroadcastReceived(messageCopy, result.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error delivering broadcast from peer {PeerInstanceId}",
                            remoteInstanceId);
                    }
                }
            }
        }
        catch (WebSocketException)
        {
            // Connection lost — expected during shutdown or network issues
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Broadcast read loop ended for peer {PeerInstanceId}", remoteInstanceId);
        }
        finally
        {
            _nodeConnections.TryRemove(remoteInstanceId, out _);
        }
    }

    private void MaintenanceCallback(object? state)
    {
        _ = Task.Run(async () =>
        {
            using var activity = _telemetryProvider.StartActivity("bannou.connect", "InterNodeBroadcastManager.MaintenanceCallback");

            try
            {
                var store = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Connect);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                // Heartbeat: refresh own registration score
                await store.SortedSetAddAsync(BROADCAST_REGISTRY_KEY, _registryMember, now);

                // Stale cleanup: find and remove entries older than threshold
                var cutoff = now - _configuration.BroadcastStaleThresholdSeconds;
                var staleEntries = await store.SortedSetRangeByScoreAsync(
                    BROADCAST_REGISTRY_KEY,
                    double.NegativeInfinity,
                    cutoff);

                foreach (var (member, score) in staleEntries)
                {
                    await store.SortedSetRemoveAsync(BROADCAST_REGISTRY_KEY, member);
                    _logger.LogInformation("Removed stale broadcast registry entry (score {Score}): {Member}",
                        score, member);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Broadcast registry maintenance failed");
            }
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _maintenanceTimer?.Dispose();
        _maintenanceTimer = null;

        // Deregister from Redis (fire-and-forget to avoid .Wait() deadlock)
        if (_isActive)
        {
            try
            {
                var store = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Connect);
                _ = store.SortedSetRemoveAsync(BROADCAST_REGISTRY_KEY, _registryMember)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            _logger.LogDebug(t.Exception, "Error deregistering from broadcast registry during shutdown");
                    }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error deregistering from broadcast registry during shutdown");
            }
        }

        // Close all inter-node WebSocket connections
        foreach (var (instanceId, ws) in _nodeConnections)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    _ = ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", CancellationToken.None)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                _logger.LogDebug(t.Exception, "Error closing broadcast connection to peer {PeerInstanceId}", instanceId);
                        }, TaskContinuationOptions.OnlyOnFaulted);
                }
                ws.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error closing broadcast connection to peer {PeerInstanceId}", instanceId);
            }
        }

        _nodeConnections.Clear();
        _logger.LogInformation("InterNodeBroadcastManager disposed");
    }
}
