using Microsoft.Extensions.Logging;

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

    /// <summary>
    /// Initializes a new instance of the P2PCoordinator.
    /// </summary>
    /// <param name="endpointRegistry">SIP endpoint registry for participant lookups.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Voice service configuration.</param>
    public P2PCoordinator(
        ISipEndpointRegistry endpointRegistry,
        ILogger<P2PCoordinator> logger,
        VoiceServiceConfiguration configuration)
    {
        _endpointRegistry = endpointRegistry ?? throw new ArgumentNullException(nameof(endpointRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public async Task<List<VoicePeer>> GetMeshPeersForNewJoinAsync(
        Guid roomId,
        string joiningSessionId,
        CancellationToken cancellationToken = default)
    {
        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);

        // Convert participants to VoicePeer, excluding the joining participant
        var peers = participants
            .Where(p => p.SessionId != joiningSessionId && !string.IsNullOrEmpty(p.SessionId))
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
    public Task<bool> ShouldUpgradeToScaledAsync(
        Guid roomId,
        int currentParticipantCount,
        CancellationToken cancellationToken = default)
    {
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

        return Task.FromResult(shouldUpgrade);
    }

    /// <inheritdoc />
    public Task<bool> CanAcceptNewParticipantAsync(
        Guid roomId,
        int currentParticipantCount,
        CancellationToken cancellationToken = default)
    {
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

        return Task.FromResult(canAccept);
    }

    /// <inheritdoc />
    public int GetP2PMaxParticipants()
    {
        return _configuration.P2PMaxParticipants > 0
            ? _configuration.P2PMaxParticipants
            : 6; // Default fallback
    }

    /// <inheritdoc />
    public Task<JoinVoiceRoomResponse> BuildP2PConnectionInfoAsync(
        Guid roomId,
        List<VoicePeer> peers,
        string defaultCodec,
        List<string> stunServers,
        bool tierUpgradePending = false,
        CancellationToken cancellationToken = default)
    {
        var response = new JoinVoiceRoomResponse
        {
            Success = true,
            RoomId = roomId,
            Tier = VoiceTier.P2p,
            Codec = ParseVoiceCodec(defaultCodec),
            Peers = peers,
            RtpServerUri = null, // P2P mode, no RTP server
            StunServers = stunServers,
            TierUpgradePending = tierUpgradePending
        };

        return Task.FromResult(response);
    }

    /// <summary>
    /// Parses codec string to VoiceCodec enum value.
    /// </summary>
    private static VoiceCodec ParseVoiceCodec(string codec)
    {
        return codec?.ToLowerInvariant() switch
        {
            "opus" => VoiceCodec.Opus,
            "g711" => VoiceCodec.G711,
            "g722" => VoiceCodec.G722,
            _ => VoiceCodec.Opus // Default
        };
    }
}
