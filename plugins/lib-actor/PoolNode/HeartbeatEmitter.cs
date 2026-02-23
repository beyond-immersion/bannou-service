// =============================================================================
// Heartbeat Emitter
// Periodically publishes heartbeat events to the control plane.
// =============================================================================

using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Actor.PoolNode;

/// <summary>
/// Emits periodic heartbeat events to the control plane.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS:</b> Heartbeats are idempotent and safe for multi-instance deployments.
/// Each pool node has a unique NodeId, so duplicate heartbeats from same node are expected.
/// </para>
/// </remarks>
public sealed class HeartbeatEmitter : IAsyncDisposable
{
    private readonly IMessageBus _messageBus;
    private readonly IActorRegistry _actorRegistry;
    private readonly ActorServiceConfiguration _configuration;
    private readonly ILogger<HeartbeatEmitter> _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly CancellationTokenSource _cts = new();
    private Task? _heartbeatTask;
    private bool _disposed;

    /// <summary>
    /// Heartbeat interval from configuration.
    /// </summary>
    private TimeSpan HeartbeatInterval => TimeSpan.FromSeconds(_configuration.HeartbeatIntervalSeconds);

    /// <summary>
    /// Creates a new HeartbeatEmitter.
    /// </summary>
    public HeartbeatEmitter(
        IMessageBus messageBus,
        IActorRegistry actorRegistry,
        ActorServiceConfiguration configuration,
        ILogger<HeartbeatEmitter> logger,
        ITelemetryProvider telemetryProvider)
    {
        _messageBus = messageBus;
        _actorRegistry = actorRegistry;
        _configuration = configuration;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Starts emitting heartbeats.
    /// </summary>
    /// <remarks>
    /// Only emits heartbeats if running in pool node mode (PoolNodeId configured).
    /// Returns immediately as no-op if not in pool node mode.
    /// </remarks>
    public void Start()
    {
        // Only run if configured as a pool node
        if (string.IsNullOrEmpty(_configuration.PoolNodeId) ||
            string.IsNullOrEmpty(_configuration.PoolNodeAppId))
        {
            _logger.LogDebug("Heartbeat emitter disabled (not running as pool node)");
            return;
        }

        if (_heartbeatTask != null)
        {
            _logger.LogWarning("Heartbeat emitter already started");
            return;
        }

        _logger.LogInformation(
            "Starting heartbeat emitter (interval: {Interval}s)",
            HeartbeatInterval.TotalSeconds);

        _heartbeatTask = EmitHeartbeatsAsync(_cts.Token);
    }

    /// <summary>
    /// Stops emitting heartbeats.
    /// </summary>
    public async Task StopAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "HeartbeatEmitter.Stop");
        if (_heartbeatTask == null)
        {
            return;
        }

        _logger.LogInformation("Stopping heartbeat emitter");

        await _cts.CancelAsync();

        try
        {
            await _heartbeatTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        _heartbeatTask = null;
    }

    private async Task EmitHeartbeatsAsync(CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "HeartbeatEmitter.EmitHeartbeats");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await EmitHeartbeatAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error emitting heartbeat");
            }

            try
            {
                await Task.Delay(HeartbeatInterval, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task EmitHeartbeatAsync(CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "HeartbeatEmitter.EmitHeartbeat");
        // Start() validates PoolNodeId/PoolNodeAppId, so these are guaranteed non-null here
        var nodeId = _configuration.PoolNodeId ?? throw new InvalidOperationException("PoolNodeId not configured");
        var appId = _configuration.PoolNodeAppId ?? throw new InvalidOperationException("PoolNodeAppId not configured");

        // Collect actor statistics
        var runningActors = _actorRegistry.GetAllRunners().ToList();
        var runningCount = runningActors.Count;

        var heartbeat = new PoolNodeHeartbeatEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = nodeId,
            AppId = appId,
            CurrentLoad = runningCount,
            Capacity = _configuration.PoolNodeCapacity
        };

        await _messageBus.TryPublishAsync("actor.pool-node.heartbeat", heartbeat, cancellationToken: ct);

        _logger.LogDebug(
            "Emitted heartbeat for node {NodeId}: {ActorCount} actors",
            nodeId, runningCount);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.actor", "HeartbeatEmitter.Dispose");
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync();
        _cts.Dispose();
    }
}
