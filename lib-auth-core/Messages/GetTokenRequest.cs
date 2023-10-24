using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Authorization.Messages;

/// <summary>
/// The request model for service API calls to `/authorization/token`.
/// </summary>
[JsonObject]
public class GetTokenRequest : ServiceRequest<GetTokenResponse>
{
}
