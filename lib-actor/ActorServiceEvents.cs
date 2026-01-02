using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Events;

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
    }

    /// <summary>
    /// Handles behavior.updated events.
    /// When a behavior is updated, actors using that behavior should be notified
    /// for potential hot-reload.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleBehaviorUpdatedAsync(BehaviorUpdatedEvent evt)
    {
        _logger.LogInformation("Received behavior.updated event for {BehaviorId}", evt.BehaviorId);

        try
        {
            // Find actors using this behavior
            var templateStore = _stateStoreFactory.Create<ActorTemplateData>(TEMPLATE_STORE);
            var allTemplates = await templateStore.GetAllAsync(CancellationToken.None);

            foreach (var (key, template) in allTemplates)
            {
                // Skip category: prefixed keys
                if (!Guid.TryParse(key, out _))
                    continue;

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
                        actor.InjectPerception(new PerceptionData
                        {
                            PerceptionType = "system",
                            SourceId = "behavior-service",
                            SourceType = "service",
                            Data = new { eventType = "behavior_updated", behaviorId = evt.BehaviorId },
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
        }
    }

    /// <summary>
    /// Handles session.disconnected events.
    /// When a session disconnects, stop any actors associated with that session.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleSessionDisconnectedAsync(SessionDisconnectedEvent evt)
    {
        _logger.LogInformation(
            "Received session.disconnected event for session {SessionId}",
            evt.SessionId);

        // Note: In the current implementation, actors are not directly tied to sessions.
        // This handler is here for future use cases where actors might be session-bound
        // (e.g., player-controlled actors that should stop when the player disconnects).

        // For NPC brain actors, they continue running even when players disconnect.
        // For session-bound actors (future), we would stop them here.

        return Task.CompletedTask;
    }
}
