using BeyondImmersion.BannouService.Auth.Services;
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

    private IAuthService? _authService;
    private IServiceProvider? _serviceProvider;


    /// <summary>
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [DaprService] attributes
        // No need to register IAuthService and AuthService here

        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // No need to register AuthServiceConfiguration here

        // Register HttpClient for OAuth provider communication (Discord, Google, Twitch, Steam)
        services.AddHttpClient();
        Logger?.LogDebug("Registered IHttpClientFactory for OAuth provider HTTP calls");

        // Register helper services for better testability and separation of concerns
        // These are .NET DI services, not separate plugins
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IOAuthProviderService, OAuthProviderService>();
        Logger?.LogDebug("Registered Auth helper services (SessionService, TokenService, OAuthProviderService)");

        // Add any service-specific dependencies
        // The generated clients (IAccountsClient) should already be registered by AddAllBannouServiceClients()

        Logger?.LogDebug("Service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogDebug("Configuring application pipeline");

        // The generated AuthController should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogDebug("Application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting service");

        try
        {
            // Get service instance from DI container with proper scope handling
            using var scope = _serviceProvider?.CreateScope();
            _authService = scope?.ServiceProvider.GetService<IAuthService>();

            if (_authService == null)
            {
                Logger?.LogError("Failed to resolve IAuthService from DI container");
                return false;
            }

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_authService is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnStartAsync for Auth service");
                await daprService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("Service started");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_authService == null) return;

        Logger?.LogDebug("Service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_authService is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnRunningAsync for Auth service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_authService == null) return;

        Logger?.LogInformation("Shutting down service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_authService is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnShutdownAsync for Auth service");
                await daprService.OnShutdownAsync();
            }

            Logger?.LogInformation("Service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during shutdown");
        }
    }
}
