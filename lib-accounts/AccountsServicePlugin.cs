using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Plugin wrapper for Accounts service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class AccountsServicePlugin : StandardServicePlugin<IAccountsService>
{
    public override string PluginName => "accounts";
    public override string DisplayName => "Accounts Service";
}
