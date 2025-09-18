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
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {

        Logger?.LogInformation("🔧 Configuring Website service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [DaprService] attributes
        // No need to register IWebsiteService and WebsiteService here

        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // No need to register WebsiteServiceConfiguration here

        // Add any service-specific dependencies
        // The generated clients should already be registered by AddAllBannouServiceClients()

        Logger?.LogInformation("✅ Website service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {

        Logger?.LogInformation("🔧 Configuring Website service application pipeline");

        // The generated WebsiteController should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("✅ Website service application pipeline configured");
    }

    /// <summary>
    /// Start the service - uses centrally resolved service from PluginLoader.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Console.WriteLine("🔍 DEBUG: WebsiteServicePlugin.OnStartAsync() called");
        Logger?.LogInformation("▶️  Starting Website service");

        try
        {
            // Get centrally resolved service from PluginLoader
            var pluginLoader = BeyondImmersion.BannouService.Program.PluginLoader;
            if (pluginLoader != null)
            {
                var resolvedService = pluginLoader.GetResolvedService("website");
                if (resolvedService != null)
                {
                    _service = resolvedService as WebsiteService;
                    Logger?.LogInformation("✅ Using centrally resolved WebsiteService");

                    // Service lifecycle is now handled centrally by PluginLoader
                    // No need to call IDaprService methods here
                    Logger?.LogInformation("✅ Website service started successfully (centrally managed)");
                    return true;
                }
                else
                {
                    Logger?.LogWarning("⚠️  No centrally resolved service found for 'website' plugin");
                }
            }
            else
            {
                Logger?.LogWarning("⚠️  PluginLoader not available for central service resolution");
            }

            // Fallback to manual service resolution (legacy approach)
            Logger?.LogInformation("🔄 Falling back to manual service resolution");

            // Debug: Check if service provider is available
            if (_serviceProvider == null)
            {
                Console.WriteLine("❌ DEBUG: Service provider is null!");
                Logger?.LogError("❌ Service provider is null - ConfigureApplication may not have been called");
                return false;
            }

            Logger?.LogInformation("🔍 Service provider available, resolving WebsiteService...");

            // Get service instance from DI container
            _service = _serviceProvider?.GetService<WebsiteService>();

            if (_service == null)
            {
                Logger?.LogError("❌ Failed to resolve WebsiteService from DI container");
                return false;
            }

            Logger?.LogInformation("✅ WebsiteService resolved successfully (fallback)");

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
            Logger?.LogError(ex, "❌ Failed to start Website service - Exception details: {ExceptionType}: {Message}", ex.GetType().Name, ex.Message);
            Logger?.LogError("❌ Stack trace: {StackTrace}", ex.StackTrace);
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

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
        if (_service == null) return;

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
