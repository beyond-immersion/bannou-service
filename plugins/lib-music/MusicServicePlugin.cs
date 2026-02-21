using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Music;

/// <summary>
/// Plugin wrapper for Music service enabling plugin-based discovery and lifecycle management.
/// MusicService is a pure computation service with no startup/shutdown lifecycle needs.
/// </summary>
public class MusicServicePlugin : StandardServicePlugin<IMusicService>
{
    public override string PluginName => "music";
    public override string DisplayName => "Music Service";
}
