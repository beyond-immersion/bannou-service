using BeyondImmersion.BannouService.Services.Template.Data;
using BeyondImmersion.BannouService.Services.Template.Messages;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for template definition handling.
/// </summary>
[DaprService("template")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class TemplateService : Controller, IDaprService
{
    /// <summary>
    /// Dapr endpoint to get a specific template definition.
    /// </summary>
    [HttpGet]
    [ServiceRoute("/{id}")]
    public async Task<ActionResult> Get([FromRoute]TemplateGetRequest request)
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
    [ServiceRoute("/list")]
    public async Task List([FromBody]TemplateListRequest request)
    {
        await Task.CompletedTask;

    }

    /// <summary>
    /// Dapr endpoint to create new template definitions.
    /// </summary>
    [HttpPost]
    [ServiceRoute("/create")]
    public async Task Create([FromBody]TemplateCreateRequest request)
    {
        await Task.CompletedTask;

    }

    /// <summary>
    /// Dapr endpoint to update an existing template definition.
    /// </summary>
    [HttpPost]
    [ServiceRoute("/update")]
    public async Task Update([FromBody]TemplateUpdateRequest request)
    {
        await Task.CompletedTask;

    }

    /// <summary>
    /// Dapr endpoint to destroy an existing template definition.
    /// </summary>
    [HttpPost]
    [ServiceRoute("/destroy")]
    public async Task Destroy([FromBody]TemplateDestroyRequest request)
    {
        await Task.CompletedTask;

    }

    /// <summary>
    /// Dapr endpoint to destroy an existing template definition.
    /// </summary>
    [HttpPost]
    [ServiceRoute("/destroy-by-key")]
    public async Task DestroyByKey([FromBody]TemplateDestroyByKeyRequest request)
    {
        await Task.CompletedTask;

    }
}
