using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Analytics;

/// <summary>
/// Plugin wrapper for Analytics service enabling plugin-based discovery and lifecycle management.
/// Registers controller history cleanup background worker.
/// </summary>
public class AnalyticsServicePlugin : StandardServicePlugin<IAnalyticsService>
{
    public override string PluginName => "analytics";
    public override string DisplayName => "Analytics Service";

    /// <summary>
    /// Registers the controller history cleanup background worker.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddHostedService<ControllerHistoryCleanupWorker>();
    }
}
