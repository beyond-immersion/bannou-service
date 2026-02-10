using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Analytics;

/// <summary>
/// Plugin wrapper for Analytics service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class AnalyticsServicePlugin : StandardServicePlugin<IAnalyticsService>
{
    public override string PluginName => "analytics";
    public override string DisplayName => "Analytics Service";
}
