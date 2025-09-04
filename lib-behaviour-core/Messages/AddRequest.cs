using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Behaviour.Messages;

/// <summary>
/// The request model for service API calls to `/behaviour/add`.
/// </summary>
[JsonObject]
public class AddRequest : ApiRequest<AddResponse>
{

}
