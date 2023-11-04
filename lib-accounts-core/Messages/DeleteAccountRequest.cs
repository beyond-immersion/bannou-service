using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Accounts.Messages;

/// <summary>
/// The request model for service API calls to `/account/delete`.
/// 
/// Will not immediately delete the account, but will instead
/// set its status to having been "removed". The account can still
/// be restored until cleanup occurs.
/// </summary>
[JsonObject]
public class DeleteAccountRequest : ServiceRequest<DeleteAccountResponse>
{
    /// <summary>
    /// Internal ID of account to delete.
    /// </summary>
    [JsonProperty("id")]
    public int ID { get; set; }
}
