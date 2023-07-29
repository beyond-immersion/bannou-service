using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Leaderboard APIs- backed by the Leaderboard service.
/// </summary>
[DaprController(template: "leaderboard", serviceType: typeof(LeaderboardService), Name = "leaderboard")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class LeaderboardController : BaseDaprController
{
    protected LeaderboardService Service { get; }

    public LeaderboardController(LeaderboardService service)
    {
        Service = service;
    }

    /// <summary>
    /// List available leaderboards.
    /// </summary>
    [DaprRoute("list")]
    public async Task ListLeaderboards(HttpContext context) => await Task.CompletedTask;

    /// <summary>
    /// Create new leaderboard.
    /// </summary>
    [DaprRoute("create")]
    public async Task CreateLeaderboard(HttpContext context) => await Task.CompletedTask;

    /// <summary>
    /// Update existing leaderboard.
    /// </summary>
    [DaprRoute("update")]
    public async Task UpdateLeaderboard(HttpContext context) => await Task.CompletedTask;

    /// <summary>
    /// Destroy existing leaderboard.
    /// </summary>
    [DaprRoute("destroy")]
    public async Task DestroyLeaderboard(HttpContext context) => await Task.CompletedTask;
}
