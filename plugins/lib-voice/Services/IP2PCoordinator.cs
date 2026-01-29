namespace BeyondImmersion.BannouService.Voice.Services;

/// <summary>
/// Service responsible for P2P mesh topology management and tier upgrade decisions.
/// Handles peer coordination for voice rooms in P2P mode.
/// </summary>
public interface IP2PCoordinator
{
    /// <summary>
    /// Gets the list of peers a new participant should connect to when joining a P2P room.
    /// Returns all existing participants' connection info for full mesh topology.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="joiningSessionId">The WebSocket session ID of the joining participant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of peer connection info for the new participant.</returns>
    Task<List<VoicePeer>> GetMeshPeersForNewJoinAsync(
        Guid roomId,
        Guid joiningSessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if a room should upgrade from P2P to scaled tier.
    /// Returns true if participant count exceeds the configured threshold.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="currentParticipantCount">Current number of participants.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if room should upgrade to scaled tier.</returns>
    Task<bool> ShouldUpgradeToScaledAsync(
        Guid roomId,
        int currentParticipantCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a new participant can join a P2P room without exceeding threshold.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="currentParticipantCount">Current number of participants.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if new participant can join, false if room is at capacity.</returns>
    Task<bool> CanAcceptNewParticipantAsync(
        Guid roomId,
        int currentParticipantCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the maximum number of participants allowed in P2P mode.
    /// </summary>
    int GetP2PMaxParticipants();

    /// <summary>
    /// Builds the JoinVoiceRoomResponse for a participant joining a P2P room.
    /// </summary>
    /// <param name="roomId">The voice room ID.</param>
    /// <param name="peers">List of peers to connect to.</param>
    /// <param name="defaultCodec">Default codec to use.</param>
    /// <param name="stunServers">List of STUN server URIs.</param>
    /// <param name="tierUpgradePending">Whether a tier upgrade is pending.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Join voice room response for service-to-service calls.</returns>
    Task<JoinVoiceRoomResponse> BuildP2PConnectionInfoAsync(
        Guid roomId,
        List<VoicePeer> peers,
        VoiceCodec defaultCodec,
        List<string> stunServers,
        bool tierUpgradePending = false,
        CancellationToken cancellationToken = default);
}
