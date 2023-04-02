using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Services.Testing;

/// <summary>
/// Tests that only require the testing service itself.
/// </summary>
public static class BasicTests
{
    [TestingService.ServiceTest(testID: "basic", serviceType: typeof(TestingService))]
    public static async Task<bool> RunBasicTests()
    {
        await Task.CompletedTask;
        Program.Logger?.Log(LogLevel.Trace, "Running all basic tests!");

        return true;
    }
}
