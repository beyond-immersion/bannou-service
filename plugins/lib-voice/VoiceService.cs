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

/// <summary>
/// Implementation of the Voice service.
/// This class contains the business logic for all Voice operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS CHECKLIST:</b>
/// <list type="bullet">
///   <item><b>Type Safety:</b> Internal POCOs MUST use proper C# types (enums, Guids, DateTimeOffset) - never string representations. No Enum.Parse in business logic.</item>
///   <item><b>Configuration:</b> ALL config properties in VoiceServiceConfiguration MUST be wired up. No hardcoded magic numbers for tunables.</item>
///   <item><b>Events:</b> ALL meaningful state changes MUST publish typed events, even without current consumers.</item>
///   <item><b>Cache Stores:</b> If state-stores.yaml defines cache stores for this service, implement read-through/write-through caching.</item>
///   <item><b>Concurrency:</b> Use GetWithETagAsync + TrySaveAsync for list/index operations. No non-atomic read-modify-write.</item>
/// </list>
/// </para>
/// <para>
/// <b>MODELS:</b> Run <c>make print-models PLUGIN="voice"</c> to view compact request/response model shapes.
/// If print-models fails or generation has not been run, DO NOT proceed with implementation.
/// Generate first (<c>cd scripts &amp;&amp; ./generate-service.sh voice</c>) or ask the developer how to continue.
/// Never guess at model definitions.
/// </para>
/// <para>
/// <b>RELATED FILES:</b>
/// <list type="bullet">
///   <item>Internal data models: VoiceServiceModels.cs (storage models, cache entries, internal DTOs)</item>
///   <item>Event handlers: VoiceServiceEvents.cs (event consumer registration and handlers)</item>
///   <item>Configuration: Generated/VoiceServiceConfiguration.cs</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("voice", typeof(IVoiceService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.AppFeatures)]
public partial class VoiceService : IVoiceService
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<VoiceService> _logger;
    private readonly VoiceServiceConfiguration _configuration;
    private readonly ISipEndpointRegistry _endpointRegistry;
    private readonly IP2PCoordinator _p2pCoordinator;
    private readonly IScaledTierCoordinator _scaledTierCoordinator;
    private readonly IClientEventPublisher _clientEventPublisher;
    private readonly IPermissionClient _permissionClient;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IStateStore<VoiceRoomData> _roomStore;
    private readonly IStateStore<string> _stringStore;

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
    /// <param name="clientEventPublisher">Client event publisher for WebSocket push events.</param>
    /// <param name="permissionClient">Permission client for managing voice permission states.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="lockProvider">Distributed lock provider for cross-instance coordination.</param>
    public VoiceService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<VoiceService> logger,
        VoiceServiceConfiguration configuration,
        ISipEndpointRegistry endpointRegistry,
        IP2PCoordinator p2pCoordinator,
        IScaledTierCoordinator scaledTierCoordinator,
        IEventConsumer eventConsumer,
        IClientEventPublisher clientEventPublisher,
        IPermissionClient permissionClient,
        ITelemetryProvider telemetryProvider,
        IDistributedLockProvider lockProvider)
    {
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _endpointRegistry = endpointRegistry;
        _p2pCoordinator = p2pCoordinator;
        _scaledTierCoordinator = scaledTierCoordinator;
        _clientEventPublisher = clientEventPublisher;
        _permissionClient = permissionClient;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
        _lockProvider = lockProvider;
        _roomStore = stateStoreFactory.GetStore<VoiceRoomData>(StateStoreDefinitions.Voice);
        _stringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.Voice);

        // Register event handlers via partial class (VoiceServiceEvents.cs)
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Creates a new voice room.
    /// </summary>
    public async Task<(StatusCodes, VoiceRoomResponse?)> CreateVoiceRoomAsync(CreateVoiceRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Creating voice room for session {SessionId}", body.SessionId);

        // Distributed lock prevents concurrent room creation for the same session
        // (check-then-act race on session-room mapping per IMPLEMENTATION TENETS)
        var lockOwner = $"create-room-{body.SessionId:N}-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.VoiceLock,
            $"session-room:{body.SessionId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken: cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire room creation lock for session {SessionId}", body.SessionId);
            return (StatusCodes.Conflict, null);
        }

        // Check if room already exists for this session
        var existingRoomIdStr = await _stringStore.GetAsync($"{SESSION_ROOM_KEY_PREFIX}{body.SessionId}", cancellationToken);

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
            Tier = body.PreferredTier ?? VoiceTier.P2P,
            Codec = body.Codec ?? VoiceCodec.Opus,
            MaxParticipants = body.MaxParticipants > 0 ? body.MaxParticipants : _p2pCoordinator.GetP2PMaxParticipants(),
            CreatedAt = now,
            RtpServerUri = null,
            AutoCleanup = body.AutoCleanup,
            Password = body.Password
        };

        // Save room data
        await _roomStore.SaveAsync($"{ROOM_KEY_PREFIX}{roomId}", roomData, cancellationToken: cancellationToken);

        // Save session -> room mapping (store Guid as string since IStateStore requires reference types)
        await _stringStore.SaveAsync($"{SESSION_ROOM_KEY_PREFIX}{body.SessionId}", roomId.ToString(), cancellationToken: cancellationToken);

        // Publish service event
        await _messageBus.TryPublishAsync("voice.room.created", new VoiceRoomCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            RoomId = roomId,
            SessionId = body.SessionId,
            Tier = roomData.Tier,
            MaxParticipants = roomData.MaxParticipants
        });

        _logger.LogInformation("Created voice room {RoomId} for session {SessionId}", roomId, body.SessionId);

        return (StatusCodes.OK, new VoiceRoomResponse
        {
            RoomId = roomId,
            SessionId = body.SessionId,
            Tier = roomData.Tier,
            Codec = roomData.Codec,
            MaxParticipants = roomData.MaxParticipants,
            CurrentParticipants = 0,
            Participants = new List<VoiceParticipant>(),
            CreatedAt = now,
            RtpServerUri = null,
            AutoCleanup = roomData.AutoCleanup,
            IsPasswordProtected = !string.IsNullOrEmpty(roomData.Password),
            BroadcastState = BroadcastConsentState.Inactive
        });
    }

    /// <summary>
    /// Gets voice room details.
    /// </summary>
    public async Task<(StatusCodes, VoiceRoomResponse?)> GetVoiceRoomAsync(GetVoiceRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting voice room {RoomId}", body.RoomId);

        var roomData = await _roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

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
            Tier = roomData.Tier,
            Codec = roomData.Codec,
            MaxParticipants = roomData.MaxParticipants,
            CurrentParticipants = participants.Count,
            Participants = participants.Select(p => p.ToVoiceParticipant()).ToList(),
            CreatedAt = roomData.CreatedAt,
            RtpServerUri = roomData.RtpServerUri,
            AutoCleanup = roomData.AutoCleanup,
            IsPasswordProtected = !string.IsNullOrEmpty(roomData.Password),
            BroadcastState = roomData.BroadcastState
        });
    }

    /// <summary>
    /// Joins a voice room and registers the participant's SIP endpoint.
    /// Handles both P2P mode (returns peer list) and scaled mode (returns SIP credentials).
    /// If AdHocRoomsEnabled and room doesn't exist, auto-creates it with autoCleanup enabled.
    /// </summary>
    public async Task<(StatusCodes, JoinVoiceRoomResponse?)> JoinVoiceRoomAsync(JoinVoiceRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Session {SessionId} joining voice room {RoomId}", body.SessionId, body.RoomId);

        // Get room data
        var roomData = await _roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

        if (roomData == null)
        {
            // Ad-hoc room support: auto-create when enabled
            if (_configuration.AdHocRoomsEnabled)
            {
                // Distributed lock prevents concurrent ad-hoc room creation for the same
                // room ID (check-then-act race per IMPLEMENTATION TENETS)
                var adHocLockOwner = $"adhoc-create-{body.SessionId:N}-{Guid.NewGuid():N}";
                await using var adHocLock = await _lockProvider.LockAsync(
                    StateStoreDefinitions.VoiceLock,
                    $"room-create:{body.RoomId}",
                    adHocLockOwner,
                    _configuration.LockTimeoutSeconds,
                    cancellationToken: cancellationToken);

                if (!adHocLock.Success)
                {
                    _logger.LogWarning("Failed to acquire ad-hoc room creation lock for room {RoomId}", body.RoomId);
                    return (StatusCodes.Conflict, null);
                }

                // Re-check after acquiring lock — another instance may have created the room
                roomData = await _roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

                if (roomData == null)
                {
                    _logger.LogInformation("Auto-creating ad-hoc voice room {RoomId}", body.RoomId);
                    var now = DateTimeOffset.UtcNow;
                    roomData = new VoiceRoomData
                    {
                        RoomId = body.RoomId,
                        SessionId = body.SessionId,
                        Tier = VoiceTier.P2P,
                        Codec = VoiceCodec.Opus,
                        MaxParticipants = _p2pCoordinator.GetP2PMaxParticipants(),
                        CreatedAt = now,
                        AutoCleanup = true
                    };

                    await _roomStore.SaveAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", roomData, cancellationToken: cancellationToken);

                    // Save session -> room mapping
                    await _stringStore.SaveAsync($"{SESSION_ROOM_KEY_PREFIX}{body.SessionId}", body.RoomId.ToString(), cancellationToken: cancellationToken);

                    // Publish room created event
                    await _messageBus.TryPublishAsync("voice.room.created", new VoiceRoomCreatedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = now,
                        RoomId = body.RoomId,
                        SessionId = body.SessionId,
                        Tier = roomData.Tier,
                        MaxParticipants = roomData.MaxParticipants
                    });
                }
            }
            else
            {
                _logger.LogWarning("Voice room {RoomId} not found", body.RoomId);
                return (StatusCodes.NotFound, null);
            }
        }

        // Password validation
        if (!string.IsNullOrEmpty(roomData.Password))
        {
            if (body.Password != roomData.Password)
            {
                _logger.LogWarning("Invalid password for voice room {RoomId}", body.RoomId);
                return (StatusCodes.Forbidden, null);
            }
        }

        // Check participant count
        var currentCount = await _endpointRegistry.GetParticipantCountAsync(body.RoomId, cancellationToken);
        var isScaledTier = roomData.Tier == VoiceTier.Scaled;

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
                    roomData = await _roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);
                    if (roomData == null)
                    {
                        _logger.LogError("Room data disappeared after tier upgrade for room {RoomId}", body.RoomId);
                        await _messageBus.TryPublishErrorAsync(
                            "voice",
                            "JoinVoiceRoom",
                            "state_consistency_error",
                            "Room data disappeared after tier upgrade",
                            dependency: "state",
                            endpoint: "post:/voice/rooms/join",
                            details: $"roomId={body.RoomId}",
                            stack: null);
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

        // Set voice:in_room permission state for the joining session
        try
        {
            await _permissionClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = body.SessionId,
                ServiceId = "voice",
                NewState = "in_room"
            }, cancellationToken);
            _logger.LogDebug("Set voice:in_room state for session {SessionId}", body.SessionId);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to set voice:in_room state for session {SessionId}", body.SessionId);
        }

        // Publish peer joined service event
        await _messageBus.TryPublishAsync("voice.peer.joined", new VoicePeerJoinedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = body.RoomId,
            PeerSessionId = body.SessionId,
            CurrentCount = newCount
        });

        // Parse STUN servers from config (StunServers has a schema default; null means infrastructure failure)
        var stunServers = _configuration.StunServers?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList() ?? throw new InvalidOperationException("StunServers configuration is required but was null");

        // Handle based on current tier
        if (isScaledTier)
        {
            _logger.LogInformation("Session {SessionId} joined scaled tier room {RoomId}", body.SessionId, body.RoomId);

            return (StatusCodes.OK, new JoinVoiceRoomResponse
            {
                RoomId = body.RoomId,
                Tier = VoiceTier.Scaled,
                Codec = roomData.Codec,
                Peers = new List<VoicePeer>(),
                RtpServerUri = roomData.RtpServerUri,
                StunServers = stunServers,
                TierUpgradePending = false,
                BroadcastState = roomData.BroadcastState
            });
        }

        // P2P tier: Return peer list
        var peers = await _p2pCoordinator.GetMeshPeersForNewJoinAsync(body.RoomId, body.SessionId, cancellationToken);

        // Check if tier upgrade is needed for future joins
        var tierUpgradePending = _configuration.TierUpgradeEnabled &&
                                _configuration.ScaledTierEnabled &&
                                await _p2pCoordinator.ShouldUpgradeToScaledAsync(body.RoomId, newCount, cancellationToken);

        // Notify existing peers about the new participant
        await NotifyPeerJoinedAsync(body.RoomId, body.SessionId, body.DisplayName, body.SipEndpoint, newCount, cancellationToken);

        // Set voice:ringing for the joining session if there are existing peers
        if (peers.Count > 0)
        {
            try
            {
                await _permissionClient.UpdateSessionStateAsync(new SessionStateUpdate
                {
                    SessionId = body.SessionId,
                    ServiceId = "voice",
                    NewState = "ringing"
                }, cancellationToken);
                _logger.LogDebug("Set voice:ringing state for joining session {SessionId} with {PeerCount} existing peers",
                    body.SessionId, peers.Count);
            }
            catch (ApiException ex)
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
                    // Use CancellationToken.None: tier upgrade must complete independently of the
                    // originating HTTP request whose cancellationToken is request-scoped
                    await TryUpgradeToScaledTierAsync(body.RoomId, roomData, CancellationToken.None);
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
            RoomId = body.RoomId,
            Tier = VoiceTier.P2P,
            Codec = roomData.Codec,
            Peers = peers,
            RtpServerUri = null,
            StunServers = stunServers,
            TierUpgradePending = tierUpgradePending,
            BroadcastState = roomData.BroadcastState
        });
    }

    /// <summary>
    /// Leaves a voice room.
    /// </summary>
    public async Task<StatusCodes> LeaveVoiceRoomAsync(LeaveVoiceRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Session {SessionId} leaving voice room {RoomId}", body.SessionId, body.RoomId);

        // Unregister participant
        var removed = await _endpointRegistry.UnregisterAsync(body.RoomId, body.SessionId, cancellationToken);

        if (removed == null)
        {
            _logger.LogDebug("Session {SessionId} not found in room {RoomId}", body.SessionId, body.RoomId);
            return StatusCodes.NotFound;
        }

        // Clear voice permission state
        try
        {
            await _permissionClient.ClearSessionStateAsync(new ClearSessionStateRequest
            {
                SessionId = body.SessionId,
                ServiceId = "voice"
            }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to clear voice permission state for session {SessionId}", body.SessionId);
        }

        // Get remaining count
        var remainingCount = await _endpointRegistry.GetParticipantCountAsync(body.RoomId, cancellationToken);

        // Publish peer left service event
        await _messageBus.TryPublishAsync("voice.peer.left", new VoicePeerLeftEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = body.RoomId,
            PeerSessionId = body.SessionId,
            RemainingCount = remainingCount
        });

        // Notify remaining peers
        await NotifyPeerLeftAsync(body.RoomId, removed.SessionId, removed.DisplayName, remainingCount, cancellationToken);

        // Check if leaving participant breaks broadcast consent
        // Distributed lock prevents concurrent leave/consent operations from racing on
        // the read-modify-write of BroadcastState (per IMPLEMENTATION TENETS)
        var lockOwner = $"leave-broadcast-{body.SessionId:N}-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.VoiceLock,
            $"broadcast-consent:{body.RoomId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken: cancellationToken);

        if (lockResponse.Success)
        {
            var roomData = await _roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

            if (roomData != null)
            {
                // If broadcasting and participant leaves, stop broadcast (consent broken)
                if (roomData.BroadcastState == BroadcastConsentState.Approved ||
                    roomData.BroadcastState == BroadcastConsentState.Pending)
                {
                    await StopBroadcastInternalAsync(body.RoomId, roomData, VoiceBroadcastStoppedReason.ConsentRevoked, cancellationToken);
                }

                // If room is now empty and AutoCleanup, set timestamp for grace period
                if (remainingCount == 0 && roomData.AutoCleanup)
                {
                    roomData.LastParticipantLeftAt = DateTimeOffset.UtcNow;
                    await _roomStore.SaveAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", roomData, cancellationToken: cancellationToken);
                }
            }
        }
        else
        {
            _logger.LogWarning("Failed to acquire broadcast consent lock during leave for room {RoomId}, broadcast state may be stale", body.RoomId);
        }

        _logger.LogInformation("Session {SessionId} left voice room {RoomId}", body.SessionId, body.RoomId);

        return StatusCodes.OK;
    }

    /// <summary>
    /// Deletes a voice room and notifies all participants.
    /// For scaled tier rooms, also releases RTP server resources.
    /// </summary>
    public async Task<StatusCodes> DeleteVoiceRoomAsync(DeleteVoiceRoomRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting voice room {RoomId}", body.RoomId);

        // Get room data
        var roomData = await _roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

        if (roomData == null)
        {
            _logger.LogDebug("Voice room {RoomId} not found", body.RoomId);
            return StatusCodes.NotFound;
        }

        // If broadcasting, stop broadcast first (with lock per IMPLEMENTATION TENETS)
        if (roomData.BroadcastState == BroadcastConsentState.Approved ||
            roomData.BroadcastState == BroadcastConsentState.Pending)
        {
            var broadcastLockOwner = $"delete-broadcast-{Guid.NewGuid():N}";
            await using var broadcastLock = await _lockProvider.LockAsync(
                StateStoreDefinitions.VoiceLock,
                $"broadcast-consent:{body.RoomId}",
                broadcastLockOwner,
                _configuration.LockTimeoutSeconds,
                cancellationToken: cancellationToken);

            if (broadcastLock.Success)
            {
                // Re-read state inside lock to avoid stale data
                var freshRoomData = await _roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);
                if (freshRoomData != null &&
                    (freshRoomData.BroadcastState == BroadcastConsentState.Approved ||
                    freshRoomData.BroadcastState == BroadcastConsentState.Pending))
                {
                    await StopBroadcastInternalAsync(body.RoomId, freshRoomData, VoiceBroadcastStoppedReason.RoomClosed, cancellationToken);
                    // Update local reference so downstream deletion uses correct state
                    roomData = freshRoomData;
                }
            }
            else
            {
                _logger.LogWarning("Failed to acquire broadcast consent lock during delete for room {RoomId}", body.RoomId);
            }
        }

        // Get all participants before clearing
        var participants = await _endpointRegistry.GetRoomParticipantsAsync(body.RoomId, cancellationToken);

        // Clear all participants
        await _endpointRegistry.ClearRoomAsync(body.RoomId, cancellationToken);

        // If this was a scaled tier room, release RTP server resources
        if (roomData.Tier == VoiceTier.Scaled && !string.IsNullOrEmpty(roomData.RtpServerUri))
        {
            await _scaledTierCoordinator.ReleaseRtpServerAsync(body.RoomId, cancellationToken);
            _logger.LogDebug("Released RTP server resources for room {RoomId}", body.RoomId);
        }

        // Delete room data
        await _roomStore.DeleteAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

        // Delete session -> room mapping
        await _stringStore.DeleteAsync($"{SESSION_ROOM_KEY_PREFIX}{roomData.SessionId}", cancellationToken);

        // Publish room deleted service event
        var deleteReason = body.Reason ?? VoiceRoomDeletedReason.Manual;
        await _messageBus.TryPublishAsync("voice.room.deleted", new VoiceRoomDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = body.RoomId,
            Reason = deleteReason
        });

        // Notify all participants that room is closed
        await NotifyRoomClosedAsync(body.RoomId, participants, deleteReason, cancellationToken);

        // Clear permission states for all participants
        foreach (var participant in participants)
        {
            try
            {
                await _permissionClient.ClearSessionStateAsync(new ClearSessionStateRequest
                {
                    SessionId = participant.SessionId,
                    ServiceId = "voice"
                }, cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Failed to clear voice permission state for session {SessionId}", participant.SessionId);
            }
        }

        _logger.LogInformation("Deleted voice room {RoomId}, notified {ParticipantCount} participants", body.RoomId, participants.Count);

        return StatusCodes.OK;
    }

    /// <summary>
    /// Updates a participant's heartbeat to keep their registration active.
    /// </summary>
    public async Task<StatusCodes> PeerHeartbeatAsync(PeerHeartbeatRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Heartbeat from {SessionId} in room {RoomId}", body.SessionId, body.RoomId);

        var updated = await _endpointRegistry.UpdateHeartbeatAsync(body.RoomId, body.SessionId, cancellationToken);

        if (!updated)
        {
            _logger.LogDebug("Session {SessionId} not found in room {RoomId}", body.SessionId, body.RoomId);
            return StatusCodes.NotFound;
        }

        return StatusCodes.OK;
    }

    /// <summary>
    /// Processes an SDP answer from a client to complete a WebRTC handshake.
    /// </summary>
    public async Task<StatusCodes> AnswerPeerAsync(AnswerPeerRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing SDP answer for target {TargetSessionId} in room {RoomId}", body.TargetSessionId, body.RoomId);

        var targetParticipant = await _endpointRegistry.GetParticipantAsync(body.RoomId, body.TargetSessionId, cancellationToken);

        if (targetParticipant == null)
        {
            _logger.LogDebug("Target session {TargetSessionId} not found in room {RoomId}", body.TargetSessionId, body.RoomId);
            return StatusCodes.NotFound;
        }

        var senderParticipant = await _endpointRegistry.GetParticipantAsync(body.RoomId, body.SenderSessionId, cancellationToken);
        var senderDisplayName = senderParticipant?.DisplayName ?? "Unknown";

        var peerUpdatedEvent = new VoicePeerUpdatedClientEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = body.RoomId,
            Peer = new VoicePeerInfo
            {
                PeerSessionId = body.SenderSessionId,
                DisplayName = senderDisplayName,
                SdpOffer = body.SdpAnswer,
                IceCandidates = body.IceCandidates?.ToList() ?? new List<string>()
            }
        };

        await _clientEventPublisher.PublishToSessionsAsync(
            new[] { body.TargetSessionId.ToString() },
            peerUpdatedEvent,
            cancellationToken);

        _logger.LogInformation("Sent SDP answer to target {TargetSessionId} in room {RoomId}", body.TargetSessionId, body.RoomId);

        return StatusCodes.OK;
    }

    #region Broadcast Consent Flow

    /// <summary>
    /// Requests broadcast consent from all room participants.
    /// </summary>
    public async Task<(StatusCodes, BroadcastConsentStatus?)> RequestBroadcastConsentAsync(BroadcastConsentRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Broadcast consent requested for room {RoomId}", body.RoomId);

        // Distributed lock prevents concurrent broadcast consent requests from racing on
        // the read-modify-write of BroadcastState (per IMPLEMENTATION TENETS)
        var lockOwner = $"request-consent-{body.RequestingSessionId:N}-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.VoiceLock,
            $"broadcast-consent:{body.RoomId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken: cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire broadcast consent lock for room {RoomId}", body.RoomId);
            return (StatusCodes.Conflict, null);
        }

        var roomData = await _roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

        if (roomData == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (roomData.BroadcastState != BroadcastConsentState.Inactive)
        {
            _logger.LogWarning("Broadcast consent already in state {State} for room {RoomId}", roomData.BroadcastState, body.RoomId);
            return (StatusCodes.Conflict, null);
        }

        // Get all current participant session IDs
        var participants = await _endpointRegistry.GetRoomParticipantsAsync(body.RoomId, cancellationToken);
        var participantSessionIds = participants.Select(p => p.SessionId).ToList();

        if (participantSessionIds.Count == 0)
        {
            return (StatusCodes.Conflict, null);
        }

        // Update room state to Pending
        roomData.BroadcastState = BroadcastConsentState.Pending;
        roomData.BroadcastRequestedBy = body.RequestingSessionId;
        roomData.BroadcastConsentedSessions = new HashSet<Guid>();
        roomData.BroadcastRequestedAt = DateTimeOffset.UtcNow;
        await _roomStore.SaveAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", roomData, cancellationToken: cancellationToken);

        // Set voice:consent_pending permission state for ALL participants
        foreach (var sessionId in participantSessionIds)
        {
            try
            {
                await _permissionClient.UpdateSessionStateAsync(new SessionStateUpdate
                {
                    SessionId = sessionId,
                    ServiceId = "voice",
                    NewState = "consent_pending"
                }, cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Failed to set voice:consent_pending for session {SessionId}", sessionId);
            }
        }

        // Get requester display name
        var requester = participants.FirstOrDefault(p => p.SessionId == body.RequestingSessionId);

        // Publish client event to all participants
        var consentRequestEvent = new VoiceBroadcastConsentRequestClientEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RoomId = body.RoomId,
            RequestedBySessionId = body.RequestingSessionId,
            RequestedByDisplayName = requester?.DisplayName
        };

        await _clientEventPublisher.PublishToSessionsAsync(
            participantSessionIds.Select(id => id.ToString()),
            consentRequestEvent,
            cancellationToken);

        _logger.LogInformation("Broadcast consent requested for room {RoomId} by {SessionId}, {Count} participants pending",
            body.RoomId, body.RequestingSessionId, participantSessionIds.Count);

        return (StatusCodes.OK, new BroadcastConsentStatus
        {
            RoomId = body.RoomId,
            State = BroadcastConsentState.Pending,
            RequestedBySessionId = body.RequestingSessionId,
            ConsentedSessionIds = new List<Guid>(),
            PendingSessionIds = participantSessionIds,
            RtpAudioEndpoint = roomData.RtpServerUri
        });
    }

    /// <summary>
    /// Responds to a broadcast consent request.
    /// </summary>
    public async Task<(StatusCodes, BroadcastConsentStatus?)> RespondBroadcastConsentAsync(BroadcastConsentResponse body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Broadcast consent response from {SessionId} for room {RoomId}: consented={Consented}",
            body.SessionId, body.RoomId, body.Consented);

        // Distributed lock prevents concurrent consent responses from overwriting each other's
        // consented session sets (read-modify-write race per IMPLEMENTATION TENETS)
        var lockOwner = $"consent-{body.SessionId:N}-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.VoiceLock,
            $"broadcast-consent:{body.RoomId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken: cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire broadcast consent lock for room {RoomId}", body.RoomId);
            return (StatusCodes.Conflict, null);
        }

        var roomData = await _roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

        if (roomData == null)
        {
            return (StatusCodes.NotFound, null);
        }

        if (roomData.BroadcastState != BroadcastConsentState.Pending)
        {
            _logger.LogWarning("Room {RoomId} not in Pending state for consent response", body.RoomId);
            return (StatusCodes.Conflict, null);
        }

        var participants = await _endpointRegistry.GetRoomParticipantsAsync(body.RoomId, cancellationToken);
        var participantSessionIds = participants.Select(p => p.SessionId).ToHashSet();

        if (!body.Consented)
        {
            // Declined: reset to Inactive
            roomData.BroadcastState = BroadcastConsentState.Inactive;
            roomData.BroadcastConsentedSessions.Clear();
            roomData.BroadcastRequestedBy = null;
            roomData.BroadcastRequestedAt = null;
            await _roomStore.SaveAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", roomData, cancellationToken: cancellationToken);

            // Clear consent_pending states
            await ClearConsentPendingStatesAsync(participantSessionIds, cancellationToken);

            // Publish declined service event
            await _messageBus.TryPublishAsync("voice.broadcast.declined", new VoiceBroadcastDeclinedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RoomId = body.RoomId,
                DeclinedBySessionId = body.SessionId
            });

            // Get decliner display name
            var decliner = participants.FirstOrDefault(p => p.SessionId == body.SessionId);

            // Publish client event
            await PublishBroadcastConsentUpdateAsync(body.RoomId, participantSessionIds,
                BroadcastConsentState.Inactive, 0, participantSessionIds.Count,
                decliner?.DisplayName, cancellationToken);

            _logger.LogInformation("Broadcast consent declined by {SessionId} for room {RoomId}", body.SessionId, body.RoomId);

            return (StatusCodes.OK, new BroadcastConsentStatus
            {
                RoomId = body.RoomId,
                State = BroadcastConsentState.Inactive,
                RequestedBySessionId = null,
                ConsentedSessionIds = new List<Guid>(),
                PendingSessionIds = new List<Guid>(),
                RtpAudioEndpoint = roomData.RtpServerUri
            });
        }

        // Consented: add to consented set
        roomData.BroadcastConsentedSessions.Add(body.SessionId);
        var consentedCount = roomData.BroadcastConsentedSessions.Count;

        // Check if all participants have consented
        if (roomData.BroadcastConsentedSessions.IsSupersetOf(participantSessionIds))
        {
            // All consented -> Approved
            roomData.BroadcastState = BroadcastConsentState.Approved;
            await _roomStore.SaveAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", roomData, cancellationToken: cancellationToken);

            // Clear consent_pending states, restore to in_room
            await ClearConsentPendingStatesAsync(participantSessionIds, cancellationToken);

            // Publish approved service event
            await _messageBus.TryPublishAsync("voice.broadcast.approved", new VoiceBroadcastApprovedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RoomId = body.RoomId,
                RequestedBySessionId = roomData.BroadcastRequestedBy,
                RtpAudioEndpoint = roomData.RtpServerUri
            });

            // Publish client event
            await PublishBroadcastConsentUpdateAsync(body.RoomId, participantSessionIds,
                BroadcastConsentState.Approved, consentedCount, participantSessionIds.Count,
                null, cancellationToken);

            _logger.LogInformation("All participants consented to broadcast in room {RoomId}", body.RoomId);

            return (StatusCodes.OK, new BroadcastConsentStatus
            {
                RoomId = body.RoomId,
                State = BroadcastConsentState.Approved,
                RequestedBySessionId = roomData.BroadcastRequestedBy,
                ConsentedSessionIds = roomData.BroadcastConsentedSessions.ToList(),
                PendingSessionIds = new List<Guid>(),
                RtpAudioEndpoint = roomData.RtpServerUri
            });
        }

        // Still waiting for more consents
        await _roomStore.SaveAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", roomData, cancellationToken: cancellationToken);

        var pendingIds = participantSessionIds.Except(roomData.BroadcastConsentedSessions).ToList();

        // Publish progress client event
        await PublishBroadcastConsentUpdateAsync(body.RoomId, participantSessionIds,
            BroadcastConsentState.Pending, consentedCount, participantSessionIds.Count,
            null, cancellationToken);

        return (StatusCodes.OK, new BroadcastConsentStatus
        {
            RoomId = body.RoomId,
            State = BroadcastConsentState.Pending,
            RequestedBySessionId = roomData.BroadcastRequestedBy,
            ConsentedSessionIds = roomData.BroadcastConsentedSessions.ToList(),
            PendingSessionIds = pendingIds,
            RtpAudioEndpoint = roomData.RtpServerUri
        });
    }

    /// <summary>
    /// Stops broadcasting from a voice room.
    /// </summary>
    public async Task<StatusCodes> StopBroadcastAsync(StopBroadcastConsentRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping broadcast for room {RoomId}", body.RoomId);

        // Distributed lock prevents concurrent stop/consent operations from racing on
        // the read-check-modify of BroadcastState (per IMPLEMENTATION TENETS)
        var lockOwner = $"stop-broadcast-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.VoiceLock,
            $"broadcast-consent:{body.RoomId}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken: cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire broadcast consent lock for room {RoomId}", body.RoomId);
            return StatusCodes.Conflict;
        }

        var roomData = await _roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

        if (roomData == null)
        {
            return StatusCodes.NotFound;
        }

        if (roomData.BroadcastState == BroadcastConsentState.Inactive)
        {
            _logger.LogDebug("Room {RoomId} is not broadcasting", body.RoomId);
            return StatusCodes.NotFound;
        }

        await StopBroadcastInternalAsync(body.RoomId, roomData, VoiceBroadcastStoppedReason.Manual, cancellationToken);

        _logger.LogInformation("Broadcast stopped for room {RoomId}", body.RoomId);
        return StatusCodes.OK;
    }

    /// <summary>
    /// Gets broadcast consent status for a room.
    /// </summary>
    public async Task<(StatusCodes, BroadcastConsentStatus?)> GetBroadcastStatusAsync(BroadcastStatusRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting broadcast status for room {RoomId}", body.RoomId);

        var roomData = await _roomStore.GetAsync($"{ROOM_KEY_PREFIX}{body.RoomId}", cancellationToken);

        if (roomData == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var participants = await _endpointRegistry.GetRoomParticipantsAsync(body.RoomId, cancellationToken);
        var participantSessionIds = participants.Select(p => p.SessionId).ToHashSet();
        var pendingIds = participantSessionIds.Except(roomData.BroadcastConsentedSessions).ToList();

        return (StatusCodes.OK, new BroadcastConsentStatus
        {
            RoomId = body.RoomId,
            State = roomData.BroadcastState,
            RequestedBySessionId = roomData.BroadcastRequestedBy,
            ConsentedSessionIds = roomData.BroadcastConsentedSessions.ToList(),
            PendingSessionIds = pendingIds,
            RtpAudioEndpoint = roomData.RtpServerUri
        });
    }

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
        await _messageBus.TryPublishAsync("voice.broadcast.stopped", new VoiceBroadcastStoppedEvent
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

    #endregion

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
            await _messageBus.TryPublishAsync("voice.room.tier-upgraded", new VoiceRoomTierUpgradedEvent
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

    #region Permission Registration

    #endregion
}
