using BeyondImmersion.BannouService.Controllers.Messages;

namespace BeyondImmersion.BannouService.Connect.Messages;

/// <summary>
/// The response model for service API calls to `/connect`.
/// Does not use JRPC, as it's exposed directly to clients.
/// </summary>
[Serializable]
public class ConnectResponse : ServiceResponse<ConnectRequest>
{
}
