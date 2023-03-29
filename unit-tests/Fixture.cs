using Xunit.Abstractions;

namespace BeyondImmersion.UnitTests;

public class CollectionFixture : IDisposable
{
    public CollectionFixture()
    {
    }

    public void Dispose()
    {
        ResetENVs();
    }

    public void ResetENVs()
    {
        Environment.SetEnvironmentVariable("TEST_REQUIRED_SERVICE_ENABLED", null);
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", null);
        Environment.SetEnvironmentVariable("TestServiceEnabled", null);
        Environment.SetEnvironmentVariable("Test_Service_Enabled", null);
        Environment.SetEnvironmentVariable("TestProperty", null);
        Environment.SetEnvironmentVariable("TestProperty_A", null);
        Environment.SetEnvironmentVariable("TestProperty_B", null);
        Environment.SetEnvironmentVariable("ForceServiceID", null);
        Environment.SetEnvironmentVariable("test_TestProperty", null);
        Environment.SetEnvironmentVariable("test_ForceServiceID", null);
    }
}
