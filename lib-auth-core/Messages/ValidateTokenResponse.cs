using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Authorization.Messages;

/// <summary>
/// The response model for service API calls to `/authorization/validate`.
/// </summary>
[JsonObject]
public class ValidateTokenResponse : ServiceResponse
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
