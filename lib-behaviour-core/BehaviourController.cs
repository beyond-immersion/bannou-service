using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Behaviour.Messages;
using BeyondImmersion.BannouService.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mime;

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
            var registerResult = await Service.AddBehaviourTree();
            if (registerResult.StatusCode != StatusCodes.OK)
            {
                if (registerResult.StatusCode == StatusCodes.InternalServerError)
                    return StatusCodes.InternalServerError.ToActionResult();

                return StatusCodes.Forbidden.ToActionResult();
            }

            var response = request.CreateResponse();

            return StatusCodes.OK.ToActionResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(Add)}] endpoint on [{nameof(BehaviourController)}].");

            return StatusCodes.InternalServerError.ToActionResult();
        }
    }
}
