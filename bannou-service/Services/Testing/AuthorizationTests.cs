using BeyondImmersion.BannouService.Controllers.Messages;

namespace BeyondImmersion.BannouService.Services.Testing;

/// <summary>
/// Tests for the authorization service.
/// </summary>
public static class AuthorizationTests
{
    private const int TEST_WAIT_TIME_MS = 200;

    private const string TEST_LOOPBACK_URI_PREFIX = $"{TEST_PROTOCOL}://{TEST_HOST_LOOPBACK}:{TEST_HOST_PORT}/v1.0/invoke/{TEST_SERVICE_NAME}/method/{TEST_CONTROLLER}/{TEST_ACTION}";
    private const string TEST_LOCALHOST_URI_PREFIX = $"{TEST_PROTOCOL}://{TEST_HOST_LOCALHOST}:{TEST_HOST_PORT}/v1.0/invoke/{TEST_SERVICE_NAME}/method/{TEST_CONTROLLER}/{TEST_ACTION}";

    private const string TEST_PROTOCOL = "http";
    private const string TEST_HOST_LOCALHOST = "localhost";
    private const string TEST_HOST_LOOPBACK = "127.0.0.1";
    private const string TEST_HOST_PORT = "3500";
    private const string TEST_SERVICE_NAME = "bannou";
    private const string TEST_CONTROLLER = "authorization";
    private const string TEST_ACTION = "token";

    [TestingService.ServiceTest(testID: "authorization", serviceType: typeof(AuthorizationService))]
    public static async Task<bool> RunAuthorizationTests(TestingService service)
    {
        await Task.CompletedTask;
        Program.Logger?.Log(LogLevel.Trace, "Running all authorization tests!");

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

        try
        {
            Func<TestingService, Task<bool>>[] tests = new Func<TestingService, Task<bool>>[]
            {
                TEST_Success,
                TEST_UsernameNotFound
            };

            foreach (var test in tests)
            {
                if (!await test.Invoke(service))
                {
                    Program.Logger?.Log(LogLevel.Error, $"Integration test [{test.Method.Name}] failed.");
                    return false;
                }
            }
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception occurred running integration test '{nameof(TEST_Success)}'.");
            return false;
        }

        return true;
    }

    private static async Task<bool> TEST_Success(TestingService service)
    {
        var testUsername = "user_1@celestialmail.com";
        var testPassword = "user_1_password";

        var request = new AuthorizationTokenRequest()
        {
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, "bannou", $"{TEST_CONTROLLER}/{TEST_ACTION}", request);
        newRequest.Headers.Add("username", testUsername);
        newRequest.Headers.Add("password", testPassword);

        try
        {
            AuthorizationTokenResponse response = await Program.DaprClient.InvokeMethodAsync<AuthorizationTokenResponse>(newRequest, Program.ShutdownCancellationTokenSource.Token);
            if (!string.IsNullOrWhiteSpace(response.Token))
                return true;
        }
        catch { }

        return false;
    }

    private static async Task<bool> TEST_UsernameNotFound(TestingService service)
    {
        var testUsername = "user_2@celestialmail.com";
        var testPassword = "user_2_password";

        var request = new AuthorizationTokenRequest()
        {
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, "bannou", $"{TEST_CONTROLLER}/{TEST_ACTION}", request);
        newRequest.Headers.Add("username", testUsername);
        newRequest.Headers.Add("password", testPassword);

        try
        {
            AuthorizationTokenResponse response = await Program.DaprClient.InvokeMethodAsync<AuthorizationTokenResponse>(newRequest, Program.ShutdownCancellationTokenSource.Token);
        }
        catch (HttpRequestException exc)
        {
            if (exc.StatusCode == System.Net.HttpStatusCode.NotFound)
                return true;
        }

        return false;
    }
}
