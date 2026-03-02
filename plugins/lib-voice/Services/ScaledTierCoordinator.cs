using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Voice.Clients;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.Voice.Services;

/// <summary>
/// Implementation of scaled tier coordinator for SFU-based voice conferencing.
/// Manages SIP credential generation and RTPEngine integration.
/// </summary>
public class ScaledTierCoordinator : IScaledTierCoordinator
{
    private readonly IRtpEngineClient _rtpEngineClient;
    private readonly ILogger<ScaledTierCoordinator> _logger;
    private readonly IMessageBus _messageBus;
    private readonly VoiceServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Initializes a new instance of the ScaledTierCoordinator.
    /// </summary>
    /// <param name="rtpEngineClient">RTPEngine client for media control.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="messageBus">Message bus for error event publishing.</param>
    /// <param name="configuration">Voice service configuration.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public ScaledTierCoordinator(
        IRtpEngineClient rtpEngineClient,
        ILogger<ScaledTierCoordinator> logger,
        IMessageBus messageBus,
        VoiceServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _rtpEngineClient = rtpEngineClient;
        _logger = logger;
        _messageBus = messageBus;
        _configuration = configuration;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc />
    public async Task<bool> CanAcceptNewParticipantAsync(
        Guid roomId,
        int currentParticipantCount,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "ScaledTierCoordinator.CanAcceptNewParticipantAsync");
        await Task.CompletedTask;
        var maxParticipants = GetScaledMaxParticipants();
        var canAccept = currentParticipantCount < maxParticipants;

        if (!canAccept)
        {
            _logger.LogWarning(
                "Room {RoomId} at scaled tier capacity ({Count}/{Max})",
                roomId, currentParticipantCount, maxParticipants);
        }

        return canAccept;
    }

    /// <inheritdoc />
    public int GetScaledMaxParticipants()
    {
        return _configuration.ScaledMaxParticipants;
    }

    /// <inheritdoc />
    public ScaledTierSipCredentials GenerateSipCredentials(Guid sessionId, Guid roomId)
    {
        // Generate deterministic password using SHA256(sessionId:roomId:salt)
        // Using sessionId instead of accountId to support multiple sessions per account
        if (string.IsNullOrWhiteSpace(_configuration.SipPasswordSalt))
        {
            throw new InvalidOperationException(
                "VOICE_SIP_PASSWORD_SALT is required for SIP credential generation. " +
                "All service instances must share the same salt for voice credentials to work correctly.");
        }
        var salt = _configuration.SipPasswordSalt;
        var sessionIdStr = sessionId.ToString();
        var input = $"{sessionIdStr}:{roomId}:{salt}";
        var passwordHash = ComputeSha256Hash(input);

        // Username is the first 8 chars of session ID (safe, not sensitive)
        var username = $"voice-{sessionIdStr[..8]}";

        // Conference URI based on room ID (SipDomain has default in configuration)
        var conferenceUri = $"sip:room-{roomId}@{_configuration.SipDomain}";

        // Credentials expire based on configured expiration
        var expiresAt = DateTimeOffset.UtcNow.AddHours(_configuration.SipCredentialExpirationHours);

        _logger.LogDebug(
            "Generated SIP credentials for session {SessionId} in room {RoomId}",
            sessionIdStr[..8], roomId);

        return new ScaledTierSipCredentials
        {
            Registrar = $"sip:{_configuration.KamailioHost}:{_configuration.KamailioSipPort}",
            Username = username,
            Password = passwordHash[..32], // Use first 32 chars for password
            ConferenceUri = conferenceUri,
            ExpiresAt = expiresAt
        };
    }

    /// <inheritdoc />
    public async Task<JoinVoiceRoomResponse> BuildScaledConnectionInfoAsync(
        Guid roomId,
        Guid sessionId,
        string rtpServerUri,
        VoiceCodec codec,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "ScaledTierCoordinator.BuildScaledConnectionInfoAsync");
        await Task.CompletedTask;
        var credentials = GenerateSipCredentials(sessionId, roomId);

        var response = new JoinVoiceRoomResponse
        {
            RoomId = roomId,
            Tier = VoiceTier.Scaled,
            Codec = codec,
            Peers = new List<VoicePeer>(), // No peers in scaled mode - use RTPEngine
            RtpServerUri = rtpServerUri,
            StunServers = GetStunServers(),
            TierUpgradePending = false
            // Note: SIP credentials should be added to JoinVoiceRoomResponse schema
            // For now, they're available via GenerateSipCredentials
        };

        _logger.LogDebug(
            "Built scaled connection info for session {SessionId} in room {RoomId}",
            sessionId.ToString()[..8], roomId);

        return response;
    }

    /// <inheritdoc />
    public async Task<string> AllocateRtpServerAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "ScaledTierCoordinator.AllocateRtpServerAsync");
        // For now, we use a single RTPEngine instance
        // In production, this would select from a pool based on load
        var rtpHost = _configuration.RtpEngineHost;
        var rtpPort = _configuration.RtpEnginePort;

        // Verify RTPEngine is healthy
        var isHealthy = await _rtpEngineClient.IsHealthyAsync(cancellationToken);
        if (!isHealthy)
        {
            _logger.LogError("RTPEngine is not healthy, cannot allocate for room {RoomId}", roomId);
            await _messageBus.TryPublishErrorAsync(
                "voice",
                "AllocateRtpServer",
                "RtpEngineUnhealthy",
                "RTPEngine health check failed",
                dependency: "rtpengine",
                details: new { roomId },
                cancellationToken: cancellationToken);
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
        using var activity = _telemetryProvider.StartActivity("bannou.voice", "ScaledTierCoordinator.ReleaseRtpServerAsync");
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

    private List<string> GetStunServers()
    {
        return _configuration.StunServers
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();
    }
}
