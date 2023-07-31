using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The request model for service API calls to `/connect`.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn)]
public class ConnectRequest
{

}
