using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Location;

/// <summary>
/// Plugin wrapper for Location service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class LocationServicePlugin : StandardServicePlugin<ILocationService>
{
    public override string PluginName => "location";
    public override string DisplayName => "Location Service";
}
