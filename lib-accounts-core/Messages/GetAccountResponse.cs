using BeyondImmersion.BannouService.Messages;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Accounts.Messages;

/// <summary>
/// The response model for service API calls to `/account/get`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class GetAccountResponse : ServiceResponse<GetAccountRequest>
{
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string? ID { get; set; }

    [JsonPropertyName("email")]
    [JsonProperty("email")]
    public string? Email { get; set; }

    [JsonPropertyName("display_name")]
    [JsonProperty("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("hashed_secret")]
    [JsonProperty("hashed_secret")]
    public string? HashedSecret { get; set; }

    [JsonPropertyName("secret_salt")]
    [JsonProperty("secret_salt")]
    public string? SecretSalt { get; set; }

    [JsonPropertyName("role")]
    [JsonProperty("role")]
    public string? Role { get; set; }
}
