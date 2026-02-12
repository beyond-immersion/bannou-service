using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Obligation;

/// <summary>
/// Plugin wrapper for Obligation service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class ObligationServicePlugin : StandardServicePlugin<IObligationService>
{
    public override string PluginName => "obligation";
    public override string DisplayName => "Obligation Service";
}
