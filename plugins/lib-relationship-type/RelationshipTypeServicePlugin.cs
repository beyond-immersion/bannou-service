using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.RelationshipType;

/// <summary>
/// Plugin wrapper for RelationshipType service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class RelationshipTypeServicePlugin : StandardServicePlugin<IRelationshipTypeService>
{
    public override string PluginName => "relationship-type";
    public override string DisplayName => "RelationshipType Service";
}
