namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Internal model for token revocation entries stored in Redis.
/// </summary>
public class TokenRevocationEntry
{
    /// <summary>
    /// JWT unique identifier.
    /// </summary>
    public string Jti { get; set; } = string.Empty;

    /// <summary>
    /// Account that owned the token.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// When the token was revoked (Unix timestamp).
    /// </summary>
    public long RevokedAtUnix { get; set; }

    /// <summary>
    /// When the revocation entry expires (Unix timestamp).
    /// </summary>
    public long ExpiresAtUnix { get; set; }

    /// <summary>
    /// Revocation reason for audit trail.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// When the token was revoked.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset RevokedAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(RevokedAtUnix);
        set => RevokedAtUnix = value.ToUnixTimeSeconds();
    }

    /// <summary>
    /// When the revocation entry expires.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset ExpiresAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(ExpiresAtUnix);
        set => ExpiresAtUnix = value.ToUnixTimeSeconds();
    }
}

/// <summary>
/// Internal model for account-level revocation entries stored in Redis.
/// </summary>
public class AccountRevocationEntry
{
    /// <summary>
    /// Account ID whose tokens are revoked.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// Reject tokens issued before this timestamp (Unix timestamp).
    /// </summary>
    public long IssuedBeforeUnix { get; set; }

    /// <summary>
    /// When the revocation was created (Unix timestamp).
    /// </summary>
    public long RevokedAtUnix { get; set; }

    /// <summary>
    /// Revocation reason for audit trail.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Reject tokens issued before this timestamp.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset IssuedBefore
    {
        get => DateTimeOffset.FromUnixTimeSeconds(IssuedBeforeUnix);
        set => IssuedBeforeUnix = value.ToUnixTimeSeconds();
    }

    /// <summary>
    /// When the revocation was created.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset RevokedAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(RevokedAtUnix);
        set => RevokedAtUnix = value.ToUnixTimeSeconds();
    }
}

/// <summary>
/// Entry for failed edge provider pushes awaiting retry.
/// </summary>
public class FailedEdgePushEntry
{
    /// <summary>
    /// Type of revocation ("token" or "account").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// JWT unique identifier (for token revocations).
    /// </summary>
    public string? Jti { get; set; }

    /// <summary>
    /// Account ID.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// TTL in seconds (for token revocations).
    /// </summary>
    public int? TtlSeconds { get; set; }

    /// <summary>
    /// Issued before timestamp (Unix, for account revocations).
    /// </summary>
    public long? IssuedBeforeUnix { get; set; }

    /// <summary>
    /// Provider ID that failed.
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Number of retry attempts.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// When the entry was created (Unix timestamp).
    /// </summary>
    public long CreatedAtUnix { get; set; }
}

/// <summary>
/// Message published to OpenResty/Redis channel for edge revocation.
/// </summary>
public class EdgeRevocationMessage
{
    /// <summary>
    /// Type of revocation ("token" or "account").
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// JWT unique identifier (for token revocations).
    /// </summary>
    public string? Jti { get; set; }

    /// <summary>
    /// Account ID.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// TTL in seconds (for token revocations).
    /// </summary>
    public int? TtlSeconds { get; set; }

    /// <summary>
    /// Issued before timestamp (ISO 8601, for account revocations).
    /// </summary>
    public DateTimeOffset? IssuedBefore { get; set; }

    /// <summary>
    /// When the revocation was created.
    /// </summary>
    public DateTimeOffset RevokedAt { get; set; }
}
