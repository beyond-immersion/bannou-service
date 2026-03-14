using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.SaveLoad.Helpers;
using BeyondImmersion.BannouService.SaveLoad.Processing;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.SaveLoad;

/// <summary>
/// Plugin wrapper for Save Load service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class SaveLoadServicePlugin : StandardServicePlugin<ISaveLoadService>
{
    public override string PluginName => "save-load";
    public override string DisplayName => "Save Load Service";

    /// <summary>
    /// Configure services for dependency injection.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register HttpClient factory for downloading save data from pre-signed URLs
        services.AddHttpClient();

        // Register background workers
        services.AddHostedService<SaveUploadWorker>();
        services.AddHostedService<CleanupService>();
    }
}
