namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service handler responsible for testing service APIs.
/// </summary>
[DaprService(name: "testing")]
public class TestingService : IDaprService
{
    public TestingServiceConfiguration Configuration { get; private set; }
        = IServiceConfiguration.BuildConfiguration<TestingServiceConfiguration>();

    /// <summary>
    /// API to run a given test.
    /// ServiceName can be null if unknown.
    /// </summary>
    public async Task<bool> Run(string id, string? service = null)
    {
        await Task.CompletedTask;
        Program.Logger.Log(LogLevel.Debug, $"{nameof(Run)} service method executed successfully.");

        return true;
    }

    /// <summary>
    /// API to run all tests against a given service.
    /// </summary>
    public async Task<bool> RunAllForService(string service)
    {
        await Task.CompletedTask;
        Program.Logger.Log(LogLevel.Debug, $"{nameof(RunAllForService)} service method executed successfully.");

        return true;
    }

    /// <summary>
    /// API to run all tests against all enabled services.
    /// </summary>
    public async Task<bool> RunAllEnabled()
    {
        await Task.CompletedTask;
        Program.Logger.Log(LogLevel.Debug, $"{nameof(RunAllEnabled)} service method executed successfully.");

        return true;
    }

    /// <summary>
    /// API to run all tests.
    /// </summary>
    public async Task<bool> RunAll()
    {
        await Task.CompletedTask;
        Program.Logger.Log(LogLevel.Debug, $"{nameof(RunAll)} service method executed successfully.");

        return true;
    }
}
