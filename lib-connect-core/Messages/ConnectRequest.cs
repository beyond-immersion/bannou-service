using BeyondImmersion.BannouService.Controllers.Messages;

namespace BeyondImmersion.BannouService.Connect.Messages;

/// <summary>
/// The request model for service API calls to `/connect`.
/// </summary>
[Serializable]
public class ConnectRequest : ServiceRequest<ServiceResponse>
{
}
