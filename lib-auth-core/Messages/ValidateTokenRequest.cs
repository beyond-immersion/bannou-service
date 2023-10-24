using BeyondImmersion.BannouService.Controllers.Messages;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Authorization.Messages;

/// <summary>
/// The request model for service API calls to `/authorization/validate`.
/// </summary>
[Serializable]
public class ValidateTokenRequest : ServiceRequest<ValidateTokenResponse>
{
    [JsonInclude]
    [JsonPropertyName("token")]
    public string? Token { get; set; }
}
