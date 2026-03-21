using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Localization;

/// <summary>
/// Plugin wrapper for Localization service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class LocalizationServicePlugin : StandardServicePlugin<ILocalizationService>
{
    /// <inheritdoc />
    public override string PluginName => "localization";

    /// <inheritdoc />
    public override string DisplayName => "Localization Service";

    // LocalizationKeyValidator auto-registered via [BannouHelperService] (Interface mode, ILocalizationKeyValidator)
}
