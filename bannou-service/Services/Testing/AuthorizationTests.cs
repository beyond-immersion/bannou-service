using BeyondImmersion.BannouService.Controllers.Messages;

namespace BeyondImmersion.BannouService.Services.Testing;

/// <summary>
/// Tests for the authorization service.
/// </summary>
public static class AuthorizationTests
{
    private const string SERVICE_NAME = "authorization";

    [TestingService.ServiceTest(testID: SERVICE_NAME, serviceType: typeof(AuthorizationService))]
    public static async Task<bool> RunAuthorizationTests(TestingService service)
    {
        await Task.CompletedTask;
        Program.Logger?.Log(LogLevel.Trace, $"Running all [{SERVICE_NAME}] integration tests!");

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
                GetJWT_Success,
                GetJWT_UsernameNotFound,
                GetJWT_PasswordNotFound
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
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception occurred running integration test [{nameof(GetJWT_Success)}].");
            return false;
        }

        return true;
    }

    private static async Task<bool> GetJWT_Success(TestingService service)
    {
        var endpointPath = $"{SERVICE_NAME}/token";
        var testUsername = "user_1@celestialmail.com";
        var testPassword = "user_1_password";

        var request = new AuthorizationTokenRequest()
        {
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, Program.GetAppByServiceName(SERVICE_NAME), endpointPath, request);
        newRequest.Headers.Add("username", testUsername);
        newRequest.Headers.Add("password", testPassword);

        try
        {
            AuthorizationTokenResponse response = await Program.DaprClient.InvokeMethodAsync<AuthorizationTokenResponse>(newRequest, Program.ShutdownCancellationTokenSource.Token);
            if (!string.IsNullOrWhiteSpace(response?.Token))
                return true;
        }
        catch { }

        return false;
    }

    private static async Task<bool> GetJWT_UsernameNotFound(TestingService service)
    {
        var endpointPath = $"{SERVICE_NAME}/token";
        var testUsername = "user_2@celestialmail.com";
        var testPassword = "user_2_password";

        var request = new AuthorizationTokenRequest()
        {
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, Program.GetAppByServiceName(SERVICE_NAME), endpointPath, request);
        newRequest.Headers.Add("username", testUsername);
        newRequest.Headers.Add("password", testPassword);

        try
        {
            HttpResponseMessage response = await Program.DaprClient.InvokeMethodWithResponseAsync(newRequest, Program.ShutdownCancellationTokenSource.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return true;
        }
        catch (HttpRequestException exc)
        {
            if (exc.StatusCode == System.Net.HttpStatusCode.NotFound)
                return true;
        }

        return false;
    }

    /// <summary>
    /// When the password is wrong, the endpoint will return 404, just like if the user didn't exist.
    /// This is simply to avoid leaking even that much information unnecessarily.
    /// </summary>
    private static async Task<bool> GetJWT_PasswordNotFound(TestingService service)
    {
        var endpointPath = $"{SERVICE_NAME}/token";
        var testUsername = "user_1@celestialmail.com";
        var testPassword = "user_2_password";

        var request = new AuthorizationTokenRequest()
        {
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, Program.GetAppByServiceName(SERVICE_NAME), endpointPath, request);
        newRequest.Headers.Add("username", testUsername);
        newRequest.Headers.Add("password", testPassword);

        try
        {
            HttpResponseMessage response = await Program.DaprClient.InvokeMethodWithResponseAsync(newRequest, Program.ShutdownCancellationTokenSource.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return true;
        }
        catch (HttpRequestException exc)
        {
            if (exc.StatusCode == System.Net.HttpStatusCode.NotFound)
                return true;
        }

        return false;
    }
}
