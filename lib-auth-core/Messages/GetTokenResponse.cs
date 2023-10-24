using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Authorization.Messages;

/// <summary>
/// The response model for service API calls to `/authorization/token`.
/// Does not use JRPC, as it's exposed directly to clients.
/// </summary>
[JsonObject]
public class GetTokenResponse : ServiceResponse<GetTokenRequest>
{
    [JsonObject]
    public class ErrorData
    {
        [JsonProperty("code")]
        public string? Code { get; set; }

        [JsonProperty("message")]
        public string? Message { get; set; }

        [JsonProperty("type")]
        public string? Type { get; set; }
    }

    [JsonProperty("token")]
    public string? Token { get; set; }

    [JsonProperty("errors")]
    public ErrorData[]? Errors { get; set; }
}
