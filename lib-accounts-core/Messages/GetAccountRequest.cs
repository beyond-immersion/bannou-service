using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Accounts.Messages;

/// <summary>
/// The request model for service API calls to `/account/get`.
/// 
/// Provide any identifier you have, and the lookup will be done
/// based on it. The internal ID is the only absolutely required
/// identifier for accounts to possess, so don't assume that all
/// accounts have a username or email associated.
/// </summary>
[JsonObject]
public class GetAccountRequest : ServiceRequest<GetAccountResponse>
{
    [JsonProperty("include_claims")]
    public bool IncludeClaims { get; set; }

    /// <summary>
    /// Internal ID of account to retrieve.
    /// </summary>
    [JsonProperty("id")]
    public int? ID { get; set; }

    /// <summary>
    /// Username of account to retrieve.
    /// </summary>
    [JsonProperty("username")]
    public string? Username { get; set; }

    /// <summary>
    /// Email of account to retrieve.
    /// </summary>
    [JsonProperty("email")]
    public string? Email { get; set; }

    /// <summary>
    /// Google ID of account to retrieve.
    /// </summary>
    [JsonProperty("google_id")]
    public string? GoogleID { get; set; }

    /// <summary>
    /// Steam ID of account to retrieve.
    /// </summary>
    [JsonProperty("steam_id")]
    public string? SteamID { get; set; }

    /// <summary>
    /// Identity claim of account to retrieve (OAUTH).
    /// </summary>
    [JsonProperty("identity_claim")]
    public string? IdentityClaim { get; set; }
}
