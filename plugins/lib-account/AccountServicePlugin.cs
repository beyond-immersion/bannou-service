using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Account;

/// <summary>
/// Plugin wrapper for Account service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class AccountServicePlugin : StandardServicePlugin<IAccountService>
{
    public override string PluginName => "account";
    public override string DisplayName => "Account Service";
}
