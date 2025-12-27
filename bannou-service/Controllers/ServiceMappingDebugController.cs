using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Debug controller for viewing service mapping state.
/// Provides endpoints to inspect current routing configuration.
/// </summary>
[ApiController]
[Route("service-mappings")]
public class ServiceMappingDebugController : ControllerBase
{
    private readonly IServiceAppMappingResolver _resolver;

    /// <inheritdoc/>
    public ServiceMappingDebugController(IServiceAppMappingResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>
    /// Debug endpoint to view current service mappings.
    /// </summary>
    [HttpGet("mappings")]
    public IActionResult GetCurrentMappings()
    {
        var mappings = _resolver.GetAllMappings();
        return Ok(new
        {
            DefaultAppId = "bannou",
            CurrentVersion = _resolver.CurrentVersion,
            CustomMappings = mappings,
            MappingCount = mappings.Count
        });
    }

    /// <summary>
    /// Simple health check endpoint to verify the controller is reachable.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            Status = "healthy",
            Controller = "ServiceMappingDebugController",
            CurrentVersion = _resolver.CurrentVersion
        });
    }
}
