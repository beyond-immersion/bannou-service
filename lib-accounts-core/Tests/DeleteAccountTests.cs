using BeyondImmersion.BannouService.Accounts.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Accounts.Tests;

/// <summary>
/// Integration tests for the account service.
/// Tests against the account/delete endpoint.
/// </summary>
public static class DeleteAccountTests
{
    private static CreateAccountResponse? _testAccountResponse;
    private static CreateAccountResponse? TestAccountData
    {
        get
        {
            if (_testAccountResponse != null)
                return _testAccountResponse;

            try
            {
                var userID = Guid.NewGuid().ToString();
                var requestModel = new CreateAccountRequest()
                {
                    Email = $"Email_{userID}@arcadia.com",
                    Username = $"Username_{userID}",
                    Password = "SimpleReadablePassword",
                    SteamID = $"SteamID_{userID}",
                    SteamToken = $"SToken_{Guid.NewGuid()}",
                    GoogleID = $"Email_{userID}@arcadia.com",
                    GoogleToken = $"GToken_{Guid.NewGuid()}",
                    IdentityClaims = new() { $"Identity_{userID}" }
                };

                if (!requestModel.ExecutePostRequest("account", "create").Result)
                {
                    Program.Logger.Log(LogLevel.Error, "Failed to set up user account for account/get tests.");
                    return null;
                }

                _testAccountResponse = requestModel.Response;
                return _testAccountResponse;
            }
            catch (Exception exc)
            {
                Program.Logger.Log(LogLevel.Error, exc, "Failed to set up user account for account/get tests.");
            }

            return null;
        }
    }

    [ServiceTest(testName: "account/delete", serviceType: typeof(IAccountService))]
    public static async Task<bool> Run(TestingService service)
        => await service.RunDelegates("account/delete", new List<Func<TestingService, Task<bool>>>()
            {
                DeleteAccount,
                DeleteAccount_NotFound,
                DeleteAccount_Conflict
            });

    private static async Task<bool> DeleteAccount(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var requestModel = new DeleteAccountRequest()
        {
            ID = TestAccountData.ID
        };

        if (!await requestModel.ExecutePostRequest("account", "delete"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    /// <summary>
    /// Exactly the same as DeleteAccount, but because it runs after, the account
    /// will already be deleted, and doing so again should return a 'Conflict'
    /// status codee response.
    /// </summary>
    private static async Task<bool> DeleteAccount_Conflict(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        // ensure the time for leeway has passed in detecting account deletion
        await Task.Delay(TimeSpan.FromSeconds(5));

        var requestModel = new DeleteAccountRequest()
        {
            ID = TestAccountData.ID
        };

        if (await requestModel.ExecutePostRequest("account", "delete") || requestModel.Response?.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            Program.Logger.Log(LogLevel.Error, "Test response missing or response status not 'Conflict'.");
            return false;
        }

        return true;
    }

    private static async Task<bool> DeleteAccount_NotFound(TestingService service)
    {
        if (TestAccountData == null)
            return false;

        var requestModel = new DeleteAccountRequest()
        {
            ID = TestAccountData.ID + 1
        };

        if (await requestModel.ExecutePostRequest("account", "delete") || requestModel.Response?.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            Program.Logger.Log(LogLevel.Error, "Test response missing or response status not 'Not Found'.");
            return false;
        }

        return true;
    }

    private static bool ValidateResponse(DeleteAccountRequest requestModel, DeleteAccountResponse? responseModel)
    {
        if (responseModel == null)
        {
            Program.Logger.Log(LogLevel.Error, "Test response not received.");
            return false;
        }

        if (requestModel.RequestIDs != null && requestModel.RequestIDs.Any())
        {
            if (responseModel.RequestIDs == null || !responseModel.RequestIDs.Any())
            {
                Program.Logger.Log(LogLevel.Error, "Test response missing REQUEST_IDS through headers.");
                return false;
            }

            foreach (var requestKVP in requestModel.RequestIDs)
            {
                if (!string.Equals(responseModel.RequestIDs[requestKVP.Key], requestKVP.Value))
                {
                    Program.Logger.Log(LogLevel.Error, "Test response REQUEST_IDS have been transformed through headers.");
                    return false;
                }
            }
        }

        if (responseModel.DeletedAt == null)
        {
            Program.Logger.Log(LogLevel.Error, $"Test response removed_at time is missing.");
            return false;
        }

        return true;
    }
}
