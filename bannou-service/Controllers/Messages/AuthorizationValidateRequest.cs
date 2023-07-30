using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The request model for service API calls to `/authorization/authenticate`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AuthorizationValidateRequest
{
    [JsonPropertyName("token")]
    [JsonProperty("token", Required = Required.Always)]
    public string Token { get; set; }
}
