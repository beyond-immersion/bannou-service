using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Collection;

/// <summary>
/// Plugin wrapper for Collection service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class CollectionServicePlugin : StandardServicePlugin<ICollectionService>
{
    public override string PluginName => "collection";
    public override string DisplayName => "Collection Service";
}
