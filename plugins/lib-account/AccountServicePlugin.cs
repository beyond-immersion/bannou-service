using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Account;

/// <summary>
/// Plugin wrapper for Account service enabling plugin-based discovery and lifecycle management.
/// Registers the retention cleanup background worker for purging soft-deleted accounts.
/// </summary>
public class AccountServicePlugin : StandardServicePlugin<IAccountService>
{
    public override string PluginName => "account";
    public override string DisplayName => "Account Service";

    /// <summary>
    /// Registers background services for the Account plugin.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddHostedService<AccountRetentionWorker>();
    }
}
