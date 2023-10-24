using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Accounts.Messages;

/// <summary>
/// The response model for service API calls to `/account/get`.
/// </summary>
[JsonObject]
public class GetAccountResponse : ServiceResponse<GetAccountRequest>
{
    [JsonProperty("id")]
    public string? ID { get; set; }

    [JsonProperty("email")]
    public string? Email { get; set; }

    [JsonProperty("display_name")]
    public string? DisplayName { get; set; }

    [JsonProperty("hashed_secret")]
    public string? HashedSecret { get; set; }

    [JsonProperty("secret_salt")]
    public string? SecretSalt { get; set; }

    [JsonProperty("role")]
    public string? Role { get; set; }
}
