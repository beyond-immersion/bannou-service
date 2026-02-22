using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Account;

/// <summary>
/// Internal data models for AccountService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>When to add models here:</b>
/// <list type="bullet">
///   <item>Storage models for state stores (different from API request/response types)</item>
///   <item>Cache entry structures</item>
///   <item>Internal DTOs for service-to-service communication not exposed in API</item>
///   <item>Helper records for intermediate processing</item>
/// </list>
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class AccountService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level below.
}

// ============================================================================
// INTERNAL DATA MODELS
// ============================================================================

/// <summary>
/// Account data model for lib-state storage.
/// Replaces Entity Framework entities.
/// </summary>
public class AccountModel
{
    /// <summary>Unique identifier for the account.</summary>
    public Guid AccountId { get; set; }

    /// <summary>Email address used for login and notifications. Null for OAuth/Steam accounts without email.</summary>
    public string? Email { get; set; }

    /// <summary>User-visible display name, optional.</summary>
    public string? DisplayName { get; set; }

    /// <summary>BCrypt hashed password for email/password authentication.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Whether the email address has been verified.</summary>
    public bool IsVerified { get; set; }

    /// <summary>User roles for permission checks (e.g., "admin", "user").</summary>
    public List<string> Roles { get; set; } = new List<string>();

    /// <summary>Custom metadata key-value pairs for the account.</summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>Whether multi-factor authentication is enabled for this account.</summary>
    public bool MfaEnabled { get; set; }

    /// <summary>Encrypted TOTP secret (AES-256-GCM ciphertext). Auth service encrypts/decrypts, Account stores opaque ciphertext.</summary>
    public string? MfaSecret { get; set; }

    /// <summary>BCrypt-hashed single-use recovery codes. Auth service generates/verifies, Account stores opaque hashes.</summary>
    public List<string>? MfaRecoveryCodes { get; set; }

    /// <summary>
    /// Unix epoch timestamp for account creation.
    /// Stored as long to avoid System.Text.Json DateTimeOffset serialization issues.
    /// </summary>
    public long CreatedAtUnix { get; set; }

    /// <summary>
    /// Unix epoch timestamp for last account update.
    /// Stored as long to avoid System.Text.Json DateTimeOffset serialization issues.
    /// </summary>
    public long UpdatedAtUnix { get; set; }

    /// <summary>
    /// Unix epoch timestamp for soft-deletion, null if not deleted.
    /// Stored as long to avoid System.Text.Json DateTimeOffset serialization issues.
    /// </summary>
    public long? DeletedAtUnix { get; set; }

    /// <summary>Computed property for code convenience - not serialized.</summary>
    [JsonIgnore]
    public DateTimeOffset CreatedAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(CreatedAtUnix);
        set => CreatedAtUnix = value.ToUnixTimeSeconds();
    }

    /// <summary>Computed property for code convenience - not serialized.</summary>
    [JsonIgnore]
    public DateTimeOffset UpdatedAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(UpdatedAtUnix);
        set => UpdatedAtUnix = value.ToUnixTimeSeconds();
    }

    /// <summary>Computed property for code convenience - not serialized.</summary>
    [JsonIgnore]
    public DateTimeOffset? DeletedAt
    {
        get => DeletedAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(DeletedAtUnix.Value) : null;
        set => DeletedAtUnix = value?.ToUnixTimeSeconds();
    }
}
