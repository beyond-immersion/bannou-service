using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Voice.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Voice.Clients;
using BeyondImmersion.BannouService.Voice.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
namespace BeyondImmersion.BannouService.Voice;

// =============================================================================
// VoiceService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by VoiceService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (VoiceService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IVoiceService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (VoiceService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for VoiceService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class VoiceService
{
    // Private/internal helper methods moved from VoiceService.cs
    /// <summary>
    /// Internal method to stop broadcast and publish events.
    /// </summary>
    private async Task StopBroadcastInternalAsync(
        Guid roomId,
        VoiceRoomData roomData,
        VoiceBroadcastStoppedReason reason,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "VoiceService.StopBroadcastInternalAsync");
        var previousState = roomData.BroadcastState;
        roomData.BroadcastState = BroadcastConsentState.Inactive;
        roomData.BroadcastConsentedSessions.Clear();
        roomData.BroadcastRequestedBy = null;
        roomData.BroadcastRequestedAt = null;

        await _roomStore.SaveAsync($"{ROOM_KEY_PREFIX}{roomId}", roomData, cancellationToken: cancellationToken);

        // Publish stopped service event
        await _messageBus.PublishVoiceBroadcastStoppedAsync(new VoiceBroadcastStoppedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = roomId,
            Reason = reason
        });

        // Publish client event
        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);
        var participantSessionIds = participants.Select(p => p.SessionId).ToHashSet();

        // Clear consent_pending states if they were set
        if (previousState == BroadcastConsentState.Pending)
        {
            await ClearConsentPendingStatesAsync(participantSessionIds, cancellationToken);
        }

        await PublishBroadcastConsentUpdateAsync(roomId, participantSessionIds,
            BroadcastConsentState.Inactive, 0, participantSessionIds.Count,
            null, cancellationToken);
    }

    /// <summary>
    /// Clears consent_pending permission states and restores to in_room.
    /// </summary>
    private async Task ClearConsentPendingStatesAsync(IEnumerable<Guid> sessionIds, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "VoiceService.ClearConsentPendingStatesAsync");
        foreach (var sessionId in sessionIds)
        {
            try
            {
                await _permissionClient.UpdateSessionStateAsync(new SessionStateUpdate
                {
                    SessionId = sessionId,
                    ServiceId = "voice",
                    NewState = "in_room"
                }, cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Failed to restore voice:in_room state for session {SessionId}", sessionId);
                await _messageBus.TryPublishErrorAsync(
                    "voice",
                    "ClearConsentPendingStates",
                    "RestoreSessionStateFailed",
                    ex.Message,
                    dependency: "permission",
                    endpoint: "update-session-state",
                    stack: ex.StackTrace,
                    cancellationToken: cancellationToken);
            }
        }
    }

    /// <summary>
    /// Publishes a VoiceBroadcastConsentUpdateClientEvent to all participants.
    /// </summary>
    private async Task PublishBroadcastConsentUpdateAsync(
        Guid roomId,
        IEnumerable<Guid> participantSessionIds,
        BroadcastConsentState state,
        int consentedCount,
        int totalCount,
        string? declinedByDisplayName,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "VoiceService.PublishBroadcastConsentUpdateAsync");
        var updateEvent = new VoiceBroadcastConsentUpdateClientEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = roomId,
            State = state,
            ConsentedCount = consentedCount,
            TotalCount = totalCount,
            DeclinedByDisplayName = declinedByDisplayName
        };

        await _clientEventPublisher.PublishToSessionsAsync(
            participantSessionIds.Select(id => id.ToString()),
            updateEvent,
            cancellationToken);
    }
    #region Client Event Publishing

    /// <summary>
    /// Notifies existing peers that a new peer has joined.
    /// Also sets the voice:ringing state for recipients so they can respond via /voice/peer/answer.
    /// </summary>
    private async Task NotifyPeerJoinedAsync(
        Guid roomId,
        Guid newPeerSessionId,
        string? displayName,
        SipEndpoint sipEndpoint,
        int currentCount,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "VoiceService.NotifyPeerJoinedAsync");
        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);
        var otherParticipants = participants
            .Where(p => p.SessionId != newPeerSessionId)
            .ToList();

        if (otherParticipants.Count == 0)
        {
            return;
        }

        // Set voice:ringing state for all recipient sessions before publishing the event
        foreach (var participant in otherParticipants)
        {
            try
            {
                await _permissionClient.UpdateSessionStateAsync(new SessionStateUpdate
                {
                    SessionId = participant.SessionId,
                    ServiceId = "voice",
                    NewState = "ringing"
                }, cancellationToken);
                _logger.LogDebug("Set voice:ringing state for session {SessionId}", participant.SessionId);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Failed to set voice:ringing state for session {SessionId}", participant.SessionId);
                await _messageBus.TryPublishErrorAsync(
                    "voice",
                    "NotifyPeerJoined",
                    "SetRingingStateFailed",
                    ex.Message,
                    dependency: "permission",
                    endpoint: "update-session-state",
                    stack: ex.StackTrace,
                    cancellationToken: cancellationToken);
            }
        }

        var peerJoinedEvent = new VoicePeerJoinedClientEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = roomId,
            Peer = new VoicePeerInfo
            {
                PeerSessionId = newPeerSessionId,
                DisplayName = displayName,
                SdpOffer = sipEndpoint.SdpOffer,
                IceCandidates = sipEndpoint.IceCandidates?.ToList(),
                IsMuted = false
            },
            CurrentParticipantCount = currentCount
        };

        var sessionIdStrings = otherParticipants.Select(p => p.SessionId.ToString());
        var publishedCount = await _clientEventPublisher.PublishToSessionsAsync(sessionIdStrings, peerJoinedEvent, cancellationToken);
        _logger.LogDebug("Published peer-joined event to {Count} sessions", publishedCount);
    }

    /// <summary>
    /// Notifies remaining peers that a peer has left.
    /// </summary>
    private async Task NotifyPeerLeftAsync(
        Guid roomId,
        Guid leftPeerSessionId,
        string? displayName,
        int remainingCount,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "VoiceService.NotifyPeerLeftAsync");
        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);

        if (participants.Count == 0)
        {
            return;
        }

        var peerLeftEvent = new VoicePeerLeftClientEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = roomId,
            PeerSessionId = leftPeerSessionId,
            DisplayName = displayName,
            RemainingParticipantCount = remainingCount
        };

        var sessionIdStrings = participants.Select(p => p.SessionId.ToString());
        var publishedCount = await _clientEventPublisher.PublishToSessionsAsync(sessionIdStrings, peerLeftEvent, cancellationToken);
        _logger.LogDebug("Published peer-left event to {Count} sessions", publishedCount);
    }

    /// <summary>
    /// Notifies all participants that the room has been closed.
    /// </summary>
    private async Task NotifyRoomClosedAsync(
        Guid roomId,
        List<ParticipantRegistration> participants,
        VoiceRoomDeletedReason reason,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "VoiceService.NotifyRoomClosedAsync");
        if (participants.Count == 0)
        {
            return;
        }

        var roomClosedEvent = new VoiceRoomClosedClientEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = roomId,
            Reason = reason
        };

        var sessionIdStrings = participants.Select(p => p.SessionId.ToString());
        var publishedCount = await _clientEventPublisher.PublishToSessionsAsync(sessionIdStrings, roomClosedEvent, cancellationToken);
        _logger.LogDebug("Published room-closed event to {Count} sessions", publishedCount);
    }

    /// <summary>
    /// Notifies all participants about tier upgrade from P2P to scaled.
    /// Each participant receives their unique SIP credentials for connecting to the RTP server.
    /// </summary>
    private async Task NotifyTierUpgradeAsync(
        Guid roomId,
        string rtpServerUri,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "VoiceService.NotifyTierUpgradeAsync");
        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);

        if (participants.Count == 0)
        {
            return;
        }

        var publishedCount = 0;
        foreach (var participant in participants)
        {
            var internalCredentials = _scaledTierCoordinator.GenerateSipCredentials(participant.SessionId, roomId);

            var clientCredentials = new SipCredentials
            {
                Username = internalCredentials.Username,
                Password = internalCredentials.Password,
                Domain = internalCredentials.Registrar,
                ExpiresAt = null
            };

            var tierUpgradeEvent = new VoiceTierUpgradeClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RoomId = roomId,
                PreviousTier = VoiceTier.P2P,
                NewTier = VoiceTier.Scaled,
                RtpServerUri = rtpServerUri,
                SipCredentials = clientCredentials,
                MigrationDeadlineMs = _configuration.TierUpgradeMigrationDeadlineMs
            };

            var success = await _clientEventPublisher.PublishToSessionAsync(participant.SessionId.ToString(), tierUpgradeEvent, cancellationToken);
            if (success)
            {
                publishedCount++;
            }
        }

        _logger.LogInformation("Published tier-upgrade event to {Count} sessions for room {RoomId}", publishedCount, roomId);
    }

    #endregion
    #region Tier Upgrade Methods

    /// <summary>
    /// Attempts to upgrade a room from P2P to scaled tier.
    /// </summary>
    private async Task<bool> TryUpgradeToScaledTierAsync(
        Guid roomId,
        VoiceRoomData roomData,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "VoiceService.TryUpgradeToScaledTierAsync");
        if (!_configuration.ScaledTierEnabled)
        {
            _logger.LogWarning("Scaled tier not enabled, cannot upgrade room {RoomId}", roomId);
            return false;
        }

        try
        {
            _logger.LogDebug("Starting tier upgrade for room {RoomId} from P2P to scaled", roomId);

            var rtpServerUri = await _scaledTierCoordinator.AllocateRtpServerAsync(roomId, cancellationToken);

            // Update room data to scaled tier (preserve new fields)
            roomData.Tier = VoiceTier.Scaled;
            roomData.MaxParticipants = _scaledTierCoordinator.GetScaledMaxParticipants();
            roomData.RtpServerUri = rtpServerUri;

            await _roomStore.SaveAsync($"{ROOM_KEY_PREFIX}{roomId}", roomData, cancellationToken: cancellationToken);

            // Publish tier upgraded service event
            await _messageBus.PublishVoiceRoomTierUpgradedAsync(new VoiceRoomTierUpgradedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RoomId = roomId,
                PreviousTier = VoiceTier.P2P,
                NewTier = VoiceTier.Scaled,
                RtpAudioEndpoint = rtpServerUri
            });

            // Notify all current participants about the tier upgrade
            await NotifyTierUpgradeAsync(roomId, rtpServerUri, cancellationToken);

            _logger.LogInformation("Successfully upgraded room {RoomId} to scaled tier with RTP server {RtpServer}", roomId, rtpServerUri);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upgrade room {RoomId} to scaled tier", roomId);
            await _messageBus.TryPublishErrorAsync(
                "voice",
                "TryUpgradeToScaledTier",
                "tier_upgrade_failed",
                ex.Message,
                dependency: "rtpengine",
                endpoint: null,
                details: $"RoomId: {roomId}",
                stack: ex.StackTrace);
            return false;
        }
    }

    #endregion
}
