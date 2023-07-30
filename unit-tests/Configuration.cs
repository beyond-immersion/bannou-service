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

    private abstract class TestConfigBase : ServiceConfiguration { }

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
        TestCollectionContext.ResetENVs();
        var switchString = IServiceConfiguration.CreateSwitchFromName(nameof(TestConfiguration_NoAttribute.TestProperty));
        Assert.Equal("--testproperty", switchString);
    }

    [Fact]
    public void CreateSwitchMappings_All()
    {
        TestCollectionContext.ResetENVs();
        IDictionary<string, string>? switchLookup = IServiceConfiguration.CreateAllSwitchMappings();
        Assert.NotNull(switchLookup);
        Assert.Equal("Force_Service_ID", switchLookup["--force-service-id"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void CreateSwitchMappings_TestConfiguration_BadType()
    {
        TestCollectionContext.ResetENVs();
        _ = Assert.Throws<InvalidCastException>(() => IServiceConfiguration.CreateSwitchMappings(typeof(TestConfiguration_Invalid)));
    }

    [Fact]
    public void CreateSwitchMappings_TestConfiguration()
    {
        TestCollectionContext.ResetENVs();
        IDictionary<string, string> switchLookup = IServiceConfiguration.CreateSwitchMappings(typeof(TestConfiguration_NoAttribute));
        Assert.NotNull(switchLookup);
        Assert.Equal("Force_Service_ID", switchLookup["--force-service-id"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void CreateSwitchMappings_TestConfiguration_Generic()
    {
        TestCollectionContext.ResetENVs();
        IDictionary<string, string> switchLookup = IServiceConfiguration.CreateSwitchMappings<TestConfiguration_NoAttribute>();
        Assert.NotNull(switchLookup);
        Assert.Equal("Force_Service_ID", switchLookup["--force-service-id"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration()
    {
        TestCollectionContext.ResetENVs();
        IServiceConfiguration testConfig = IServiceConfiguration.BuildConfiguration<TestConfiguration_RequiredProperty>();
        Assert.False(testConfig.HasRequired());

        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        testConfig = IServiceConfiguration.BuildConfiguration<TestConfiguration_RequiredProperty>();
        Assert.True(testConfig.HasRequired());
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration_BadType()
    {
        TestCollectionContext.ResetENVs();
        _ = Assert.Throws<InvalidCastException>(() => IServiceConfiguration.HasRequiredForType(typeof(TestConfiguration_Invalid)));
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration_ByType()
    {
        TestCollectionContext.ResetENVs();
        Assert.False(IServiceConfiguration.HasRequiredForType(typeof(TestConfiguration_RequiredProperty)));

        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Assert.True(IServiceConfiguration.HasRequiredForType(typeof(TestConfiguration_RequiredProperty)));
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration_ByType_Generic()
    {
        TestCollectionContext.ResetENVs();
        Assert.False(IServiceConfiguration.HasRequiredForType<TestConfiguration_RequiredProperty>());

        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Assert.True(IServiceConfiguration.HasRequiredForType<TestConfiguration_RequiredProperty>());
    }

    [Fact]
    public void GlobalConfiguration_Root()
    {
        TestCollectionContext.ResetENVs();
        IConfigurationRoot configRoot = IServiceConfiguration.BuildConfigurationRoot();
        Assert.NotNull(configRoot);
        Assert.Null(configRoot["Force_Service_ID"]);
        Assert.Null(configRoot["force_service_id"]);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
        configRoot = IServiceConfiguration.BuildConfigurationRoot();
        Assert.NotNull(configRoot);
        Assert.Equal(serviceID, configRoot["Force_Service_ID"]);
    }

    [Fact]
    public void GlobalConfiguration_Root_WithArgs()
    {
        TestCollectionContext.ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        IConfigurationRoot configRoot = IServiceConfiguration.BuildConfigurationRoot(args: new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.NotNull(configRoot);
        Assert.Equal(serviceID, configRoot["Force_Service_ID"]);
    }

    [Fact]
    public void GlobalConfiguration_Root_WithPrefix()
    {
        TestCollectionContext.ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", serviceID);
        IConfigurationRoot configRoot = IServiceConfiguration.BuildConfigurationRoot(envPrefix: "test_");
        Assert.NotNull(configRoot);
        Assert.Null(configRoot["Test_Force_Service_ID"]);
        Assert.Equal(serviceID, configRoot["Force_Service_ID"]);
    }

    [Fact]
    public void GlobalConfiguration()
    {
        TestCollectionContext.ResetENVs();
        var config = IServiceConfiguration.BuildConfiguration();
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
        config = IServiceConfiguration.BuildConfiguration();
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void GlobalConfiguration_WithArgs()
    {
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
        var config = IServiceConfiguration.BuildConfiguration(envPrefix: "test_");
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
        config = IServiceConfiguration.BuildConfiguration(envPrefix: "test_");
        Assert.Null(config.Force_Service_ID);

        Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", serviceID);
        config = IServiceConfiguration.BuildConfiguration(envPrefix: "test_");
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void TestConfiguration_BadType()
    {
        TestCollectionContext.ResetENVs();
        _ = Assert.Throws<InvalidCastException>(() => IServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_Invalid)));
    }

    [Fact]
    public void TestConfiguration()
    {
        TestCollectionContext.ResetENVs();
        var config = (TestConfiguration_NoAttribute?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_NoAttribute));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
        config = (TestConfiguration_NoAttribute?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_NoAttribute));
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void TestConfiguration_WithArgs()
    {
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
        TestConfiguration_NoAttribute config = IServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_NoAttribute>();
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void TestConfiguration_Generic_WithArgs()
    {
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
        var config = (TestConfiguration_Attribute_NoService?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_NoService));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
        config = (TestConfiguration_Attribute_NoService?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_NoService));
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void TestConfiguration_Generic_WithAttribute()
    {
        TestCollectionContext.ResetENVs();
        TestConfiguration_Attribute_NoService config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_NoService>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_NoService>();
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void TestConfiguration_ForService()
    {
        TestCollectionContext.ResetENVs();
        var config = (TestConfiguration_Attribute_TestService?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
        config = (TestConfiguration_Attribute_TestService?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService));
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void TestConfiguration_ForService_WithArgs()
    {
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
        TestConfiguration_Attribute_TestService config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService>();
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void TestConfiguration_Generic_ForService_WithArgs()
    {
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
        var config = (TestConfiguration_Attribute_TestService_WithPrefix?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService_WithPrefix), envPrefix: "test_");
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
        config = (TestConfiguration_Attribute_TestService_WithPrefix?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService_WithPrefix), envPrefix: "test_");
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        Environment.SetEnvironmentVariable("TEST_TESTPROPERTY", "Test");
        Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", serviceID);
        config = (TestConfiguration_Attribute_TestService_WithPrefix?)IServiceConfiguration.BuildConfiguration(
                        typeof(TestConfiguration_Attribute_TestService_WithPrefix), envPrefix: "test_");
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void TestConfiguration_Generic_ForService_WithPrefix()
    {
        TestCollectionContext.ResetENVs();
        TestConfiguration_Attribute_TestService_WithPrefix config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService_WithPrefix>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService_WithPrefix>();
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        Environment.SetEnvironmentVariable("TEST_TESTPROPERTY", "Test");
        Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", serviceID);
        config = IServiceConfiguration.BuildConfiguration<TestConfiguration_Attribute_TestService_WithPrefix>();
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }
}
