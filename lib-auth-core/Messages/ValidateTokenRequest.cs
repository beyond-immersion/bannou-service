using BeyondImmersion.BannouService.Messages;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Authorization.Messages;

/// <summary>
/// The request model for service API calls to `/authorization/validate`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class ValidateTokenRequest : ServiceRequest<ValidateTokenResponse>
{
    [JsonPropertyName("token")]
    [JsonProperty("token", Required = Required.Always)]
    public string? Token { get; set; }
}
