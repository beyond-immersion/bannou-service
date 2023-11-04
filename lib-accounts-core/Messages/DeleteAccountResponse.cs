using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Accounts.Messages;

/// <summary>
/// The response model for service API calls to `/account/delete`.
/// </summary>
[JsonObject]
public class DeleteAccountResponse : ServiceResponse
{
    /// <summary>
    /// The date/time the account was deleted (soft delete).
    /// </summary>
    [JsonProperty("deleted_at")]
    public DateTime? DeletedAt { get; set; }
}
