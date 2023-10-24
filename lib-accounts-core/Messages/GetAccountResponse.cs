using BeyondImmersion.BannouService.Controllers.Messages;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Accounts.Messages;

/// <summary>
/// The response model for service API calls to `/account/get`.
/// </summary>
[Serializable]
public class GetAccountResponse : ServiceResponse<GetAccountRequest>
{
    [JsonInclude]
    [JsonPropertyName("id")]
    public string? ID { get; set; }

    [JsonInclude]
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonInclude]
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonInclude]
    [JsonPropertyName("hashed_secret")]
    public string? HashedSecret { get; set; }

    [JsonInclude]
    [JsonPropertyName("secret_salt")]
    public string? SecretSalt { get; set; }

    [JsonInclude]
    [JsonPropertyName("role")]
    public string? Role { get; set; }
}
