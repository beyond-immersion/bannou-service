using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Realm;

/// <summary>
/// Plugin wrapper for Realm service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class RealmServicePlugin : StandardServicePlugin<IRealmService>
{
    public override string PluginName => "realm";
    public override string DisplayName => "Realm Service";
}
