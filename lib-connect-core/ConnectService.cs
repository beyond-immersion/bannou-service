namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for login/connection handling.
/// </summary>
[DaprService("connect", lifetime: ServiceLifetime.Singleton)]
public sealed class ConnectService : IDaprService
{
    async Task IDaprService.OnStart()
    {
        await Task.CompletedTask;
    }
}
