using BeyondImmersion.BannouService.Controllers.Messages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Service component responsible for template definition handling.
/// </summary>
[DaprController(template: "testing", serviceType: typeof(TestingService))]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class TestingController : BaseDaprController
{
    private readonly TestingService _service;
    public TestingController(TestingService service)
    {
        _service = service;
    }

    /// <summary>
    /// API to run all tests.
    /// </summary>
    [HttpGet]
    [HttpPost]
    [DaprRoute("/run-all")]
    public async Task<IActionResult> RunAll()
    {
        await Task.CompletedTask;
        return new OkObjectResult(await _service.RunAll());
    }

    /// <summary>
    /// API to run all tests against all enabled services.
    /// </summary>
    [HttpGet]
    [HttpPost]
    [DaprRoute("/run-enabled")]
    public async Task<IActionResult> RunEnabled()
    {
        await Task.CompletedTask;
        return new OkObjectResult(await _service.RunAllEnabled());
    }

    /// <summary>
    /// API to run a given test by ID.
    /// </summary>
    [HttpGet]
    [DaprRoute("/run/{id}")]
    public async Task<IActionResult> Run([FromRoute] string id)
    {
        await Task.CompletedTask;
        return new OkObjectResult(await _service.Run(id, null));
    }

    /// <summary>
    /// API to run a given test by ID.
    /// </summary>
    [HttpPost]
    [DaprRoute("/run")]
    public async Task<IActionResult> Run([FromBody] TestingRunTestRequest request)
    {
        await Task.CompletedTask;
        return new OkObjectResult(await _service.Run(request.ID, request.Service));
    }

    /// <summary>
    /// API to run all tests against a given service.
    /// </summary>
    [HttpPost]
    [DaprRoute("/run-all-service")]
    public async Task<IActionResult> RunAllService([FromBody] TestingRunAllServiceTestsRequest request)
    {
        await Task.CompletedTask;
        return new OkObjectResult(await _service.RunAllForService(request.Service));
    }
}
