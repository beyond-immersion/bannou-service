namespace BeyondImmersion.BannouService.Broadcast;

/// <summary>
/// Internal data models for BroadcastService state stores.
/// These are NOT exposed via the API — they represent the shapes stored in Redis/MySQL.
/// </summary>
public partial class BroadcastService
{
    // Partial class declaration to signal model ownership
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Platform link record stored in broadcast-platforms (MySQL).
/// Represents a linked streaming platform account with encrypted OAuth tokens.
/// </summary>
internal class PlatformLinkModel
{
    /// <summary>Unique link identifier</summary>
    public Guid LinkId { get; set; }

    /// <summary>Account that owns this link</summary>
    public Guid AccountId { get; set; }

    /// <summary>Streaming platform type</summary>
    public PlatformType Platform { get; set; }

    /// <summary>Platform display name (from OAuth profile or user-provided)</summary>
    public string? DisplayName { get; set; }

    /// <summary>Encrypted OAuth access token (null for Custom RTMP)</summary>
    public string? EncryptedAccessToken { get; set; }

    /// <summary>Encrypted OAuth refresh token (null for Custom RTMP)</summary>
    public string? EncryptedRefreshToken { get; set; }

    /// <summary>OAuth token expiry (null for Custom RTMP)</summary>
    public DateTimeOffset? TokenExpiresAt { get; set; }

    /// <summary>RTMP URL for Custom platform (encrypted for storage)</summary>
    public string? EncryptedRtmpUrl { get; set; }

    /// <summary>When the platform was linked</summary>
    public DateTimeOffset LinkedAt { get; set; }

    /// <summary>When this record was created</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this record was last updated</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Active platform session stored in broadcast-sessions (Redis).
/// Tracks a live streaming session on a platform.
/// </summary>
internal class PlatformSessionModel
{
    /// <summary>Unique platform session identifier</summary>
    public Guid PlatformSessionId { get; set; }

    /// <summary>Reference to the platform link</summary>
    public Guid LinkId { get; set; }

    /// <summary>Account that owns this session</summary>
    public Guid AccountId { get; set; }

    /// <summary>Platform type (denormalized from link)</summary>
    public PlatformType Platform { get; set; }

    /// <summary>Platform-specific stream identifier</summary>
    public string? PlatformStreamId { get; set; }

    /// <summary>Current viewer count</summary>
    public int ViewerCount { get; set; }

    /// <summary>Peak viewer count during this session</summary>
    public int PeakViewerCount { get; set; }

    /// <summary>When the session started</summary>
    public DateTimeOffset StartTime { get; set; }

    /// <summary>Associated in-game stream session (opaque GUID, no validation against lib-showtime)</summary>
    public Guid? StreamSessionId { get; set; }

    /// <summary>Current session state</summary>
    public PlatformSessionState State { get; set; }

    /// <summary>When the session ended (null if active)</summary>
    public DateTimeOffset? EndedAt { get; set; }
}

/// <summary>
/// Broadcast output stored in broadcast-outputs (Redis).
/// Represents an active FFmpeg broadcast process.
/// </summary>
internal class BroadcastOutputModel
{
    /// <summary>Unique broadcast identifier</summary>
    public Guid BroadcastId { get; set; }

    /// <summary>Source type (Camera, GameAudio, VoiceRoom)</summary>
    public BroadcastSourceType SourceType { get; set; }

    /// <summary>Source identifier (cameraId, roomId, etc.)</summary>
    public string? SourceId { get; set; }

    /// <summary>Encrypted RTMP destination URL</summary>
    public string EncryptedRtmpUrl { get; set; } = string.Empty;

    /// <summary>Masked RTMP URL for responses (stream key hidden)</summary>
    public string MaskedRtmpUrl { get; set; } = string.Empty;

    /// <summary>Instance that owns the FFmpeg process</summary>
    public string OwningInstanceId { get; set; } = string.Empty;

    /// <summary>Current broadcast state</summary>
    public BroadcastState State { get; set; }

    /// <summary>Current video source (after fallback cascade)</summary>
    public string? CurrentVideoSource { get; set; }

    /// <summary>When the broadcast started</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Current health status</summary>
    public BroadcastHealth Health { get; set; }

    /// <summary>Account that initiated this broadcast</summary>
    public Guid? InitiatorAccountId { get; set; }

    /// <summary>Fallback stream URL</summary>
    public string? FallbackStreamUrl { get; set; }

    /// <summary>Fallback image URL</summary>
    public string? FallbackImageUrl { get; set; }

    /// <summary>Background video URL</summary>
    public string? BackgroundVideoUrl { get; set; }
}

/// <summary>
/// Camera source stored in broadcast-cameras (Redis) with TTL heartbeat.
/// </summary>
internal class CameraSourceModel
{
    /// <summary>Camera identifier</summary>
    public string CameraId { get; set; } = string.Empty;

    /// <summary>RTMP input URL for this camera</summary>
    public string RtmpInputUrl { get; set; } = string.Empty;

    /// <summary>Video resolution (e.g., "1920x1080")</summary>
    public string? Resolution { get; set; }

    /// <summary>Video codec</summary>
    public string? Codec { get; set; }

    /// <summary>Last heartbeat timestamp</summary>
    public DateTimeOffset HeartbeatAt { get; set; }
}

/// <summary>
/// Buffered sentiment entry stored in broadcast-sentiment-buffer (Redis).
/// Awaits batch publication by SentimentBatchPublisher.
/// </summary>
internal class BufferedSentimentEntry
{
    /// <summary>Platform session this entry belongs to</summary>
    public Guid PlatformSessionId { get; set; }

    /// <summary>Sequence number for ordering</summary>
    public long Sequence { get; set; }

    /// <summary>Sentiment category</summary>
    public SentimentCategory Category { get; set; }

    /// <summary>Sentiment intensity (0.0 to 1.0)</summary>
    public float Intensity { get; set; }

    /// <summary>Anonymous tracking ID for this viewer</summary>
    public Guid? TrackingId { get; set; }

    /// <summary>Viewer type classification</summary>
    public TrackedViewerType? ViewerType { get; set; }

    /// <summary>When this entry was created</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
