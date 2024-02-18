using BeyondImmersion.BannouService.Controllers;
using Microsoft.AspNetCore.Mvc;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Behaviour.Messages;
using Microsoft.Extensions.Logging;
using System.Net.Mime;
using System.Net;

namespace BeyondImmersion.BannouService.Behaviour;

/// <summary>
/// Behaviour APIs- backed by the Behaviour service.
/// </summary>
[DaprController(typeof(IBehaviourService))]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public sealed class BehaviourController : BaseDaprController
{
    public IBehaviourService Service { get; }

    public BehaviourController(IBehaviourService service)
    {
        Service = service;
    }

    /// <summary>
    /// Adds new behaviour tree to system.
    /// </summary>
    [HttpPost]
    [DaprRoute("add")]
    public async Task<IActionResult> Add([FromBody] AddRequest request)
    {
        try
        {
            (HttpStatusCode, object?) registerResult = await Service.AddBehaviourTree();
            if (registerResult.Item1 != HttpStatusCode.OK)
            {
                if (registerResult.Item1 == HttpStatusCode.InternalServerError)
                    return StatusCodes.ServerError.ToActionResult();

                return StatusCodes.Unauthorized.ToActionResult();
            }

            var response = request.CreateResponse();

            return StatusCodes.Ok.ToActionResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(Add)}] endpoint on [{nameof(BehaviourController)}].");

            return StatusCodes.ServerError.ToActionResult();
        }
    }
}
