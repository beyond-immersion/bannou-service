using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Voice.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Voice.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Voice;

/// <summary>
/// Partial class for VoiceService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
/// <remarks>
/// Subscribes to Connect (L1) session lifecycle events for participant cleanup
/// and state restoration. L3 subscribing to L1 events is correct hierarchy direction
/// per FOUNDATION TENETS.
/// </remarks>
public partial class VoiceService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IVoiceService, SessionDisconnectedEvent>(
            "session.disconnected",
            async (svc, evt) => await ((VoiceService)svc).HandleSessionDisconnectedAsync(evt));

        eventConsumer.RegisterHandler<IVoiceService, SessionReconnectedEvent>(
            "session.reconnected",
            async (svc, evt) => await ((VoiceService)svc).HandleSessionReconnectedAsync(evt));
    }

    /// <summary>
    /// Handles session.disconnected events from Connect (L1).
    /// Cleans up voice room participation for disconnected sessions.
    /// The ParticipantEvictionWorker remains as a safety net for missed events.
    /// </summary>
    /// <param name="evt">The session disconnected event.</param>
    private async Task HandleSessionDisconnectedAsync(SessionDisconnectedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "VoiceServiceEvents.HandleSessionDisconnectedAsync");

        // Check if this session was in a voice room
        var roomIdStr = await _stringStore.GetAsync($"{SESSION_ROOM_KEY_PREFIX}{evt.SessionId}", CancellationToken.None);

        if (string.IsNullOrEmpty(roomIdStr) || !Guid.TryParse(roomIdStr, out var roomId))
        {
            _logger.LogDebug("Disconnected session {SessionId} was not in a voice room", evt.SessionId);
            return;
        }

        _logger.LogInformation("Cleaning up voice room {RoomId} participation for disconnected session {SessionId}",
            roomId, evt.SessionId);

        // Unregister participant from endpoint registry
        var removed = await _endpointRegistry.UnregisterAsync(roomId, evt.SessionId, CancellationToken.None);

        if (removed == null)
        {
            _logger.LogDebug("Session {SessionId} already removed from room {RoomId} endpoint registry", evt.SessionId, roomId);
        }

        // Clear voice permission state
        try
        {
            await _permissionClient.ClearSessionStateAsync(new ClearSessionStateRequest
            {
                SessionId = evt.SessionId,
                ServiceId = "voice"
            }, CancellationToken.None);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to clear voice permission state for disconnected session {SessionId}", evt.SessionId);
        }

        // Delete session -> room mapping
        await _stringStore.DeleteAsync($"{SESSION_ROOM_KEY_PREFIX}{evt.SessionId}", CancellationToken.None);

        // Get remaining count for event and peer notification
        var remainingCount = await _endpointRegistry.GetParticipantCountAsync(roomId, CancellationToken.None);

        // Publish peer left service event
        await _messageBus.PublishVoicePeerLeftAsync(new VoicePeerLeftEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = roomId,
            PeerSessionId = evt.SessionId,
            RemainingCount = remainingCount
        });

        // Notify remaining peers
        if (removed != null)
        {
            await NotifyPeerLeftAsync(roomId, removed.SessionId, removed.DisplayName, remainingCount, CancellationToken.None);
        }

        // Handle broadcast consent state and empty room cleanup
        // Distributed lock prevents concurrent leave/consent operations from racing
        // on the read-modify-write of BroadcastState (per IMPLEMENTATION TENETS)
        var lockOwner = $"disconnect-broadcast-{evt.SessionId:N}-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.VoiceLock,
            $"broadcast-consent:{roomId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken: CancellationToken.None);

        if (lockResponse.Success)
        {
            var roomData = await _roomStore.GetAsync($"{ROOM_KEY_PREFIX}{roomId}", CancellationToken.None);

            if (roomData != null)
            {
                // If broadcasting or consent pending, stop broadcast (consent broken by departure)
                if (roomData.BroadcastState == BroadcastConsentState.Approved ||
                    roomData.BroadcastState == BroadcastConsentState.Pending)
                {
                    await StopBroadcastInternalAsync(roomId, roomData, VoiceBroadcastStoppedReason.ConsentRevoked, CancellationToken.None);
                }

                // If room is now empty and AutoCleanup, set timestamp for grace period
                if (remainingCount == 0 && roomData.AutoCleanup)
                {
                    roomData.LastParticipantLeftAt = DateTimeOffset.UtcNow;
                    await _roomStore.SaveAsync($"{ROOM_KEY_PREFIX}{roomId}", roomData, cancellationToken: CancellationToken.None);
                }
            }
        }
        else
        {
            _logger.LogWarning("Failed to acquire broadcast consent lock during disconnect cleanup for room {RoomId}", roomId);
        }

        _logger.LogInformation("Cleaned up voice room {RoomId} for disconnected session {SessionId}, {RemainingCount} participants remaining",
            roomId, evt.SessionId, remainingCount);
    }

    /// <summary>
    /// Handles session.reconnected events from Connect (L1).
    /// Restores voice room state for reconnected sessions that were still in a room.
    /// </summary>
    /// <param name="evt">The session reconnected event.</param>
    private async Task HandleSessionReconnectedAsync(SessionReconnectedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "VoiceServiceEvents.HandleSessionReconnectedAsync");

        // Check if this session was in a voice room
        var roomIdStr = await _stringStore.GetAsync($"{SESSION_ROOM_KEY_PREFIX}{evt.SessionId}", CancellationToken.None);

        if (string.IsNullOrEmpty(roomIdStr) || !Guid.TryParse(roomIdStr, out var roomId))
        {
            _logger.LogDebug("Reconnected session {SessionId} was not in a voice room", evt.SessionId);
            return;
        }

        // Verify session is still registered as participant
        var participant = await _endpointRegistry.GetParticipantAsync(roomId, evt.SessionId, CancellationToken.None);

        if (participant == null)
        {
            _logger.LogDebug("Reconnected session {SessionId} no longer registered in room {RoomId}, cleaning up stale mapping",
                evt.SessionId, roomId);
            // Clean up stale session-room mapping
            await _stringStore.DeleteAsync($"{SESSION_ROOM_KEY_PREFIX}{evt.SessionId}", CancellationToken.None);
            return;
        }

        // Load room data for state restoration
        var roomData = await _roomStore.GetAsync($"{ROOM_KEY_PREFIX}{roomId}", CancellationToken.None);

        if (roomData == null)
        {
            _logger.LogWarning("Room {RoomId} no longer exists for reconnected session {SessionId}, cleaning up",
                roomId, evt.SessionId);
            await _stringStore.DeleteAsync($"{SESSION_ROOM_KEY_PREFIX}{evt.SessionId}", CancellationToken.None);
            return;
        }

        _logger.LogInformation("Restoring voice room {RoomId} state for reconnected session {SessionId}",
            roomId, evt.SessionId);

        // Re-set voice:in_room permission state
        try
        {
            await _permissionClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = evt.SessionId,
                ServiceId = "voice",
                NewState = "in_room"
            }, CancellationToken.None);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to restore voice permission state for reconnected session {SessionId}", evt.SessionId);
        }

        // Get current peers for room state event (skip peers with no endpoint data)
        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, CancellationToken.None);
        var peers = participants
            .Where(p => p.SessionId != evt.SessionId && p.Endpoint != null)
            .Select(p => new VoicePeerInfo
            {
                PeerSessionId = p.SessionId,
                DisplayName = p.DisplayName,
                // Where clause filters null Endpoints; coalesce satisfies compiler nullable analysis
                SdpOffer = p.Endpoint?.SdpOffer ?? string.Empty,
                IceCandidates = p.Endpoint?.IceCandidates?.ToList()
            })
            .ToList();

        // Parse STUN servers from config (StunServers has a schema default; null means infrastructure failure)
        var stunServers = _configuration.StunServers?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList() ?? throw new InvalidOperationException("StunServers configuration is required but was null");

        // Publish VoiceRoomStateClientEvent to restore client voice state
        await _clientEventPublisher.PublishToSessionAsync(evt.SessionId.ToString(), new VoiceRoomStateClientEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = roomId,
            SessionId = evt.SessionId,
            Tier = roomData.Tier,
            Codec = roomData.Codec,
            Peers = peers,
            RtpServerUri = roomData.Tier == VoiceTier.Scaled ? roomData.RtpServerUri : null,
            StunServers = stunServers
        }, CancellationToken.None);

        _logger.LogInformation("Restored voice room {RoomId} state for reconnected session {SessionId} with {PeerCount} peers",
            roomId, evt.SessionId, peers.Count);
    }
}
