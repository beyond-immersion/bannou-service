using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BeyondImmersion.BannouService.Resource;

/// <summary>
/// Plugin wrapper for Resource service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class ResourceServicePlugin : StandardServicePlugin<IResourceService>
{
    public override string PluginName => "resource";
    public override string DisplayName => "Resource Service";

    /// <summary>
    /// Registers the TransactionRecoveryWorker background service.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddHostedService<TransactionRecoveryWorker>();
    }
}
