using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The request model for service API calls to `/account/get`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AccountGetAccountRequest : ServiceRequest<AccountGetAccountResponse>
{
    /// <summary>
    /// Email of account to retrieve.
    /// </summary>
    [JsonProperty("email", Required = Required.Always)]
    public string Email { get; set; }
}
