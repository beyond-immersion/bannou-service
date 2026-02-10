using BeyondImmersion.BannouService.Common;

namespace BeyondImmersion.BannouService.Chat;

/// <summary>
/// Internal data models for ChatService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models used exclusively by this service.
/// These are NOT exposed via the API and are NOT generated from schemas.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations.
/// </para>
/// </remarks>
public partial class ChatService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Internal storage model for room type definitions.
/// Stored in MySQL via chat-room-types state store.
/// </summary>
internal class ChatRoomTypeModel
{
    /// <summary>
    /// Unique room type code (e.g., "text", "guild_board").
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name for the room type.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the room type.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Game service scope (null for global built-in types).
    /// </summary>
    public Guid? GameServiceId { get; set; }

    /// <summary>
    /// Content format this room type accepts.
    /// </summary>
    public MessageFormat MessageFormat { get; set; }

    /// <summary>
    /// Validation rules for messages (null uses format defaults).
    /// </summary>
    public ValidatorConfigModel? ValidatorConfig { get; set; }

    /// <summary>
    /// Message storage mode (ephemeral or persistent).
    /// </summary>
    public PersistenceMode PersistenceMode { get; set; }

    /// <summary>
    /// Default participant limit (null uses service default).
    /// </summary>
    public int? DefaultMaxParticipants { get; set; }

    /// <summary>
    /// Message retention in days (null uses service default).
    /// </summary>
    public int? RetentionDays { get; set; }

    /// <summary>
    /// Default contract template for rooms of this type.
    /// </summary>
    public Guid? DefaultContractTemplateId { get; set; }

    /// <summary>
    /// Whether null senderId is allowed in messages.
    /// </summary>
    public bool AllowAnonymousSenders { get; set; }

    /// <summary>
    /// Messages per minute per participant (null uses service default).
    /// </summary>
    public int? RateLimitPerMinute { get; set; }

    /// <summary>
    /// Arbitrary JSON metadata for client rendering hints.
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Current lifecycle status.
    /// </summary>
    public RoomTypeStatus Status { get; set; }

    /// <summary>
    /// When the room type was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the room type was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Internal model for room type validation rules.
/// </summary>
internal class ValidatorConfigModel
{
    /// <summary>
    /// Maximum message length in characters for text and custom formats.
    /// </summary>
    public int? MaxMessageLength { get; set; }

    /// <summary>
    /// Regex pattern for content validation.
    /// </summary>
    public string? AllowedPattern { get; set; }

    /// <summary>
    /// Whitelist of allowed values (emoji codes, etc.).
    /// </summary>
    public List<string>? AllowedValues { get; set; }

    /// <summary>
    /// Required JSON fields for Custom format messages.
    /// </summary>
    public List<string>? RequiredFields { get; set; }

    /// <summary>
    /// Full JSON Schema string for complex Custom format validation.
    /// </summary>
    public string? JsonSchema { get; set; }
}

/// <summary>
/// Internal storage model for chat rooms.
/// Stored in MySQL via chat-rooms state store, cached in Redis via chat-rooms-cache.
/// </summary>
internal class ChatRoomModel
{
    /// <summary>
    /// Unique room identifier.
    /// </summary>
    public Guid RoomId { get; set; }

    /// <summary>
    /// Room type code determining message format and validation.
    /// </summary>
    public string RoomTypeCode { get; set; } = string.Empty;

    /// <summary>
    /// Connect session ID for companion rooms.
    /// </summary>
    public Guid? SessionId { get; set; }

    /// <summary>
    /// Governing contract ID for lifecycle management.
    /// </summary>
    public Guid? ContractId { get; set; }

    /// <summary>
    /// Human-readable room name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Current room lifecycle status.
    /// </summary>
    public ChatRoomStatus Status { get; set; }

    /// <summary>
    /// Maximum participant limit (null uses type/service default).
    /// </summary>
    public int? MaxParticipants { get; set; }

    /// <summary>
    /// Action when governing contract is fulfilled.
    /// </summary>
    public ContractRoomAction? ContractFulfilledAction { get; set; }

    /// <summary>
    /// Action when governing contract is breached.
    /// </summary>
    public ContractRoomAction? ContractBreachAction { get; set; }

    /// <summary>
    /// Action when governing contract is terminated.
    /// </summary>
    public ContractRoomAction? ContractTerminatedAction { get; set; }

    /// <summary>
    /// Action when governing contract expires.
    /// </summary>
    public ContractRoomAction? ContractExpiredAction { get; set; }

    /// <summary>
    /// Whether the room has been archived via Resource.
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// Arbitrary JSON metadata for client rendering hints.
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// When the room was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Last message or activity timestamp for idle room detection.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; set; }
}

/// <summary>
/// Internal storage model for room participants.
/// Stored in Redis via chat-participants state store, keyed by {roomId}:{sessionId}.
/// </summary>
internal class ChatParticipantModel
{
    /// <summary>
    /// Room the participant belongs to.
    /// </summary>
    public Guid RoomId { get; set; }

    /// <summary>
    /// Connect session ID of the participant.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// Opaque sender type (e.g., "session", "character", "system").
    /// </summary>
    public string? SenderType { get; set; }

    /// <summary>
    /// Sender entity ID (nullable for anonymous).
    /// </summary>
    public Guid? SenderId { get; set; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Participant role within the room.
    /// </summary>
    public ChatParticipantRole Role { get; set; }

    /// <summary>
    /// When the participant joined.
    /// </summary>
    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>
    /// When the participant last sent a message or performed an action.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>
    /// Whether the participant is currently muted.
    /// </summary>
    public bool IsMuted { get; set; }

    /// <summary>
    /// When the mute expires (null for permanent mute).
    /// </summary>
    public DateTimeOffset? MutedUntil { get; set; }
}

/// <summary>
/// Internal storage model for chat messages.
/// Stored in MySQL (persistent) or Redis (ephemeral) based on room type.
/// </summary>
internal class ChatMessageModel
{
    /// <summary>
    /// Unique message identifier.
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// Room the message belongs to.
    /// </summary>
    public Guid RoomId { get; set; }

    /// <summary>
    /// Opaque sender type.
    /// </summary>
    public string? SenderType { get; set; }

    /// <summary>
    /// Sender entity ID.
    /// </summary>
    public Guid? SenderId { get; set; }

    /// <summary>
    /// Sender display name at time of message.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// When the message was sent.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Message format type for content discrimination.
    /// </summary>
    public MessageFormat MessageFormat { get; set; }

    /// <summary>
    /// Text content (for Text format).
    /// </summary>
    public string? TextContent { get; set; }

    /// <summary>
    /// Sentiment category (for Sentiment format).
    /// </summary>
    public SentimentCategory? SentimentCategory { get; set; }

    /// <summary>
    /// Sentiment intensity 0.0-1.0 (for Sentiment format).
    /// </summary>
    public float? SentimentIntensity { get; set; }

    /// <summary>
    /// Emoji code (for Emoji format).
    /// </summary>
    public string? EmojiCode { get; set; }

    /// <summary>
    /// Custom emoji set reference (for Emoji format).
    /// </summary>
    public Guid? EmojiSetId { get; set; }

    /// <summary>
    /// JSON string payload (for Custom format).
    /// </summary>
    public string? CustomPayload { get; set; }

    /// <summary>
    /// Whether the message is pinned.
    /// </summary>
    public bool IsPinned { get; set; }
}

/// <summary>
/// Internal storage model for participant bans.
/// Stored in MySQL via chat-bans state store.
/// </summary>
internal class ChatBanModel
{
    /// <summary>
    /// Unique ban identifier.
    /// </summary>
    public Guid BanId { get; set; }

    /// <summary>
    /// Room the ban applies to.
    /// </summary>
    public Guid RoomId { get; set; }

    /// <summary>
    /// Connect session ID of the banned participant.
    /// </summary>
    public Guid TargetSessionId { get; set; }

    /// <summary>
    /// Connect session ID of the moderator who issued the ban.
    /// </summary>
    public Guid BannedBySessionId { get; set; }

    /// <summary>
    /// Optional reason for the ban.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// When the ban was issued.
    /// </summary>
    public DateTimeOffset BannedAt { get; set; }

    /// <summary>
    /// When the ban expires (null for permanent ban).
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
