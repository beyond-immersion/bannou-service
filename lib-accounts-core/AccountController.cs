using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc.ModelBinding;

/// <summary>
/// Auth APIs- backed by the Account service.
/// </summary>
[DaprController(typeof(IAccountService))]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class AccountController : BeyondImmersion.BannouService.Controllers.BaseDaprController
{
    protected IAccountService Service { get; }
    protected ILogger Logger { get; }

    public AccountController(IAccountService service, ILogger<AccountController> logger)
    {
        Service = service;
        Logger = logger;
    }

    [HttpPost]
    [DaprRoute("get")]
    public async Task<IActionResult> GetAccount(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Disallow)] GetAccountRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return new BadRequestResult();

            AccountData? accountData = await Service.GetAccount(request.Email);
            if (accountData == null)
                return new NotFoundResult();

            var response = new GetAccountResponse()
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
