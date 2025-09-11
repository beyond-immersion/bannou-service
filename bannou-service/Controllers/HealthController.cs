using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Application healthcheck endpoint.
/// </summary>
[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Gets the current health status of the application.
    /// </summary>
    /// <returns>HTTP 200 if healthy, 503 if starting, 500 if error.</returns>
    [HttpGet]
    public Task<IActionResult> Get()
    {
        return Program.AppRunningState switch
        {
            AppRunningStates.Running => Task.FromResult<IActionResult>(Ok("Healthy")),
            AppRunningStates.Starting => Task.FromResult<IActionResult>(StatusCode(503, "Service unavailable")),
            _ => Task.FromResult<IActionResult>(StatusCode(500, "Service error")),
        };
    }

    /// <summary>
    /// Checks the current health status of the application via POST.
    /// </summary>
    /// <returns>HTTP 200 if healthy, 503 if starting, 500 if error.</returns>
    [HttpPost]
    public Task<IActionResult> Post()
    {
        return Program.AppRunningState switch
        {
            AppRunningStates.Running => Task.FromResult<IActionResult>(Ok("Healthy")),
            AppRunningStates.Starting => Task.FromResult<IActionResult>(StatusCode(503, "Service unavailable")),
            _ => Task.FromResult<IActionResult>(StatusCode(500, "Service error")),
        };
    }
}
