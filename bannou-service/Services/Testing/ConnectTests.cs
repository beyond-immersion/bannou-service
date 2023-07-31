using BeyondImmersion.BannouService.Controllers.Messages;

namespace BeyondImmersion.BannouService.Services.Testing;

/// <summary>
/// Tests for the connect service.
/// </summary>
public static class ConnectTests
{
    private const string SERVICE_NAME = "connect";

    [TestingService.ServiceTest(testID: SERVICE_NAME, serviceType: typeof(ConnectService))]
    public static async Task<bool> RunConnectTests(TestingService service)
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

        Func<TestingService, Task<bool>>[] tests = new Func<TestingService, Task<bool>>[]
        {
            Connect_Success,
            Connect_TokenEmpty_BadRequest,
            Connect_TokenMissing_BadRequest
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

    private static async Task<bool> Connect_Success(TestingService service)
    {
        var endpointPath = $"{SERVICE_NAME}";
        var testToken = "some_token";

        var request = new ConnectRequest()
        {
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, Program.GetAppByServiceName(SERVICE_NAME), endpointPath, request);
        newRequest.Headers.Add("token", testToken);

        try
        {
            ConnectResponse response = await Program.DaprClient.InvokeMethodAsync<ConnectResponse>(newRequest, Program.ShutdownCancellationTokenSource.Token);
            return true;
        }
        catch { }

        return false;
    }

    private static async Task<bool> Connect_TokenEmpty_BadRequest(TestingService service)
    {
        var endpointPath = $"{SERVICE_NAME}";
        var testToken = "";

        var request = new ConnectRequest()
        {
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, Program.GetAppByServiceName(SERVICE_NAME), endpointPath, request);
        newRequest.Headers.Add("token", testToken);

        try
        {
            ConnectResponse response = await Program.DaprClient.InvokeMethodAsync<ConnectResponse>(newRequest, Program.ShutdownCancellationTokenSource.Token);
            return false;
        }
        catch (HttpRequestException exc)
        {
            if (exc.StatusCode == System.Net.HttpStatusCode.BadRequest)
                return true;
        }
        catch (Dapr.Client.InvocationException exc)
        {
            if (exc.Response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                return true;
        }

        return false;
    }

    private static async Task<bool> Connect_TokenMissing_BadRequest(TestingService service)
    {
        var endpointPath = $"{SERVICE_NAME}";

        var request = new ConnectRequest()
        {
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, Program.GetAppByServiceName(SERVICE_NAME), endpointPath, request);

        try
        {
            ConnectResponse response = await Program.DaprClient.InvokeMethodAsync<ConnectResponse>(newRequest, Program.ShutdownCancellationTokenSource.Token);
            return false;
        }
        catch (HttpRequestException exc)
        {
            if (exc.StatusCode == System.Net.HttpStatusCode.BadRequest)
                return true;
        }
        catch (Dapr.Client.InvocationException exc)
        {
            if (exc.Response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                return true;
        }

        return false;
    }
}
