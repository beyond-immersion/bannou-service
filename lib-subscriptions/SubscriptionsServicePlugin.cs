using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Subscriptions;

/// <summary>
/// Plugin wrapper for Subscriptions service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class SubscriptionsServicePlugin : StandardServicePlugin<ISubscriptionsService>
{
    public override string PluginName => "subscriptions";
    public override string DisplayName => "Subscriptions Service";

    public override void ConfigureServices(IServiceCollection services)
    {
        // Register the subscription expiration background service
        services.AddHostedService<SubscriptionExpirationService>();
    }
}
