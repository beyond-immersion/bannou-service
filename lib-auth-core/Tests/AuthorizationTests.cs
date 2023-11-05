using BeyondImmersion.BannouService.Authorization.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Authorization.Tests;

/// <summary>
/// Integration tests for the authorization service.
/// </summary>
public static class AuthorizationTests
{
    [ServiceTest(testName: "authorization/register", serviceType: typeof(IAuthorizationService))]
    public static async Task<bool> Run(TestingService service)
        => await service.RunDelegates("authorization/register", new List<Func<TestingService, Task<bool>>>()
            {
                RegisterAccount_UsernamePassword,
                RegisterAccount_AllParameters
            });

    private static async Task<bool> RegisterAccount_UsernamePassword(TestingService service)
    {
        var userID = Guid.NewGuid().ToString();
        var requestModel = new RegisterRequest()
        {
            Email = null,
            Username = $"TestUser_{userID}",
            Password = "SimpleReadablePassword",
            RequestIDs = new()
            {
                ["USER_ID"] = userID
            }
        };

        if (!await requestModel.ExecutePostRequest("authorization", "register"))
            return false;

        return true;
    }

    private static async Task<bool> RegisterAccount_AllParameters(TestingService service)
    {
        var userID = Guid.NewGuid().ToString();
        var requestModel = new RegisterRequest()
        {
            Email = $"$Email_{userID}@arcadia.com",
            Username = $"TestUser_{userID}",
            Password = "SimpleReadablePassword",
            RequestIDs = new()
            {
                ["USER_ID"] = userID
            }
        };

        if (!await requestModel.ExecutePostRequest("authorization", "register"))
            return false;

        return true;
    }
}
