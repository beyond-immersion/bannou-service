using BeyondImmersion.BannouService.Controllers.Messages;
using System.Diagnostics.CodeAnalysis;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Tests for the authorization service.
/// </summary>
public static class AuthorizationTests
{
    private const string AUTHORIZATION_SERVICE_NAME = "authorization";

    [TestingService.ServiceTest(testID: AUTHORIZATION_SERVICE_NAME, serviceType: typeof(AuthorizationService))]
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
            GetJWT_Success,
            GetJWT_UsernameNotFound,
            GetJWT_PasswordNotFound
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
        var endpointPath = $"{AUTHORIZATION_SERVICE_NAME}/token";

        var request = new GetTokenRequest()
        {
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, Program.GetAppByServiceName(AUTHORIZATION_SERVICE_NAME), endpointPath, request);
        newRequest.Headers.Add("username", ServiceConstants.TEST_ACCOUNT_EMAIL);
        newRequest.Headers.Add("password", ServiceConstants.TEST_ACCOUNT_SECRET);

        try
        {
            GetTokenResponse response = await Program.DaprClient.InvokeMethodAsync<GetTokenResponse>(newRequest, Program.ShutdownCancellationTokenSource.Token);
            if (!string.IsNullOrWhiteSpace(response?.Token))
                return true;
        }
        catch { }

        return false;
    }

    private static async Task<bool> GetJWT_UsernameNotFound(TestingService service)
    {
        var endpointPath = $"{AUTHORIZATION_SERVICE_NAME}/token";

        var request = new GetTokenRequest()
        {
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, Program.GetAppByServiceName(AUTHORIZATION_SERVICE_NAME), endpointPath, request);
        newRequest.Headers.Add("username", "fail_" + ServiceConstants.TEST_ACCOUNT_EMAIL);
        newRequest.Headers.Add("password", ServiceConstants.TEST_ACCOUNT_SECRET);

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
        var endpointPath = $"{AUTHORIZATION_SERVICE_NAME}/token";

        var request = new GetTokenRequest()
        {
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, Program.GetAppByServiceName(AUTHORIZATION_SERVICE_NAME), endpointPath, request);
        newRequest.Headers.Add("username", ServiceConstants.TEST_ACCOUNT_EMAIL);
        newRequest.Headers.Add("password", "fail_" + ServiceConstants.TEST_ACCOUNT_SECRET);

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
