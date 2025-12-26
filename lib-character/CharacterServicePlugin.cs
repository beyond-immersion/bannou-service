using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Character;

/// <summary>
/// Plugin wrapper for Character service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class CharacterServicePlugin : StandardServicePlugin<ICharacterService>
{
    public override string PluginName => "character";
    public override string DisplayName => "Character Service";
}
