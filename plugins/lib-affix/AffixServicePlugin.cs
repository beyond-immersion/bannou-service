using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Affix;

/// <summary>
/// Plugin wrapper for Affix service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class AffixServicePlugin : StandardServicePlugin<IAffixService>
{
    public override string PluginName => "affix";
    public override string DisplayName => "Affix Service";
}
