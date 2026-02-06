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
        eventConsumer.RegisterHandler<IActorService, BehaviorUpdatedEvent>(
            "behavior.updated",
            async (svc, evt) => await ((ActorService)svc).HandleBehaviorUpdatedAsync(evt));

        eventConsumer.RegisterHandler<IActorService, SessionDisconnectedEvent>(
            "session.disconnected",
            async (svc, evt) => await ((ActorService)svc).HandleSessionDisconnectedAsync(evt));

        // Note: Personality and quest cache invalidation is now handled by the services that own
        // those caches (lib-character-personality, lib-quest) via their own event handlers.
        // Actor (L2) does not own these caches - it gets fresh data from provider factories.

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
    }

    /// <summary>
    /// Handles behavior.updated events.
    /// When a behavior is updated, invalidate the cache and notify running actors
    /// for hot-reload.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleBehaviorUpdatedAsync(BehaviorUpdatedEvent evt)
    {
        _logger.LogInformation("Received behavior.updated event for {BehaviorId}", evt.BehaviorId);

        try
        {
            // Invalidate cached behavior documents (enables hot-reload)
            _behaviorCache.InvalidateByBehaviorId(evt.BehaviorId);
            _logger.LogDebug("Invalidated cached behaviors matching {BehaviorId}", evt.BehaviorId);

            // Also invalidate by asset ID if present
            if (!string.IsNullOrEmpty(evt.AssetId))
            {
                _behaviorCache.Invalidate(evt.AssetId);
            }

            // Find actors using this behavior
            var templateStore = _stateStoreFactory.GetStore<ActorTemplateData>(StateStoreDefinitions.ActorTemplates);
            var indexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.ActorTemplates);

            // Get all template IDs from index
            var allIds = await indexStore.GetAsync(ALL_TEMPLATES_KEY, CancellationToken.None) ?? new List<string>();

            if (allIds.Count == 0)
                return;

            // Load all templates
            var allTemplates = await templateStore.GetBulkAsync(allIds, CancellationToken.None);

            foreach (var template in allTemplates.Values)
            {
                // Check if template uses this behavior
                if (string.Equals(template.BehaviorRef, evt.AssetId, StringComparison.OrdinalIgnoreCase) ||
                    template.BehaviorRef.Contains(evt.BehaviorId, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug(
                        "Template {TemplateId} uses updated behavior {BehaviorId}",
                        template.TemplateId, evt.BehaviorId);

                    // Get running actors for this template
                    var actors = _actorRegistry.GetByTemplateId(template.TemplateId).ToList();

                    foreach (var actor in actors)
                    {
                        // Inject a notification perception to inform the actor
                        // Use Dictionary<string, object?> instead of anonymous object per FOUNDATION TENETS
                        actor.InjectPerception(new PerceptionData
                        {
                            PerceptionType = "system",
                            SourceId = "behavior-service",
                            SourceType = PerceptionSourceType.Service,
                            Data = new Dictionary<string, object?>
                            {
                                ["eventType"] = "behavior_updated",
                                ["behaviorId"] = evt.BehaviorId
                            },
                            Urgency = 0.5f
                        });

                        _logger.LogDebug(
                            "Notified actor {ActorId} of behavior update",
                            actor.ActorId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling behavior.updated event for {BehaviorId}", evt.BehaviorId);
            await _messageBus.TryPublishErrorAsync(
                "actor",
                "HandleBehaviorUpdated",
                ex.GetType().Name,
                ex.Message,
                details: new { evt.BehaviorId, evt.AssetId },
                stack: ex.StackTrace);
        }
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
        if (_configuration.DeploymentMode == DeploymentMode.Bannou)
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
        if (_configuration.DeploymentMode == DeploymentMode.Bannou)
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
        if (_configuration.DeploymentMode == DeploymentMode.Bannou)
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
        if (_configuration.DeploymentMode == DeploymentMode.Bannou)
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
        if (_configuration.DeploymentMode == DeploymentMode.Bannou)
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
}
