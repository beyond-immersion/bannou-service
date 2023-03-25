using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.UnitTests;

public class ConfigurationTests
{
    [DaprService("test")]
    public class TestService : IDaprService { }

    [DaprService("test")]
    public class RequiredTestService : IDaprService { }

    [DaprService("test")]
    public class MultipleRequiredTestService : IDaprService { }

    public abstract class TestConfigBase : IServiceConfiguration
    {
        public string? ForceServiceID { get; }
    }

    [ServiceConfiguration]
    public class TestConfiguration_Attribute_NoService : TestConfigBase
    {
        public string? TestProperty { get; set; }
    }

    [ServiceConfiguration(typeof(TestService))]
    public class TestConfiguration_Attribute_TestService : TestConfigBase
    {
        public string? TestProperty { get; set; }
    }

    [ServiceConfiguration(typeof(TestService), envPrefix: "test_")]
    public class TestConfiguration_Attribute_TestService_WithPrefix : TestConfigBase
    {
        public string? TestProperty { get; set; }
    }

    public class TestConfiguration_NoAttribute : TestConfigBase
    {
        public string? TestProperty { get; set; }
    }

    [ServiceConfiguration(typeof(RequiredTestService))]
    public class TestConfiguration_RequiredProperty : TestConfigBase
    {
        [ConfigRequired(AllowEmptyStrings = false)]
        public string? TestProperty { get; set; }
    }

    [ServiceConfiguration(typeof(MultipleRequiredTestService))]
    public class TestConfiguration_MultipleRequiredProperties_A : TestConfigBase
    {
        [ConfigRequired(AllowEmptyStrings = false)]
        public string? TestProperty_A { get; set; }
    }

    [ServiceConfiguration(typeof(MultipleRequiredTestService))]
    public class TestConfiguration_MultipleRequiredProperties_B : TestConfigBase
    {
        [ConfigRequired(AllowEmptyStrings = false)]
        public string? TestProperty_B { get; set; }
    }

    public ConfigurationTests()
    {
        ResetENVs();
    }

    private static void ResetENVs()
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

    [Fact]
    public void ServiceEnabled_Default()
    {
        ResetENVs();
        if (ServiceConstants.ENABLE_SERVICES_BY_DEFAULT)
            Assert.True(IServiceConfiguration.IsAnyServiceEnabled());
        else
            Assert.False(IServiceConfiguration.IsAnyServiceEnabled());
    }

    [Fact]
    public void ServiceEnabled_TestType()
    {
        ResetENVs();
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "false");
        Assert.False(IServiceConfiguration.IsServiceEnabled(typeof(TestService)));

        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(IServiceConfiguration.IsServiceEnabled(typeof(TestService)));
    }

    [Fact]
    public void ServiceEnabled_TestType_Generic()
    {
        ResetENVs();
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "false");
        Assert.False(IServiceConfiguration.IsServiceEnabled<TestService>());

        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(IServiceConfiguration.IsServiceEnabled<TestService>());
    }

    [Fact]
    public void ServiceEnabled_TestString()
    {
        ResetENVs();
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "false");
        Assert.False(IServiceConfiguration.IsServiceEnabled("TestService"));

        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(IServiceConfiguration.IsServiceEnabled("TestService"));
    }

    [Fact]
    public void ServiceEnabled_TestString_Lower()
    {
        ResetENVs();
        Environment.SetEnvironmentVariable("test_service_enabled", "false");
        Assert.False(IServiceConfiguration.IsServiceEnabled("testservice"));

        Environment.SetEnvironmentVariable("test_service_enabled", "true");
        Assert.True(IServiceConfiguration.IsServiceEnabled("testservice"));
    }

    [Fact]
    public void CreateSwitchFromProperty()
    {
        ResetENVs();
        var switchString = IServiceConfiguration.CreateSwitchFromName(nameof(TestConfiguration_NoAttribute.TestProperty));
        Assert.Equal("--testproperty", switchString);
    }

    [Fact]
    public void CreateSwitchMappings_All()
    {
        ResetENVs();
        var switchLookup = IServiceConfiguration.CreateAllSwitchMappings();
        Assert.NotNull(switchLookup);
        Assert.Equal("ForceServiceID", switchLookup["--forceserviceid"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void CreateSwitchMappings_TestConfiguration()
    {
        ResetENVs();
        var switchLookup = IServiceConfiguration.CreateSwitchMappings(typeof(TestConfiguration_NoAttribute));
        Assert.NotNull(switchLookup);
        Assert.Equal("ForceServiceID", switchLookup["--forceserviceid"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void CreateSwitchMappings_TestConfiguration_Generic()
    {
        ResetENVs();
        var switchLookup = IServiceConfiguration.CreateSwitchMappings<TestConfiguration_NoAttribute>();
        Assert.NotNull(switchLookup);
        Assert.Equal("ForceServiceID", switchLookup["--forceserviceid"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration()
    {
        ResetENVs();
        Assert.False(IServiceConfiguration.HasRequiredConfiguration(typeof(TestConfiguration_RequiredProperty)));

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.True(IServiceConfiguration.HasRequiredConfiguration(typeof(TestConfiguration_RequiredProperty)));
    }

    [Fact]
    public void HasRequiredConfig_ByService()
    {
        ResetENVs();
        Assert.False(IServiceConfiguration.HasRequiredConfiguration<RequiredTestService>());

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.True(IServiceConfiguration.HasRequiredConfiguration<RequiredTestService>());
    }

    [Fact]
    public void HasRequiredConfig_ByService_MultipleConfigTypes()
    {
        ResetENVs();
        Assert.False(IServiceConfiguration.HasRequiredConfiguration<MultipleRequiredTestService>());

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.False(IServiceConfiguration.HasRequiredConfiguration<MultipleRequiredTestService>());

        Environment.SetEnvironmentVariable("TestProperty_A", "Test");
        Assert.False(IServiceConfiguration.HasRequiredConfiguration<MultipleRequiredTestService>());

        Environment.SetEnvironmentVariable("TestProperty_B", "Test");
        Assert.True(IServiceConfiguration.HasRequiredConfiguration<MultipleRequiredTestService>());
    }

    [Fact]
    public void GlobalConfiguration_Root()
    {
        ResetENVs();
        var configRoot = IServiceConfiguration.BuildConfigurationRoot();
        Assert.NotNull(configRoot);
        Assert.Null(configRoot["ForceServiceID"]);
        Assert.Null(configRoot["forceserviceid"]);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        configRoot = IServiceConfiguration.BuildConfigurationRoot();
        Assert.NotNull(configRoot);
        Assert.Equal(serviceID, configRoot["ForceServiceID"]);
    }

    [Fact]
    public void GlobalConfiguration_Root_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var configRoot = IServiceConfiguration.BuildConfigurationRoot(args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.NotNull(configRoot);
        Assert.Equal(serviceID, configRoot["ForceServiceID"]);
    }

    [Fact]
    public void GlobalConfiguration_Root_WithPrefix()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("Test_ForceServiceID", serviceID);
        var configRoot = IServiceConfiguration.BuildConfigurationRoot(envPrefix: "test_");
        Assert.NotNull(configRoot);
        Assert.Null(configRoot["Test_ForceServiceID"]);
        Assert.Equal(serviceID, configRoot["ForceServiceID"]);
    }

    [Fact]
    public void GlobalConfiguration()
    {
        ResetENVs();
        var config = IServiceConfiguration.BuildConfiguration();
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = IServiceConfiguration.BuildConfiguration();
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void GlobalConfiguration_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = IServiceConfiguration.BuildConfiguration(new string[] {$"--ForceServiceID={serviceID}"});
        Assert.Equal(serviceID, config.ForceServiceID);

        config = IServiceConfiguration.BuildConfiguration(new string[] { $"--forceserviceid={serviceID}" });
        Assert.Equal(serviceID, config.ForceServiceID);

        config = IServiceConfiguration.BuildConfiguration();
        Assert.Null(config.ForceServiceID);
    }

    [Fact]
    public void GlobalConfiguration_WithPrefix()
    {
        ResetENVs();
        var config = IServiceConfiguration.BuildConfiguration(envPrefix: "test_");
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = IServiceConfiguration.BuildConfiguration(envPrefix: "test_");
        Assert.Null(config.ForceServiceID);

        Environment.SetEnvironmentVariable("test_ForceServiceID", serviceID);
        config = IServiceConfiguration.BuildConfiguration(envPrefix: "test_");
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration()
    {
        ResetENVs();
        var config = (TestConfiguration_NoAttribute?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_NoAttribute));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = (TestConfiguration_NoAttribute?)IServiceConfiguration.BuildConfiguration(
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
        var config = IServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_NoAttribute),
                        args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);

        config = IServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_NoAttribute),
                        args: new string[] { $"--forceserviceid={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_Generic()
    {
        ResetENVs();
        var config = IServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>();
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_Generic_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = IServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>(
                        args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.Equal(serviceID, config.ForceServiceID);

        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>(
                        args: new string[] { $"--forceserviceid={serviceID}" });
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_WithAttribute()
    {
        ResetENVs();
        var config = (TestConfiguration_Attribute_NoService?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_NoService));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = (TestConfiguration_Attribute_NoService?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_NoService));
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_Generic_WithAttribute()
    {
        ResetENVs();
        var config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_NoService>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_NoService>();
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_ForService()
    {
        ResetENVs();
        var config = (TestConfiguration_Attribute_TestService?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = (TestConfiguration_Attribute_TestService?)IServiceConfiguration.BuildConfiguration(
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
        var config = IServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_Attribute_TestService),
                        args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);

        config = IServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_Attribute_TestService),
                        args: new string[] { $"--forceserviceid={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_Generic_ForService()
    {
        ResetENVs();
        var config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>();
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_Generic_ForService_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>(
                        args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.Equal(serviceID, config.ForceServiceID);

        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>(
                        args: new string[] { $"--forceserviceid={serviceID}" });
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_ForService_WithPrefix()
    {
        ResetENVs();
        var config = (TestConfiguration_Attribute_TestService_WithPrefix?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService_WithPrefix), envPrefix: "test_");
        Assert.Null(config);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = (TestConfiguration_Attribute_TestService_WithPrefix?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService_WithPrefix), envPrefix: "test_");
        Assert.Null(config);

        Environment.SetEnvironmentVariable("test_TestProperty", "Test");
        Environment.SetEnvironmentVariable("test_ForceServiceID", serviceID);
        config = (TestConfiguration_Attribute_TestService_WithPrefix?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService_WithPrefix), envPrefix: "test_");
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_Generic_ForService_WithPrefix()
    {
        ResetENVs();
        var config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService_WithPrefix>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService_WithPrefix>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        Environment.SetEnvironmentVariable("test_TestProperty", "Test");
        Environment.SetEnvironmentVariable("test_ForceServiceID", serviceID);
        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService_WithPrefix>();
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_ByService()
    {
        ResetENVs();
        var config = (TestConfiguration_Attribute_TestService?)IServiceConfiguration.BuildServiceConfiguration<TestService>();
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = (TestConfiguration_Attribute_TestService?)IServiceConfiguration.BuildServiceConfiguration<TestService>();
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void TestConfiguration_ByService_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = IServiceConfiguration.BuildServiceConfiguration<TestService>(
                        args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);

        config = IServiceConfiguration.BuildServiceConfiguration<TestService>(
                        args: new string[] { $"--forceserviceid={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);
    }
}
