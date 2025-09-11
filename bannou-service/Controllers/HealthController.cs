using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Application healthcheck endpoint.
/// </summary>
[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
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
