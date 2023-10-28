using BeyondImmersion.BannouService.Accounts.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace BeyondImmersion.BannouService.Accounts.Tests;

/// <summary>
/// Integration tests for the account service.
/// Tests against the account/get endpoint.
/// </summary>
[SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "Integration test logging benefits from being as specific as possible.")]
public static class GetAccountTests
{
    [ServiceTest(testName: "account/get", serviceType: typeof(IAccountService))]
    public static async Task<bool> Run(TestingService service)
    {
        await Task.CompletedTask;
        Program.Logger.Log(LogLevel.Trace, "Running `account/get` tests!");

        if (service == null)
        {
            Program.Logger.Log(LogLevel.Error, "Testing service not found.");
            return false;
        }

        if (Program.DaprClient == null)
        {
            Program.Logger.Log(LogLevel.Error, "Dapr client is not loaded.");
            return false;
        }

        var tests = new List<Func<TestingService, Task<bool>>>()
        {
        };

        foreach (var test in tests)
        {
            try
            {
                if (!await test.Invoke(service))
                {
                    Program.Logger.Log(LogLevel.Error, $"Integration test [{test.Method.Name}] failed.");
                    return false;
                }
            }
            catch (Dapr.Client.InvocationException exc)
            {
                var logMsg = $"Integration test [{test.Method.Name}] failed on invoking method {exc.MethodName} for app {exc.AppId}.";
                if (exc.Response != null)
                    logMsg += $"\nCode: {exc.Response.StatusCode}, Reason: {exc.Response.ReasonPhrase}";

                Program.Logger.Log(LogLevel.Error, exc, logMsg);
                return false;
            }
            catch (Exception exc)
            {
                Program.Logger.Log(LogLevel.Error, exc, $"An exception occurred running integration test [{test.Method.Name}].");
                return false;
            }
        }

        return true;
    }

    private static bool ValidateGetResponse(string userID, GetAccountRequest requestModel, GetAccountResponse responseModel)
    {
        if (responseModel == null)
            return false;

        if (!responseModel.RequestIDs.TryGetValue("USER_ID", out var userIDValue) || !string.Equals(userID, userIDValue))
            return false;

        if (string.IsNullOrWhiteSpace(responseModel.GUID))
            return false;

        if (!string.Equals(requestModel.Username, responseModel.Username))
            return false;

        if (!string.Equals(requestModel.Email, responseModel.Email))
            return false;

        return true;
    }
}
