using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Service component responsible for login queue handling.
/// 
/// Can have the login service make an additional endpoint for `/login_{service_guid}` which would be unique to this service instance.
/// This would allow communication with a specific instance, in order to count down a queue position. This means the bulk of the work
/// for the queue could be done internally, with minimal traffic to the datastore only to relay metrics used to bring instances up and
/// down as demand dictates.
/// 
/// This would mean that a bad actor could spam a specific login server instance, but if that doesn't actually increase the internal
/// network traffic on each request, I'm not sure it matters.
/// </summary>
[DaprController(template: "login", serviceType: typeof(LoginService), Name = "login")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public sealed class LoginController : BaseDaprController
{
    protected LoginService Service { get; }

    public LoginController(LoginService service)
    {
        Service = service;
    }

    /// <summary>
    /// Shared login endpoint / first point of contact for clients.
    /// </summary>
    [DaprRoute("login")]
    public async Task Login(HttpContext context)
    {
        string? queueID = null;
        if (context.Request.Headers.TryGetValue("queue_id", out Microsoft.Extensions.Primitives.StringValues queueIDHeader))
            queueID = queueIDHeader.ToString();

        var response = await Service.EnqueueClient(queueID);

        await context.Response.StartAsync();
    }
}
