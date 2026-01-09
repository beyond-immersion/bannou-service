using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Relationship;

/// <summary>
/// Plugin wrapper for Relationship service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class RelationshipServicePlugin : StandardServicePlugin<IRelationshipService>
{
    public override string PluginName => "relationship";
    public override string DisplayName => "Relationship Service";
}
