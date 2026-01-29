using BeyondImmersion.BannouService.Plugins;

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
}
