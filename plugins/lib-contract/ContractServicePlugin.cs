using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Contract;

/// <summary>
/// Plugin wrapper for Contract service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class ContractServicePlugin : StandardServicePlugin<IContractService>
{
    /// <summary>
    /// Gets the unique identifier for this plugin.
    /// </summary>
    public override string PluginName => "contract";

    /// <summary>
    /// Gets the human-readable display name for this plugin.
    /// </summary>
    public override string DisplayName => "Contract Service";

    /// <summary>
    /// Configures services for the Contract plugin.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Register the milestone expiration background service
        services.AddHostedService<ContractMilestoneExpirationService>();
    }
}
