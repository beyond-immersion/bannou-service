using BeyondImmersion.BannouService.Accounts.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Accounts.Tests;

/// <summary>
/// Integration tests for the account service.
/// Tests against the account/get endpoint.
/// </summary>
public static class GetAccountTests
{
    [ServiceTest(testName: "account/get", serviceType: typeof(IAccountService))]
    public static async Task<bool> Run(TestingService service)
        => await service.RunDelegates("account/get", new List<Func<TestingService, Task<bool>>>()
            {
            });

    private static bool ValidateGetResponse(string userID, GetAccountRequest requestModel, GetAccountResponse responseModel)
    {
        if (responseModel == null)
            return false;

        if (!responseModel.RequestIDs.TryGetValue("USER_ID", out var userIDValue) || !string.Equals(userID, userIDValue))
            return false;

        if (!string.Equals(requestModel.Username, responseModel.Username))
            return false;

        if (!string.Equals(requestModel.Email, responseModel.Email))
            return false;

        return true;
    }
}
