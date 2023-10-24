using BeyondImmersion.BannouService.Controllers.Messages;

namespace BeyondImmersion.BannouService.Authorization.Messages;

/// <summary>
/// The request model for service API calls to `/authorization/token`.
/// </summary>
[Serializable]
public class GetTokenRequest : ServiceRequest<GetTokenResponse>
{
}
