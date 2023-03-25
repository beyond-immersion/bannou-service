using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.UnitTests;

public class ConfigurationTests
{
    [DaprService("test")]
    public class TestService : Controller, IDaprService { }

    [DaprService("test")]
    public class RequiredTestService : Controller, IDaprService { }

    [ServiceConfiguration]
    public class TestConfiguration_Attribute_NoService : ServiceConfiguration
    {
        public string? TestProperty { get; set; }
    }

    [ServiceConfiguration(typeof(TestService))]
    public class TestConfiguration_Attribute_TestService : ServiceConfiguration
    {
        public string? TestProperty { get; set; }
    }

    [ServiceConfiguration(typeof(TestService), envPrefix: "test_")]
    public class TestConfiguration_Attribute_TestService_WithPrefix : ServiceConfiguration
    {
        public string? TestProperty { get; set; }
    }

    public class TestConfiguration_NoAttribute : ServiceConfiguration
    {
        public string? TestProperty { get; set; }
    }

    [ServiceConfiguration(typeof(RequiredTestService))]
    public class TestConfiguration_RequiredProperty : ServiceConfiguration
    {
        [ServiceConfigRequired(AllowEmptyStrings = false)]
        public string? TestProperty { get; set; }
    }

    private static void ResetENVs()
    {
        Environment.SetEnvironmentVariable("TestServiceEnabled", null);
        Environment.SetEnvironmentVariable("Test_Service_Enabled", null);
        Environment.SetEnvironmentVariable("TestProperty", null);
        Environment.SetEnvironmentVariable("ForceServiceID", null);
        Environment.SetEnvironmentVariable("test_TestProperty", null);
        Environment.SetEnvironmentVariable("test_ForceServiceID", null);
    }

    [Fact]
    public void ServiceEnabled_Default()
    {
        ResetENVs();
        if (ServiceConstants.ENABLE_SERVICES_BY_DEFAULT)
            Assert.True(ServiceConfiguration.IsAnyServiceEnabled());
        else
            Assert.False(ServiceConfiguration.IsAnyServiceEnabled());
    }

    [Fact]
    public void ServiceEnabled_TestType()
    {
        ResetENVs();
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "false");
        Assert.False(ServiceConfiguration.IsServiceEnabled(typeof(TestService)));

        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(ServiceConfiguration.IsServiceEnabled(typeof(TestService)));
    }

    [Fact]
    public void ServiceEnabled_TestType_Generic()
    {
        ResetENVs();
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "false");
        Assert.False(ServiceConfiguration.IsServiceEnabled<TestService>());

        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(ServiceConfiguration.IsServiceEnabled<TestService>());
    }

    [Fact]
    public void ServiceEnabled_TestString()
    {
        ResetENVs();
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "false");
        Assert.False(ServiceConfiguration.IsServiceEnabled("TestService"));

        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(ServiceConfiguration.IsServiceEnabled("TestService"));
    }

    [Fact]
    public void ServiceEnabled_TestString_Lower()
    {
        ResetENVs();
        Environment.SetEnvironmentVariable("test_service_enabled", "false");
        Assert.False(ServiceConfiguration.IsServiceEnabled("testservice"));

        Environment.SetEnvironmentVariable("test_service_enabled", "true");
        Assert.True(ServiceConfiguration.IsServiceEnabled("testservice"));
    }

    [Fact]
    public void CreateSwitchFromProperty()
    {
        ResetENVs();
        var switchString = ServiceConfiguration.CreateSwitchFromName(nameof(TestConfiguration_NoAttribute.TestProperty));
        Assert.Equal("--testproperty", switchString);
    }

    [Fact]
    public void CreateSwitchMappings_All()
    {
        ResetENVs();
        var switchLookup = ServiceConfiguration.CreateAllSwitchMappings();
        Assert.NotNull(switchLookup);
        Assert.Equal("ForceServiceID", switchLookup["--forceserviceid"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void CreateSwitchMappings_TestConfiguration()
    {
        ResetENVs();
        var switchLookup = ServiceConfiguration.CreateSwitchMappings(typeof(TestConfiguration_NoAttribute));
        Assert.NotNull(switchLookup);
        Assert.Equal("ForceServiceID", switchLookup["--forceserviceid"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void CreateSwitchMappings_TestConfiguration_Generic()
    {
        ResetENVs();
        var switchLookup = ServiceConfiguration.CreateSwitchMappings<TestConfiguration_NoAttribute>();
        Assert.NotNull(switchLookup);
        Assert.Equal("ForceServiceID", switchLookup["--forceserviceid"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void GlobalConfiguration_Root()
    {
        ResetENVs();
        var configRoot = ServiceConfiguration.BuildConfigurationRoot();
        Assert.NotNull(configRoot);
        Assert.Null(configRoot["ForceServiceID"]);
        Assert.Null(configRoot["forceserviceid"]);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        configRoot = ServiceConfiguration.BuildConfigurationRoot();
        Assert.NotNull(configRoot);
        Assert.Equal(serviceID, configRoot["ForceServiceID"]);
    }

    [Fact]
    public void GlobalConfiguration_Root_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var configRoot = ServiceConfiguration.BuildConfigurationRoot(args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.NotNull(configRoot);
        Assert.Equal(serviceID, configRoot["ForceServiceID"]);
    }

    [Fact]
    public void GlobalConfiguration_Root_WithPrefix()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("Test_ForceServiceID", serviceID);
        var configRoot = ServiceConfiguration.BuildConfigurationRoot(envPrefix: "test_");
        Assert.NotNull(configRoot);
        Assert.Null(configRoot["Test_ForceServiceID"]);
        Assert.Equal(serviceID, configRoot["ForceServiceID"]);
    }

    [Fact]
    public void GlobalConfiguration()
    {
        ResetENVs();
        var config = ServiceConfiguration.BuildConfiguration();
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = ServiceConfiguration.BuildConfiguration();
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void GlobalConfiguration_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = ServiceConfiguration.BuildConfiguration(new string[] {$"--ForceServiceID={serviceID}"});
        Assert.Equal(serviceID, config.ForceServiceID);

        config = ServiceConfiguration.BuildConfiguration(new string[] { $"--forceserviceid={serviceID}" });
        Assert.Equal(serviceID, config.ForceServiceID);

        config = ServiceConfiguration.BuildConfiguration();
        Assert.Null(config.ForceServiceID);
    }

    [Fact]
    public void GlobalConfiguration_WithPrefix()
    {
        ResetENVs();
        var config = ServiceConfiguration.BuildConfiguration(envPrefix: "test_");
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = ServiceConfiguration.BuildConfiguration(envPrefix: "test_");
        Assert.Null(config.ForceServiceID);

        Environment.SetEnvironmentVariable("test_ForceServiceID", serviceID);
        config = ServiceConfiguration.BuildConfiguration(envPrefix: "test_");
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration()
    {
        ResetENVs();
        var config = (TestConfiguration_NoAttribute?)ServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_NoAttribute));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = (TestConfiguration_NoAttribute?)ServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_NoAttribute));
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = ServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_NoAttribute),
                        args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);

        config = ServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_NoAttribute),
                        args: new string[] { $"--forceserviceid={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_Generic()
    {
        ResetENVs();
        var config = ServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = ServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>();
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_Generic_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = ServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>(
                        args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.Equal(serviceID, config.ForceServiceID);

        config = ServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>(
                        args: new string[] { $"--forceserviceid={serviceID}" });
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_WithAttribute()
    {
        ResetENVs();
        var config = (TestConfiguration_Attribute_NoService?)ServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_NoService));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = (TestConfiguration_Attribute_NoService?)ServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_NoService));
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_Generic_WithAttribute()
    {
        ResetENVs();
        var config = ServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_NoService>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = ServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_NoService>();
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_ForService()
    {
        ResetENVs();
        var config = (TestConfiguration_Attribute_TestService?)ServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = (TestConfiguration_Attribute_TestService?)ServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService));
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_ForService_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = ServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_Attribute_TestService),
                        args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);

        config = ServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_Attribute_TestService),
                        args: new string[] { $"--forceserviceid={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_Generic_ForService()
    {
        ResetENVs();
        var config = ServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = ServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>();
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_Generic_ForService_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = ServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>(
                        args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.Equal(serviceID, config.ForceServiceID);

        config = ServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>(
                        args: new string[] { $"--forceserviceid={serviceID}" });
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_ForService_WithPrefix()
    {
        ResetENVs();
        var config = (TestConfiguration_Attribute_TestService_WithPrefix?)ServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService_WithPrefix), envPrefix: "test_");
        Assert.Null(config);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = (TestConfiguration_Attribute_TestService_WithPrefix?)ServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService_WithPrefix), envPrefix: "test_");
        Assert.Null(config);

        Environment.SetEnvironmentVariable("test_TestProperty", "Test");
        Environment.SetEnvironmentVariable("test_ForceServiceID", serviceID);
        config = (TestConfiguration_Attribute_TestService_WithPrefix?)ServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService_WithPrefix), envPrefix: "test_");
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_Generic_ForService_WithPrefix()
    {
        ResetENVs();
        var config = ServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService_WithPrefix>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = ServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService_WithPrefix>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        Environment.SetEnvironmentVariable("test_TestProperty", "Test");
        Environment.SetEnvironmentVariable("test_ForceServiceID", serviceID);
        config = ServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService_WithPrefix>();
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration()
    {
        ResetENVs();
        Assert.False(ServiceConfiguration.HasRequiredConfiguration(typeof(TestConfiguration_RequiredProperty)));

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.True(ServiceConfiguration.HasRequiredConfiguration(typeof(TestConfiguration_RequiredProperty)));
    }

    [Fact]
    public void HasRequiredConfig_ByService()
    {
        ResetENVs();
        Assert.False(ServiceConfiguration.HasRequiredConfiguration<RequiredTestService>());

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.True(ServiceConfiguration.HasRequiredConfiguration<RequiredTestService>());
    }

    [Fact]
    public void TestConfiguration_ByService()
    {
        ResetENVs();
        var config = (TestConfiguration_Attribute_TestService?)ServiceConfiguration.BuildServiceConfiguration<TestService>();
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = (TestConfiguration_Attribute_TestService?)ServiceConfiguration.BuildServiceConfiguration<TestService>();
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_ByService_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = ServiceConfiguration.BuildServiceConfiguration<TestService>(
                        args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);

        config = ServiceConfiguration.BuildServiceConfiguration<TestService>(
                        args: new string[] { $"--forceserviceid={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);
    }
}
