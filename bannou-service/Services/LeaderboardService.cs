namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for leaderboard handling.
/// </summary>
[DaprService("leaderboard")]
public class LeaderboardService : IDaprService
{
    /// <summary>
    /// List available leaderboards.
    /// </summary>
    public async Task ListLeaderboards() => await Task.CompletedTask;

    /// <summary>
    /// Create new leaderboard.
    /// </summary>
    public async Task CreateLeaderboard() => await Task.CompletedTask;

    /// <summary>
    /// Update existing leaderboard.
    /// </summary>
    public async Task UpdateLeaderboard() => await Task.CompletedTask;

    /// <summary>
    /// Destroy existing leaderboard.
    /// </summary>
    public async Task DestroyLeaderboard() => await Task.CompletedTask;
}
