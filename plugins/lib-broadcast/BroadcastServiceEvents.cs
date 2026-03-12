using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Broadcast;

/// <summary>
/// Partial class for BroadcastService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class BroadcastService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IBroadcastService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((BroadcastService)svc).HandleAccountDeletedAsync(evt));

        eventConsumer.RegisterHandler<IBroadcastService, VoiceBroadcastApprovedEvent>(
            "voice.broadcast.approved",
            async (svc, evt) => await ((BroadcastService)svc).HandleVoiceBroadcastApprovedAsync(evt));

        eventConsumer.RegisterHandler<IBroadcastService, VoiceBroadcastStoppedEvent>(
            "voice.broadcast.stopped",
            async (svc, evt) => await ((BroadcastService)svc).HandleVoiceBroadcastStoppedAsync(evt));

        eventConsumer.RegisterHandler<IBroadcastService, SessionDisconnectedEvent>(
            "session.disconnected",
            async (svc, evt) => await ((BroadcastService)svc).HandleSessionDisconnectedAsync(evt));

    }

    /// <summary>
    /// Handles account.deleted events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        // TODO: Implement account.deleted event handling
        _logger.LogInformation("Received {Topic} event", "account.deleted");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles voice.broadcast.approved events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleVoiceBroadcastApprovedAsync(VoiceBroadcastApprovedEvent evt)
    {
        // TODO: Implement voice.broadcast.approved event handling
        _logger.LogInformation("Received {Topic} event", "voice.broadcast.approved");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles voice.broadcast.stopped events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleVoiceBroadcastStoppedAsync(VoiceBroadcastStoppedEvent evt)
    {
        // TODO: Implement voice.broadcast.stopped event handling
        _logger.LogInformation("Received {Topic} event", "voice.broadcast.stopped");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles session.disconnected events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleSessionDisconnectedAsync(SessionDisconnectedEvent evt)
    {
        // TODO: Implement session.disconnected event handling
        _logger.LogInformation("Received {Topic} event", "session.disconnected");
        return Task.CompletedTask;
    }
}
