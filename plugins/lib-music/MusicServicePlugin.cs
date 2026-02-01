using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Music;

/// <summary>
/// Plugin wrapper for Music service enabling plugin-based discovery and lifecycle management.
/// MusicService is a pure computation service with no startup/shutdown lifecycle needs.
/// </summary>
public class MusicServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "music";
    public override string DisplayName => "Music Service";

    /// <summary>
    /// Configure services for dependency injection.
    /// </summary>
    /// <remarks>
    /// Service registration is handled centrally by PluginLoader based on [BannouService] attributes.
    /// Configuration registration is handled centrally based on [ServiceConfiguration] attributes.
    /// </remarks>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Music service dependencies configured (no additional registrations needed)");
    }

    /// <summary>
    /// Configure application pipeline.
    /// </summary>
    /// <remarks>
    /// The generated MusicController is discovered via standard ASP.NET Core controller discovery.
    /// </remarks>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Music service application pipeline configured");
    }

    /// <summary>
    /// Start the service.
    /// </summary>
    /// <remarks>
    /// MusicService is a pure computation service with no startup initialization required.
    /// All state (composition cache) is lazily initialized on first use via IStateStoreFactory.
    /// </remarks>
    protected override Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Music service started");
        return Task.FromResult(true);
    }

    /// <summary>
    /// Running phase - no ongoing work for this service.
    /// </summary>
    protected override Task OnRunningAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Shutdown the service - no cleanup required.
    /// </summary>
    protected override Task OnShutdownAsync()
    {
        Logger?.LogInformation("Music service shutdown complete");
        return Task.CompletedTask;
    }
}
