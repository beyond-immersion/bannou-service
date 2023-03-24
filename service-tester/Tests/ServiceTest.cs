namespace BeyondImmersion.ServiceTester.Tests;

public sealed class ServiceTest
{
    public string Name { get; }
    public string Description { get; }
    public string Type { get; }
    public Action<string[]> Target { get; }

    private ServiceTest() { }
    public ServiceTest(Action<string[]> target, string name, string type, string description)
    {
        Name = name;
        Description = description;
        Type = type;
        Target = target;
    }
}
