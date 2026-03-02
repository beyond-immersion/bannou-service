namespace BeyondImmersion.BannouService.Voice.Services;

/// <summary>
/// Internal state model for a voice room stored in state store.
/// This is the source of truth for room configuration; participants are stored separately.
/// </summary>
public class VoiceRoomData
{
    /// <summary>
    /// Unique voice room identifier.
    /// </summary>
    public Guid RoomId { get; set; }

    /// <summary>
    /// Associated session ID (any service can create a voice room for a session).
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// Current voice tier (p2p or scaled).
    /// </summary>
    public VoiceTier Tier { get; set; } = VoiceTier.P2P;

    /// <summary>
    /// Audio codec for the room.
    /// </summary>
    public VoiceCodec Codec { get; set; } = VoiceCodec.Opus;

    /// <summary>
    /// Maximum participants before tier upgrade.
    /// </summary>
    public int MaxParticipants { get; set; } = 6;

    /// <summary>
    /// When the room was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// RTP server URI (only set when in scaled tier).
    /// </summary>
    public string? RtpServerUri { get; set; }

    // Broadcast consent tracking

    /// <summary>
    /// Current broadcast consent state for this room.
    /// </summary>
    public BroadcastConsentState BroadcastState { get; set; } = BroadcastConsentState.Inactive;

    /// <summary>
    /// Session ID of the participant who requested broadcast consent.
    /// </summary>
    public Guid? BroadcastRequestedBy { get; set; }

    /// <summary>
    /// Session IDs that have consented to broadcasting.
    /// </summary>
    public HashSet<Guid> BroadcastConsentedSessions { get; set; } = new();

    /// <summary>
    /// When the broadcast consent request was initiated (for timeout tracking).
    /// </summary>
    public DateTimeOffset? BroadcastRequestedAt { get; set; }

    // Room mode

    /// <summary>
    /// If true, room auto-deletes when empty after grace period.
    /// </summary>
    public bool AutoCleanup { get; set; }

    /// <summary>
    /// Optional room password for access control.
    /// </summary>
    public string? Password { get; set; }

    // Participant tracking enhancement

    /// <summary>
    /// When the last participant left (for grace period tracking on auto-cleanup rooms).
    /// </summary>
    public DateTimeOffset? LastParticipantLeftAt { get; set; }
}

/// <summary>
/// Internal state model for a participant's registration in a voice room.
/// This is used for state storage, not API responses.
/// Keyed by SessionId to support multiple connections from the same account.
/// </summary>
public class ParticipantRegistration
{
    /// <summary>
    /// WebSocket session ID (unique participant identifier).
    /// This is the primary key for participant tracking.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// Display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// The participant's SIP endpoint for P2P connections.
    /// </summary>
    public SipEndpoint? Endpoint { get; set; }

    /// <summary>
    /// When the participant joined the room.
    /// </summary>
    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>
    /// Last heartbeat timestamp for TTL tracking.
    /// </summary>
    public DateTimeOffset LastHeartbeat { get; set; }

    /// <summary>
    /// Whether the participant is muted.
    /// </summary>
    public bool IsMuted { get; set; }

    /// <summary>
    /// Converts to API VoiceParticipant model.
    /// </summary>
    public VoiceParticipant ToVoiceParticipant()
    {
        return new VoiceParticipant
        {
            SessionId = SessionId,
            DisplayName = DisplayName,
            JoinedAt = JoinedAt,
            IsMuted = IsMuted
        };
    }

    /// <summary>
    /// Converts to VoicePeer model for P2P connections.
    /// </summary>
    public VoicePeer ToVoicePeer()
    {
        return new VoicePeer
        {
            SessionId = SessionId,
            DisplayName = DisplayName,
            SipEndpoint = Endpoint ?? new SipEndpoint()
        };
    }
}
