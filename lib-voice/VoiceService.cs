using BeyondImmersion.Bannou.Voice.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Voice.Services;
using Dapr.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-voice.tests")]

namespace BeyondImmersion.BannouService.Voice;

/// <summary>
/// Implementation of the Voice service.
/// Manages P2P voice room coordination for game sessions.
/// </summary>
[DaprService("voice", typeof(IVoiceService), lifetime: ServiceLifetime.Scoped)]
public class VoiceService : IVoiceService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<VoiceService> _logger;
    private readonly VoiceServiceConfiguration _configuration;
    private readonly IErrorEventEmitter _errorEventEmitter;
    private readonly ISipEndpointRegistry _endpointRegistry;
    private readonly IP2PCoordinator _p2pCoordinator;
    private readonly IClientEventPublisher? _clientEventPublisher;

    private const string STATE_STORE = "voice-statestore";
    private const string ROOM_KEY_PREFIX = "voice:room:";
    private const string SESSION_ROOM_KEY_PREFIX = "voice:session-room:";

    /// <summary>
    /// Initializes a new instance of the VoiceService.
    /// </summary>
    /// <param name="daprClient">Dapr client for state and pub/sub operations.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Voice service configuration.</param>
    /// <param name="errorEventEmitter">Error event emitter for unexpected failures.</param>
    /// <param name="endpointRegistry">SIP endpoint registry for participant tracking.</param>
    /// <param name="p2pCoordinator">P2P coordinator for mesh topology management.</param>
    /// <param name="clientEventPublisher">Optional client event publisher for WebSocket push events. May be null if Connect service is not loaded.</param>
    public VoiceService(
        DaprClient daprClient,
        ILogger<VoiceService> logger,
        VoiceServiceConfiguration configuration,
        IErrorEventEmitter errorEventEmitter,
        ISipEndpointRegistry endpointRegistry,
        IP2PCoordinator p2pCoordinator,
        IClientEventPublisher? clientEventPublisher = null)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _errorEventEmitter = errorEventEmitter ?? throw new ArgumentNullException(nameof(errorEventEmitter));
        _endpointRegistry = endpointRegistry ?? throw new ArgumentNullException(nameof(endpointRegistry));
        _p2pCoordinator = p2pCoordinator ?? throw new ArgumentNullException(nameof(p2pCoordinator));
        _clientEventPublisher = clientEventPublisher; // Optional - may be null if Connect service not loaded (Tenet 5)
    }

    /// <summary>
    /// Creates a new voice room for a game session.
    /// </summary>
    public async Task<(StatusCodes, VoiceRoomResponse?)> CreateVoiceRoomAsync(CreateVoiceRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating voice room for session {SessionId}", body.SessionId);

        try
        {
            // Check if room already exists for this session
            var existingRoomId = await _daprClient.GetStateAsync<Guid?>(
                STATE_STORE,
                $"{SESSION_ROOM_KEY_PREFIX}{body.SessionId}",
                cancellationToken: cancellationToken);

            if (existingRoomId.HasValue)
            {
                _logger.LogWarning("Voice room already exists for session {SessionId}", body.SessionId);
                return (StatusCodes.Conflict, null);
            }

            // Create new room
            var roomId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var roomData = new VoiceRoomData
            {
                RoomId = roomId,
                SessionId = body.SessionId,
                Tier = body.PreferredTier == VoiceTier.Scaled ? "scaled" : "p2p",
                Codec = body.Codec == VoiceCodec.G711 ? "g711" : body.Codec == VoiceCodec.G722 ? "g722" : "opus",
                MaxParticipants = body.MaxParticipants > 0 ? body.MaxParticipants : _p2pCoordinator.GetP2PMaxParticipants(),
                CreatedAt = now,
                RtpServerUri = null
            };

            // Save room data
            await _daprClient.SaveStateAsync(STATE_STORE, $"{ROOM_KEY_PREFIX}{roomId}", roomData, cancellationToken: cancellationToken);

            // Save session -> room mapping
            await _daprClient.SaveStateAsync(STATE_STORE, $"{SESSION_ROOM_KEY_PREFIX}{body.SessionId}", roomId, cancellationToken: cancellationToken);

            _logger.LogInformation("Created voice room {RoomId} for session {SessionId}", roomId, body.SessionId);

            return (StatusCodes.Created, new VoiceRoomResponse
            {
                RoomId = roomId,
                SessionId = body.SessionId,
                Tier = ParseVoiceTier(roomData.Tier),
                Codec = ParseVoiceCodec(roomData.Codec),
                MaxParticipants = roomData.MaxParticipants,
                CurrentParticipants = 0,
                Participants = new List<VoiceParticipant>(),
                CreatedAt = now,
                RtpServerUri = null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating voice room for session {SessionId}", body.SessionId);
            await _errorEventEmitter.TryPublishAsync(
                "voice",
                "CreateVoiceRoom",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/voice/room/create",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets voice room details.
    /// </summary>
    public async Task<(StatusCodes, VoiceRoomResponse?)> GetVoiceRoomAsync(GetVoiceRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting voice room {RoomId}", body.RoomId);

        try
        {
            var roomData = await _daprClient.GetStateAsync<VoiceRoomData>(
                STATE_STORE,
                $"{ROOM_KEY_PREFIX}{body.RoomId}",
                cancellationToken: cancellationToken);

            if (roomData == null)
            {
                _logger.LogDebug("Voice room {RoomId} not found", body.RoomId);
                return (StatusCodes.NotFound, null);
            }

            // Get participants from registry
            var participants = await _endpointRegistry.GetRoomParticipantsAsync(body.RoomId, cancellationToken);

            return (StatusCodes.OK, new VoiceRoomResponse
            {
                RoomId = roomData.RoomId,
                SessionId = roomData.SessionId,
                Tier = ParseVoiceTier(roomData.Tier),
                Codec = ParseVoiceCodec(roomData.Codec),
                MaxParticipants = roomData.MaxParticipants,
                CurrentParticipants = participants.Count,
                Participants = participants.Select(p => p.ToVoiceParticipant()).ToList(),
                CreatedAt = roomData.CreatedAt,
                RtpServerUri = roomData.RtpServerUri
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting voice room {RoomId}", body.RoomId);
            await _errorEventEmitter.TryPublishAsync(
                "voice",
                "GetVoiceRoom",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/voice/room/get",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Joins a voice room and registers the participant's SIP endpoint.
    /// </summary>
    public async Task<(StatusCodes, JoinVoiceRoomResponse?)> JoinVoiceRoomAsync(JoinVoiceRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Account {AccountId} joining voice room {RoomId}", body.AccountId, body.RoomId);

        try
        {
            // Get room data
            var roomData = await _daprClient.GetStateAsync<VoiceRoomData>(
                STATE_STORE,
                $"{ROOM_KEY_PREFIX}{body.RoomId}",
                cancellationToken: cancellationToken);

            if (roomData == null)
            {
                _logger.LogWarning("Voice room {RoomId} not found", body.RoomId);
                return (StatusCodes.NotFound, null);
            }

            // Check participant count
            var currentCount = await _endpointRegistry.GetParticipantCountAsync(body.RoomId, cancellationToken);

            // Check if room can accept new participant
            var canAccept = await _p2pCoordinator.CanAcceptNewParticipantAsync(body.RoomId, currentCount, cancellationToken);
            if (!canAccept)
            {
                _logger.LogWarning("Voice room {RoomId} at capacity, cannot accept participant", body.RoomId);
                return (StatusCodes.Conflict, null);
            }

            // Register participant
            var registered = await _endpointRegistry.RegisterAsync(
                body.RoomId,
                body.AccountId,
                body.SipEndpoint,
                body.SessionId,
                body.DisplayName,
                cancellationToken);

            if (!registered)
            {
                _logger.LogWarning("Participant {AccountId} already in room {RoomId}", body.AccountId, body.RoomId);
                return (StatusCodes.Conflict, null);
            }

            // Get peers for the new participant (excluding themselves)
            var peers = await _p2pCoordinator.GetMeshPeersForNewJoinAsync(body.RoomId, body.AccountId, cancellationToken);

            // Convert to VoicePeer list for response
            var peerList = peers.Select(p => new VoicePeer
            {
                AccountId = p.AccountId,
                DisplayName = p.DisplayName,
                SipEndpoint = new SipEndpoint
                {
                    SdpOffer = p.SdpOffer,
                    IceCandidates = p.IceCandidates ?? new List<string>()
                }
            }).ToList();

            // Parse STUN servers from config
            var stunServers = _configuration.StunServers?
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList() ?? new List<string> { "stun:stun.l.google.com:19302" };

            // Check if tier upgrade is needed
            var newCount = currentCount + 1;
            var tierUpgradePending = await _p2pCoordinator.ShouldUpgradeToScaledAsync(body.RoomId, newCount, cancellationToken);

            // Notify existing peers about the new participant
            await NotifyPeerJoinedAsync(body.RoomId, body.AccountId, body.DisplayName, body.SipEndpoint, newCount, cancellationToken);

            _logger.LogInformation("Account {AccountId} joined voice room {RoomId}, {PeerCount} peers", body.AccountId, body.RoomId, peers.Count);

            return (StatusCodes.OK, new JoinVoiceRoomResponse
            {
                Success = true,
                RoomId = body.RoomId,
                Tier = ParseVoiceTier(roomData.Tier),
                Codec = ParseVoiceCodec(roomData.Codec),
                Peers = peerList,
                RtpServerUri = roomData.RtpServerUri,
                StunServers = stunServers,
                TierUpgradePending = tierUpgradePending
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining voice room {RoomId}", body.RoomId);
            await _errorEventEmitter.TryPublishAsync(
                "voice",
                "JoinVoiceRoom",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/voice/room/join",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Leaves a voice room.
    /// </summary>
    public async Task<(StatusCodes, object?)> LeaveVoiceRoomAsync(LeaveVoiceRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Account {AccountId} leaving voice room {RoomId}", body.AccountId, body.RoomId);

        try
        {
            // Unregister participant
            var removed = await _endpointRegistry.UnregisterAsync(body.RoomId, body.AccountId, cancellationToken);

            if (removed == null)
            {
                _logger.LogDebug("Participant {AccountId} not found in room {RoomId}", body.AccountId, body.RoomId);
                return (StatusCodes.NotFound, null);
            }

            // Get remaining count
            var remainingCount = await _endpointRegistry.GetParticipantCountAsync(body.RoomId, cancellationToken);

            // Notify remaining peers
            await NotifyPeerLeftAsync(body.RoomId, body.AccountId, removed.DisplayName, remainingCount, cancellationToken);

            _logger.LogInformation("Account {AccountId} left voice room {RoomId}", body.AccountId, body.RoomId);

            return (StatusCodes.OK, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving voice room {RoomId}", body.RoomId);
            await _errorEventEmitter.TryPublishAsync(
                "voice",
                "LeaveVoiceRoom",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/voice/room/leave",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deletes a voice room and notifies all participants.
    /// </summary>
    public async Task<(StatusCodes, object?)> DeleteVoiceRoomAsync(DeleteVoiceRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting voice room {RoomId}", body.RoomId);

        try
        {
            // Get room data
            var roomData = await _daprClient.GetStateAsync<VoiceRoomData>(
                STATE_STORE,
                $"{ROOM_KEY_PREFIX}{body.RoomId}",
                cancellationToken: cancellationToken);

            if (roomData == null)
            {
                _logger.LogDebug("Voice room {RoomId} not found", body.RoomId);
                return (StatusCodes.NotFound, null);
            }

            // Get all participants before clearing
            var participants = await _endpointRegistry.GetRoomParticipantsAsync(body.RoomId, cancellationToken);

            // Clear all participants
            await _endpointRegistry.ClearRoomAsync(body.RoomId, cancellationToken);

            // Delete room data
            await _daprClient.DeleteStateAsync(STATE_STORE, $"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken: cancellationToken);

            // Delete session -> room mapping
            await _daprClient.DeleteStateAsync(STATE_STORE, $"{SESSION_ROOM_KEY_PREFIX}{roomData.SessionId}", cancellationToken: cancellationToken);

            // Notify all participants that room is closed
            await NotifyRoomClosedAsync(body.RoomId, participants, body.Reason ?? "session_ended", cancellationToken);

            _logger.LogInformation("Deleted voice room {RoomId}, notified {ParticipantCount} participants", body.RoomId, participants.Count);

            return (StatusCodes.OK, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting voice room {RoomId}", body.RoomId);
            await _errorEventEmitter.TryPublishAsync(
                "voice",
                "DeleteVoiceRoom",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/voice/room/delete",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Updates a participant's heartbeat to keep their registration active.
    /// </summary>
    public async Task<(StatusCodes, object?)> PeerHeartbeatAsync(PeerHeartbeatRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Heartbeat from {AccountId} in room {RoomId}", body.AccountId, body.RoomId);

        try
        {
            var updated = await _endpointRegistry.UpdateHeartbeatAsync(body.RoomId, body.AccountId, cancellationToken);

            if (!updated)
            {
                _logger.LogDebug("Peer {AccountId} not found in room {RoomId}", body.AccountId, body.RoomId);
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing heartbeat for {AccountId} in room {RoomId}", body.AccountId, body.RoomId);
            await _errorEventEmitter.TryPublishAsync(
                "voice",
                "PeerHeartbeat",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/voice/peer/heartbeat",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Updates a participant's SIP endpoint (e.g., ICE candidate change).
    /// </summary>
    public async Task<(StatusCodes, object?)> UpdatePeerEndpointAsync(UpdatePeerEndpointRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating endpoint for {AccountId} in room {RoomId}", body.AccountId, body.RoomId);

        try
        {
            var updated = await _endpointRegistry.UpdateEndpointAsync(body.RoomId, body.AccountId, body.SipEndpoint, cancellationToken);

            if (!updated)
            {
                _logger.LogDebug("Peer {AccountId} not found in room {RoomId}", body.AccountId, body.RoomId);
                return (StatusCodes.NotFound, null);
            }

            // Get participant info for the event
            var participant = await _endpointRegistry.GetParticipantAsync(body.RoomId, body.AccountId, cancellationToken);

            // Notify other peers about the endpoint update
            if (participant != null)
            {
                await NotifyPeerUpdatedAsync(body.RoomId, body.AccountId, participant.DisplayName, body.SipEndpoint, cancellationToken);
            }

            _logger.LogInformation("Updated endpoint for {AccountId} in room {RoomId}", body.AccountId, body.RoomId);

            return (StatusCodes.OK, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating endpoint for {AccountId} in room {RoomId}", body.AccountId, body.RoomId);
            await _errorEventEmitter.TryPublishAsync(
                "voice",
                "UpdatePeerEndpoint",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/voice/peer/update-endpoint",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Client Event Publishing

    /// <summary>
    /// Notifies existing peers that a new peer has joined.
    /// </summary>
    private async Task NotifyPeerJoinedAsync(
        Guid roomId,
        Guid newAccountId,
        string? displayName,
        SipEndpoint sipEndpoint,
        int currentCount,
        CancellationToken cancellationToken)
    {
        if (_clientEventPublisher == null)
        {
            _logger.LogDebug("Client event publisher not available, skipping peer_joined notification");
            return;
        }

        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);
        var sessionIds = participants
            .Where(p => p.AccountId != newAccountId && !string.IsNullOrEmpty(p.SessionId))
            .Select(p => p.SessionId!)
            .ToList();

        if (sessionIds.Count == 0)
        {
            return;
        }

        var peerJoinedEvent = new VoicePeerJoinedEvent
        {
            Event_name = VoicePeerJoinedEventEvent_name.Voice_peer_joined,
            Event_id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Room_id = roomId,
            Peer = new VoicePeerInfo
            {
                Account_id = newAccountId,
                Display_name = displayName,
                Sdp_offer = sipEndpoint.SdpOffer,
                Ice_candidates = sipEndpoint.IceCandidates?.ToList(),
                Is_muted = false
            },
            Current_participant_count = currentCount
        };

        var publishedCount = await _clientEventPublisher.PublishToSessionsAsync(sessionIds, peerJoinedEvent, cancellationToken);
        _logger.LogDebug("Published peer_joined event to {Count} sessions", publishedCount);
    }

    /// <summary>
    /// Notifies remaining peers that a peer has left.
    /// </summary>
    private async Task NotifyPeerLeftAsync(
        Guid roomId,
        Guid leftAccountId,
        string? displayName,
        int remainingCount,
        CancellationToken cancellationToken)
    {
        if (_clientEventPublisher == null)
        {
            _logger.LogDebug("Client event publisher not available, skipping peer_left notification");
            return;
        }

        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);
        var sessionIds = participants
            .Where(p => !string.IsNullOrEmpty(p.SessionId))
            .Select(p => p.SessionId!)
            .ToList();

        if (sessionIds.Count == 0)
        {
            return;
        }

        var peerLeftEvent = new VoicePeerLeftEvent
        {
            Event_name = VoicePeerLeftEventEvent_name.Voice_peer_left,
            Event_id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Room_id = roomId,
            Account_id = leftAccountId,
            Display_name = displayName,
            Remaining_participant_count = remainingCount
        };

        var publishedCount = await _clientEventPublisher.PublishToSessionsAsync(sessionIds, peerLeftEvent, cancellationToken);
        _logger.LogDebug("Published peer_left event to {Count} sessions", publishedCount);
    }

    /// <summary>
    /// Notifies other peers that a peer has updated their endpoint.
    /// </summary>
    private async Task NotifyPeerUpdatedAsync(
        Guid roomId,
        Guid updatedAccountId,
        string? displayName,
        SipEndpoint sipEndpoint,
        CancellationToken cancellationToken)
    {
        if (_clientEventPublisher == null)
        {
            _logger.LogDebug("Client event publisher not available, skipping peer_updated notification");
            return;
        }

        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);
        var sessionIds = participants
            .Where(p => p.AccountId != updatedAccountId && !string.IsNullOrEmpty(p.SessionId))
            .Select(p => p.SessionId!)
            .ToList();

        if (sessionIds.Count == 0)
        {
            return;
        }

        var peerUpdatedEvent = new VoicePeerUpdatedEvent
        {
            Event_name = VoicePeerUpdatedEventEvent_name.Voice_peer_updated,
            Event_id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Room_id = roomId,
            Peer = new VoicePeerInfo
            {
                Account_id = updatedAccountId,
                Display_name = displayName,
                Sdp_offer = sipEndpoint.SdpOffer,
                Ice_candidates = sipEndpoint.IceCandidates?.ToList(),
                Is_muted = false
            }
        };

        var publishedCount = await _clientEventPublisher.PublishToSessionsAsync(sessionIds, peerUpdatedEvent, cancellationToken);
        _logger.LogDebug("Published peer_updated event to {Count} sessions", publishedCount);
    }

    /// <summary>
    /// Notifies all participants that the room has been closed.
    /// </summary>
    private async Task NotifyRoomClosedAsync(
        Guid roomId,
        List<ParticipantRegistration> participants,
        string reason,
        CancellationToken cancellationToken)
    {
        if (_clientEventPublisher == null)
        {
            _logger.LogDebug("Client event publisher not available, skipping room_closed notification");
            return;
        }

        var sessionIds = participants
            .Where(p => !string.IsNullOrEmpty(p.SessionId))
            .Select(p => p.SessionId!)
            .ToList();

        if (sessionIds.Count == 0)
        {
            return;
        }

        var reasonEnum = reason.ToLowerInvariant() switch
        {
            "admin_action" => VoiceRoomClosedEventReason.Admin_action,
            "error" => VoiceRoomClosedEventReason.Error,
            _ => VoiceRoomClosedEventReason.Session_ended
        };

        var roomClosedEvent = new VoiceRoomClosedEvent
        {
            Event_name = VoiceRoomClosedEventEvent_name.Voice_room_closed,
            Event_id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Room_id = roomId,
            Reason = reasonEnum
        };

        var publishedCount = await _clientEventPublisher.PublishToSessionsAsync(sessionIds, roomClosedEvent, cancellationToken);
        _logger.LogDebug("Published room_closed event to {Count} sessions", publishedCount);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Parses a tier string to VoiceTier enum.
    /// </summary>
    private static VoiceTier ParseVoiceTier(string tier)
    {
        return tier?.ToLowerInvariant() switch
        {
            "scaled" => VoiceTier.Scaled,
            _ => VoiceTier.P2p
        };
    }

    /// <summary>
    /// Parses a codec string to VoiceCodec enum.
    /// </summary>
    private static VoiceCodec ParseVoiceCodec(string codec)
    {
        return codec?.ToLowerInvariant() switch
        {
            "g711" => VoiceCodec.G711,
            "g722" => VoiceCodec.G722,
            _ => VoiceCodec.Opus
        };
    }

    #endregion
}
