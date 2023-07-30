using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for login/connection handling.
/// </summary>
[DaprService("connect", lifetime: ServiceLifetime.Singleton)]
public sealed class ConnectService : IDaprService
{
    async Task<bool> IDaprService.OnBuild(IApplicationBuilder appBuilder)
    {
        await Task.CompletedTask;
        return true;
    }
}
