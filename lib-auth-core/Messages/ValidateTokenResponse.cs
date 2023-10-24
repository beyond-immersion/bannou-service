using BeyondImmersion.BannouService.Controllers.Messages;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Authorization.Messages;

/// <summary>
/// The response model for service API calls to `/authorization/validate`.
/// </summary>
[Serializable]
public class ValidateTokenResponse : ServiceResponse<ValidateTokenRequest>
{
    [Serializable]
    public class ErrorData
    {
        [JsonInclude]
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonInclude]
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonInclude]
        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }

    [JsonInclude]
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonInclude]
    [JsonPropertyName("errors")]
    public ErrorData[]? Errors { get; set; }
}
