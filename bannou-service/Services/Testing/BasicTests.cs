using BeyondImmersion.BannouService.Controllers.Messages;

namespace BeyondImmersion.BannouService.Services.Testing;

/// <summary>
/// Tests that only require the testing service itself.
/// </summary>
public static class BasicTests
{
    private const int TEST_WAIT_TIME_MS = 200;

    private const string TEST_LOOPBACK_URI_PREFIX = $"{TEST_PROTOCOL}://{TEST_HOST_LOOPBACK}:{TEST_HOST_PORT}/v1.0/invoke/{TEST_SERVICE_NAME}/method/{TEST_CONTROLLER}/{TEST_ACTION}";
    private const string TEST_LOCALHOST_URI_PREFIX = $"{TEST_PROTOCOL}://{TEST_HOST_LOCALHOST}:{TEST_HOST_PORT}/v1.0/invoke/{TEST_SERVICE_NAME}/method/{TEST_CONTROLLER}/{TEST_ACTION}";

    private const string TEST_PROTOCOL = "http";
    private const string TEST_HOST_LOCALHOST = "localhost";
    private const string TEST_HOST_LOOPBACK = "127.0.0.1";
    private const string TEST_HOST_PORT = "3500";
    private const string TEST_SERVICE_NAME = "bannou";
    private const string TEST_CONTROLLER = "testing";
    private const string TEST_ACTION = "dapr-test";

    [TestingService.ServiceTest(testID: "basic", serviceType: typeof(TestingService))]
    public static async Task<bool> RunBasicTests(TestingService service)
    {
        await Task.CompletedTask;
        Program.Logger?.Log(LogLevel.Trace, "Running all basic tests!");

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

        var testMethods = new List<Func<TestingService, Task<bool>>>()
        {
            TEST_GET_Loopback,
            //TEST_GET_Localhost,
            TEST_GET_ID,
            TEST_GET_Service_ID,
            TEST_POST
        };

        foreach (Func<TestingService, Task<bool>> method in testMethods)
        {
            try
            {
                if (!await method.Invoke(service))
                {
                    Program.Logger?.Log(LogLevel.Error, $"Integration test '{method.Method.Name}' failed.");
                    return false;
                }
            }
            catch (Exception exc)
            {
                Program.Logger?.Log(LogLevel.Error, exc, $"An exception occurred running integration test '{method.Method.Name}'.");
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> TEST_GET_Loopback(TestingService service)
    {
        service.ResetTestVars();
        var testID = "test_id_1";

        var request = new HttpRequestMessage(HttpMethod.Get, TEST_LOOPBACK_URI_PREFIX + $"/{testID}");
        await Program.DaprClient.InvokeMethodAsync(request, Program.ShutdownCancellationTokenSource.Token);

        await Task.Delay(TEST_WAIT_TIME_MS);

        return service.LastTestID == testID;
    }

    /// <summary>
    /// Skip- doesn't work as expected.
    /// 
    /// "System.InvalidOperationException: The provided request URI is not a Dapr service invocation URI."
    /// </summary>
    private static async Task<bool> TEST_GET_Localhost(TestingService service)
    {
        service.ResetTestVars();
        var testID = "test_id_1";

        var request = new HttpRequestMessage(HttpMethod.Get, TEST_LOCALHOST_URI_PREFIX + $"/{testID}");
        await Program.DaprClient.InvokeMethodAsync(request, Program.ShutdownCancellationTokenSource.Token);

        await Task.Delay(TEST_WAIT_TIME_MS);

        return service.LastTestID == testID;
    }

    private static async Task<bool> TEST_GET_ID(TestingService service)
    {
        service.ResetTestVars();
        var testID = "test_id_1";

        HttpRequestMessage request = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Get, "bannou", $"{TEST_CONTROLLER}/{TEST_ACTION}/{testID}");
        await Program.DaprClient.InvokeMethodAsync(request, Program.ShutdownCancellationTokenSource.Token);

        await Task.Delay(TEST_WAIT_TIME_MS);

        return service.LastTestID == testID;
    }

    private static async Task<bool> TEST_GET_Service_ID(TestingService service)
    {
        service.ResetTestVars();
        var testID = "test_id_1";
        var testService = "inventory";

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Get, "bannou", $"{TEST_CONTROLLER}/{TEST_ACTION}/{testService}/{testID}");
        await Program.DaprClient.InvokeMethodAsync(newRequest, Program.ShutdownCancellationTokenSource.Token);

        await Task.Delay(TEST_WAIT_TIME_MS);

        return service.LastTestID == testID && service.LastTestService == testService;
    }

    private static async Task<bool> TEST_POST(TestingService service)
    {
        service.ResetTestVars();
        var testID = "test_id_1";
        var testService = "inventory";
        var dataModel = new TestingRunTestRequest()
        {
            ID = testID,
            Service = testService
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, "bannou", $"{TEST_CONTROLLER}/{TEST_ACTION}", dataModel);
        await Program.DaprClient.InvokeMethodAsync(newRequest, Program.ShutdownCancellationTokenSource.Token);

        await Task.Delay(TEST_WAIT_TIME_MS);

        if (service.LastTestRequest == null)
            return false;

        var receivedData = (TestingRunTestRequest)service.LastTestRequest;
        return receivedData.ID == testID && receivedData.Service == testService;
    }
}
