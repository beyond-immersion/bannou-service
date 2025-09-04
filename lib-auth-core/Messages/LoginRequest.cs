using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Authorization.Messages;

/// <summary>
/// The request model for service API calls to `/authorization/login`.
/// </summary>
[JsonObject]
public class LoginRequest : ApiRequest<LoginResponse>
{
}
