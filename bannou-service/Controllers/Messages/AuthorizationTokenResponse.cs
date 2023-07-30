using Newtonsoft.Json;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The response model for service API calls to `/authorization/token`.
/// Does not use JRPC, as it's exposed directly to clients.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn, ItemNullValueHandling = NullValueHandling.Ignore)]
public class AuthorizationTokenResponse
{
    [JsonObject(MemberSerialization = MemberSerialization.OptIn)]
    public class ErrorData
    {
        [JsonPropertyName("code")]
        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonPropertyName("message")]
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonPropertyName("type")]
        [JsonProperty("type")]
        public string Type { get; set; }
    }

    [JsonPropertyName("token")]
    [JsonProperty("token", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Token { get; set; }

    [JsonPropertyName("errors")]
    [JsonProperty("errors", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public ErrorData[] Errors { get; set; }
}
