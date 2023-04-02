using BeyondImmersion.BannouService.Controllers.Messages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Service component responsible for template definition handling.
/// </summary>
[DaprController(template: "testing", serviceType: typeof(TestingService), Name = "testing")]
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
    [DaprRoute("run-all")]
    public async Task<IActionResult> RunAll()
    {
        var result = await _service.RunAll();
        if (result)
            return Ok();

        return Conflict();
    }

    /// <summary>
    /// API to run all tests against all enabled services.
    /// </summary>
    [HttpGet]
    [HttpPost]
    [DaprRoute("run-enabled")]
    public async Task<IActionResult> RunEnabled()
    {
        var result = await _service.RunAllEnabled();
        if (result)
            return Ok();

        return Conflict();
    }

    /// <summary>
    /// API to run a given test by ID.
    /// </summary>
    [HttpGet]
    [DaprRoute("run/{id:string}")]
    public async Task<IActionResult> Run([FromRoute] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest();

        var result = await _service.Run(id: id);
        if (result)
            return Ok();

        return Conflict();
    }

    /// <summary>
    /// API to run a given test by ID.
    /// </summary>
    [HttpGet]
    [DaprRoute("run/{serviceName:string}/{id:string}")]
    public async Task<IActionResult> Run([FromRoute] string service, [FromRoute] string id)
    {
        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(service))
            return BadRequest();

        bool result;
        if (string.IsNullOrWhiteSpace(id))
            result = await _service.RunAllForService(service: service);
        else
            result = await _service.Run(service: service, id: id);

        if (result)
            return Ok();

        return Conflict();
    }

    /// <summary>
    /// API to run a given test by ID.
    /// </summary>
    [HttpPost]
    [DaprRoute("run")]
    public async Task<IActionResult> Run([FromBody]TestingRunTestRequest request)
    {
        if (request == null)
            return BadRequest();

        bool result;
        if (string.IsNullOrWhiteSpace(request.ID))
        {
            if (string.IsNullOrWhiteSpace(request.Service))
                return BadRequest();

            result = await _service.RunAllForService(service: request.Service);
        }
        else
            result = await _service.Run(service: request.Service, id: request.ID);

        if (result)
            return Ok();

        return Conflict();
    }
}
