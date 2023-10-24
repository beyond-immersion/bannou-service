using BeyondImmersion.BannouService.Controllers.Messages;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Accounts.Messages;

/// <summary>
/// The request model for service API calls to `/account/get`.
/// </summary>
[Serializable]
public class GetAccountRequest : ServiceRequest<GetAccountResponse>
{
    /// <summary>
    /// Email of account to retrieve.
    /// </summary>
    [JsonInclude]
    [JsonPropertyName("email")]
    public string? Email { get; set; }
}
