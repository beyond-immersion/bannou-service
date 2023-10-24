using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Authorization.Messages;

/// <summary>
/// The request model for service API calls to `/authorization/validate`.
/// </summary>
[JsonObject]
public class ValidateTokenRequest : ServiceRequest<ValidateTokenResponse>
{
    [JsonProperty("token")]
    public string? Token { get; set; }
}
