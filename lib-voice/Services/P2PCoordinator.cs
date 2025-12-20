using BeyondImmersion.BannouService.GameSession;
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
    public async Task<List<VoicePeerInfo>> GetMeshPeersForNewJoinAsync(
        Guid roomId,
        Guid joiningAccountId,
        CancellationToken cancellationToken = default)
    {
        var participants = await _endpointRegistry.GetRoomParticipantsAsync(roomId, cancellationToken);

        // Convert participants to VoicePeerInfo, excluding the joining participant
        var peers = participants
            .Where(p => p.AccountId != joiningAccountId)
            .Select(p => new VoicePeerInfo
            {
                AccountId = p.AccountId,
                DisplayName = p.DisplayName,
                SdpOffer = p.Endpoint?.SdpOffer ?? string.Empty,
                IceCandidates = p.Endpoint?.IceCandidates?.ToList()
            })
            .ToList();

        _logger.LogDebug(
            "Built peer list for {JoiningAccountId} in room {RoomId}: {PeerCount} peers",
            joiningAccountId, roomId, peers.Count);

        return peers;
    }

    /// <inheritdoc />
    public Task<bool> ShouldUpgradeToScaledAsync(
        Guid roomId,
        int currentParticipantCount,
        CancellationToken cancellationToken = default)
    {
        var maxP2P = GetP2PMaxParticipants();
        var shouldUpgrade = currentParticipantCount >= maxP2P;

        if (shouldUpgrade)
        {
            _logger.LogInformation(
                "Room {RoomId} reached P2P threshold ({Count}/{Max}), should upgrade to scaled tier",
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
        // In Phase 1, scaled tier is not enabled, so we reject if at capacity
        var maxP2P = GetP2PMaxParticipants();
        var scaledEnabled = _configuration.ScaledTierEnabled;

        if (scaledEnabled)
        {
            // When scaled tier is enabled, always accept (will upgrade)
            return Task.FromResult(true);
        }

        // P2P only mode: reject if at capacity
        var canAccept = currentParticipantCount < maxP2P;

        if (!canAccept)
        {
            _logger.LogWarning(
                "Room {RoomId} at P2P capacity ({Count}/{Max}), scaled tier disabled - rejecting participant",
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
    public Task<VoiceConnectionInfo> BuildP2PConnectionInfoAsync(
        Guid roomId,
        List<VoicePeerInfo> peers,
        string defaultCodec,
        List<string> stunServers,
        CancellationToken cancellationToken = default)
    {
        var connectionInfo = new VoiceConnectionInfo
        {
            RoomId = roomId,
            Tier = VoiceConnectionInfoTier.P2p,
            Codec = ParseCodec(defaultCodec),
            Peers = peers,
            RtpServerUri = null, // P2P mode, no RTP server
            StunServers = stunServers
        };

        return Task.FromResult(connectionInfo);
    }

    /// <summary>
    /// Parses codec string to enum value.
    /// </summary>
    private static VoiceConnectionInfoCodec ParseCodec(string codec)
    {
        return codec?.ToLowerInvariant() switch
        {
            "opus" => VoiceConnectionInfoCodec.Opus,
            "g711" => VoiceConnectionInfoCodec.G711,
            "g722" => VoiceConnectionInfoCodec.G722,
            _ => VoiceConnectionInfoCodec.Opus // Default
        };
    }
}
