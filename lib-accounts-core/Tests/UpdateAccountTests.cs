using BeyondImmersion.BannouService.Accounts.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Accounts.Tests;

/// <summary>
/// Integration tests for the account service.
/// Tests against the account/update endpoint.
/// </summary>
public static class UpdateAccountTests
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

    [ServiceTest(testName: "account/update", serviceType: typeof(IAccountService))]
    public static async Task<bool> Run(TestingService service)
        => await service.RunDelegates("account/update", new List<Func<TestingService, Task<bool>>>()
            {
                UpdateAccount_Username,
                UpdateAccount_Email,
                UpdateAccount_GoogleID,
                UpdateAccount_SteamID
            });

    private static async Task<bool> UpdateAccount_Username(TestingService service)
    {
        service.ResetTestVars();
        if (TestAccountData == null)
            return false;

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            Username = $"Username_{Guid.NewGuid()}"
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> UpdateAccount_Email(TestingService service)
    {
        service.ResetTestVars();
        if (TestAccountData == null)
            return false;

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            Email = $"Email_{Guid.NewGuid()}@arcadia.com"
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> UpdateAccount_SteamID(TestingService service)
    {
        service.ResetTestVars();
        if (TestAccountData == null)
            return false;

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            SteamID = $"SteamID_{Guid.NewGuid()}"
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static async Task<bool> UpdateAccount_GoogleID(TestingService service)
    {
        service.ResetTestVars();
        if (TestAccountData == null)
            return false;

        var requestModel = new UpdateAccountRequest()
        {
            ID = TestAccountData.ID,
            GoogleID = $"Email_{Guid.NewGuid()}@arcadia.com"
        };

        if (!await requestModel.ExecutePostRequest("account", "update"))
            return false;

        if (!ValidateResponse(requestModel, requestModel.Response))
            return false;

        return true;
    }

    private static bool ValidateResponse(UpdateAccountRequest requestModel, UpdateAccountResponse? responseModel)
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

        if (requestModel.ID != responseModel.ID)
        {
            Program.Logger.Log(LogLevel.Error, $"Test response Id {responseModel.ID} does not match request Id {requestModel.ID}.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestModel.Username) && !string.Equals(requestModel.Username, responseModel.Username))
        {
            Program.Logger.Log(LogLevel.Error, $"Test response Username {responseModel.Username} does not match request Username {requestModel.Username}.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestModel.Email) && !string.Equals(requestModel.Email, responseModel.Email))
        {
            Program.Logger.Log(LogLevel.Error, $"Test response Email {responseModel.Email} does not match request Email {requestModel.Email}.");
            return false;
        }

        var googleIDs = responseModel.IdentityClaims?.Where(t => t.StartsWith("GoogleID:"))?.Select(t => t.Remove(0, "GoogleID:".Length));
        if (!string.IsNullOrWhiteSpace(requestModel.GoogleID) && !(googleIDs?.Contains(requestModel.GoogleID) ?? false))
        {
            Program.Logger.Log(LogLevel.Error, $"Test response GoogleIDs do not contain request SteamID {requestModel.GoogleID}.");
            return false;
        }

        var steamIDs = responseModel.IdentityClaims?.Where(t => t.StartsWith("SteamID:"))?.Select(t => t.Remove(0, "SteamID:".Length));
        if (!string.IsNullOrWhiteSpace(requestModel.SteamID) && !(steamIDs?.Contains(requestModel.SteamID) ?? false))
        {
            Program.Logger.Log(LogLevel.Error, $"Test response SteamIDs do not contain request SteamID {requestModel.SteamID}.");
            return false;
        }

        return true;
    }
}
