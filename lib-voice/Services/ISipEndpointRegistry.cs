namespace BeyondImmersion.BannouService.Voice.Services;

/// <summary>
/// Service responsible for tracking SIP endpoint registrations within voice rooms.
/// Uses thread-safe collections for multi-instance safety (Tenet 4).
/// </summary>
public interface ISipEndpointRegistry
{
    /// <summary>
    /// Registers a participant's SIP endpoint in a voice room.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="accountId">The participant's account ID.</param>
    /// <param name="endpoint">The SIP endpoint information.</param>
    /// <param name="sessionId">WebSocket session ID for event delivery.</param>
    /// <param name="displayName">Optional display name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if registration successful, false if participant already registered.</returns>
    Task<bool> RegisterAsync(
        Guid roomId,
        Guid accountId,
        SipEndpoint endpoint,
        string? sessionId = null,
        string? displayName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters a participant from a voice room.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="accountId">The participant's account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The removed participant info, or null if not found.</returns>
    Task<ParticipantRegistration?> UnregisterAsync(
        Guid roomId,
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all participants in a voice room.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of participants in the room.</returns>
    Task<List<ParticipantRegistration>> GetRoomParticipantsAsync(
        Guid roomId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific participant's info.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="accountId">The participant's account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Participant info if found, null otherwise.</returns>
    Task<ParticipantRegistration?> GetParticipantAsync(
        Guid roomId,
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a participant's heartbeat timestamp to prevent TTL expiration.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="accountId">The participant's account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if updated, false if participant not found.</returns>
    Task<bool> UpdateHeartbeatAsync(
        Guid roomId,
        Guid accountId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a participant's SIP endpoint information (e.g., ICE candidate changes).
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="accountId">The participant's account ID.</param>
    /// <param name="newEndpoint">The updated endpoint information.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if updated, false if participant not found.</returns>
    Task<bool> UpdateEndpointAsync(
        Guid roomId,
        Guid accountId,
        SipEndpoint newEndpoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of participants in a room.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of participants.</returns>
    Task<int> GetParticipantCountAsync(
        Guid roomId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all participants from a room (used when room is deleted).
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of removed participants.</returns>
    Task<List<ParticipantRegistration>> ClearRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken = default);
}
