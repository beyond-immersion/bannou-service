using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Voice.Services;

/// <summary>
/// Implementation of P2P mesh topology coordinator.
/// Manages peer connections and tier upgrade decisions for voice rooms.
/// </summary>
public class P2PCoordinator : IP2PCoordinator
{
    private readonly ISipEndpointRegistry _endpointRegistry;
    private readonly ILogger<P2PCoordinator> _logger;
    private readonly VoiceServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Initializes a new instance of the P2PCoordinator.
    /// </summary>
    /// <param name="endpointRegistry">SIP endpoint registry for participant lookups.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Voice service configuration.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public P2PCoordinator(
        ISipEndpointRegistry endpointRegistry,
        ILogger<P2PCoordinator> logger,
        VoiceServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _endpointRegistry = endpointRegistry;
        _logger = logger;
        _configuration = configuration;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc />
    public async Task<List<VoicePeer>> GetMeshPeersForNewJoinAsync(
        Guid roomId,
        Guid joiningSessionId,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "P2PCoordinator.GetMeshPeersForNewJoinAsync");
        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);

        // Convert participants to VoicePeer, excluding the joining participant
        var peers = participants
            .Where(p => p.SessionId != joiningSessionId)
            .Select(p => new VoicePeer
            {
                SessionId = p.SessionId,
                DisplayName = p.DisplayName,
                SipEndpoint = p.Endpoint ?? new SipEndpoint { SdpOffer = string.Empty, IceCandidates = new List<string>() }
            })
            .ToList();

        _logger.LogDebug(
            "Built peer list for {JoiningSessionId} in room {RoomId}: {PeerCount} peers",
            joiningSessionId, roomId, peers.Count);

        return peers;
    }

    /// <inheritdoc />
    public async Task<bool> ShouldUpgradeToScaledAsync(
        Guid roomId,
        int currentParticipantCount,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "P2PCoordinator.ShouldUpgradeToScaledAsync");
        await Task.CompletedTask;
        var maxP2P = GetP2PMaxParticipants();
        // Upgrade when EXCEEDING capacity (>), not when AT capacity (>=)
        // P2P with max=2: 2 participants is fine, 3 participants needs upgrade
        var shouldUpgrade = currentParticipantCount > maxP2P;

        if (shouldUpgrade)
        {
            _logger.LogInformation(
                "Room {RoomId} exceeded P2P threshold ({Count}/{Max}), should upgrade to scaled tier",
                roomId, currentParticipantCount, maxP2P);
        }

        return shouldUpgrade;
    }

    /// <inheritdoc />
    public async Task<bool> CanAcceptNewParticipantAsync(
        Guid roomId,
        int currentParticipantCount,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "P2PCoordinator.CanAcceptNewParticipantAsync");
        await Task.CompletedTask;
        // Check if room is at P2P capacity
        var maxP2P = GetP2PMaxParticipants();
        var canAccept = currentParticipantCount < maxP2P;

        if (!canAccept)
        {
            // Room is at P2P capacity - return false to trigger tier upgrade logic in VoiceService
            // VoiceService will check if scaled tier is enabled and attempt upgrade
            _logger.LogInformation(
                "Room {RoomId} at P2P capacity ({Count}/{Max}), returning false to trigger upgrade check",
                roomId, currentParticipantCount, maxP2P);
        }

        return canAccept;
    }

    /// <inheritdoc />
    public int GetP2PMaxParticipants()
    {
        return _configuration.P2PMaxParticipants;
    }

    /// <inheritdoc />
    public async Task<JoinVoiceRoomResponse> BuildP2PConnectionInfoAsync(
        Guid roomId,
        List<VoicePeer> peers,
        VoiceCodec defaultCodec,
        List<string> stunServers,
        bool tierUpgradePending = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "P2PCoordinator.BuildP2PConnectionInfoAsync");
        await Task.CompletedTask;
        var response = new JoinVoiceRoomResponse
        {
            RoomId = roomId,
            Tier = VoiceTier.P2P,
            Codec = defaultCodec,
            Peers = peers,
            RtpServerUri = null, // P2P mode, no RTP server
            StunServers = stunServers,
            TierUpgradePending = tierUpgradePending
        };

        return response;
    }
}
