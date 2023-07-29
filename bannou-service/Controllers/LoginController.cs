using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;
using System.Net.Mime;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Login APIs- backed by the Login service.
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
}
