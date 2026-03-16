using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.CharacterLifecycle;

/// <summary>
/// Plugin wrapper for CharacterLifecycle service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class CharacterLifecycleServicePlugin : StandardServicePlugin<ICharacterLifecycleService>
{
    public override string PluginName => "character-lifecycle";
    public override string DisplayName => "CharacterLifecycle Service";
}
