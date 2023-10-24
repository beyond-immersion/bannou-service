using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Testing;
using System.Diagnostics.CodeAnalysis;

namespace BeyondImmersion.BannouService.Authorization.Testing;

/// <summary>
/// Tests for the authorization service.
/// </summary>
public static class AuthorizationTests
{
    private const string AUTHORIZATION_SERVICE_NAME = "authorization";

    [TestingService.ServiceTest(testID: AUTHORIZATION_SERVICE_NAME, serviceType: typeof(IAuthorizationService))]
    [SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "Identifying failed integration tests")]
    public static async Task<bool> RunAuthorizationTests(TestingService service)
    {
        await Task.CompletedTask;
        Program.Logger?.Log(LogLevel.Trace, $"Running all [{AUTHORIZATION_SERVICE_NAME}] integration tests!");

        if (service == null)
        {
            Program.Logger?.Log(LogLevel.Error, "Testing service not found.");
            return false;
        }

        if (Program.DaprClient == null)
        {
            Program.Logger?.Log(LogLevel.Error, "Dapr client is not loaded.");
            return false;
        }

        var tests = new Func<TestingService, Task<bool>>[]
        {
            GetJWT_Success
        };

        foreach (var test in tests)
        {
            try
            {
                if (!await test.Invoke(service))
                {
                    Program.Logger?.Log(LogLevel.Error, $"Integration test [{test.Method.Name}] failed.");
                    return false;
                }
            }
            catch (Exception exc)
            {
                Program.Logger?.Log(LogLevel.Error, exc, $"An exception occurred running integration test [{test.Method.Name}].");
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> GetJWT_Success(TestingService service)
    {
        await Task.CompletedTask;
        return true;
    }
}
