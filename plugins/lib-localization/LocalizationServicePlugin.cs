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

    /// <summary>
    /// Registers DI services including the ILocalizationKeyValidator for cross-layer validation.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register key validator as Singleton — discovered by L2+ via IEnumerable<ILocalizationKeyValidator>
        services.AddSingleton<ILocalizationKeyValidator, LocalizationKeyValidator>();
    }
}
