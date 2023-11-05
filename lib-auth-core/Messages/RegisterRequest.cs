using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Authorization.Messages;

/// <summary>
/// The request model for service API calls to `/authorization/register`.
/// </summary>
[JsonObject]
public class RegisterRequest : ServiceRequest<RegisterResponse>
{
    [JsonProperty("username", Required = Required.Always)]
    public string? Username { get; set; }

    [JsonProperty("password", Required = Required.Always)]
    public string? Password { get; set; }

    [JsonProperty("email")]
    public string? Email { get; set; }
}
