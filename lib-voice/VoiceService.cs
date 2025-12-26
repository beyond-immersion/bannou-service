using BeyondImmersion.Bannou.Voice.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Permissions;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Voice.Clients;
using BeyondImmersion.BannouService.Voice.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

// Alias to disambiguate from internal SipCredentials type in Services namespace
using ClientSipCredentials = BeyondImmersion.Bannou.Voice.ClientEvents.SipCredentials;

[assembly: InternalsVisibleTo("lib-voice.tests")]

namespace BeyondImmersion.BannouService.Voice;

/// <summary>
/// Implementation of the Voice service.
/// Manages P2P and scaled tier voice room coordination for game sessions.
/// </summary>
[DaprService("voice", typeof(IVoiceService), lifetime: ServiceLifetime.Scoped)]
public partial class VoiceService : IVoiceService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<VoiceService> _logger;
    private readonly VoiceServiceConfiguration _configuration;
    private readonly ISipEndpointRegistry _endpointRegistry;
    private readonly IP2PCoordinator _p2pCoordinator;
    private readonly IScaledTierCoordinator _scaledTierCoordinator;
    private readonly IClientEventPublisher? _clientEventPublisher;
    private readonly IPermissionsClient? _permissionsClient;

    private const string STATE_STORE = "voice-statestore";
    private const string ROOM_KEY_PREFIX = "voice:room:";
    private const string SESSION_ROOM_KEY_PREFIX = "voice:session-room:";

    /// <summary>
    /// Initializes a new instance of the VoiceService.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for state operations.</param>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Voice service configuration.</param>
    /// <param name="endpointRegistry">SIP endpoint registry for participant tracking.</param>
    /// <param name="p2pCoordinator">P2P coordinator for mesh topology management.</param>
    /// <param name="scaledTierCoordinator">Scaled tier coordinator for SFU-based conferencing.</param>
    /// <param name="eventConsumer">Event consumer for registering event handlers.</param>
    /// <param name="clientEventPublisher">Optional client event publisher for WebSocket push events. May be null if Connect service is not loaded.</param>
    /// <param name="permissionsClient">Optional permissions client for setting voice:ringing state. May be null if Permissions service is not loaded.</param>
    public VoiceService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<VoiceService> logger,
        VoiceServiceConfiguration configuration,
        ISipEndpointRegistry endpointRegistry,
        IP2PCoordinator p2pCoordinator,
        IScaledTierCoordinator scaledTierCoordinator,
        IEventConsumer eventConsumer,
        IClientEventPublisher? clientEventPublisher = null,
        IPermissionsClient? permissionsClient = null)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _endpointRegistry = endpointRegistry ?? throw new ArgumentNullException(nameof(endpointRegistry));
        _p2pCoordinator = p2pCoordinator ?? throw new ArgumentNullException(nameof(p2pCoordinator));
        _scaledTierCoordinator = scaledTierCoordinator ?? throw new ArgumentNullException(nameof(scaledTierCoordinator));
        _clientEventPublisher = clientEventPublisher; // Optional - may be null if Connect service not loaded (Tenet 5)
        _permissionsClient = permissionsClient; // Optional - may be null if Permissions service not loaded (Tenet 5)

        // Register event handlers via partial class (VoiceServiceEvents.cs)
        ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
        ((IDaprService)this).RegisterEventConsumers(eventConsumer);
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
            var stringStore = _stateStoreFactory.GetStore<string>(STATE_STORE);
            var existingRoomIdStr = await stringStore.GetAsync($"{SESSION_ROOM_KEY_PREFIX}{body.SessionId}", cancellationToken);

            if (!string.IsNullOrEmpty(existingRoomIdStr) && Guid.TryParse(existingRoomIdStr, out _))
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
            var roomStore = _stateStoreFactory.GetStore<VoiceRoomData>(STATE_STORE);
            await roomStore.SaveAsync($"{ROOM_KEY_PREFIX}{roomId}", roomData, cancellationToken: cancellationToken);

            // Save session -> room mapping (store Guid as string since IStateStore requires reference types)
            await stringStore.SaveAsync($"{SESSION_ROOM_KEY_PREFIX}{body.SessionId}", roomId.ToString(), cancellationToken: cancellationToken);

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
            await _messageBus.TryPublishErrorAsync(
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
            var roomStore = _stateStoreFactory.GetStore<VoiceRoomData>(STATE_STORE);
            var roomData = await roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

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
            await _messageBus.TryPublishErrorAsync(
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
    /// Handles both P2P mode (returns peer list) and scaled mode (returns SIP credentials).
    /// </summary>
    public async Task<(StatusCodes, JoinVoiceRoomResponse?)> JoinVoiceRoomAsync(JoinVoiceRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Session {SessionId} joining voice room {RoomId}", body.SessionId, body.RoomId);

        try
        {
            // Get room data
            var roomStore = _stateStoreFactory.GetStore<VoiceRoomData>(STATE_STORE);
            var roomData = await roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

            if (roomData == null)
            {
                _logger.LogWarning("Voice room {RoomId} not found", body.RoomId);
                return (StatusCodes.NotFound, null);
            }

            // Check participant count
            var currentCount = await _endpointRegistry.GetParticipantCountAsync(body.RoomId, cancellationToken);
            var isScaledTier = roomData.Tier?.ToLowerInvariant() == "scaled";

            // Check if room can accept new participant based on current tier
            bool canAccept;
            if (isScaledTier)
            {
                canAccept = await _scaledTierCoordinator.CanAcceptNewParticipantAsync(body.RoomId, currentCount, cancellationToken);
            }
            else
            {
                canAccept = await _p2pCoordinator.CanAcceptNewParticipantAsync(body.RoomId, currentCount, cancellationToken);
            }

            if (!canAccept)
            {
                // If P2P is full and scaled tier is enabled, check if we can upgrade
                if (!isScaledTier && _configuration.ScaledTierEnabled && _configuration.TierUpgradeEnabled)
                {
                    _logger.LogInformation("P2P room {RoomId} at capacity, attempting tier upgrade to scaled", body.RoomId);
                    var upgradeResult = await TryUpgradeToScaledTierAsync(body.RoomId, roomData, cancellationToken);
                    if (upgradeResult)
                    {
                        // Reload room data after upgrade
                        roomData = await roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);
                        if (roomData == null)
                        {
                            _logger.LogError("Room data disappeared after tier upgrade for room {RoomId}", body.RoomId);
                            return (StatusCodes.InternalServerError, null);
                        }
                        isScaledTier = true;
                    }
                    else
                    {
                        _logger.LogWarning("Voice room {RoomId} at capacity and tier upgrade failed", body.RoomId);
                        return (StatusCodes.Conflict, null);
                    }
                }
                else
                {
                    _logger.LogWarning("Voice room {RoomId} at capacity, cannot accept participant", body.RoomId);
                    return (StatusCodes.Conflict, null);
                }
            }

            // Register participant
            var registered = await _endpointRegistry.RegisterAsync(
                body.RoomId,
                body.SessionId,
                body.SipEndpoint,
                body.DisplayName,
                cancellationToken);

            if (!registered)
            {
                _logger.LogWarning("Session {SessionId} already in room {RoomId}", body.SessionId, body.RoomId);
                return (StatusCodes.Conflict, null);
            }

            var newCount = currentCount + 1;

            // Parse STUN servers from config
            var stunServers = _configuration.StunServers?
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList() ?? new List<string> { "stun:stun.l.google.com:19302" };

            // Handle based on current tier
            if (isScaledTier)
            {
                // Scaled tier: Return RTP server URI and SIP credentials
                // Note: SIP credentials are generated per-session for security
                _logger.LogInformation("Session {SessionId} joined scaled tier room {RoomId}", body.SessionId, body.RoomId);

                return (StatusCodes.OK, new JoinVoiceRoomResponse
                {
                    Success = true,
                    RoomId = body.RoomId,
                    Tier = VoiceTier.Scaled,
                    Codec = ParseVoiceCodec(roomData.Codec),
                    Peers = new List<VoicePeer>(), // No peers in scaled mode
                    RtpServerUri = roomData.RtpServerUri,
                    StunServers = stunServers,
                    TierUpgradePending = false
                });
            }

            // P2P tier: Return peer list (P2PCoordinator already returns VoicePeer objects)
            var peers = await _p2pCoordinator.GetMeshPeersForNewJoinAsync(body.RoomId, body.SessionId, cancellationToken);

            // Check if tier upgrade is needed for future joins
            var tierUpgradePending = _configuration.TierUpgradeEnabled &&
                                    _configuration.ScaledTierEnabled &&
                                    await _p2pCoordinator.ShouldUpgradeToScaledAsync(body.RoomId, newCount, cancellationToken);

            // Notify existing peers about the new participant (use sessionId for privacy - don't leak accountId)
            await NotifyPeerJoinedAsync(body.RoomId, body.SessionId, body.DisplayName, body.SipEndpoint, newCount, cancellationToken);

            // Also set voice:ringing for the joining session if there are existing peers
            // This enables them to call /voice/peer/answer to send SDP answers to those peers (Tenet 10)
            if (peers.Count > 0 && _permissionsClient != null)
            {
                try
                {
                    await _permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
                    {
                        SessionId = body.SessionId,
                        ServiceId = "voice",
                        NewState = "ringing"
                    }, cancellationToken);
                    _logger.LogDebug("Set voice:ringing state for joining session {SessionId} with {PeerCount} existing peers",
                        body.SessionId, peers.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set voice:ringing state for joining session {SessionId}", body.SessionId);
                }
            }

            // If tier upgrade is pending and enabled, trigger the upgrade now
            if (tierUpgradePending)
            {
                _logger.LogInformation("Triggering tier upgrade for room {RoomId} at {Count} participants", body.RoomId, newCount);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await TryUpgradeToScaledTierAsync(body.RoomId, roomData, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Background tier upgrade failed for room {RoomId}", body.RoomId);
                    }
                });
            }

            _logger.LogInformation("Session {SessionId} joined P2P voice room {RoomId}, {PeerCount} peers", body.SessionId, body.RoomId, peers.Count);

            return (StatusCodes.OK, new JoinVoiceRoomResponse
            {
                Success = true,
                RoomId = body.RoomId,
                Tier = VoiceTier.P2p,
                Codec = ParseVoiceCodec(roomData.Codec),
                Peers = peers,
                RtpServerUri = null,
                StunServers = stunServers,
                TierUpgradePending = tierUpgradePending
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining voice room {RoomId}", body.RoomId);
            await _messageBus.TryPublishErrorAsync(
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
        _logger.LogInformation("Session {SessionId} leaving voice room {RoomId}", body.SessionId, body.RoomId);

        try
        {
            // Unregister participant
            var removed = await _endpointRegistry.UnregisterAsync(body.RoomId, body.SessionId, cancellationToken);

            if (removed == null)
            {
                _logger.LogDebug("Session {SessionId} not found in room {RoomId}", body.SessionId, body.RoomId);
                return (StatusCodes.NotFound, null);
            }

            // Get remaining count
            var remainingCount = await _endpointRegistry.GetParticipantCountAsync(body.RoomId, cancellationToken);

            // Notify remaining peers
            await NotifyPeerLeftAsync(body.RoomId, removed.SessionId, removed.DisplayName, remainingCount, cancellationToken);

            _logger.LogInformation("Session {SessionId} left voice room {RoomId}", body.SessionId, body.RoomId);

            return (StatusCodes.OK, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving voice room {RoomId}", body.RoomId);
            await _messageBus.TryPublishErrorAsync(
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
    /// For scaled tier rooms, also releases RTP server resources.
    /// </summary>
    public async Task<(StatusCodes, object?)> DeleteVoiceRoomAsync(DeleteVoiceRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting voice room {RoomId}", body.RoomId);

        try
        {
            // Get room data
            var roomStore = _stateStoreFactory.GetStore<VoiceRoomData>(STATE_STORE);
            var roomData = await roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

            if (roomData == null)
            {
                _logger.LogDebug("Voice room {RoomId} not found", body.RoomId);
                return (StatusCodes.NotFound, null);
            }

            // Get all participants before clearing
            var participants = await _endpointRegistry.GetRoomParticipantsAsync(body.RoomId, cancellationToken);

            // Clear all participants
            await _endpointRegistry.ClearRoomAsync(body.RoomId, cancellationToken);

            // If this was a scaled tier room, release RTP server resources
            // Fail-fast: RTP cleanup failures propagate to caller for proper error handling
            if (roomData.Tier?.ToLowerInvariant() == "scaled" && !string.IsNullOrEmpty(roomData.RtpServerUri))
            {
                await _scaledTierCoordinator.ReleaseRtpServerAsync(body.RoomId, cancellationToken);
                _logger.LogDebug("Released RTP server resources for room {RoomId}", body.RoomId);
            }

            // Delete room data
            await roomStore.DeleteAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

            // Delete session -> room mapping
            var stringStore = _stateStoreFactory.GetStore<string>(STATE_STORE);
            await stringStore.DeleteAsync($"{SESSION_ROOM_KEY_PREFIX}{roomData.SessionId}", cancellationToken);

            // Notify all participants that room is closed
            await NotifyRoomClosedAsync(body.RoomId, participants, body.Reason ?? "session_ended", cancellationToken);

            _logger.LogInformation("Deleted voice room {RoomId}, notified {ParticipantCount} participants", body.RoomId, participants.Count);

            return (StatusCodes.OK, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting voice room {RoomId}", body.RoomId);
            await _messageBus.TryPublishErrorAsync(
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
        _logger.LogDebug("Heartbeat from {SessionId} in room {RoomId}", body.SessionId, body.RoomId);

        try
        {
            var updated = await _endpointRegistry.UpdateHeartbeatAsync(body.RoomId, body.SessionId, cancellationToken);

            if (!updated)
            {
                _logger.LogDebug("Session {SessionId} not found in room {RoomId}", body.SessionId, body.RoomId);
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing heartbeat for {SessionId} in room {RoomId}", body.SessionId, body.RoomId);
            await _messageBus.TryPublishErrorAsync(
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
    /// Processes an SDP answer from a client to complete a WebRTC handshake.
    /// Called by clients after receiving VoicePeerJoinedEvent with an SDP offer.
    /// </summary>
    public async Task<(StatusCodes, object?)> AnswerPeerAsync(AnswerPeerRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing SDP answer for target {TargetSessionId} in room {RoomId}", body.TargetSessionId, body.RoomId);

        try
        {
            // Find the target participant to send the answer to
            var targetParticipant = await _endpointRegistry.GetParticipantAsync(body.RoomId, body.TargetSessionId, cancellationToken);

            if (targetParticipant == null)
            {
                _logger.LogDebug("Target session {TargetSessionId} not found in room {RoomId}", body.TargetSessionId, body.RoomId);
                return (StatusCodes.NotFound, null);
            }

            if (_clientEventPublisher == null)
            {
                _logger.LogDebug("Client event publisher not available, cannot send answer to target");
                return (StatusCodes.OK, null);
            }

            // Get the sender's display name for the event
            var senderParticipant = await _endpointRegistry.GetParticipantAsync(body.RoomId, body.SenderSessionId, cancellationToken);
            var senderDisplayName = senderParticipant?.DisplayName ?? "Unknown";

            // Send VoicePeerUpdatedEvent directly to the TARGET session
            // The Peer describes who sent the update (the answering peer)
            var peerUpdatedEvent = new VoicePeerUpdatedEvent
            {
                Event_name = VoicePeerUpdatedEventEvent_name.Voice_peer_updated,
                Event_id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Room_id = body.RoomId,
                Peer = new VoicePeerInfo
                {
                    Peer_session_id = body.SenderSessionId,
                    Display_name = senderDisplayName,
                    Sdp_offer = body.SdpAnswer, // Using Sdp_offer to carry the SDP answer
                    Ice_candidates = body.IceCandidates?.ToList() ?? new List<string>()
                }
            };

            // Send directly to the target session
            await _clientEventPublisher.PublishToSessionsAsync(
                new[] { body.TargetSessionId },
                peerUpdatedEvent,
                cancellationToken);

            _logger.LogInformation("Sent SDP answer to target {TargetSessionId} in room {RoomId}", body.TargetSessionId, body.RoomId);

            return (StatusCodes.OK, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing SDP answer for target {TargetSessionId} in room {RoomId}", body.TargetSessionId, body.RoomId);
            await _messageBus.TryPublishErrorAsync(
                "voice",
                "AnswerPeer",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/voice/peer/answer",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Client Event Publishing

    /// <summary>
    /// Notifies existing peers that a new peer has joined.
    /// Also sets the voice:ringing state for recipients so they can respond via /voice/peer/answer.
    /// </summary>
    private async Task NotifyPeerJoinedAsync(
        Guid roomId,
        string newPeerSessionId,
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
            .Where(p => !string.IsNullOrEmpty(p.SessionId) && p.SessionId != newPeerSessionId)
            .Select(p => p.SessionId ?? string.Empty)
            .ToList();

        if (sessionIds.Count == 0)
        {
            return;
        }

        // Set voice:ringing state for all recipient sessions before publishing the event
        // This enables them to call /voice/peer/answer to send SDP answers (Tenet 10)
        if (_permissionsClient != null)
        {
            foreach (var sessionId in sessionIds)
            {
                try
                {
                    await _permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
                    {
                        SessionId = sessionId,
                        ServiceId = "voice",
                        NewState = "ringing"
                    }, cancellationToken);
                    _logger.LogDebug("Set voice:ringing state for session {SessionId}", sessionId);
                }
                catch (Exception ex)
                {
                    // Log but don't fail - the event is more important
                    _logger.LogWarning(ex, "Failed to set voice:ringing state for session {SessionId}", sessionId);
                }
            }
        }
        else
        {
            _logger.LogDebug("Permissions client not available, voice:ringing state not set");
        }

        var peerJoinedEvent = new VoicePeerJoinedEvent
        {
            Event_name = VoicePeerJoinedEventEvent_name.Voice_peer_joined,
            Event_id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Room_id = roomId,
            Peer = new VoicePeerInfo
            {
                Peer_session_id = newPeerSessionId,
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
        string leftPeerSessionId,
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
            .Select(p => p.SessionId ?? string.Empty)
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
            Peer_session_id = leftPeerSessionId,
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
        string updatedPeerSessionId,
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
            .Where(p => !string.IsNullOrEmpty(p.SessionId) && p.SessionId != updatedPeerSessionId)
            .Select(p => p.SessionId ?? string.Empty)
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
                Peer_session_id = updatedPeerSessionId,
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
            .Select(p => p.SessionId ?? string.Empty)
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

    /// <summary>
    /// Notifies all participants about tier upgrade from P2P to scaled.
    /// Each participant receives their unique SIP credentials for connecting to the RTP server.
    /// </summary>
    private async Task NotifyTierUpgradeAsync(
        Guid roomId,
        string rtpServerUri,
        CancellationToken cancellationToken)
    {
        if (_clientEventPublisher == null)
        {
            _logger.LogDebug("Client event publisher not available, skipping tier_upgrade notification");
            return;
        }

        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);
        var participantsWithSessions = participants
            .Where(p => !string.IsNullOrEmpty(p.SessionId))
            .ToList();

        if (participantsWithSessions.Count == 0)
        {
            return;
        }

        var publishedCount = 0;
        foreach (var participant in participantsWithSessions)
        {
            // SessionId is known non-null from the Where filter above
            var sessionId = participant.SessionId ?? throw new InvalidOperationException("SessionId was null after filtering");

            // Generate unique SIP credentials for this participant
            var internalCredentials = _scaledTierCoordinator.GenerateSipCredentials(sessionId, roomId);

            // Map internal credentials to client event model
            var clientCredentials = new ClientSipCredentials
            {
                Username = internalCredentials.Username,
                Password = internalCredentials.Password,
                Domain = internalCredentials.Registrar,
                Expires_at = null // Credentials valid for session duration
            };

            var tierUpgradeEvent = new VoiceTierUpgradeEvent
            {
                Event_name = VoiceTierUpgradeEventEvent_name.Voice_tier_upgrade,
                Event_id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Room_id = roomId,
                Previous_tier = VoiceTierUpgradeEventPrevious_tier.P2p,
                New_tier = VoiceTierUpgradeEventNew_tier.Scaled,
                Rtp_server_uri = rtpServerUri,
                Sip_credentials = clientCredentials,
                Migration_deadline_ms = _configuration.TierUpgradeMigrationDeadlineMs
            };

            var success = await _clientEventPublisher.PublishToSessionAsync(sessionId, tierUpgradeEvent, cancellationToken);
            if (success)
            {
                publishedCount++;
            }
        }

        _logger.LogInformation("Published tier_upgrade event to {Count} sessions for room {RoomId}", publishedCount, roomId);
    }

    #endregion

    #region Tier Upgrade Methods

    /// <summary>
    /// Attempts to upgrade a room from P2P to scaled tier.
    /// </summary>
    /// <param name="roomId">The room ID to upgrade.</param>
    /// <param name="roomData">Current room data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if upgrade succeeded, false otherwise.</returns>
    private async Task<bool> TryUpgradeToScaledTierAsync(
        Guid roomId,
        VoiceRoomData roomData,
        CancellationToken cancellationToken)
    {
        if (!_configuration.ScaledTierEnabled)
        {
            _logger.LogWarning("Scaled tier not enabled, cannot upgrade room {RoomId}", roomId);
            return false;
        }

        try
        {
            _logger.LogInformation("Starting tier upgrade for room {RoomId} from P2P to scaled", roomId);

            // Allocate an RTP server for this room
            var rtpServerUri = await _scaledTierCoordinator.AllocateRtpServerAsync(roomId, cancellationToken);

            // Update room data to scaled tier
            var updatedRoomData = new VoiceRoomData
            {
                RoomId = roomData.RoomId,
                SessionId = roomData.SessionId,
                Tier = "scaled",
                Codec = roomData.Codec,
                MaxParticipants = _scaledTierCoordinator.GetScaledMaxParticipants(),
                CreatedAt = roomData.CreatedAt,
                RtpServerUri = rtpServerUri
            };

            var roomStore = _stateStoreFactory.GetStore<VoiceRoomData>(STATE_STORE);
            await roomStore.SaveAsync($"{ROOM_KEY_PREFIX}{roomId}", updatedRoomData, cancellationToken: cancellationToken);

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

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permissions service on startup.
    /// Uses generated permission data from x-permissions sections in the OpenAPI schema.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Voice service permissions...");
        await VoicePermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
    }

    #endregion
}
