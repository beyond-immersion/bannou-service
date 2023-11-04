using Newtonsoft.Json.Linq;

namespace BeyondImmersion.BannouService.Services.Tests;

/// <summary>
/// Tests that only require the testing service itself.
/// </summary>
public static class BasicControllerTests
{
    private const string LOOPBACK_URI_PREFIX = $"{PROTOCOL}://{HOST_LOOPBACK}:{HOST_PORT}/v1.0/invoke/{SERVICE_NAME}/method/{CONTROLLER_NAME}/{ACTION_NAME}";
    private const string PROTOCOL = "http";
    private const string HOST_LOOPBACK = "127.0.0.1";
    private const string HOST_PORT = "3500";
    private const string SERVICE_NAME = "bannou";
    private const string CONTROLLER_NAME = "testing";
    private const string ACTION_NAME = "dapr-test";

    [ServiceTest(testName: "basic", serviceType: typeof(TestingService))]
    public static async Task<bool> Run(TestingService service)
        => await service.RunDelegates("basic", new List<Func<TestingService, Task<bool>>>()
            {
                Get_Loopback_StringFromRoute,
                Get_StringInRoute,
                Get_MultipleStringsInRoute,
                Post_ObjectModel,
                Post_ObjectModel_HeaderArrays,
                Post_ExecuteApiRequest
            });

    private static async Task<bool> Get_Loopback_StringFromRoute(TestingService service)
    {
        var testID = "test_id_1";

        var request = new HttpRequestMessage(HttpMethod.Get, LOOPBACK_URI_PREFIX + $"/{testID}");
        await Program.DaprClient.InvokeMethodAsync(request, Program.ShutdownCancellationTokenSource.Token);

        return service.LastTestID == testID;
    }

    private static async Task<bool> Get_StringInRoute(TestingService service)
    {
        var testID = "test_id_1";

        HttpRequestMessage request = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Get, "bannou", $"{CONTROLLER_NAME}/{ACTION_NAME}/{testID}");
        await Program.DaprClient.InvokeMethodAsync(request, Program.ShutdownCancellationTokenSource.Token);

        return service.LastTestID == testID;
    }

    private static async Task<bool> Get_MultipleStringsInRoute(TestingService service)
    {
        var testID = "test_id_1";
        var testService = "inventory";

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Get, "bannou", $"{CONTROLLER_NAME}/{ACTION_NAME}/{testService}/{testID}");
        await Program.DaprClient.InvokeMethodAsync(newRequest, Program.ShutdownCancellationTokenSource.Token);

        return service.LastTestID == testID && service.LastTestService == testService;
    }

    private static async Task<bool> Post_ObjectModel(TestingService service)
    {
        var testID = "test_id_1";
        var testService = "inventory";
        var dataModel = new TestingRunTestRequest()
        {
            ID = testID,
            Service = testService
        };

        HttpRequestMessage newRequest = Program.DaprClient.CreateInvokeMethodRequest(HttpMethod.Post, "bannou", $"{CONTROLLER_NAME}/{ACTION_NAME}", dataModel);
        await Program.DaprClient.InvokeMethodAsync(newRequest, Program.ShutdownCancellationTokenSource.Token);

        if (service.LastTestRequest == null)
            return false;

        var receivedData = (TestingRunTestRequest)service.LastTestRequest;
        return receivedData.ID == testID && receivedData.Service == testService;
    }

    private static async Task<bool> Post_ObjectModel_HeaderArrays(TestingService service)
    {
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
        newRequest.AddPropertyHeaders(dataModel);

        if (!newRequest.Headers.TryGetValues("REQUEST_IDS", out var headerValues))
        {
            Program.Logger.Log(LogLevel.Error, "Could not retrieve 'REQUEST_IDS' from request headers.");
            return false;
        }

        await Program.DaprClient.InvokeMethodAsync(newRequest, Program.ShutdownCancellationTokenSource.Token);
        if (service.LastTestRequest == null)
        {
            Program.Logger.Log(LogLevel.Error, "The cached 'last request' for the testing service is null / missing. " +
                "Likely the API Controller couldn't be reached.");

            return false;
        }

        // ensure ID is still in request object
        var receivedData = (TestingRunTestRequest)service.LastTestRequest;
        if (dataModel?.RequestIDs?["SERVICE_ID"] != serviceID)
        {
            Program.Logger.Log(LogLevel.Error, "The original request model is now missing the 'REQUEST_IDS' that were just set. " +
                "Most likely this indicates an issue with the model binding unsetting the original value.");

            return false;
        }

        // ensure ID is in received / serialized+deserialized payload too
        if (receivedData?.RequestIDs?["SERVICE_ID"] != serviceID)
        {
            Program.Logger.Log(LogLevel.Error, "The cached 'last request' for the testing service has the 'REQUEST_IDS' value missing. " +
                "Headers for the payload were lost in binding to the model or in executing the action.");

            return false;
        }

        return true;
    }

    private static async Task<bool> Post_ExecuteApiRequest(TestingService service)
    {
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

        if (!await dataModel.ExecutePostRequest(CONTROLLER_NAME, ACTION_NAME))
        {
            Program.Logger.Log(LogLevel.Error, $"Failure to execute testing API with [{nameof(ServiceRequest.ExecutePostRequest)}] helper method.");
            return false;
        }

        if (service.LastTestRequest == null)
        {
            Program.Logger.Log(LogLevel.Error, "The cached 'last request' for the testing service is null / missing. " +
                "Likely the API Controller couldn't be reached.");

            return false;
        }

        // ensure ID is still in request object
        var receivedData = (TestingRunTestRequest)service.LastTestRequest;
        if (dataModel?.RequestIDs?["SERVICE_ID"] != serviceID)
        {
            Program.Logger.Log(LogLevel.Error, "The original request model is now missing the 'REQUEST_IDS' that were just set. " +
                "Most likely this indicates an issue with the model binding unsetting the original value.");

            return false;
        }

        // ensure ID is in received / serialized+deserialized payload too
        if (receivedData?.RequestIDs?["SERVICE_ID"] != serviceID)
        {
            Program.Logger.Log(LogLevel.Error, "The cached 'last request' for the testing service has the 'REQUEST_IDS' value missing. " +
                "Headers for the payload were lost in binding to the model or in executing the action.");

            return false;
        }

        return true;
    }
}
