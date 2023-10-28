using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Accounts.Messages;

/// <summary>
/// The request model for service API calls to `/account/create`.
/// </summary>
[JsonObject]
public class CreateAccountRequest : ServiceRequest
{
    /// <summary>
    /// Username- optional if using OAUTH.
    /// </summary>
    [JsonProperty("username")]
    public string? Username { get; set; }

    /// <summary>
    /// Password- optional if using OAUTH.
    /// </summary>
    [JsonProperty("password")]
    public string? Password { get; set; }

    /// <summary>
    /// Steam user ID, if using Steam OAUTH.
    /// </summary>
    [JsonProperty("steam_id")]
    public string? SteamID { get; set; }

    /// <summary>
    /// Steam user token, if using Steam OAUTH.
    /// </summary>
    [JsonProperty("steam_token")]
    public string? SteamToken { get; set; }

    /// <summary>
    /// Google user ID, if using Google OAUTH.
    /// </summary>
    [JsonProperty("google_id")]
    public string? GoogleID { get; set; }

    /// <summary>
    /// Google user token, if using Google OAUTH.
    /// </summary>
    [JsonProperty("google_token")]
    public string? GoogleToken { get; set; }

    /// <summary>
    /// Email address- optional.
    /// </summary>
    [JsonProperty("email")]
    public string? Email { get; set; }

    /// <summary>
    /// Whether the email should be considered verified.
    /// </summary>
    [JsonProperty("email_verified")]
    public bool EmailVerified { get; set; }

    /// <summary>
    /// Whether 2-factor authentication has been enabled.
    /// </summary>
    [JsonProperty("two_factor_enabled")]
    public bool TwoFactorEnabled { get; set; }

    /// <summary>
    /// Set initial roles. Applies system-wide- if you want
    /// to set permission to use an application, or within,
    /// use the other claim tables.
    /// 
    /// Examples:
    ///     Administrator
    ///     User
    /// </summary>
    [JsonProperty("role_claims")]
    public HashSet<string>? RoleClaims { get; set; }

    /// <summary>
    /// Set initial app claims. Apps are largely given access
    /// through purchase, download, or license keys, so some
    /// app claims might contain licenses prefixed with the
    /// application it applies to, etc.
    /// 
    /// Examples:
    ///     SiteAPI
    ///     ArcadiaGame
    ///     ArcadiaGame:LICENSE_!2109u13894y2t52390u-
    /// </summary>
    [JsonProperty("app_claims")]
    public HashSet<string>? AppClaims { get; set; }

    /// <summary>
    /// Claims to set specific permissions within an
    /// app.
    /// </summary>
    [JsonProperty("scope_claims")]
    public HashSet<string>? ScopeClaims { get; set; }

    /// <summary>
    /// Claims to add for identity (mostly OAUTH).
    /// 
    /// The response will come back with `HashedPassword:` in the claims,
    /// if you provide a password to create the account.
    /// 
    /// Examples:
    ///     STEAM_OAUTH_ENABLED
    ///     GOOGLE_OAUTH_ENABLED
    ///     SteamIDToken:mwem91jg54nd08n30n0qe9j135t35tw4
    ///     GoogleIDToken:m1093wf8f09n30f765h9j09k204y619k
    /// </summary>
    [JsonProperty("identity_claims")]
    public HashSet<string>? IdentityClaims { get; set; }

    /// <summary>
    /// Claims to add generic profile information for the account.
    /// 
    /// Examples:
    ///     Age:38
    ///     Gender:Unicorn
    ///     PictureUri:link-to-photo/gravatar
    /// </summary>
    [JsonProperty("profile_claims")]
    public HashSet<string>? ProfileClaims { get; set; }
}
