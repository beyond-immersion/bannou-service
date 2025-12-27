using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Plugin wrapper for AuthService enabling plugin-based discovery and lifecycle management.
/// </summary>
public class AuthServicePlugin : StandardServicePlugin<IAuthService>
{
    public override string PluginName => "auth";
    public override string DisplayName => "Auth Service";

    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Register HttpClient for OAuth provider communication (Discord, Google, Twitch, Steam)
        services.AddHttpClient();
        Logger?.LogDebug("Registered IHttpClientFactory for OAuth provider HTTP calls");

        // Register helper services for better testability and separation of concerns
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IOAuthProviderService, OAuthProviderService>();
        Logger?.LogDebug("Registered Auth helper services (SessionService, TokenService, OAuthProviderService)");

        Logger?.LogDebug("Service dependencies configured");
    }
}
