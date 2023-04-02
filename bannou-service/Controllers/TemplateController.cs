using BeyondImmersion.BannouService.Controllers.Messages;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Service component responsible for template definition handling.
/// </summary>
[DaprController(template: "template", serviceType: typeof(TemplateService), Name = "template")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class TemplateController : BaseDaprController
{
    /// <summary>
    /// Dapr endpoint to get a specific template definition.
    /// </summary>
    [HttpGet]
    [DaprRoute("{id}")]
    public async Task<ActionResult> Get([FromRoute] TemplateGetRequest request)
    {
        await Task.CompletedTask;

        var response = request.CreateResponse();
        response.Code = 200;
        return Ok(response);
    }

    /// <summary>
    /// Dapr endpoint to list template definitions.
    /// </summary>
    [HttpGet]
    [DaprRoute("list")]
    public async Task List([FromBody] TemplateListRequest request)
    {
        await Task.CompletedTask;

    }

    /// <summary>
    /// Dapr endpoint to create new template definitions.
    /// </summary>
    [HttpPost]
    [DaprRoute("create")]
    public async Task Create([FromBody] TemplateCreateRequest request)
    {
        await Task.CompletedTask;

    }

    /// <summary>
    /// Dapr endpoint to update an existing template definition.
    /// </summary>
    [HttpPost]
    [DaprRoute("update")]
    public async Task Update([FromBody] TemplateUpdateRequest request)
    {
        await Task.CompletedTask;

    }

    /// <summary>
    /// Dapr endpoint to destroy an existing template definition.
    /// </summary>
    [HttpPost]
    [DaprRoute("destroy")]
    public async Task Destroy([FromBody] TemplateDestroyRequest request)
    {
        await Task.CompletedTask;

    }

    /// <summary>
    /// Dapr endpoint to destroy an existing template definition.
    /// </summary>
    [HttpPost]
    [DaprRoute("destroy-by-key")]
    public async Task DestroyByKey([FromBody] TemplateDestroyByKeyRequest request)
    {
        await Task.CompletedTask;

    }
}
