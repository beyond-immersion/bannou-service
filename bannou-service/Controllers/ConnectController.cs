using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Login APIs- backed by the Login service.
/// </summary>
[DaprController(template: "connect", serviceType: typeof(ConnectService), Name = "connect")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public sealed class ConnectController : BaseDaprController
{
    public ConnectService Service { get; }

    public ConnectController(ConnectService service)
    {
        Service = service;
    }
}
