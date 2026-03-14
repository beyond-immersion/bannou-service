using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Mapping;

/// <summary>
/// Plugin wrapper for Mapping service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class MappingServicePlugin : StandardServicePlugin<IMappingService>
{
    public override string PluginName => "mapping";
    public override string DisplayName => "Mapping Service";
}
