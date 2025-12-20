using BeyondImmersion.BannouService.Voice.Clients;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.Voice.Services;

/// <summary>
/// Implementation of scaled tier coordinator for SFU-based voice conferencing.
/// Manages SIP credential generation and RTPEngine integration.
/// </summary>
public class ScaledTierCoordinator : IScaledTierCoordinator
{
    private readonly IKamailioClient _kamailioClient;
    private readonly IRtpEngineClient _rtpEngineClient;
    private readonly ILogger<ScaledTierCoordinator> _logger;
    private readonly VoiceServiceConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of the ScaledTierCoordinator.
    /// </summary>
    /// <param name="kamailioClient">Kamailio client for SIP control.</param>
    /// <param name="rtpEngineClient">RTPEngine client for media control.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Voice service configuration.</param>
    public ScaledTierCoordinator(
        IKamailioClient kamailioClient,
        IRtpEngineClient rtpEngineClient,
        ILogger<ScaledTierCoordinator> logger,
        VoiceServiceConfiguration configuration)
    {
        _kamailioClient = kamailioClient ?? throw new ArgumentNullException(nameof(kamailioClient));
        _rtpEngineClient = rtpEngineClient ?? throw new ArgumentNullException(nameof(rtpEngineClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <inheritdoc />
    public Task<bool> CanAcceptNewParticipantAsync(
        Guid roomId,
        int currentParticipantCount,
        CancellationToken cancellationToken = default)
    {
        var maxParticipants = GetScaledMaxParticipants();
        var canAccept = currentParticipantCount < maxParticipants;

        if (!canAccept)
        {
            _logger.LogWarning(
                "Room {RoomId} at scaled tier capacity ({Count}/{Max})",
                roomId, currentParticipantCount, maxParticipants);
        }

        return Task.FromResult(canAccept);
    }

    /// <inheritdoc />
    public int GetScaledMaxParticipants()
    {
        return _configuration.ScaledMaxParticipants > 0
            ? _configuration.ScaledMaxParticipants
            : 100; // Default fallback for scaled tier
    }

    /// <inheritdoc />
    public SipCredentials GenerateSipCredentials(string sessionId, Guid roomId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            throw new ArgumentException("Session ID cannot be null or empty", nameof(sessionId));
        }

        // Generate deterministic password using SHA256(sessionId:roomId:salt)
        // Using sessionId instead of accountId to support multiple sessions per account
        var salt = _configuration.SipPasswordSalt ?? "bannou-voice-default-salt";
        var input = $"{sessionId}:{roomId}:{salt}";
        var passwordHash = ComputeSha256Hash(input);

        // Username is the session ID (safe, not sensitive)
        var username = $"voice-{sessionId[..Math.Min(8, sessionId.Length)]}";

        // Conference URI based on room ID
        var sipDomain = _configuration.SipDomain ?? "voice.bannou";
        var conferenceUri = $"sip:room-{roomId}@{sipDomain}";

        // Credentials expire with session (default 24h)
        var expiresAt = DateTimeOffset.UtcNow.AddHours(24);

        _logger.LogDebug(
            "Generated SIP credentials for session {SessionId} in room {RoomId}",
            sessionId[..Math.Min(8, sessionId.Length)], roomId);

        return new SipCredentials
        {
            Registrar = $"sip:{_configuration.KamailioHost ?? "localhost"}:{_configuration.KamailioRpcPort}",
            Username = username,
            Password = passwordHash[..32], // Use first 32 chars for password
            ConferenceUri = conferenceUri,
            ExpiresAt = expiresAt
        };
    }

    /// <inheritdoc />
    public Task<JoinVoiceRoomResponse> BuildScaledConnectionInfoAsync(
        Guid roomId,
        string sessionId,
        string rtpServerUri,
        string codec,
        CancellationToken cancellationToken = default)
    {
        var credentials = GenerateSipCredentials(sessionId, roomId);

        var response = new JoinVoiceRoomResponse
        {
            Success = true,
            RoomId = roomId,
            Tier = VoiceTier.Scaled,
            Codec = ParseVoiceCodec(codec),
            Peers = new List<VoicePeer>(), // No peers in scaled mode - use RTPEngine
            RtpServerUri = rtpServerUri,
            StunServers = GetStunServers(),
            TierUpgradePending = false
            // Note: SIP credentials should be added to JoinVoiceRoomResponse schema
            // For now, they're available via GenerateSipCredentials
        };

        _logger.LogDebug(
            "Built scaled connection info for session {SessionId} in room {RoomId}",
            sessionId[..Math.Min(8, sessionId.Length)], roomId);

        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public async Task<string> AllocateRtpServerAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        // For now, we use a single RTPEngine instance
        // In production, this would select from a pool based on load
        var rtpHost = _configuration.RtpEngineHost ?? "localhost";
        var rtpPort = _configuration.RtpEnginePort > 0 ? _configuration.RtpEnginePort : 22222;

        // Verify RTPEngine is healthy
        var isHealthy = await _rtpEngineClient.IsHealthyAsync(cancellationToken);
        if (!isHealthy)
        {
            _logger.LogError("RTPEngine is not healthy, cannot allocate for room {RoomId}", roomId);
            throw new InvalidOperationException("RTPEngine is not available");
        }

        _logger.LogInformation("Allocated RTPEngine at {Host}:{Port} for room {RoomId}", rtpHost, rtpPort, roomId);

        return $"udp://{rtpHost}:{rtpPort}";
    }

    /// <inheritdoc />
    public async Task ReleaseRtpServerAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        // Query for any active streams for this room and clean them up
        // Fail-fast: RTP cleanup failures should propagate to caller for proper error handling
        var callId = $"room-{roomId}";
        var queryResult = await _rtpEngineClient.QueryAsync(callId, cancellationToken);

        if (queryResult.IsSuccess && queryResult.StreamCount > 0)
        {
            _logger.LogInformation(
                "Cleaning up {StreamCount} streams for room {RoomId}",
                queryResult.StreamCount, roomId);

            // Delete the session from RTPEngine
            await _rtpEngineClient.DeleteAsync(callId, "bannou", cancellationToken);
        }
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

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

    private List<string> GetStunServers()
    {
        var stunServers = _configuration.StunServers;
        if (string.IsNullOrWhiteSpace(stunServers))
        {
            return new List<string> { "stun:stun.l.google.com:19302" };
        }
        return stunServers
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();
    }
}
