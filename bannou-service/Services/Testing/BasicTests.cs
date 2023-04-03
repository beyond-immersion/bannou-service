using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Services.Testing;

/// <summary>
/// Tests that only require the testing service itself.
/// </summary>
public static class BasicTests
{
    private const string TEST_URI_PREFIX = $"{TEST_PROTOCOL}://{TEST_HOST_DOMAIN}:{TEST_HOST_PORT}/v1.0/invoke/{TEST_SERVICE_NAME}/method/{TEST_CONTROLLER}/{TEST_ACTION}";
    private const string TEST_PROTOCOL = "http";
    private const string TEST_HOST_DOMAIN = "127.0.0.1";
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

        try
        {
            service.SetLastTestID(null);
            string newTestID = "test_id_1";
            var newRequest = new HttpRequestMessage(HttpMethod.Get, TEST_URI_PREFIX + $"/{newTestID}");
            await Program.DaprClient.InvokeMethodAsync(newRequest, Program.ShutdownCancellationTokenSource.Token);

            await Task.Delay(500);
            if (service.LastTestID != newTestID)
            {
                Program.Logger?.Log(LogLevel.Error, "Basic dapr method invocation test failed.");
                return false;
            }
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, "An error occurred sending invoking dapr method.");
            return false;
        }

        return true;
    }
}
