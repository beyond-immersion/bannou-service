using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Auth APIs- backed by the Account service.
/// </summary>
[DaprController(template: "account", serviceType: typeof(AccountService), Name = "account")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class AccountController : BaseDaprController
{
    protected AccountService Service { get; }

    public AccountController(AccountService service)
    {
        Service = service;
    }

    [HttpPost]
    [DaprRoute("get")]
    public async Task<IActionResult> GetAccount(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] AccountGetAccountRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return new BadRequestResult();

            AccountModel? accountData = await Service.GetAccount(request.Email);
            if (accountData == null)
                return new NotFoundResult();

            var response = new AccountGetAccountResponse()
            {
                ID = accountData.ID,
                Email = accountData.Email,
                HashedSecret = accountData.HashedSecret,
                SecretSalt = accountData.SecretSalt,
                DisplayName = accountData.DisplayName,
                Role = accountData.Role
            };
            return new OkObjectResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(GetAccount)}] endpoint on [{nameof(AccountController)}].");
            return new StatusCodeResult(500);
        }
    }
}
