using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Accounts.Messages;

/// <summary>
/// The request model for service API calls to `/accounts/get`.
/// </summary>
[JsonObject]
public class GetAccountsRequest : ServiceRequest
{
    /// <summary>
    /// Internal GUID of account to retrieve.
    /// </summary>
    [JsonProperty("guid")]
    public string? GUID { get; set; }

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
    /// Identity claim of account to retrieve (OAUTH).
    /// </summary>
    [JsonProperty("identity_claim")]
    public string? IdentityClaim { get; set; }
}
