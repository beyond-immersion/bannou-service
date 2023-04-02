using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace BeyondImmersion.UnitTests;

[Collection("unit tests")]
public class Configuration : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    [DaprService("Test")]
    private class TestService_Attribute : IDaprService { }

    [DaprService("test")]
    private class TestService_WithPrefix : IDaprService { }

    [DaprService("test")]
    private class TestService_Required : IDaprService { }

    [DaprService("test")]
    private class TestService_MultipleRequired : IDaprService { }

    private abstract class TestConfigBase : ServiceConfiguration { }

    private class TestConfiguration_Invalid
    {
        public string? TestProperty { get; set; }
    }

    private class TestConfiguration_Invalid_Attribute
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

    private Configuration(CollectionFixture collectionContext)
    {
        TestCollectionContext = collectionContext;
    }

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
        Assert.Equal("ForceServiceID", switchLookup["--forceserviceid"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void CreateSwitchMappings_TestConfiguration_BadType()
    {
        TestCollectionContext.ResetENVs();
        Assert.Throws<InvalidCastException>(() => IServiceConfiguration.CreateSwitchMappings(typeof(TestConfiguration_Invalid)));
    }

    [Fact]
    public void CreateSwitchMappings_TestConfiguration()
    {
        TestCollectionContext.ResetENVs();
        IDictionary<string, string> switchLookup = IServiceConfiguration.CreateSwitchMappings(typeof(TestConfiguration_NoAttribute));
        Assert.NotNull(switchLookup);
        Assert.Equal("ForceServiceID", switchLookup["--forceserviceid"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void CreateSwitchMappings_TestConfiguration_Generic()
    {
        TestCollectionContext.ResetENVs();
        IDictionary<string, string> switchLookup = IServiceConfiguration.CreateSwitchMappings<TestConfiguration_NoAttribute>();
        Assert.NotNull(switchLookup);
        Assert.Equal("ForceServiceID", switchLookup["--forceserviceid"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration()
    {
        TestCollectionContext.ResetENVs();
        IServiceConfiguration testConfig = IServiceConfiguration.BuildConfiguration<TestConfiguration_RequiredProperty>();
        Assert.False(testConfig.HasRequired());

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        testConfig = IServiceConfiguration.BuildConfiguration<TestConfiguration_RequiredProperty>();
        Assert.True(testConfig.HasRequired());
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration_BadType()
    {
        TestCollectionContext.ResetENVs();
        Assert.Throws<InvalidCastException>(() => IServiceConfiguration.HasRequiredForType(typeof(TestConfiguration_Invalid)));
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration_ByType()
    {
        TestCollectionContext.ResetENVs();
        Assert.False(IServiceConfiguration.HasRequiredForType(typeof(TestConfiguration_RequiredProperty)));

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.True(IServiceConfiguration.HasRequiredForType(typeof(TestConfiguration_RequiredProperty)));
    }

    [Fact]
    public void HasRequiredConfig_TestConfiguration_ByType_Generic()
    {
        TestCollectionContext.ResetENVs();
        Assert.False(IServiceConfiguration.HasRequiredForType<TestConfiguration_RequiredProperty>());

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.True(IServiceConfiguration.HasRequiredForType<TestConfiguration_RequiredProperty>());
    }

    [Fact]
    public void GlobalConfiguration_Root()
    {
        TestCollectionContext.ResetENVs();
        IConfigurationRoot configRoot = IServiceConfiguration.BuildConfigurationRoot();
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
        TestCollectionContext.ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        IConfigurationRoot configRoot = IServiceConfiguration.BuildConfigurationRoot(args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.NotNull(configRoot);
        Assert.Equal(serviceID, configRoot["ForceServiceID"]);
    }

    [Fact]
    public void GlobalConfiguration_Root_WithPrefix()
    {
        TestCollectionContext.ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("Test_ForceServiceID", serviceID);
        IConfigurationRoot configRoot = IServiceConfiguration.BuildConfigurationRoot(envPrefix: "test_");
        Assert.NotNull(configRoot);
        Assert.Null(configRoot["Test_ForceServiceID"]);
        Assert.Equal(serviceID, configRoot["ForceServiceID"]);
    }

    [Fact]
    public void GlobalConfiguration()
    {
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
    public void TestConfiguration_BadType()
    {
        TestCollectionContext.ResetENVs();
        Assert.Throws<InvalidCastException>(() => IServiceConfiguration.BuildConfiguration(typeof(TestConfiguration_Invalid)));
    }

    [Fact]
    public void TestConfiguration()
    {
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
}
