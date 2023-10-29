using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Service component responsible for login/connection handling.
/// </summary>
[DaprService("connect", typeof(IConnectService))]
public sealed class ConnectService : IConnectService
{
    async Task IDaprService.OnStart(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }
}
