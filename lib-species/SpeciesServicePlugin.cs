using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Species;

/// <summary>
/// Plugin wrapper for Species service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class SpeciesServicePlugin : StandardServicePlugin<ISpeciesService>
{
    public override string PluginName => "species";
    public override string DisplayName => "Species Service";
}
