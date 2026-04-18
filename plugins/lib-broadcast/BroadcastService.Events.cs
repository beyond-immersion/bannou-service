using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Voice;
using Microsoft.Extensions.DependencyInjection;
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
        using var activity = _telemetryProvider.StartActivity(
            "bannou.broadcast", "BroadcastService.HandleAccountDeleted");

        _logger.LogInformation("Handling account.deleted for account {AccountId}", evt.AccountId);
        try
        {
            await CleanupByAccountAsync(
                new CleanupByAccountRequest { AccountId = evt.AccountId },
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up broadcast data for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "broadcast", "CleanupForAccount", ex.GetType().Name, ex.Message,
                endpoint: "account.deleted", details: $"accountId={evt.AccountId}",
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Handles voice.broadcast.approved events. All voice room participants have consented
    /// to broadcasting. Starts RTMP output for voice room. Soft — no-op if lib-voice absent.
    /// </summary>
    /// <param name="evt">The voice broadcast approved event data.</param>
    public async Task HandleVoiceBroadcastApprovedAsync(VoiceBroadcastApprovedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.broadcast", "BroadcastService.HandleVoiceBroadcastApproved");

        if (!_configuration.OutputEnabled || !_configuration.BroadcastEnabled)
        {
            _logger.LogDebug("Voice broadcast approved ignored: broadcast or output disabled");
            return;
        }

        if (evt.RtpAudioEndpoint == null)
        {
            _logger.LogWarning("Voice broadcast approved ignored for room {RoomId}: no RTP audio endpoint", evt.RoomId);
            return;
        }

        _logger.LogInformation("Handling voice.broadcast.approved for room {RoomId}", evt.RoomId);

        // Validate RTP endpoint reachability
        var rtmpValid = await _broadcastCoordinator.ValidateRtmpUrlAsync(
            evt.RtpAudioEndpoint, _configuration.RtmpProbeTimeoutSeconds, CancellationToken.None);
        if (!rtmpValid)
        {
            _logger.LogError("RTP endpoint validation failed for voice broadcast: {Endpoint}", evt.RtpAudioEndpoint);
            return;
        }

        // Check concurrent output limit
        if (_outputStore is IQueryableStateStore<BroadcastOutputModel> queryableStore)
        {
            var activeCount = (await queryableStore.QueryAsync(
                b => b.State == BroadcastState.Active, CancellationToken.None)).Count();
            if (activeCount >= _configuration.MaxConcurrentOutputs)
            {
                _logger.LogWarning("Voice broadcast rejected: concurrent output limit reached ({Count}/{Max})",
                    activeCount, _configuration.MaxConcurrentOutputs);
                return;
            }
        }

        var broadcastId = Guid.NewGuid();

        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.BroadcastLock,
            $"broadcast:{broadcastId}",
            Guid.NewGuid().ToString(),
            _configuration.DistributedLockTimeoutSeconds,
            CancellationToken.None);
        if (!lockHandle.Success)
        {
            _logger.LogWarning("Voice broadcast lock acquisition failed for {BroadcastId}", broadcastId);
            return;
        }

        // Resolve IVoiceClient (soft L3 dependency)
        var voiceClient = _serviceProvider.GetService<IVoiceClient>();
        if (voiceClient == null)
        {
            _logger.LogError("Voice broadcast failed: lib-voice not available");
            return;
        }

        // Validate room exists per map specification
        try
        {
            await voiceClient.GetVoiceRoomAsync(new GetVoiceRoomRequest { RoomId = evt.RoomId }, CancellationToken.None);
        }
        catch (ApiException ex)
        {
            _logger.LogError("Voice broadcast failed: room {RoomId} not found", evt.RoomId);
            await _messageBus.TryPublishErrorAsync(
                "broadcast",
                "HandleVoiceBroadcastApproved",
                "VoiceRoomNotFound",
                ex.Message,
                dependency: "voice",
                endpoint: "get-voice-room",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
            return;
        }

        var sourceUrl = evt.RtpAudioEndpoint;
        await _broadcastCoordinator.StartBroadcastAsync(
            broadcastId, evt.RtpAudioEndpoint, sourceUrl, CancellationToken.None);

        var now = DateTimeOffset.UtcNow;
        var maskedUrl = MaskRtmpUrl(evt.RtpAudioEndpoint);
        var instanceId = _meshInstanceIdentifier.InstanceId.ToString();

        var model = new BroadcastOutputModel
        {
            BroadcastId = broadcastId,
            SourceType = BroadcastSourceType.VoiceRoom,
            SourceId = evt.RoomId.ToString(),
            EncryptedRtmpUrl = evt.RtpAudioEndpoint,
            MaskedRtmpUrl = maskedUrl,
            OwningInstanceId = instanceId,
            State = BroadcastState.Active,
            StartedAt = now,
            Health = BroadcastHealth.Healthy
        };

        await _outputStore.SaveAsync(BuildOutputKey(broadcastId), model, cancellationToken: CancellationToken.None);

        await _messageBus.PublishOutputCreatedAsync(new OutputCreatedEvent
        {
            BroadcastId = broadcastId,
            SourceType = BroadcastSourceType.VoiceRoom,
            SourceId = evt.RoomId.ToString(),
            MaskedRtmpUrl = maskedUrl,
            State = BroadcastState.Active,
            OwningInstanceId = instanceId,
            StartedAt = now,
            Health = BroadcastHealth.Healthy,
            CreatedAt = now,
            UpdatedAt = now
        }, CancellationToken.None);
    }

    /// <summary>
    /// Handles voice.broadcast.stopped events. Consent revoked, room closed, or manual stop.
    /// Stops the RTMP output for the voice room.
    /// </summary>
    /// <param name="evt">The voice broadcast stopped event data.</param>
    public async Task HandleVoiceBroadcastStoppedAsync(VoiceBroadcastStoppedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.broadcast", "BroadcastService.HandleVoiceBroadcastStopped");

        _logger.LogInformation("Handling voice.broadcast.stopped for room {RoomId}", evt.RoomId);

        if (_outputStore is IQueryableStateStore<BroadcastOutputModel> queryableStore)
        {
            var broadcasts = await queryableStore.QueryAsync(
                b => b.SourceType == BroadcastSourceType.VoiceRoom
                    && b.SourceId == evt.RoomId.ToString()
                    && b.State == BroadcastState.Active,
                CancellationToken.None);

            var broadcast = broadcasts.FirstOrDefault();
            if (broadcast == null)
            {
                _logger.LogDebug("No active voice room broadcast found for room {RoomId}", evt.RoomId);
                return;
            }

            await using var lockHandle = await _lockProvider.LockAsync(
                StateStoreDefinitions.BroadcastLock,
                $"broadcast:{broadcast.BroadcastId}",
                Guid.NewGuid().ToString(),
                _configuration.DistributedLockTimeoutSeconds,
                CancellationToken.None);
            if (!lockHandle.Success)
            {
                _logger.LogWarning("Lock acquisition failed for voice broadcast stop {BroadcastId}",
                    broadcast.BroadcastId);
                return;
            }

            await _broadcastCoordinator.StopBroadcastAsync(broadcast.BroadcastId, CancellationToken.None);
            await _outputStore.DeleteAsync(BuildOutputKey(broadcast.BroadcastId), CancellationToken.None);

            await _messageBus.PublishOutputDeletedAsync(new OutputDeletedEvent
            {
                BroadcastId = broadcast.BroadcastId,
                SourceType = broadcast.SourceType,
                SourceId = broadcast.SourceId,
                MaskedRtmpUrl = broadcast.MaskedRtmpUrl,
                State = BroadcastState.Stopped,
                OwningInstanceId = broadcast.OwningInstanceId,
                StartedAt = broadcast.StartedAt,
                Health = broadcast.Health,
                CreatedAt = broadcast.StartedAt,
                UpdatedAt = DateTimeOffset.UtcNow
            }, CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles session.disconnected events. Accelerated cleanup for platform sessions
    /// when a WebSocket client disconnects. Sessions have TTL safety net.
    /// </summary>
    /// <param name="evt">The session disconnected event data.</param>
    public async Task HandleSessionDisconnectedAsync(SessionDisconnectedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.broadcast", "BroadcastService.HandleSessionDisconnected");

        if (evt.AccountId == null)
        {
            return;
        }

        var session = await _sessionStore.GetAsync(
            BuildSessionAccountKey(evt.AccountId.Value), CancellationToken.None);
        if (session == null || session.State != PlatformSessionState.Active)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var duration = (int)(now - session.StartTime).TotalSeconds;

        await _sentimentProcessor.CleanupSessionTrackingAsync(session.PlatformSessionId, CancellationToken.None);
        await _sessionStore.DeleteAsync(BuildSessionKey(session.PlatformSessionId), CancellationToken.None);
        await _sessionStore.DeleteAsync(BuildSessionAccountKey(evt.AccountId.Value), CancellationToken.None);

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
