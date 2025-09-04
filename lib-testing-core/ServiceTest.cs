namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Represents a single test that can be executed by either HTTP or WebSocket clients.
/// </summary>
public sealed class ServiceTest
{
    public string Name { get; }
    public string Description { get; }
    public string Type { get; }
    public Func<ITestClient, string[], Task<TestResult>> TestAction { get; }

    private ServiceTest() { }

    public ServiceTest(Func<ITestClient, string[], Task<TestResult>> testAction, string name, string type, string description)
    {
        Name = name;
        Description = description;
        Type = type;
        TestAction = testAction;
    }

    /// <summary>
    /// Legacy constructor for backward compatibility with Action-based tests
    /// </summary>
    public ServiceTest(Action<string[]> legacyAction, string name, string type, string description)
    {
        Name = name;
        Description = description;
        Type = type;
        TestAction = (client, args) =>
        {
            legacyAction(args);
            return Task.FromResult(new TestResult(true, "Legacy test completed"));
        };
    }
}
