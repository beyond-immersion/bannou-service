using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Plugin wrapper for Escrow service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class EscrowServicePlugin : StandardServicePlugin<IEscrowService>
{
    public override string PluginName => "escrow";
    public override string DisplayName => "Escrow Service";
}
