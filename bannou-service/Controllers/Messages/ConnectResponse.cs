using Newtonsoft.Json;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Controllers.Messages;

/// <summary>
/// The response model for service API calls to `/connect`.
/// Does not use JRPC, as it's exposed directly to clients.
/// </summary>
[JsonObject(MemberSerialization = MemberSerialization.OptIn, ItemNullValueHandling = NullValueHandling.Ignore)]
public class ConnectResponse
{

}
