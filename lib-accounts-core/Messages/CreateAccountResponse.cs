using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Accounts.Messages;

/// <summary>
/// The response model for service API calls to `/account/create`.
/// </summary>
[JsonObject]
public class CreateAccountResponse : ServiceResponse
{
    /// <summary>
    /// The unique internal-only account ID.
    /// </summary>
    [JsonProperty("id", Required = Required.Always)]
    public int ID { get; set; }

    /// <summary>
    /// The unique account username (can be null with OAUTH).
    /// </summary>
    [JsonProperty("username")]
    public string? Username { get; set; }

    /// <summary>
    /// The unique account email address (can be null with OAUTH).
    /// </summary>
    [JsonProperty("email")]
    public string? Email { get; set; }

    /// <summary>
    /// The account creation region.
    /// Used in routing + metrics.
    /// </summary>
    [JsonProperty("region")]
    public string? Region { get; set; }

    /// <summary>
    /// Whether the email address has been verified by the client
    /// yet.
    /// </summary>
    [JsonProperty("email_verified", Required = Required.Always)]
    public bool EmailVerified { get; set; }

    /// <summary>
    /// The last (unique) security token generated for the account.
    /// 
    /// Changes only to reset authorization, such as on password
    /// updates, changing emails, or being given a new role or
    /// application claim.
    /// </summary>
    [JsonProperty("security_token", Required = Required.Always)]
    public string? SecurityToken { get; set; }

    /// <summary>
    /// Whether the user has enabled 2-factor authentication.
    /// </summary>
    [JsonProperty("two_factor_enabled", Required = Required.Always)]
    public bool TwoFactorEnabled { get; set; }

    /// <summary>
    /// The time the account will cease to be locked out, if
    /// they've been temporarily blocked from accessing the
    /// system for some reason.
    /// </summary>
    [JsonProperty("lockout_end")]
    public DateTime? LockoutEnd { get; set; }

    /// <summary>
    /// The date/time of the last login.
    /// </summary>
    [JsonProperty("last_login_at", Required = Required.Always)]
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// The date/time the account was created.
    /// </summary>
    [JsonProperty("created_at", Required = Required.Always)]
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// The date/time the account was last updated.
    /// </summary>
    [JsonProperty("updated_at", Required = Required.Always)]
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// The date/time the account was removed (soft delete).
    /// </summary>
    [JsonProperty("removed_at")]
    public DateTime? RemovedAt { get; set; }

    /// <summary>
    /// Role claims are system-wide, affecting more or less
    /// everything else. Largely indicates whether someone
    /// is a staff member or not, and what their particular
    /// job is within the system.
    /// 
    /// Unlocks additional controls in UI, gives access to
    /// some APIs/tools, etc.
    /// 
    /// Examples:
    ///     Administrator
    ///     Developer
    ///     Operations
    ///     Support
    ///     Trusted
    ///     User
    /// </summary>
    [JsonProperty("role_claims")]
    public HashSet<string>? RoleClaims { get; set; }

    /// <summary>
    /// Claims granting access to applications/specific APIs.
    /// 
    /// These are largely determined by how the client creates
    /// the account and accesses the system.
    /// 
    /// Examples:
    ///     SiteAPI
    ///     ArcadiaGame
    ///     AppTools
    ///     GameTools
    /// </summary>
    [JsonProperty("app_claims")]
    public HashSet<string>? AppClaims { get; set; }

    /// <summary>
    /// Claims which grant or remove specific permissions within
    /// an application or API. Since the scope (no pun intended)
    /// of this usage is so broad, it can be convention to use
    /// key:value or some other kind-of prefix to avoid overlap.
    /// 
    /// Examples:
    ///     
    /// </summary>
    [JsonProperty("scope_claims")]
    public HashSet<string>? ScopeClaims { get; set; }

    /// <summary>
    /// Claims used for identity information, such as the
    /// password hash for username/password logins, steam ID
    /// and token, Google ID and token, etc.
    /// 
    /// Considered sensitive, and most of these values should
    /// never leave the system.
    /// 
    /// Examples:
    ///     SteamIDToken:mwem91jg54nd08n30n0qe9j135t35tw4
    ///     GoogleIDToken:m1093wf8f09n30f765h9j09k204y619k
    ///     PasswordHash:234p9u384y1283ty40183-0498133y14g
    /// </summary>
    [JsonProperty("identity_claims")]
    public HashSet<string>? IdentityClaims { get; set; }

    /// <summary>
    /// Claims used for general profile information, which may
    /// be of interest & shared with many applications.
    /// 
    /// Examples:
    ///     Age:38
    ///     Gender:Unicorn
    ///     PictureUri:link-to-photo/gravatar
    /// </summary>
    [JsonProperty("profile_claims")]
    public HashSet<string>? ProfileClaims { get; set; }

    public CreateAccountResponse() { }
}
