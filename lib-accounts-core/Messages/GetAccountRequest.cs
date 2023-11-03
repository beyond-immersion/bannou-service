using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Accounts.Messages;

/// <summary>
/// The request model for service API calls to `/account/get`.
/// </summary>
[JsonObject]
public class GetAccountRequest : ServiceRequest<GetAccountResponse>
{
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
