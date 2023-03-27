namespace BeyondImmersion.UnitTests;

public class Fixture : IDisposable
{
    public Fixture()
    {
    }

    public void Dispose()
    {
    }

    public void ResetENVs()
    {
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
