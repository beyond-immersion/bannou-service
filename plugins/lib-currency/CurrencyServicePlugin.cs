using BeyondImmersion.BannouService.Currency.Services;
using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Currency;

/// <summary>
/// Plugin wrapper for Currency service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class CurrencyServicePlugin : StandardServicePlugin<ICurrencyService>
{
    public override string PluginName => "currency";
    public override string DisplayName => "Currency Service";

    /// <summary>
    /// Configure services for dependency injection.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register background task processing services
        services.AddHostedService<CurrencyAutogainTaskService>();
        services.AddHostedService<CurrencyExpirationTaskService>();
        services.AddHostedService<HoldExpirationTaskService>();
    }
}
