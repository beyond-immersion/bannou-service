using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for login handling.
/// </summary>
[DaprService("login", lifetime: ServiceLifetime.Singleton)]
public sealed class LoginService : IDaprService
{
    bool IDaprService.OnLoad()
    {
        return true;
    }

    void IDaprService.OnExit()
    {
    }
}
