// =============================================================================
// Pool Health Monitor
// BackgroundService that monitors pool node heartbeats and marks unhealthy nodes.
// =============================================================================

using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Actor.Pool;

/// <summary>
/// Background service that monitors pool node heartbeats.
/// Detects nodes that have missed heartbeats and publishes unhealthy events.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS:</b> Uses distributed state, safe for multi-instance control planes.
/// Only one instance will "win" the race to mark a node unhealthy (idempotent).
/// </para>
/// </remarks>
public sealed class PoolHealthMonitor : BackgroundService
{
    private readonly IActorPoolManager _poolManager;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<PoolHealthMonitor> _logger;
    private readonly ActorServiceConfiguration _configuration;

    /// <summary>
    /// Check interval for scanning heartbeats (half of timeout for faster detection).
    /// </summary>
    private TimeSpan CheckInterval => TimeSpan.FromSeconds(_configuration.HeartbeatTimeoutSeconds / 2.0);

    /// <summary>
    /// Heartbeat timeout - nodes without heartbeat for this duration are marked unhealthy.
    /// </summary>
    private TimeSpan HeartbeatTimeout => TimeSpan.FromSeconds(_configuration.HeartbeatTimeoutSeconds);

    /// <summary>
    /// Creates a new PoolHealthMonitor.
    /// </summary>
    public PoolHealthMonitor(
        IActorPoolManager poolManager,
        IMessageBus messageBus,
        ILogger<PoolHealthMonitor> logger,
        ActorServiceConfiguration configuration)
    {
        _poolManager = poolManager;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Only run in control plane mode (non-bannou deployment)
        // Pool nodes don't need to monitor other nodes - the control plane does
        if (_configuration.DeploymentMode == "bannou")
        {
            _logger.LogDebug("Pool health monitor disabled in bannou mode (local actors only)");
            return;
        }

        // Also don't run on pool nodes themselves
        if (!string.IsNullOrEmpty(_configuration.PoolNodeId))
        {
            _logger.LogDebug("Pool health monitor disabled on pool node (control plane responsibility)");
            return;
        }

        _logger.LogInformation(
            "Pool health monitor started (check interval: {Interval}s, timeout: {Timeout}s)",
            CheckInterval.TotalSeconds, HeartbeatTimeout.TotalSeconds);

        // Wait a bit for initial node registrations
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckPoolHealthAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during pool health check");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Pool health monitor stopped");
    }

    /// <summary>
    /// Checks for unhealthy nodes and publishes events.
    /// </summary>
    private async Task CheckPoolHealthAsync(CancellationToken ct)
    {
        var unhealthyNodes = await _poolManager.GetUnhealthyNodesAsync(HeartbeatTimeout, ct);

        foreach (var node in unhealthyNodes)
        {
            _logger.LogWarning(
                "Pool node {NodeId} missed heartbeat (last seen: {LastSeen})",
                node.NodeId, node.LastHeartbeat);

            // Get count of actors on this node before removal
            var actors = await _poolManager.ListActorsByNodeAsync(node.NodeId, ct);

            // Publish unhealthy event
            var unhealthyEvent = new PoolNodeUnhealthyEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                NodeId = node.NodeId,
                AppId = node.AppId,
                Reason = "heartbeat_timeout",
                LastHeartbeat = node.LastHeartbeat,
                ActorCount = actors.Count
            };

            await _messageBus.TryPublishAsync("actor.pool-node.unhealthy", unhealthyEvent, cancellationToken: ct);

            // Remove the node from the pool
            await _poolManager.RemoveNodeAsync(node.NodeId, "heartbeat_timeout", ct);

            _logger.LogInformation(
                "Marked pool node {NodeId} as unhealthy and removed ({ActorCount} actors affected)",
                node.NodeId, actors.Count);
        }

        // Log summary if any nodes were found unhealthy
        if (unhealthyNodes.Count > 0)
        {
            var summary = await _poolManager.GetCapacitySummaryAsync(ct);
            _logger.LogInformation(
                "Pool capacity after health check: {HealthyNodes} healthy, {TotalCapacity} capacity, {TotalLoad} load",
                summary.HealthyNodes, summary.TotalCapacity, summary.TotalLoad);
        }
    }
}
