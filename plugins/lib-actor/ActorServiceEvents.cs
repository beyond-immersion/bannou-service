using BeyondImmersion.BannouService.Actor.Pool;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Actor;

/// <summary>
/// Partial class for ActorService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class ActorService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IActorService, SessionDisconnectedEvent>(
            "session.disconnected",
            async (svc, evt) => await ((ActorService)svc).HandleSessionDisconnectedAsync(evt));

        // Pool node events (control plane only - when _poolManager is available)
        eventConsumer.RegisterHandler<IActorService, PoolNodeRegisteredEvent>(
            "actor.pool-node.registered",
            async (svc, evt) => await ((ActorService)svc).HandlePoolNodeRegisteredAsync(evt));

        eventConsumer.RegisterHandler<IActorService, PoolNodeHeartbeatEvent>(
            "actor.pool-node.heartbeat",
            async (svc, evt) => await ((ActorService)svc).HandlePoolNodeHeartbeatAsync(evt));

        eventConsumer.RegisterHandler<IActorService, PoolNodeDrainingEvent>(
            "actor.pool-node.draining",
            async (svc, evt) => await ((ActorService)svc).HandlePoolNodeDrainingAsync(evt));

        eventConsumer.RegisterHandler<IActorService, ActorStatusChangedEvent>(
            "actor.instance.status-changed",
            async (svc, evt) => await ((ActorService)svc).HandleActorStatusChangedAsync(evt));

        eventConsumer.RegisterHandler<IActorService, ActorCompletedEvent>(
            "actor.instance.completed",
            async (svc, evt) => await ((ActorService)svc).HandleActorCompletedAsync(evt));

        eventConsumer.RegisterHandler<IActorService, ActorTemplateUpdatedEvent>(
            "actor-template.updated",
            async (svc, evt) => await ((ActorService)svc).HandleActorTemplateUpdatedAsync(evt));
    }

    /// <summary>
    /// Handles session.disconnected events.
    /// When a session disconnects, stop any actors associated with that session.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleSessionDisconnectedAsync(SessionDisconnectedEvent evt)
    {
        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
        _logger.LogInformation(
            "Received session.disconnected event for session {SessionId}",
            evt.SessionId);

        // Note: In the current implementation, actors are not directly tied to sessions.
        // This handler is here for future use cases where actors might be session-bound
        // (e.g., player-controlled actors that should stop when the player disconnects).

        // For NPC brain actors, they continue running even when players disconnect.
        // For session-bound actors (future), we would stop them here.
    }

    #region Pool Node Event Handlers

    /// <summary>
    /// Handles pool node registration events.
    /// Called when a pool node starts and registers with the control plane.
    /// </summary>
    /// <param name="evt">The registration event.</param>
    public async Task HandlePoolNodeRegisteredAsync(PoolNodeRegisteredEvent evt)
    {
        if (_configuration.DeploymentMode == ActorDeploymentMode.Bannou)
        {
            _logger.LogDebug("Ignoring pool-node.registered event (not in control plane mode)");
            return;
        }

        _logger.LogInformation(
            "Pool node registered: NodeId={NodeId}, AppId={AppId}, PoolType={PoolType}, Capacity={Capacity}",
            evt.NodeId, evt.AppId, evt.PoolType, evt.Capacity);

        try
        {
            var success = await _poolManager.RegisterNodeAsync(evt, CancellationToken.None);
            if (!success)
            {
                _logger.LogWarning("Pool node {NodeId} already registered (duplicate registration)", evt.NodeId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering pool node {NodeId}", evt.NodeId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "HandlePoolNodeRegistered",
                ex.GetType().Name,
                ex.Message,
                details: new { evt.NodeId, evt.AppId, evt.PoolType },
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Handles pool node heartbeat events.
    /// Called periodically by pool nodes to indicate they are healthy.
    /// </summary>
    /// <param name="evt">The heartbeat event.</param>
    public async Task HandlePoolNodeHeartbeatAsync(PoolNodeHeartbeatEvent evt)
    {
        if (_configuration.DeploymentMode == ActorDeploymentMode.Bannou)
        {
            _logger.LogDebug("Ignoring pool-node.heartbeat event (not in control plane mode)");
            return;
        }

        _logger.LogDebug(
            "Pool node heartbeat: NodeId={NodeId}, CurrentLoad={Load}",
            evt.NodeId, evt.CurrentLoad);

        try
        {
            await _poolManager.UpdateNodeHeartbeatAsync(evt, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating heartbeat for pool node {NodeId}", evt.NodeId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "HandlePoolNodeHeartbeat",
                ex.GetType().Name,
                ex.Message,
                details: new { evt.NodeId, evt.CurrentLoad },
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Handles pool node draining events.
    /// Called when a pool node is shutting down and draining its actors.
    /// </summary>
    /// <param name="evt">The draining event.</param>
    public async Task HandlePoolNodeDrainingAsync(PoolNodeDrainingEvent evt)
    {
        if (_configuration.DeploymentMode == ActorDeploymentMode.Bannou)
        {
            _logger.LogDebug("Ignoring pool-node.draining event (not in control plane mode)");
            return;
        }

        _logger.LogInformation(
            "Pool node draining: NodeId={NodeId}, RemainingActors={Count}",
            evt.NodeId, evt.RemainingActors);

        try
        {
            await _poolManager.DrainNodeAsync(evt.NodeId, evt.RemainingActors, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking pool node {NodeId} as draining", evt.NodeId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "HandlePoolNodeDraining",
                ex.GetType().Name,
                ex.Message,
                details: new { evt.NodeId, evt.RemainingActors },
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Handles actor status changed events.
    /// Called when an actor's status changes (running, paused, error, etc.).
    /// </summary>
    /// <param name="evt">The status changed event.</param>
    public async Task HandleActorStatusChangedAsync(ActorStatusChangedEvent evt)
    {
        if (_configuration.DeploymentMode == ActorDeploymentMode.Bannou)
        {
            _logger.LogDebug("Ignoring actor.instance.status-changed event (not in control plane mode)");
            return;
        }

        _logger.LogDebug(
            "Actor status changed: ActorId={ActorId}, {OldStatus} -> {NewStatus}",
            evt.ActorId, evt.PreviousStatus, evt.NewStatus);

        try
        {
            await _poolManager.UpdateActorStatusAsync(evt.ActorId, evt.NewStatus, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating status for actor {ActorId}", evt.ActorId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "HandleActorStatusChanged",
                ex.GetType().Name,
                ex.Message,
                details: new { evt.ActorId, evt.PreviousStatus, evt.NewStatus },
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Handles actor completed events.
    /// Called when an actor finishes execution (behavior complete, error, stopped).
    /// </summary>
    /// <param name="evt">The completed event.</param>
    public async Task HandleActorCompletedAsync(ActorCompletedEvent evt)
    {
        if (_configuration.DeploymentMode == ActorDeploymentMode.Bannou)
        {
            _logger.LogDebug("Ignoring actor.instance.completed event (not in control plane mode)");
            return;
        }

        _logger.LogInformation(
            "Actor completed: ActorId={ActorId}, ExitReason={Reason}, Iterations={Iterations}",
            evt.ActorId, evt.ExitReason, evt.LoopIterations);

        try
        {
            var removed = await _poolManager.RemoveActorAssignmentAsync(evt.ActorId, CancellationToken.None);
            if (!removed)
            {
                _logger.LogDebug("No assignment found for completed actor {ActorId}", evt.ActorId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing assignment for completed actor {ActorId}", evt.ActorId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "HandleActorCompleted",
                ex.GetType().Name,
                ex.Message,
                details: new { evt.ActorId, evt.ExitReason, evt.LoopIterations },
                stack: ex.StackTrace);
        }
    }

    #endregion

    #region Template Update Event Handlers

    /// <summary>
    /// Handles actor-template.updated events.
    /// When a template's BehaviorRef changes, invalidates the behavior document cache
    /// and signals running actors on this node to reload on their next tick.
    /// </summary>
    /// <param name="evt">The template updated event.</param>
    public async Task HandleActorTemplateUpdatedAsync(ActorTemplateUpdatedEvent evt)
    {
        // Only act on BehaviorRef changes — other field updates don't affect cached behaviors
        if (!evt.ChangedFields.Contains("behaviorRef"))
        {
            _logger.LogDebug(
                "Template {TemplateId} updated but BehaviorRef unchanged, skipping cache invalidation",
                evt.TemplateId);
            return;
        }

        _logger.LogInformation(
            "Template {TemplateId} BehaviorRef changed to {BehaviorRef}, invalidating behavior caches",
            evt.TemplateId, evt.BehaviorRef);

        try
        {
            // Invalidate provider chain caches so next load fetches the updated behavior
            _behaviorLoader.Invalidate(evt.BehaviorRef);

            // Signal running actors on this node that use this template to reload
            var affectedRunners = _actorRegistry.GetByTemplateId(evt.TemplateId).ToList();
            foreach (var runner in affectedRunners)
            {
                runner.InvalidateCachedBehavior();
            }

            if (affectedRunners.Count > 0)
            {
                _logger.LogInformation(
                    "Invalidated cached behavior for {Count} running actors using template {TemplateId}",
                    affectedRunners.Count, evt.TemplateId);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating behavior caches for template {TemplateId}", evt.TemplateId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "HandleActorTemplateUpdated",
                ex.GetType().Name,
                ex.Message,
                details: new { evt.TemplateId, evt.BehaviorRef },
                stack: ex.StackTrace);
        }
    }

    #endregion
}
