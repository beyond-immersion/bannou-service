using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Plugin wrapper for GameSession service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IDaprService implementation with the new Plugin system.
/// </summary>
public class GameSessionServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "game-session";
    public override string DisplayName => "GameSession Service";

    private GameSessionService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Validate that this plugin should be loaded based on environment configuration.
    /// </summary>
    protected override bool OnValidatePlugin()
    {
        var enabled = Environment.GetEnvironmentVariable("GAME-SESSION_SERVICE_ENABLED")?.ToLower();
        Logger?.LogDebug("🔍 GameSession service enabled check: {EnabledValue}", enabled);
        return enabled == "true";
    }

    /// <summary>
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("⏭️  GameSession service disabled, skipping service registration");
            return;
        }

        Logger?.LogInformation("🔧 Configuring GameSession service dependencies");

        // Register the service implementation (existing pattern from [DaprService] attribute)
        services.AddScoped<IGameSessionService, GameSessionService>();
        services.AddScoped<GameSessionService>();

        // Register generated configuration class
        services.AddScoped<GameSessionServiceConfiguration>();

        // Add any service-specific dependencies
        // The generated clients should already be registered by AddAllBannouServiceClients()

        Logger?.LogInformation("✅ GameSession service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("⏭️  GameSession service disabled, skipping application configuration");
            return;
        }

        Logger?.LogInformation("🔧 Configuring GameSession service application pipeline");

        // The generated GameSessionController should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("✅ GameSession service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        if (!OnValidatePlugin()) return true;

        Logger?.LogInformation("▶️  Starting GameSession service");

        try
        {
            // Get service instance from DI container
            _service = _serviceProvider?.GetService<GameSessionService>();

            if (_service == null)
            {
                Logger?.LogError("❌ Failed to resolve GameSessionService from DI container");
                return false;
            }

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnStartAsync for GameSession service");
                await daprService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("✅ GameSession service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "❌ Failed to start GameSession service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogDebug("🏃 GameSession service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnRunningAsync for GameSession service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "⚠️  Exception during GameSession service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogInformation("🛑 Shutting down GameSession service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnShutdownAsync for GameSession service");
                await daprService.OnShutdownAsync();
            }

            Logger?.LogInformation("✅ GameSession service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "⚠️  Exception during GameSession service shutdown");
        }
    }
}
