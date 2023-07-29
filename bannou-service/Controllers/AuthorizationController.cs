using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Auth APIs- backed by the Authorization service.
/// </summary>
[DaprController(template: "authorization", serviceType: typeof(AuthorizationService), Name = "authorization")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class AuthorizationController : BaseDaprController
{
    protected AuthorizationService Service { get; }

    public AuthorizationController(AuthorizationService service)
    {
        Service = service;
    }

    /// <summary>
    /// Shared endpoint to try authorizing a client connection.
    /// Will hand back a specific instance endpoint to use, for
    /// follow-up requests / exchanges.
    /// </summary>
    [DaprRoute("")]
    public async Task Authorize(HttpContext context) => await Task.CompletedTask;

    /// <summary>
    /// Instance endpoint, for any follow-up exchanges beyond the
    /// initial handshake, for authorizing a client connection.
    /// </summary>
    [DaprRoute($"{ServiceConstants.SERVICE_UUID_PLACEHOLDER}")]
    public async Task AuthorizeDirect(HttpContext context) => await Task.CompletedTask;
}
