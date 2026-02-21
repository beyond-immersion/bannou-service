using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Leaderboard;

/// <summary>
/// Plugin wrapper for Leaderboard service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class LeaderboardServicePlugin : StandardServicePlugin<ILeaderboardService>
{
    public override string PluginName => "leaderboard";
    public override string DisplayName => "Leaderboard Service";
}
