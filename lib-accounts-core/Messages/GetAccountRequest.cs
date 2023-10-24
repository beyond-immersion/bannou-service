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
    /// Email of account to retrieve.
    /// </summary>
    [JsonProperty("email")]
    public string? Email { get; set; }
}
