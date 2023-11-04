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
        switch (Program.AppRunningState)
        {
            case AppRunningStates.Running:
                return Task.FromResult<IActionResult>(Ok("Healthy"));
            case AppRunningStates.Starting:
                return Task.FromResult<IActionResult>(StatusCode(503, "Service unavailable"));
            default:
            case AppRunningStates.Stopped:
                return Task.FromResult<IActionResult>(StatusCode(500, "Service error"));
        }
    }

    [HttpPost]
    public Task<IActionResult> Post()
    {
        switch (Program.AppRunningState)
        {
            case AppRunningStates.Running:
                return Task.FromResult<IActionResult>(Ok("Healthy"));
            case AppRunningStates.Starting:
                return Task.FromResult<IActionResult>(StatusCode(503, "Service unavailable"));
            default:
            case AppRunningStates.Stopped:
                return Task.FromResult<IActionResult>(StatusCode(500, "Service error"));
        }
    }
}
