using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The request model for service API calls to `/authorization/token`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class AuthorizationGetTokenRequest
{

}
