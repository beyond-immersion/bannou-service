using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SendGrid;

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

        // Register email service based on configuration (default: console logging)
        services.AddSingleton<IEmailService>(sp =>
        {
            var config = sp.GetRequiredService<AuthServiceConfiguration>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            switch (config.EmailProvider)
            {
                case EmailProvider.Sendgrid:
                    {
                        if (string.IsNullOrWhiteSpace(config.SendGridApiKey))
                        {
                            throw new InvalidOperationException(
                                "AUTH_SENDGRID_API_KEY is required when EmailProvider is 'sendgrid'");
                        }

                        if (string.IsNullOrWhiteSpace(config.EmailFromAddress))
                        {
                            throw new InvalidOperationException(
                                "AUTH_EMAIL_FROM_ADDRESS is required when EmailProvider is 'sendgrid'");
                        }

                        ISendGridClient client = new SendGridClient(config.SendGridApiKey);
                        var from = new SendGrid.Helpers.Mail.EmailAddress(config.EmailFromAddress, config.EmailFromName);
                        return new SendGridEmailService(client, from,
                            loggerFactory.CreateLogger<SendGridEmailService>());
                    }
                case EmailProvider.Smtp:
                    {
                        if (string.IsNullOrWhiteSpace(config.SmtpHost))
                        {
                            throw new InvalidOperationException(
                                "AUTH_SMTP_HOST is required when EmailProvider is 'smtp'");
                        }

                        if (string.IsNullOrWhiteSpace(config.EmailFromAddress))
                        {
                            throw new InvalidOperationException(
                                "AUTH_EMAIL_FROM_ADDRESS is required when EmailProvider is 'smtp'");
                        }

                        // MailboxAddress requires non-null display name; coalesce satisfies constructor (empty if not configured)
                        var from = new MimeKit.MailboxAddress(config.EmailFromName ?? "", config.EmailFromAddress);
                        return new SmtpEmailService(
                            config.SmtpHost, config.SmtpPort,
                            config.SmtpUsername, config.SmtpPassword, config.SmtpUseSsl,
                            from, loggerFactory.CreateLogger<SmtpEmailService>());
                    }
                case EmailProvider.Ses:
                    {
                        if (string.IsNullOrWhiteSpace(config.SesAccessKeyId))
                        {
                            throw new InvalidOperationException(
                                "AUTH_SES_ACCESS_KEY_ID is required when EmailProvider is 'ses'");
                        }

                        if (string.IsNullOrWhiteSpace(config.SesSecretAccessKey))
                        {
                            throw new InvalidOperationException(
                                "AUTH_SES_SECRET_ACCESS_KEY is required when EmailProvider is 'ses'");
                        }

                        if (string.IsNullOrWhiteSpace(config.EmailFromAddress))
                        {
                            throw new InvalidOperationException(
                                "AUTH_EMAIL_FROM_ADDRESS is required when EmailProvider is 'ses'");
                        }

                        var credentials = new BasicAWSCredentials(config.SesAccessKeyId, config.SesSecretAccessKey);
                        var sesClient = new AmazonSimpleEmailServiceV2Client(
                            credentials, RegionEndpoint.GetBySystemName(config.SesRegion));
                        return new SesEmailService(sesClient, config.EmailFromAddress,
                            loggerFactory.CreateLogger<SesEmailService>());
                    }
                default:
                    return new ConsoleEmailService(loggerFactory.CreateLogger<ConsoleEmailService>());
            }
        });
        Logger?.LogDebug("Registered email service (provider: configuration-driven)");

        // Register helper services for better testability and separation of concerns
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IOAuthProviderService, OAuthProviderService>();
        services.AddScoped<IMfaService, MfaService>();
        Logger?.LogDebug("Registered Auth helper services (SessionService, TokenService, OAuthProviderService, MfaService)");

        Logger?.LogDebug("Service dependencies configured");
    }
}
