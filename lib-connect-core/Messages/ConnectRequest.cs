using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Connect.Messages;

/// <summary>
/// The request model for service API calls to `/connect`.
/// </summary>
[JsonObject]
public class ConnectRequest : ServiceRequest<ServiceResponse>
{
}
