using BeyondImmersion.BannouService.Services;
using System.Net;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Service component responsible for login/connection handling.
/// </summary>
public interface IConnectService : IDaprService
{
    public sealed class ConnectResult
    {

    }

    (StatusCodes, ConnectResult?) Connect();
}
