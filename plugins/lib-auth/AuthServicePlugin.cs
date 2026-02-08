using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
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
        // and CloudFlare edge revocation provider
        services.AddHttpClient();
        Logger?.LogDebug("Registered IHttpClientFactory for OAuth provider HTTP calls");

        // Register edge revocation providers
        services.AddScoped<IEdgeRevocationProvider, CloudflareEdgeProvider>();
        services.AddScoped<IEdgeRevocationProvider, OpenrestyEdgeProvider>();
        Logger?.LogDebug("Registered edge revocation providers (CloudFlare, OpenResty)");

        // Register edge revocation service (must be before SessionService which depends on it)
        services.AddScoped<IEdgeRevocationService, EdgeRevocationService>();
        Logger?.LogDebug("Registered EdgeRevocationService");

        // Register email service (default: console logging for development)
        // Replace ConsoleEmailService with a concrete provider (SendGrid, SES, etc.) for production
        services.AddSingleton<IEmailService, ConsoleEmailService>();
        Logger?.LogDebug("Registered ConsoleEmailService (replace with production provider for deployment)");

        // Register helper services for better testability and separation of concerns
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IOAuthProviderService, OAuthProviderService>();
        Logger?.LogDebug("Registered Auth helper services (SessionService, TokenService, OAuthProviderService)");

        Logger?.LogDebug("Service dependencies configured");
    }
}
