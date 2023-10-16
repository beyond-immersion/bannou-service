using BeyondImmersion.BannouService.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Accounts.Messages;

/// <summary>
/// The request model for service API calls to `/account/get`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class GetAccountRequest : ServiceRequest<GetAccountResponse>
{
    /// <summary>
    /// Email of account to retrieve.
    /// </summary>
    [JsonProperty("email", Required = Required.Always)]
    public string Email { get; set; }
}
