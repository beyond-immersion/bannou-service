using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Profile APIs- backed by the Profile service.
/// </summary>
[DaprController(template: "profile", serviceType: typeof(ProfileService), Name = "profile")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class ProfileController : BaseDaprController
{
    protected ProfileService Service { get; }

    public ProfileController(ProfileService service)
    {
        Service = service;
    }

    /// <summary>
    /// Create new player profile.
    /// </summary>
    [DaprRoute("create")]
    public async Task CreateProfile(HttpContext context) => await Task.CompletedTask;

    /// <summary>
    /// Update existing player profile.
    /// </summary>
    [DaprRoute("update")]
    public async Task UpdateProfile(HttpContext context) => await Task.CompletedTask;

    /// <summary>
    /// Destroy existing player profile.
    /// </summary>
    [DaprRoute("destroy")]
    public async Task DestroyProfile(HttpContext context) => await Task.CompletedTask;
}
