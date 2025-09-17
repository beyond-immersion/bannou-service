using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Plugin wrapper for Behavior service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IDaprService implementation with the new Plugin system.
/// </summary>
public class BehaviorServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "behavior";
    public override string DisplayName => "Behavior Service";

    private BehaviorService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Validate that this plugin should be loaded based on environment configuration.
    /// </summary>
    protected override bool OnValidatePlugin()
    {
        var enabled = Environment.GetEnvironmentVariable("BEHAVIOR_SERVICE_ENABLED")?.ToLower();
        Logger?.LogDebug("🔍 Behavior service enabled check: {EnabledValue}", enabled);
        return enabled == "true";
    }

    /// <summary>
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("⏭️  Behavior service disabled, skipping service registration");
            return;
        }

        Logger?.LogInformation("🔧 Configuring Behavior service dependencies");

        // Register the service implementation (existing pattern from [DaprService] attribute)
        services.AddScoped<IBehaviorService, BehaviorService>();
        services.AddScoped<BehaviorService>();

        // Register generated configuration class
        services.AddScoped<BehaviorServiceConfiguration>();

        // Add any service-specific dependencies
        // The generated clients should already be registered by AddAllBannouServiceClients()

        Logger?.LogInformation("✅ Behavior service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("⏭️  Behavior service disabled, skipping application configuration");
            return;
        }

        Logger?.LogInformation("🔧 Configuring Behavior service application pipeline");

        // The generated BehaviorController should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("✅ Behavior service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        if (!OnValidatePlugin()) return true;

        Logger?.LogInformation("▶️  Starting Behavior service");

        try
        {
            // Get service instance from DI container
            _service = _serviceProvider?.GetService<BehaviorService>();

            if (_service == null)
            {
                Logger?.LogError("❌ Failed to resolve BehaviorService from DI container");
                return false;
            }

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnStartAsync for Behavior service");
                await daprService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("✅ Behavior service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "❌ Failed to start Behavior service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogDebug("🏃 Behavior service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnRunningAsync for Behavior service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "⚠️  Exception during Behavior service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogInformation("🛑 Shutting down Behavior service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnShutdownAsync for Behavior service");
                await daprService.OnShutdownAsync();
            }

            Logger?.LogInformation("✅ Behavior service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "⚠️  Exception during Behavior service shutdown");
        }
    }
}
