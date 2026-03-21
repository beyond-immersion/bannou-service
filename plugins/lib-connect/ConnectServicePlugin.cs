using BeyondImmersion.BannouService.Connect.Helpers;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Plugin wrapper for Connect service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class ConnectServicePlugin : StandardServicePlugin<IConnectService>
{
    public override string PluginName => "connect";
    public override string DisplayName => "Connect Service";

    // InterNodeBroadcastManager auto-registered via [BannouHelperService] (DependencyMode.Concrete)
}
