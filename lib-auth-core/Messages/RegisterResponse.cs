using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Authorization.Messages;

/// <summary>
/// The response model for service API calls to `/authorization/register`.
/// </summary>
[JsonObject]
public class RegisterResponse : ServiceResponse
{
    /// <summary>
    /// The security token- can be used in place of a raw password
    /// for logins, until its reset. If 403 is returned from login,
    /// try the real password instead, or check for a new token to
    /// use from the portal.
    /// </summary>
    [JsonProperty("token")]
    public string? Token { get; set; }
}
