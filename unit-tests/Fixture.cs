[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace BeyondImmersion.UnitTests;

public class CollectionFixture : IDisposable
{
    public CollectionFixture() => ResetENVs();

    public void Dispose() => ResetENVs();

    public void ResetENVs()
    {
        Environment.SetEnvironmentVariable("TEST_REQUIRED_SERVICE_ENABLED", null);
        Environment.SetEnvironmentVariable("TESTSERVICEENABLED", null);
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", null);
        Environment.SetEnvironmentVariable("TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("TESTPROPERTY_A", null);
        Environment.SetEnvironmentVariable("TESTPROPERTY_B", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        Environment.SetEnvironmentVariable("TEST_TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", null);

        Thread.Sleep(TimeSpan.Zero);
    }
}
