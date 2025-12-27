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
    public async Task<IActionResult> Get()
    {
        await Task.CompletedTask; // Satisfy async requirement for sync method
        return Program.AppRunningState switch
        {
            AppRunningStates.Running => Ok("Healthy"),
            AppRunningStates.Starting => StatusCode(503, "Service unavailable"),
            _ => StatusCode(500, "Service error"),
        };
    }

    /// <summary>
    /// Checks the current health status of the application via POST.
    /// </summary>
    /// <returns>HTTP 200 if healthy, 503 if starting, 500 if error.</returns>
    [HttpPost]
    public async Task<IActionResult> Post()
    {
        await Task.CompletedTask; // Satisfy async requirement for sync method
        return Program.AppRunningState switch
        {
            AppRunningStates.Running => Ok("Healthy"),
            AppRunningStates.Starting => StatusCode(503, "Service unavailable"),
            _ => StatusCode(500, "Service error"),
        };
    }
}
