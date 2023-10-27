using Dapr.Client;

namespace BeyondImmersion.BannouService.Services.Tests;

/// <summary>
/// Tests that only require the testing service itself.
/// </summary>
public static class BasicTests
{
    public const int ASYNC_HTTP_DELAY = 200;

    private const string LOOPBACK_URI_PREFIX = $"{PROTOCOL}://{HOST_LOOPBACK}:{HOST_PORT}/v1.0/invoke/{SERVICE_NAME}/method/{CONTROLLER_NAME}/{ACTION_NAME}";
    private const string PROTOCOL = "http";
    private const string HOST_LOOPBACK = "127.0.0.1";
    private const string HOST_PORT = "3500";
    private const string SERVICE_NAME = "bannou";
    private const string CONTROLLER_NAME = "testing";
    private const string ACTION_NAME = "dapr-test";

    [ServiceTest(testName: "basic", serviceType: typeof(TestingService))]
    public static async Task<bool> BasicControllerTests(TestingService service)
    {
        await Task.CompletedTask;
        Program.Logger.Log(LogLevel.Trace, "Running all Basic tests!");

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
            Get_Loopback_StringFromRoute,
            Get_StringInRoute,
            Get_MultipleStringsInRoute,
            Post_ObjectModel,
            Post_ObjectModel_HeaderArrays
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
            catch (InvocationException exc)
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

    private static async Task<bool> Get_Loopback_StringFromRoute(TestingService service)
    {
        service.ResetTestVars();
        var testID = "test_id_1";

        var request = new HttpRequestMessage(HttpMethod.Get, LOOPBACK_URI_PREFIX + $"/{testID}");
        await Program.DaprClient.InvokeMethodAsync(request, Program.ShutdownCancellationTokenSource.Token);

        await Task.Delay(ASYNC_HTTP_DELAY);

        return service.LastTestID == testID;
    }

    private static async Task<bool> Get_StringInRoute(TestingService service)
    {
        service.ResetTestVars();
        var testID = "test_id_1";

        HttpRequestMessage request = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Get, "bannou", $"{CONTROLLER_NAME}/{ACTION_NAME}/{testID}");
        await Program.DaprClient.InvokeMethodAsync(request, Program.ShutdownCancellationTokenSource.Token);

        await Task.Delay(ASYNC_HTTP_DELAY);

        return service.LastTestID == testID;
    }

    private static async Task<bool> Get_MultipleStringsInRoute(TestingService service)
    {
        service.ResetTestVars();
        var testID = "test_id_1";
        var testService = "inventory";

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Get, "bannou", $"{CONTROLLER_NAME}/{ACTION_NAME}/{testService}/{testID}");
        await Program.DaprClient.InvokeMethodAsync(newRequest, Program.ShutdownCancellationTokenSource.Token);

        await Task.Delay(ASYNC_HTTP_DELAY);

        return service.LastTestID == testID && service.LastTestService == testService;
    }

    private static async Task<bool> Post_ObjectModel(TestingService service)
    {
        service.ResetTestVars();
        var testID = "test_id_1";
        var testService = "inventory";
        var dataModel = new TestingRunTestRequest()
        {
            ID = testID,
            Service = testService
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, "bannou", $"{CONTROLLER_NAME}/{ACTION_NAME}", dataModel);
        await Program.DaprClient.InvokeMethodAsync(newRequest, Program.ShutdownCancellationTokenSource.Token);

        await Task.Delay(ASYNC_HTTP_DELAY);

        if (service.LastTestRequest == null)
            return false;

        var receivedData = (TestingRunTestRequest)service.LastTestRequest;
        return receivedData.ID == testID && receivedData.Service == testService;
    }

    private static async Task<bool> Post_ObjectModel_HeaderArrays(TestingService service)
    {
        service.ResetTestVars();
        var testID = "test_id_1";
        var testService = "inventory";
        var serviceID = Guid.NewGuid().ToString();
        var dataModel = new TestingRunTestRequest()
        {
            ID = testID,
            Service = testService,
            RequestIDs = new()
            {
                ["SERVICE_ID"] = serviceID
            }
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, "bannou", $"{CONTROLLER_NAME}/{ACTION_NAME}", dataModel);
        await Program.DaprClient.InvokeMethodAsync(newRequest, Program.ShutdownCancellationTokenSource.Token);

        await Task.Delay(ASYNC_HTTP_DELAY);

        if (service.LastTestRequest == null)
            return false;

        // ensure ID is still in request object
        var receivedData = (TestingRunTestRequest)service.LastTestRequest;
        if (dataModel?.RequestIDs?["SERVICE_ID"] != serviceID)
            return false;

        // ensure ID is in received / serialized+deserialized payload too
        return receivedData?.RequestIDs?["SERVICE_ID"] == serviceID;
    }
}
