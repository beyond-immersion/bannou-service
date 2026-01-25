namespace BeyondImmersion.BannouService.Voice.Services;

/// <summary>
/// Service responsible for scaled tier (SFU) voice room management.
/// Handles SIP credential generation and RTPEngine integration for large conferences.
/// </summary>
public interface IScaledTierCoordinator
{
    /// <summary>
    /// Checks if a new participant can join a scaled tier room.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="currentParticipantCount">Current number of participants.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if new participant can join.</returns>
    Task<bool> CanAcceptNewParticipantAsync(
        Guid roomId,
        int currentParticipantCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the maximum number of participants allowed in scaled mode.
    /// </summary>
    int GetScaledMaxParticipants();

    /// <summary>
    /// Generates SIP credentials for a participant joining a scaled tier room.
    /// Uses session-specific salt to avoid conflicts with multiple sessions from same account.
    /// </summary>
    /// <param name="sessionId">The WebSocket session ID (unique per connection).</param>
    /// <param name="roomId">The voice room ID.</param>
    /// <returns>SIP credentials for the participant.</returns>
    SipCredentials GenerateSipCredentials(Guid sessionId, Guid roomId);

    /// <summary>
    /// Builds the JoinVoiceRoomResponse for a participant joining a scaled tier room.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="sessionId">The WebSocket session ID.</param>
    /// <param name="rtpServerUri">The RTPEngine server URI.</param>
    /// <param name="codec">Codec to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Join voice room response for service-to-service calls.</returns>
    Task<JoinVoiceRoomResponse> BuildScaledConnectionInfoAsync(
        Guid roomId,
        Guid sessionId,
        string rtpServerUri,
        VoiceCodec codec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates an RTP server for a room upgrade from P2P to scaled.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The RTP server URI to use for this room.</returns>
    Task<string> AllocateRtpServerAsync(
        Guid roomId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases RTP server resources when a room is destroyed.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReleaseRtpServerAsync(
        Guid roomId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// SIP credentials for a scaled tier voice participant.
/// </summary>
public class SipCredentials
{
    /// <summary>
    /// SIP registrar hostname (e.g., sip.bannou.local).
    /// </summary>
    public string Registrar { get; init; } = string.Empty;

    /// <summary>
    /// SIP username for this participant (session-based).
    /// </summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Temporary SIP password (valid for session duration).
    /// </summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// SIP URI for the conference room.
    /// </summary>
    public string ConferenceUri { get; init; } = string.Empty;

    /// <summary>
    /// When credentials expire.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }
}
