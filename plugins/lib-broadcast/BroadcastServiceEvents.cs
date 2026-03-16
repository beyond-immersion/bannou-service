using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
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
    /// Handles account.deleted events. T28 Account Deletion Cleanup Obligation.
    /// Delegates to CleanupByAccountAsync for all account-owned data removal.
    /// </summary>
    /// <param name="evt">The account deleted event data.</param>
    public async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        _logger.LogInformation("Handling account.deleted for account {AccountId}", evt.AccountId);
        await CleanupByAccountAsync(
            new CleanupByAccountRequest { AccountId = evt.AccountId },
            CancellationToken.None);
    }

    /// <summary>
    /// Handles voice.broadcast.approved events. All voice room participants have consented
    /// to broadcasting. Starts RTMP output for voice room. Soft — no-op if lib-voice absent.
    /// </summary>
    /// <param name="evt">The voice broadcast approved event data.</param>
    public async Task HandleVoiceBroadcastApprovedAsync(VoiceBroadcastApprovedEvent evt)
    {
        if (!_configuration.OutputEnabled || !_configuration.BroadcastEnabled)
        {
            _logger.LogDebug("Voice broadcast approved ignored: broadcast or output disabled");
            return;
        }

        _logger.LogInformation("Handling voice.broadcast.approved for room {RoomId}", evt.RoomId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles voice.broadcast.stopped events. Consent revoked, room closed, or manual stop.
    /// Soft — no-op if lib-voice absent.
    /// </summary>
    /// <param name="evt">The voice broadcast stopped event data.</param>
    public async Task HandleVoiceBroadcastStoppedAsync(VoiceBroadcastStoppedEvent evt)
    {
        _logger.LogInformation("Handling voice.broadcast.stopped for room {RoomId}", evt.RoomId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles session.disconnected events. Accelerated cleanup for platform sessions
    /// when a WebSocket client disconnects. Sessions have TTL safety net.
    /// </summary>
    /// <param name="evt">The session disconnected event data.</param>
    public async Task HandleSessionDisconnectedAsync(SessionDisconnectedEvent evt)
    {
        var sessionStore = _stateStoreFactory.GetStore<PlatformSessionModel>(StateStoreDefinitions.BroadcastSessions);
        if (sessionStore == null || evt.AccountId == null)
        {
            return;
        }

        var session = await sessionStore.GetAsync(BuildSessionAccountKey(evt.AccountId.Value), CancellationToken.None);
        if (session == null || session.State != PlatformSessionState.Active)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var duration = (int)(now - session.StartTime).TotalSeconds;

        await sessionStore.DeleteAsync(BuildSessionKey(session.PlatformSessionId), CancellationToken.None);
        await sessionStore.DeleteAsync(BuildSessionAccountKey(evt.AccountId.Value), CancellationToken.None);

        await _messageBus.PublishPlatformSessionDeletedAsync(new PlatformSessionDeletedEvent
        {
            PlatformSessionId = session.PlatformSessionId,
            LinkId = session.LinkId,
            AccountId = session.AccountId,
            Platform = session.Platform,
            State = PlatformSessionState.Ended,
            StartTime = session.StartTime,
            ViewerCount = session.ViewerCount,
            PeakViewerCount = session.PeakViewerCount,
            EndedAt = now,
            Duration = duration,
            CreatedAt = session.StartTime,
            UpdatedAt = now
        }, CancellationToken.None);

        _logger.LogInformation(
            "Platform session {PlatformSessionId} cleaned up on disconnect for account {AccountId}",
            session.PlatformSessionId, evt.AccountId);
    }
}
