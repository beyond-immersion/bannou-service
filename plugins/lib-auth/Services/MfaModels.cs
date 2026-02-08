namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Temporary data for MFA challenge during login.
/// Stored in auth-statestore (Redis) with configurable TTL.
/// </summary>
internal class MfaChallengeData
{
    /// <summary>Account ID that passed password verification.</summary>
    public Guid AccountId { get; set; }

    /// <summary>When this challenge token expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Temporary data for MFA setup flow.
/// Stored in auth-statestore (Redis) until user confirms with TOTP code.
/// </summary>
public class MfaSetupData
{
    /// <summary>Account ID initiating MFA setup.</summary>
    public Guid AccountId { get; set; }

    /// <summary>AES-256-GCM encrypted TOTP secret.</summary>
    public string EncryptedSecret { get; set; } = string.Empty;

    /// <summary>BCrypt-hashed recovery codes.</summary>
    public List<string> HashedRecoveryCodes { get; set; } = new();

    /// <summary>When this setup token expires.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
