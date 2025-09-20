using BeyondImmersion.BannouService.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.UnitTests;

[Collection("unit tests")]
public class Configuration : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    [DaprService("ConfigTests.Test")]
    private class Service_Attribute : IDaprService { }

    [DaprService("ConfigTests.test")]
    private class Service_WithPrefix : IDaprService { }

    [DaprService("ConfigTests.test")]
    private class Service_Required : IDaprService { }

    private abstract class ConfigBase : IServiceConfiguration
    {
        public string? Force_Service_ID { get; set; }
        public bool? Service_Disabled { get; set; }
    }

    private class Configuration_Invalid
    {
        public string? Property { get; set; }
    }

    [ServiceConfiguration]
    private class Configuration_Attribute_NoService : ConfigBase
    {
        public string? Property { get; set; }
    }

    [ServiceConfiguration(typeof(Service_Attribute))]
    private class Configuration_Attribute_TestService : ConfigBase
    {
        public string? Property { get; set; }
    }

    [ServiceConfiguration(typeof(Service_WithPrefix), envPrefix: "test_")]
    private class Configuration_Attribute_TestService_WithPrefix : ConfigBase
    {
        public string? Property { get; set; }
    }

    [ServiceConfiguration(typeof(Service_WithPrefix), envPrefix: "BANNOU_")]
    private class Configuration_Attribute_BannouPrefix : ConfigBase
    {
        public string? JwtSecret { get; set; }
        public string? Property { get; set; }
    }

    private class Configuration_NoAttribute : ConfigBase
    {
        public string? Property { get; set; }
    }

    [ServiceConfiguration(typeof(Service_Required))]
    private class Configuration_RequiredProperty : ConfigBase
    {
        [ConfigRequired(AllowEmptyStrings = false)]
        public string? Property { get; set; }
    }

    private Configuration(CollectionFixture collectionContext) => TestCollectionContext = collectionContext;

    public Configuration(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Configuration>();
    }

    [Fact]
    public void Configuration_CreateSwitchFromProperty()
    {
        var switchString = IServiceConfiguration.CreateSwitchFromName(nameof(Configuration_NoAttribute.Property));
        Assert.Equal("--property", switchString);
    }

    [Fact]
    public void Configuration_CreateAllSwitchMappings()
    {
        IDictionary<string, string>? switchLookup = IServiceConfiguration.CreateAllSwitchMappings();
        Assert.NotNull(switchLookup);
        Assert.Equal("Force_Service_ID", switchLookup["--force-service-id"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void Configuration_CreateSwitchMappings_Invalid()
    {
        _ = Assert.Throws<InvalidCastException>(() => IServiceConfiguration.CreateSwitchMappings(typeof(Configuration_Invalid)));
    }

    [Fact]
    public void Configuration_CreateSwitchMappings_NoAttr()
    {
        IDictionary<string, string> switchLookup = IServiceConfiguration.CreateSwitchMappings(typeof(Configuration_NoAttribute));
        Assert.NotNull(switchLookup);
        Assert.Equal("Force_Service_ID", switchLookup["--force-service-id"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void Configuration_CreateSwitchMappings_NoAttr_Generic()
    {
        IDictionary<string, string> switchLookup = IServiceConfiguration.CreateSwitchMappings<Configuration_NoAttribute>();
        Assert.NotNull(switchLookup);
        Assert.Equal("Force_Service_ID", switchLookup["--force-service-id"]);
        Assert.False(switchLookup.ContainsKey("--test-service-enabled"));
    }

    [Fact]
    public void Configuration_HasRequired()
    {
        Environment.SetEnvironmentVariable("PROPERTY", null);
        Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_PROPERTY", null);

        IServiceConfiguration testConfig = IServiceConfiguration.BuildConfiguration<Configuration_RequiredProperty>();
        Assert.False(testConfig.HasRequired());

        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            testConfig = IServiceConfiguration.BuildConfiguration<Configuration_RequiredProperty>();
            Assert.False(testConfig.HasRequired());
            Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_PROPERTY", "Test");
            testConfig = IServiceConfiguration.BuildConfiguration<Configuration_RequiredProperty>();
            Assert.True(testConfig.HasRequired());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROPERTY", null);
            Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_PROPERTY", null);
        }
    }

    [Fact]
    public void Configuration_HasRequired_Invalid()
    {
        _ = Assert.Throws<InvalidCastException>(() => IServiceConfiguration.HasRequiredForType(typeof(Configuration_Invalid)));
    }

    [Fact]
    public void Configuration_HasRequired_ByType()
    {
        Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_PROPERTY", null);
        Assert.False(IServiceConfiguration.HasRequiredForType(typeof(Configuration_RequiredProperty)));

        try
        {
            Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_PROPERTY", "Test");
            Assert.True(IServiceConfiguration.HasRequiredForType(typeof(Configuration_RequiredProperty)));
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_PROPERTY", null);
        }
    }

    [Fact]
    public void Configuration_HasRequired_ByType_Generic()
    {
        Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_PROPERTY", null);
        Assert.False(IServiceConfiguration.HasRequiredForType<Configuration_RequiredProperty>());

        try
        {
            Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_PROPERTY", "Test");
            Assert.True(IServiceConfiguration.HasRequiredForType<Configuration_RequiredProperty>());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ConfigTests.test".ToUpper()}_PROPERTY", null);
        }
    }

    [Fact]
    public void Configuration_AppConfigRoot()
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
    public void Configuration_AppConfigRoot_WithArgs()
    {
        var serviceID = Guid.NewGuid().ToString().ToLower();
        IConfigurationRoot configRoot = IServiceConfiguration.BuildConfigurationRoot(args: new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.NotNull(configRoot);
        Assert.Equal(serviceID, configRoot["Force_Service_ID"]);
    }

    [Fact]
    public void Configuration_AppConfigRoot_WithPrefix()
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
    public void Configuration_AppConfig()
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
    public void Configuration_AppConfig_WithArgs()
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
    public void Configuration_AppConfig_WithPrefix()
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
    public void Configuration_NoAttr()
    {
        Environment.SetEnvironmentVariable("PROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);

        var config = (Configuration_NoAttribute?)IServiceConfiguration.BuildConfiguration(
                        typeof(Configuration_NoAttribute));
        Assert.NotNull(config);
        Assert.Null(config.Property);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = (Configuration_NoAttribute?)IServiceConfiguration.BuildConfiguration(
                            typeof(Configuration_NoAttribute));
            Assert.NotNull(config);
            Assert.Equal("Test", config.Property);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void Configuration_NoAttr_WithArgs()
    {
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = IServiceConfiguration.BuildConfiguration(typeof(Configuration_NoAttribute),
                        args: new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.Force_Service_ID);

        config = IServiceConfiguration.BuildConfiguration(typeof(Configuration_NoAttribute),
                        args: new string[] { $"--force-service-id={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void Configuration_NoAttr_Generic()
    {
        Environment.SetEnvironmentVariable("PROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);

        Configuration_NoAttribute config = IServiceConfiguration.BuildConfiguration<Configuration_NoAttribute>();
        Assert.Null(config.Property);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration<Configuration_NoAttribute>();
            Assert.Equal("Test", config.Property);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void Configuration_NoAttr_WithArgs_Generic()
    {
        var serviceID = Guid.NewGuid().ToString().ToLower();
        Configuration_NoAttribute config = IServiceConfiguration.BuildConfiguration<Configuration_NoAttribute>(
                        args: new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.Equal(serviceID, config.Force_Service_ID);

        config = IServiceConfiguration.BuildConfiguration<Configuration_NoAttribute>(
                        args: new string[] { $"--force-service-id={serviceID}" });
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void Configuration_WithAttr_NoService()
    {
        Environment.SetEnvironmentVariable("PROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);

        var config = (Configuration_Attribute_NoService?)IServiceConfiguration.BuildConfiguration(
                        typeof(Configuration_Attribute_NoService));
        Assert.NotNull(config);
        Assert.Null(config.Property);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = (Configuration_Attribute_NoService?)IServiceConfiguration.BuildConfiguration(
                            typeof(Configuration_Attribute_NoService));
            Assert.NotNull(config);
            Assert.Equal("Test", config.Property);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void Configuration_WithAttr_NoService_Generic()
    {
        Environment.SetEnvironmentVariable("PROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);

        Configuration_Attribute_NoService config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_NoService>();
        Assert.Null(config.Property);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_NoService>();
            Assert.Equal("Test", config.Property);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void Configuration_ForService()
    {
        Environment.SetEnvironmentVariable("PROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);

        var config = (Configuration_Attribute_TestService?)IServiceConfiguration.BuildConfiguration(
                        typeof(Configuration_Attribute_TestService));
        Assert.NotNull(config);
        Assert.Null(config.Property);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = (Configuration_Attribute_TestService?)IServiceConfiguration.BuildConfiguration(
                            typeof(Configuration_Attribute_TestService));
            Assert.NotNull(config);
            Assert.Equal("Test", config.Property);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void Configuration_ForService_WithArgs()
    {
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = IServiceConfiguration.BuildConfiguration(typeof(Configuration_Attribute_TestService),
                        args: new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.Force_Service_ID);

        config = IServiceConfiguration.BuildConfiguration(typeof(Configuration_Attribute_TestService),
                        args: new string[] { $"--force-service-id={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void Configuration_ForService_Generic()
    {
        Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_PROPERTY", null);
        Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_FORCE_SERVICE_ID", null);

        Configuration_Attribute_TestService config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_TestService>();
        Assert.Null(config.Property);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_PROPERTY", "Test");
            Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_FORCE_SERVICE_ID", serviceID);

            config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_TestService>();
            Assert.Equal("Test", config.Property);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_PROPERTY", null);
            Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void Configuration_ForService_WithArgs_Generic()
    {
        var serviceID = Guid.NewGuid().ToString().ToLower();
        Configuration_Attribute_TestService config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_TestService>(
                        args: new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.Equal(serviceID, config.Force_Service_ID);

        config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_TestService>(
                        args: new string[] { $"--force-service-id={serviceID}" });
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void Configuration_ForService_WithPrefix()
    {
        Environment.SetEnvironmentVariable("PROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        Environment.SetEnvironmentVariable("TEST_PROPERTY", null);
        Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", null);

        var config = (Configuration_Attribute_TestService_WithPrefix?)IServiceConfiguration.BuildConfiguration(
                        typeof(Configuration_Attribute_TestService_WithPrefix), envPrefix: "test_");
        Assert.NotNull(config);
        Assert.Null(config.Property);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = (Configuration_Attribute_TestService_WithPrefix?)IServiceConfiguration.BuildConfiguration(
                            typeof(Configuration_Attribute_TestService_WithPrefix), envPrefix: "test_");
            Assert.NotNull(config);
            Assert.Null(config.Property);
            Assert.Null(config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }

        try
        {
            Environment.SetEnvironmentVariable("TEST_PROPERTY", "Test");
            Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", serviceID);
            config = (Configuration_Attribute_TestService_WithPrefix?)IServiceConfiguration.BuildConfiguration(
                            typeof(Configuration_Attribute_TestService_WithPrefix), envPrefix: "test_");
            Assert.NotNull(config);
            Assert.Equal("Test", config.Property);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_PROPERTY", null);
            Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void Configuration_ForService_WithPrefix_Generic()
    {
        Environment.SetEnvironmentVariable("PROPERTY", null);
        Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        Environment.SetEnvironmentVariable("TEST_PROPERTY", null);
        Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", null);

        Configuration_Attribute_TestService_WithPrefix config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_TestService_WithPrefix>();
        Assert.Null(config.Property);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_TestService_WithPrefix>();
            Assert.Null(config.Property);
            Assert.Null(config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROPERTY", null);
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", null);
        }

        try
        {
            Environment.SetEnvironmentVariable("TEST_PROPERTY", "Test");
            Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_TestService_WithPrefix>();
            Assert.Equal("Test", config.Property);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_PROPERTY", null);
            Environment.SetEnvironmentVariable("TEST_FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void Configuration_ForService_WithBannouPrefix_Generic()
    {
        Environment.SetEnvironmentVariable("PROPERTY", null);
        Environment.SetEnvironmentVariable("JWTSECRET", null);
        Environment.SetEnvironmentVariable("BANNOU_PROPERTY", null);
        Environment.SetEnvironmentVariable("BANNOU_JWTSECRET", null);

        Configuration_Attribute_BannouPrefix config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_BannouPrefix>();
        Assert.Null(config.Property);
        Assert.Null(config.JwtSecret);

        var testSecret = "bannou-test-jwt-secret";
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("JWTSECRET", testSecret);
            config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_BannouPrefix>();
            Assert.Null(config.Property);
            Assert.Null(config.JwtSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROPERTY", null);
            Environment.SetEnvironmentVariable("JWTSECRET", null);
        }

        try
        {
            Environment.SetEnvironmentVariable("BANNOU_PROPERTY", "Test");
            Environment.SetEnvironmentVariable("BANNOU_JWTSECRET", testSecret);
            config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_BannouPrefix>();
            Assert.Equal("Test", config.Property);
            Assert.Equal(testSecret, config.JwtSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BANNOU_PROPERTY", null);
            Environment.SetEnvironmentVariable("BANNOU_JWTSECRET", null);
        }
    }
}
