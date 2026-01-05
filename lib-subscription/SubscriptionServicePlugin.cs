using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Subscription;

/// <summary>
/// Plugin wrapper for Subscription service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class SubscriptionServicePlugin : StandardServicePlugin<ISubscriptionService>
{
    public override string PluginName => "subscription";
    public override string DisplayName => "Subscription Service";

    public override void ConfigureServices(IServiceCollection services)
    {
        // Register the subscription expiration background service
        services.AddHostedService<SubscriptionExpirationService>();
    }
}
