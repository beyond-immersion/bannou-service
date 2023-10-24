using BeyondImmersion.BannouService.Testing.Messages;
using Microsoft.AspNetCore.Mvc;
using System.Collections;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Test APIs- backed by the Testing service.
/// </summary>
[DaprController(typeof(TestingService))]
public class TestingController : BaseDaprController
{
    protected TestingService Service { get; }

    public TestingController(TestingService service)
    {
        Service = service;
    }

    /// <summary>
    /// API to run all tests.
    /// </summary>
    [HttpGet]
    [HttpPost]
    [DaprRoute("run-all")]
    public async Task<IActionResult> RunAll()
    {
        var result = await Service.RunAll();
        return result ? Ok() : Conflict();
    }

    /// <summary>
    /// API to run all tests against all enabled services.
    /// </summary>
    [HttpGet]
    [HttpPost]
    [DaprRoute("run-enabled")]
    public async Task<IActionResult> RunEnabled()
    {
        var result = await Service.RunAllEnabled();
        return result ? Ok() : Conflict();
    }

    /// <summary>
    /// API to run a given test by ID.
    /// </summary>
    [HttpGet]
    [DaprRoute("run/{id}")]
    public async Task<IActionResult> Run([FromRoute] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest();

        var result = await Service.Run(id: id);
        return result ? Ok() : Conflict();
    }

    /// <summary>
    /// API to run a given test by ID.
    /// </summary>
    [HttpGet]
    [DaprRoute("run/{serviceName}/{id}")]
    public async Task<IActionResult> Run([FromRoute] string service, [FromRoute] string id)
    {
        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(service))
            return BadRequest();

        var result = string.IsNullOrWhiteSpace(id)
            ? await Service.RunAllForService(service: service)
            : await Service.Run(service: service, id: id);
        return result ? Ok() : Conflict();
    }

    /// <summary>
    /// API to run a given test by ID.
    /// </summary>
    [HttpPost]
    [DaprRoute("run")]
    public async Task<IActionResult> Run([FromBody] TestingRunTestRequest request)
    {
        if (request == null)
            return BadRequest();

        bool result;
        if (string.IsNullOrWhiteSpace(request.ID))
        {
            if (string.IsNullOrWhiteSpace(request.Service))
                return BadRequest();

            result = await Service.RunAllForService(service: request.Service);
        }
        else
        {
            result = await Service.Run(service: request.Service, id: request.ID);
        }

        return result ? Ok() : Conflict();
    }

    [HttpGet]
    [DaprRoute("dapr-test/{id}")]
    public async Task<IActionResult> TestGET_ID([FromRoute] string id)
    {
        await Task.CompletedTask;

        if (!string.IsNullOrWhiteSpace(id))
            Service.SetLastTestID(id);

        return Ok();
    }

    [HttpGet]
    [DaprRoute("dapr-test/{service}/{id}")]
    public async Task<IActionResult> TestGET_Service_ID([FromRoute] string service, [FromRoute] string id)
    {
        await Task.CompletedTask;

        if (!string.IsNullOrWhiteSpace(service))
            Service.SetLastTestService(service);

        if (!string.IsNullOrWhiteSpace(id))
            Service.SetLastTestID(id);

        return Ok();
    }

    [HttpPost]
    [DaprRoute("dapr-test")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<IActionResult> TestPOST_Model([FromBody] TestingRunTestRequest request)
    {
        await Task.CompletedTask;
        if (!ModelState.IsValid)
        {
            var errors = ModelState.SelectMany(x => x.Value?.Errors.Select(p => p.ErrorMessage)).ToList();
            foreach (var errorMsg in errors)
                Program.Logger.LogError($"MODEL VALIDATION ERROR! : {errorMsg}");

            return BadRequest(new { Errors = errors });
        }

        Service.SetLastPostRequest(request);

        return Ok();
    }
}
