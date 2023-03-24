using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for leaderboard handling.
/// </summary>
[DaprService("leaderboard")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class LeaderboardService : IDaprService
{
    /// <summary>
    /// List available leaderboards.
    /// </summary>
    [ServiceRoute("/list")]
    public async Task ListLeaderboards(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Create new leaderboard.
    /// </summary>
    [ServiceRoute("/create")]
    public async Task CreateLeaderboard(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Update existing leaderboard.
    /// </summary>
    [ServiceRoute("/update")]
    public async Task UpdateLeaderboard(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Destroy existing leaderboard.
    /// </summary>
    [ServiceRoute("/destroy")]
    public async Task DestroyLeaderboard(HttpContext context)
    {
        await Task.CompletedTask;
    }
}
