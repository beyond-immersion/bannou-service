using BeyondImmersion.BannouService.Controllers.Messages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Service component responsible for player profile handling.
/// </summary>
[DaprController(template: "profile", serviceType: typeof(ProfileService))]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class ProfileController : BaseDaprController
{
    /// <summary>
    /// Create new player profile.
    /// </summary>
    [DaprRoute("/create")]
    public async Task CreateProfile(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Update existing player profile.
    /// </summary>
    [DaprRoute("/update")]
    public async Task UpdateProfile(HttpContext context)
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// Destroy existing player profile.
    /// </summary>
    [DaprRoute("/destroy")]
    public async Task DestroyProfile(HttpContext context)
    {
        await Task.CompletedTask;
    }
}
