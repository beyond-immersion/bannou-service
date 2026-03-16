using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Localization;

/// <summary>
/// Plugin wrapper for Localization service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class LocalizationServicePlugin : StandardServicePlugin<ILocalizationService>
{
    public override string PluginName => "localization";
    public override string DisplayName => "Localization Service";
}
