using BeyondImmersion.BannouService.Messages;
using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Connect.Messages;

/// <summary>
/// The response model for service API calls to `/connect`.
/// Does not use JRPC, as it's exposed directly to clients.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn, ItemNullValueHandling = NullValueHandling.Ignore)]
public class ConnectResponse : ServiceResponse<ConnectRequest>
{

}
