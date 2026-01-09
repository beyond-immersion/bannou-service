using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Website;

/// <summary>
/// Plugin wrapper for Website service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class WebsiteServicePlugin : StandardServicePlugin<IWebsiteService>
{
    public override string PluginName => "website";
    public override string DisplayName => "Website Service";
}
