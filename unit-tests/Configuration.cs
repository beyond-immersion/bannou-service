using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace BeyondImmersion.UnitTests.Configuration;

[Collection("unit tests")]
public class Configuration : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    [DaprService("ConfigTests.Test")]
    private class TestService_Attribute : IDaprService { }

    [DaprService("ConfigTests.test")]
    private class TestService_WithPrefix : IDaprService { }

    [DaprService("ConfigTests.test")]
    private class TestService_Required : IDaprService { }

    private abstract class TestConfigBase : IServiceConfiguration
    {
        public string? Force_Service_ID { get; set; }
    }

    private class TestConfiguration_Invalid
    {
        public string? TestProperty { get; set; }
    }

    [ServiceConfiguration]
    private class TestConfiguration_Attribute_NoService : TestConfigBase
    {
        public string? TestProperty { get; set; }
    }

    [ServiceConfiguration(typeof(TestService_Attribute))]
    private class TestConfiguration_Attribute_TestService : TestConfigBase
    {
        public string? TestProperty { get; set; }
    }

    [ServiceConfiguration(typeof(TestService_WithPrefix), envPrefix: "test_")]
    private class TestConfiguration_Attribute_TestService_WithPrefix : TestConfigBase
    {
        public string? TestProperty { get; set; }
    }

    private class TestConfiguration_NoAttribute : TestConfigBase
    {
        public string? TestProperty { get; set; }
    }

    [ServiceConfiguration(typeof(TestService_Required))]
    private class TestConfiguration_RequiredProperty : TestConfigBase
    {
        [ConfigRequired(AllowEmptyStrings = false)]
        public string? TestProperty { get; set; }
    }

    private Configuration(CollectionFixture collectionContext) => TestCollectionContext = collectionContext;

    public Configuration(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Configuration>();
    }

    [Fact]
    public void CreateSwitchFromProperty()
    {
        var switchString = IServiceConfiguration.CreateSwitchFromName(nameof(TestConfiguration_NoAttribute.TestProperty));
        Assert.Equal("--testproperty", switchString);
    }

    [Fact]
    public void CreateSwitchMappings_All()
    {
        IDictionary<string, string>? switchLookup = IServiceConfiguration.CreateAllSwitchMappings();
        Assert.NotNull(switchLookup);
        Assert.Equal("Force_Service_ID", switchLookup["--force-service-id"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void CreateSwitchMappings_TestConfiguration_BadType()
    {
        _ = Assert.Throws<InvalidCastException>(() => IServiceConfiguration.CreateSwitchMappings(typeof(TestConfiguration_Invalid)));
    }

    [Fact]
    public void CreateSwitchMappings_TestConfiguration()
    {
        IDictionary<string, string> switchLookup = IServiceConfiguration.CreateSwitchMappings(typeof(TestConfiguration_NoAttribute));
        Assert.NotNull(switchLookup);
        Assert.Equal("Force_Service_ID", switchLookup["--force-service-id"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void CreateSwitchMappings_TestConfiguration_Generic()
    {
        IDictionary<string, string> switchLookup = IServiceConfiguration.CreateSwitchMappings<TestConfiguration_NoAttribute>();
        Assert.NotNull(switchLookup);
        Assert.Equal("Force_Service_ID", switchLookup["--force-service-id"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration()
    {
        Environment.SetEnvironmentVariable("TESTPROPERTY", null);
        Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_TESTPROPERTY", null);

        IServiceConfiguration testConfig = IServiceConfiguration.BuildConfiguration<TestConfiguration_RequiredProperty>();
        Assert.False(testConfig.HasRequired());

        try
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
            testConfig = IServiceConfiguration.BuildConfiguration<TestConfiguration_RequiredProperty>();
            Assert.False(testConfig.HasRequired());
            Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_TESTPROPERTY", "Test");
            testConfig = IServiceConfiguration.BuildConfiguration<TestConfiguration_RequiredProperty>();
            Assert.True(testConfig.HasRequired());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", null);
            Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_TESTPROPERTY", null);
        }
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration_BadType()
    {
        _ = Assert.Throws<InvalidCastException>(() => IServiceConfiguration.HasRequiredForType(typeof(TestConfiguration_Invalid)));
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration_ByType()
    {
        Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_TESTPROPERTY", null);
        Assert.False(IServiceConfiguration.HasRequiredForType(typeof(TestConfiguration_RequiredProperty)));

        try
        {
            Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_TESTPROPERTY", "Test");
            Assert.True(IServiceConfiguration.HasRequiredForType(typeof(TestConfiguration_RequiredProperty)));
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_TESTPROPERTY", null);
        }
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration_ByType_Generic()
    {
        Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_TESTPROPERTY", null);
        Assert.False(IServiceConfiguration.HasRequiredForType<TestConfiguration_RequiredProperty>());

        try
        {
            Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_TESTPROPERTY", "Test");
            Assert.True(IServiceConfiguration.HasRequiredForType<TestConfiguration_RequiredProperty>());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_TESTPROPERTY", null);
        }
    }

    [Fact]
    public void GlobalConfiguration_Root()
    {
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);

        IConfigurationRoot configRoot = IServiceConfiguration.BuildConfigurationRoot();
        Assert.NotNull(configRoot);
        Assert.Null(configRoot["Force_Service_ID"]);
        Assert.Null(configRoot["force_service_id"]);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            configRoot = IServiceConfiguration.BuildConfigurationRoot();
            Assert.NotNull(configRoot);
            Assert.Equal(serviceID, configRoot["Force_Service_ID"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void GlobalConfiguration_Root_WithArgs()
    {
        var serviceID = Guid.NewGuid().ToString().ToLower();
        IConfigurationRoot configRoot = IServiceConfiguration.BuildConfigurationRoot(args: new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.NotNull(configRoot);
        Assert.Equal(serviceID, configRoot["Force_Service_ID"]);
    }

    [Fact]
    public void GlobalConfiguration_Root_WithPrefix()
    {
        Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", null);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", serviceID);
            IConfigurationRoot configRoot = IServiceConfiguration.BuildConfigurationRoot(envPrefix: "test_");
            Assert.NotNull(configRoot);
            Assert.Null(configRoot["Test_Force_Service_ID"]);
            Assert.Equal(serviceID, configRoot["Force_Service_ID"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void GlobalConfiguration()
    {
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);

        var config = IServiceConfiguration.BuildConfiguration();
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration();
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void GlobalConfiguration_WithArgs()
    {
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = IServiceConfiguration.BuildConfiguration(new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.Equal(serviceID, config.Force_Service_ID);

        config = IServiceConfiguration.BuildConfiguration(new string[] { $"--force-service-id={serviceID}" });
        Assert.Equal(serviceID, config.Force_Service_ID);

        config = IServiceConfiguration.BuildConfiguration();
        Assert.Null(config.Force_Service_ID);
    }

    [Fact]
    public void GlobalConfiguration_WithPrefix()
    {
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", null);

        var config = IServiceConfiguration.BuildConfiguration(envPrefix: "test_");
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration(envPrefix: "test_");
            Assert.Null(config.Force_Service_ID);
            Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration(envPrefix: "test_");
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
            Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void TestConfiguration_BadType()
    {
        _ = Assert.Throws<InvalidCastException>(() => IServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_Invalid)));
    }

    [Fact]
    public void TestConfiguration()
    {
        Environment.SetEnvironmentVariable("TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);

        var config = (TestConfiguration_NoAttribute?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_NoAttribute));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = (TestConfiguration_NoAttribute?)IServiceConfiguration.BuildConfiguration(
                            typeof(TestConfiguration_NoAttribute));
            Assert.NotNull(config);
            Assert.Equal("Test", config.TestProperty);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void TestConfiguration_WithArgs()
    {
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = IServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_NoAttribute),
                        args: new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.Force_Service_ID);

        config = IServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_NoAttribute),
                        args: new string[] { $"--force-service-id={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void TestConfiguration_Generic()
    {
        Environment.SetEnvironmentVariable("TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);

        TestConfiguration_NoAttribute config = IServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>();
            Assert.Equal("Test", config.TestProperty);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void TestConfiguration_Generic_WithArgs()
    {
        var serviceID = Guid.NewGuid().ToString().ToLower();
        TestConfiguration_NoAttribute config = IServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>(
                        args: new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.Equal(serviceID, config.Force_Service_ID);

        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>(
                        args: new string[] { $"--force-service-id={serviceID}" });
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void TestConfiguration_WithAttribute()
    {
        Environment.SetEnvironmentVariable("TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);

        var config = (TestConfiguration_Attribute_NoService?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_NoService));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = (TestConfiguration_Attribute_NoService?)IServiceConfiguration.BuildConfiguration(
                            typeof(TestConfiguration_Attribute_NoService));
            Assert.NotNull(config);
            Assert.Equal("Test", config.TestProperty);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void TestConfiguration_Generic_WithAttribute()
    {
        Environment.SetEnvironmentVariable("TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);

        TestConfiguration_Attribute_NoService config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_NoService>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_NoService>();
            Assert.Equal("Test", config.TestProperty);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void TestConfiguration_ForService()
    {
        Environment.SetEnvironmentVariable("TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);

        var config = (TestConfiguration_Attribute_TestService?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = (TestConfiguration_Attribute_TestService?)IServiceConfiguration.BuildConfiguration(
                            typeof(TestConfiguration_Attribute_TestService));
            Assert.NotNull(config);
            Assert.Equal("Test", config.TestProperty);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void TestConfiguration_ForService_WithArgs()
    {
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = IServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_Attribute_TestService),
                        args: new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.Force_Service_ID);

        config = IServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_Attribute_TestService),
                        args: new string[] { $"--force-service-id={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void TestConfiguration_Generic_ForService()
    {
        Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_TESTPROPERTY", null);
        Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_FORCE_SERVICE_ID", null);

        TestConfiguration_Attribute_TestService config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_TESTPROPERTY", "Test");
            Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_FORCE_SERVICE_ID", serviceID);

            config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>();
            Assert.Equal("Test", config.TestProperty);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_TESTPROPERTY", null);
            Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void TestConfiguration_Generic_ForService_WithArgs()
    {
        var serviceID = Guid.NewGuid().ToString().ToLower();
        TestConfiguration_Attribute_TestService config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>(
                        args: new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.Equal(serviceID, config.Force_Service_ID);

        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>(
                        args: new string[] { $"--force-service-id={serviceID}" });
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void TestConfiguration_ForService_WithPrefix()
    {
        Environment.SetEnvironmentVariable("TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        Environment.SetEnvironmentVariable("TEST_TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", null);

        var config = (TestConfiguration_Attribute_TestService_WithPrefix?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService_WithPrefix), envPrefix: "test_");
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = (TestConfiguration_Attribute_TestService_WithPrefix?)IServiceConfiguration.BuildConfiguration(
                            typeof(TestConfiguration_Attribute_TestService_WithPrefix), envPrefix: "test_");
            Assert.NotNull(config);
            Assert.Null(config.TestProperty);
            Assert.Null(config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }

        try
        {
            Environment.SetEnvironmentVariable("TEST_TESTPROPERTY", "Test");
            Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", serviceID);
            config = (TestConfiguration_Attribute_TestService_WithPrefix?)IServiceConfiguration.BuildConfiguration(
                            typeof(TestConfiguration_Attribute_TestService_WithPrefix), envPrefix: "test_");
            Assert.NotNull(config);
            Assert.Equal("Test", config.TestProperty);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_TESTPROPERTY", null);
            Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void TestConfiguration_Generic_ForService_WithPrefix()
    {
        Environment.SetEnvironmentVariable("TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        Environment.SetEnvironmentVariable("TEST_TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", null);

        TestConfiguration_Attribute_TestService_WithPrefix config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService_WithPrefix>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService_WithPrefix>();
            Assert.Null(config.TestProperty);
            Assert.Null(config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }

        try
        {
            Environment.SetEnvironmentVariable("TEST_TESTPROPERTY", "Test");
            Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService_WithPrefix>();
            Assert.Equal("Test", config.TestProperty);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_TESTPROPERTY", null);
            Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", null);
        }
    }
}
