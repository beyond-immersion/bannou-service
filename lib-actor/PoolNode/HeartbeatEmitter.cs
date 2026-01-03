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
public sealed class HeartbeatEmitter : IDisposable
{
    private readonly IMessageBus _messageBus;
    private readonly IActorRegistry _actorRegistry;
    private readonly ActorServiceConfiguration _configuration;
    private readonly ILogger<HeartbeatEmitter> _logger;
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
        ILogger<HeartbeatEmitter> logger)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _actorRegistry = actorRegistry ?? throw new ArgumentNullException(nameof(actorRegistry));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts emitting heartbeats.
    /// </summary>
    public void Start()
    {
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
        var nodeId = _configuration.PoolNodeId;
        var appId = _configuration.PoolNodeAppId;

        if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(appId))
        {
            _logger.LogWarning("Cannot emit heartbeat: PoolNodeId or PoolNodeAppId not configured");
            return;
        }

        // Collect actor statistics
        var runningActors = _actorRegistry.GetAllRunners().ToList();
        var runningCount = runningActors.Count;

        var heartbeat = new PoolNodeHeartbeatEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            NodeId = nodeId,
            AppId = appId,
            CurrentLoad = runningCount
        };

        await _messageBus.TryPublishAsync("actor.pool-node.heartbeat", heartbeat, cancellationToken: ct);

        _logger.LogDebug(
            "Emitted heartbeat for node {NodeId}: {ActorCount} actors",
            nodeId, runningCount);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}
