using Xunit.Abstractions;

namespace BeyondImmersion.UnitTests;

public class CollectionFixture : IDisposable
{
    public CollectionFixture()
    {
        ResetENVs();
    }

    public void Dispose()
    {
        ResetENVs();
    }

    public void ResetENVs()
    {
        Environment.SetEnvironmentVariable("test_required_service_enabled", null);
        Environment.SetEnvironmentVariable("Test_Required_Service_Enabled", null);
        Environment.SetEnvironmentVariable("Test_Required_SERVICE_ENABLED", null);
        Environment.SetEnvironmentVariable("TEST_REQUIRED_Service_Enabled", null);
        Environment.SetEnvironmentVariable("TEST_REQUIRED_SERVICE_ENABLED", null);

        Environment.SetEnvironmentVariable("testserviceenabled", null);
        Environment.SetEnvironmentVariable("TestServiceEnabled", null);
        Environment.SetEnvironmentVariable("TESTSERVICEENABLED", null);

        Environment.SetEnvironmentVariable("test_service_enabled", null);
        Environment.SetEnvironmentVariable("Test_Service_Enabled", null);
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", null);

        Environment.SetEnvironmentVariable("testproperty", null);
        Environment.SetEnvironmentVariable("TestProperty", null);
        Environment.SetEnvironmentVariable("TESTPROPERTY", null);

        Environment.SetEnvironmentVariable("testproperty_a", null);
        Environment.SetEnvironmentVariable("TestProperty_A", null);
        Environment.SetEnvironmentVariable("TESTPROPERTY_A", null);

        Environment.SetEnvironmentVariable("testproperty_b", null);
        Environment.SetEnvironmentVariable("TestProperty_B", null);
        Environment.SetEnvironmentVariable("TESTPROPERTY_B", null);

        Environment.SetEnvironmentVariable("forceserviceid", null);
        Environment.SetEnvironmentVariable("FORCESERVICEID", null);
        Environment.SetEnvironmentVariable("ForceServiceID", null);

        Environment.SetEnvironmentVariable("test_testproperty", null);
        Environment.SetEnvironmentVariable("test_TestProperty", null);
        Environment.SetEnvironmentVariable("Test_TestProperty", null);
        Environment.SetEnvironmentVariable("TEST_TestProperty", null);
        Environment.SetEnvironmentVariable("Test_TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("TEST_TESTPROPERTY", null);

        Environment.SetEnvironmentVariable("test_forceserviceid", null);
        Environment.SetEnvironmentVariable("test_ForceServiceID", null);
        Environment.SetEnvironmentVariable("Test_ForceServiceID", null);
        Environment.SetEnvironmentVariable("Test_FORCESERVICEID", null);
        Environment.SetEnvironmentVariable("TEST_ForceServiceID", null);
        Environment.SetEnvironmentVariable("TEST_FORCESERVICEID", null);

        Thread.Sleep(TimeSpan.Zero);
    }
}
