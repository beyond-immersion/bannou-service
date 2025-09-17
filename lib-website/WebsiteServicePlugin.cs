using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Website;

/// <summary>
/// Plugin wrapper for Website service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IDaprService implementation with the new Plugin system.
/// </summary>
public class WebsiteServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "website";
    public override string DisplayName => "Website Service";

    private WebsiteService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Validate that this plugin should be loaded based on environment configuration.
    /// </summary>
    protected override bool OnValidatePlugin()
    {
        var enabled = Environment.GetEnvironmentVariable("WEBSITE_SERVICE_ENABLED")?.ToLower();
        Logger?.LogDebug("🔍 Website service enabled check: {EnabledValue}", enabled);
        return enabled == "true";
    }

    /// <summary>
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("⏭️  Website service disabled, skipping service registration");
            return;
        }

        Logger?.LogInformation("🔧 Configuring Website service dependencies");

        // Register the service implementation (existing pattern from [DaprService] attribute)
        services.AddScoped<IWebsiteService, WebsiteService>();
        services.AddScoped<WebsiteService>();

        // Register generated configuration class
        services.AddScoped<WebsiteServiceConfiguration>();

        // Add any service-specific dependencies
        // The generated clients should already be registered by AddAllBannouServiceClients()

        Logger?.LogInformation("✅ Website service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("⏭️  Website service disabled, skipping application configuration");
            return;
        }

        Logger?.LogInformation("🔧 Configuring Website service application pipeline");

        // The generated WebsiteController should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("✅ Website service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        if (!OnValidatePlugin()) return true;

        Logger?.LogInformation("▶️  Starting Website service");

        try
        {
            // Get service instance from DI container
            _service = _serviceProvider?.GetService<WebsiteService>();

            if (_service == null)
            {
                Logger?.LogError("❌ Failed to resolve WebsiteService from DI container");
                return false;
            }

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnStartAsync for Website service");
                await daprService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("✅ Website service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "❌ Failed to start Website service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogDebug("🏃 Website service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnRunningAsync for Website service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "⚠️  Exception during Website service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogInformation("🛑 Shutting down Website service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnShutdownAsync for Website service");
                await daprService.OnShutdownAsync();
            }

            Logger?.LogInformation("✅ Website service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "⚠️  Exception during Website service shutdown");
        }
    }
}
