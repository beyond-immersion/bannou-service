using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Plugin wrapper for AuthService enabling plugin-based discovery and lifecycle management.
/// Bridges existing IDaprService implementation with the new Plugin system.
/// </summary>
public class AuthServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "auth";
    public override string DisplayName => "Auth Service";

    private AuthService? _authService;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Validate that this plugin should be loaded based on environment configuration.
    /// </summary>
    protected override bool OnValidatePlugin()
    {
        var enabled = Environment.GetEnvironmentVariable("AUTH_SERVICE_ENABLED")?.ToLower();
        Logger?.LogDebug("🔍 Auth service enabled check: {EnabledValue}", enabled);
        return enabled == "true";
    }

    /// <summary>
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("⏭️  Auth service disabled, skipping service registration");
            return;
        }

        Logger?.LogInformation("🔧 Configuring Auth service dependencies");

        // Register the service implementation (existing pattern from [DaprService] attribute)
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<AuthService>();

        // Register generated configuration class
        services.AddScoped<AuthServiceConfiguration>();

        // Add any service-specific dependencies
        // The generated clients (IAccountsClient) should already be registered by AddAllBannouServiceClients()

        Logger?.LogInformation("✅ Auth service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("⏭️  Auth service disabled, skipping application configuration");
            return;
        }

        Logger?.LogInformation("🔧 Configuring Auth service application pipeline");

        // The generated AuthController should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("✅ Auth service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        if (!OnValidatePlugin()) return true;

        Logger?.LogInformation("▶️  Starting Auth service");

        try
        {
            // Get service instance from DI container
            _authService = _serviceProvider?.GetService<AuthService>();

            if (_authService == null)
            {
                Logger?.LogError("❌ Failed to resolve AuthService from DI container");
                return false;
            }

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_authService is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnStartAsync for Auth service");
                await daprService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("✅ Auth service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "❌ Failed to start Auth service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (!OnValidatePlugin() || _authService == null) return;

        Logger?.LogDebug("🏃 Auth service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_authService is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnRunningAsync for Auth service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "⚠️  Exception during Auth service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (!OnValidatePlugin() || _authService == null) return;

        Logger?.LogInformation("🛑 Shutting down Auth service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_authService is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnShutdownAsync for Auth service");
                await daprService.OnShutdownAsync();
            }

            Logger?.LogInformation("✅ Auth service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "⚠️  Exception during Auth service shutdown");
        }
    }
}
