using BeyondImmersion.BannouService.Attributes;
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

    [DaprService("game-session")]
    private class Service_HyphenatedName : IDaprService { }

    [DaprService("relationship-type")]
    private class Service_MultiHyphenatedName : IDaprService { }

    private abstract class ConfigBase : IServiceConfiguration
    {
        // Note: Force_Service_ID uses legacy underscore naming from interface
        // New tests should use PascalCase properties (Property, TestValue, etc.)
        public string? Force_Service_ID { get; set; }
        public string? ForceServiceId { get; set; } // PascalCase version for normalization tests
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

    [ServiceConfiguration(typeof(Service_HyphenatedName))]
    private class Configuration_HyphenatedService : ConfigBase
    {
        public string? ServerSalt { get; set; }
        public int MaxPlayersPerSession { get; set; }
    }

    [ServiceConfiguration(typeof(Service_MultiHyphenatedName))]
    private class Configuration_MultiHyphenatedService : ConfigBase
    {
        public string? TypeName { get; set; }
        public bool Enabled { get; set; }
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
        Assert.Null(config.ForceServiceId);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = (Configuration_NoAttribute?)IServiceConfiguration.BuildConfiguration(
                            typeof(Configuration_NoAttribute));
            Assert.NotNull(config);
            Assert.Equal("Test", config.Property);
            // FORCE_SERVICE_ID normalizes to ForceServiceId (PascalCase)
            Assert.Equal(serviceID, config.ForceServiceId);
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
        Assert.Null(config.ForceServiceId);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration<Configuration_NoAttribute>();
            Assert.Equal("Test", config.Property);
            // FORCE_SERVICE_ID normalizes to ForceServiceId (PascalCase)
            Assert.Equal(serviceID, config.ForceServiceId);
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
        Assert.Null(config.ForceServiceId);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = (Configuration_Attribute_NoService?)IServiceConfiguration.BuildConfiguration(
                            typeof(Configuration_Attribute_NoService));
            Assert.NotNull(config);
            Assert.Equal("Test", config.Property);
            // FORCE_SERVICE_ID normalizes to ForceServiceId (PascalCase)
            Assert.Equal(serviceID, config.ForceServiceId);
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
        Assert.Null(config.ForceServiceId);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_NoService>();
            Assert.Equal("Test", config.Property);
            // FORCE_SERVICE_ID normalizes to ForceServiceId (PascalCase)
            Assert.Equal(serviceID, config.ForceServiceId);
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
        Assert.Null(config.ForceServiceId);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = (Configuration_Attribute_TestService?)IServiceConfiguration.BuildConfiguration(
                            typeof(Configuration_Attribute_TestService));
            Assert.NotNull(config);
            Assert.Equal("Test", config.Property);
            // FORCE_SERVICE_ID normalizes to ForceServiceId (PascalCase)
            Assert.Equal(serviceID, config.ForceServiceId);
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
        Assert.Null(config.ForceServiceId);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_PROPERTY", "Test");
            Environment.SetEnvironmentVariable($"{"ConfigTests.Test".ToUpper()}_FORCE_SERVICE_ID", serviceID);

            config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_TestService>();
            Assert.Equal("Test", config.Property);
            // FORCE_SERVICE_ID normalizes to ForceServiceId (PascalCase)
            Assert.Equal(serviceID, config.ForceServiceId);
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
        Assert.Null(config.ForceServiceId);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = (Configuration_Attribute_TestService_WithPrefix?)IServiceConfiguration.BuildConfiguration(
                            typeof(Configuration_Attribute_TestService_WithPrefix), envPrefix: "test_");
            Assert.NotNull(config);
            Assert.Null(config.Property);
            Assert.Null(config.ForceServiceId);
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
            // TEST_FORCE_SERVICE_ID normalizes to ForceServiceId (PascalCase)
            Assert.Equal(serviceID, config.ForceServiceId);
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
        Assert.Null(config.ForceServiceId);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("FORCE_SERVICE_ID", serviceID);
            config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_TestService_WithPrefix>();
            Assert.Null(config.Property);
            Assert.Null(config.ForceServiceId);
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
            // TEST_FORCE_SERVICE_ID normalizes to ForceServiceId (PascalCase)
            Assert.Equal(serviceID, config.ForceServiceId);
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
        // Clear any existing env vars - use UPPER_SNAKE_CASE format for normalization
        Environment.SetEnvironmentVariable("PROPERTY", null);
        Environment.SetEnvironmentVariable("JWT_SECRET", null);
        Environment.SetEnvironmentVariable("BANNOU_PROPERTY", null);
        Environment.SetEnvironmentVariable("BANNOU_JWT_SECRET", null);

        Configuration_Attribute_BannouPrefix config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_BannouPrefix>();
        Assert.Null(config.Property);
        Assert.Null(config.JwtSecret);

        var testSecret = "bannou-test-jwt-secret";
        try
        {
            // Set env vars WITHOUT prefix - should not be picked up
            Environment.SetEnvironmentVariable("PROPERTY", "Test");
            Environment.SetEnvironmentVariable("JWT_SECRET", testSecret);
            config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_BannouPrefix>();
            Assert.Null(config.Property);
            Assert.Null(config.JwtSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROPERTY", null);
            Environment.SetEnvironmentVariable("JWT_SECRET", null);
        }

        try
        {
            // Set env vars WITH prefix - should be picked up
            // BANNOU_JWT_SECRET -> strip prefix -> JWT_SECRET -> normalize -> JwtSecret
            Environment.SetEnvironmentVariable("BANNOU_PROPERTY", "Test");
            Environment.SetEnvironmentVariable("BANNOU_JWT_SECRET", testSecret);
            config = IServiceConfiguration.BuildConfiguration<Configuration_Attribute_BannouPrefix>();
            Assert.Equal("Test", config.Property);
            Assert.Equal(testSecret, config.JwtSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable("BANNOU_PROPERTY", null);
            Environment.SetEnvironmentVariable("BANNOU_JWT_SECRET", null);
        }
    }

    #region Environment Variable Key Normalization Tests

    [Theory]
    [InlineData("STORAGE_ACCESS_KEY", "StorageAccessKey")]
    [InlineData("REDIS_CONNECTION_STRING", "RedisConnectionString")]
    [InlineData("SERVER_SALT", "ServerSalt")]
    [InlineData("MAX_PLAYERS_PER_SESSION", "MaxPlayersPerSession")]
    [InlineData("ENABLED", "Enabled")]
    [InlineData("JWT_SECRET", "JwtSecret")]
    [InlineData("SINGLE", "Single")]
    [InlineData("", "")]
    public void NormalizeEnvVarKey_ConvertsUpperSnakeCaseToPascalCase(string input, string expected)
    {
        var result = IServiceConfiguration.NormalizeEnvVarKey(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("FORCE_SERVICE_ID", "ForceServiceId")]
    [InlineData("DEFAULT_CONSISTENCY", "DefaultConsistency")]
    [InlineData("ENABLE_METRICS", "EnableMetrics")]
    public void NormalizeEnvVarKey_HandlesCommonPatterns(string input, string expected)
    {
        var result = IServiceConfiguration.NormalizeEnvVarKey(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetNormalizedEnvVars_FiltersAndNormalizesWithPrefix()
    {
        // Arrange - set up env vars with TEST_ prefix
        Environment.SetEnvironmentVariable("TEST_STORAGE_ACCESS_KEY", "test-key");
        Environment.SetEnvironmentVariable("TEST_MAX_SIZE", "100");
        Environment.SetEnvironmentVariable("OTHER_VALUE", "should-be-ignored");

        try
        {
            // Act
            var result = IServiceConfiguration.GetNormalizedEnvVars("TEST_");

            // Assert
            Assert.True(result.ContainsKey("StorageAccessKey"));
            Assert.Equal("test-key", result["StorageAccessKey"]);
            Assert.True(result.ContainsKey("MaxSize"));
            Assert.Equal("100", result["MaxSize"]);
            Assert.False(result.ContainsKey("OtherValue"));
            Assert.False(result.ContainsKey("OTHER_VALUE"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_STORAGE_ACCESS_KEY", null);
            Environment.SetEnvironmentVariable("TEST_MAX_SIZE", null);
            Environment.SetEnvironmentVariable("OTHER_VALUE", null);
        }
    }

    [Fact]
    public void GetNormalizedEnvVars_CaseInsensitivePrefixMatching()
    {
        // Arrange
        Environment.SetEnvironmentVariable("test_property", "lowercase-prefix");

        try
        {
            // Act - prefix matching should be case-insensitive
            var result = IServiceConfiguration.GetNormalizedEnvVars("TEST_");

            // Assert
            Assert.True(result.ContainsKey("Property"));
            Assert.Equal("lowercase-prefix", result["Property"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("test_property", null);
        }
    }

    #endregion

    #region Hyphenated Service Name Prefix Tests

    [Fact]
    public void ServiceConfigurationAttribute_RemovesHyphensFromPrefix()
    {
        // Arrange & Act - get the attribute from our test configuration
        var configType = typeof(Configuration_HyphenatedService);
        var attr = configType.GetCustomAttributes(typeof(ServiceConfigurationAttribute), false)
            .Cast<ServiceConfigurationAttribute>()
            .FirstOrDefault();

        // Assert - prefix should be "GAMESESSION_" not "GAME-SESSION_"
        Assert.NotNull(attr);
        Assert.Equal("GAMESESSION_", attr.EnvPrefix);
    }

    [Fact]
    public void ServiceConfigurationAttribute_RemovesMultipleHyphensFromPrefix()
    {
        // Arrange & Act
        var configType = typeof(Configuration_MultiHyphenatedService);
        var attr = configType.GetCustomAttributes(typeof(ServiceConfigurationAttribute), false)
            .Cast<ServiceConfigurationAttribute>()
            .FirstOrDefault();

        // Assert - prefix should be "RELATIONSHIPTYPE_" not "RELATIONSHIP-TYPE_"
        Assert.NotNull(attr);
        Assert.Equal("RELATIONSHIPTYPE_", attr.EnvPrefix);
    }

    [Fact]
    public void Configuration_HyphenatedService_BindsWithNormalizedPrefix()
    {
        // Arrange - use GAMESESSION_ prefix (hyphen removed)
        Environment.SetEnvironmentVariable("GAMESESSION_SERVER_SALT", "test-salt-value");
        Environment.SetEnvironmentVariable("GAMESESSION_MAX_PLAYERS_PER_SESSION", "32");

        try
        {
            // Act
            var config = IServiceConfiguration.BuildConfiguration<Configuration_HyphenatedService>();

            // Assert
            Assert.Equal("test-salt-value", config.ServerSalt);
            Assert.Equal(32, config.MaxPlayersPerSession);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GAMESESSION_SERVER_SALT", null);
            Environment.SetEnvironmentVariable("GAMESESSION_MAX_PLAYERS_PER_SESSION", null);
        }
    }

    [Fact]
    public void Configuration_HyphenatedService_IgnoresHyphenatedPrefix()
    {
        // Arrange - use incorrect GAME-SESSION_ prefix (with hyphen)
        Environment.SetEnvironmentVariable("GAME-SESSION_SERVER_SALT", "wrong-prefix");
        Environment.SetEnvironmentVariable("GAMESESSION_SERVER_SALT", null);

        try
        {
            // Act
            var config = IServiceConfiguration.BuildConfiguration<Configuration_HyphenatedService>();

            // Assert - should NOT bind because prefix doesn't match
            Assert.Null(config.ServerSalt);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GAME-SESSION_SERVER_SALT", null);
        }
    }

    [Fact]
    public void Configuration_MultiHyphenatedService_BindsCorrectly()
    {
        // Arrange - use RELATIONSHIPTYPE_ prefix
        Environment.SetEnvironmentVariable("RELATIONSHIPTYPE_TYPE_NAME", "test-type");
        Environment.SetEnvironmentVariable("RELATIONSHIPTYPE_ENABLED", "true");

        try
        {
            // Act
            var config = IServiceConfiguration.BuildConfiguration<Configuration_MultiHyphenatedService>();

            // Assert
            Assert.Equal("test-type", config.TypeName);
            Assert.True(config.Enabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RELATIONSHIPTYPE_TYPE_NAME", null);
            Environment.SetEnvironmentVariable("RELATIONSHIPTYPE_ENABLED", null);
        }
    }

    #endregion

    #region End-to-End Configuration Binding Tests

    [Fact]
    public void Configuration_UpperSnakeCaseEnvVars_BindToPascalCaseProperties()
    {
        // Arrange - simulate real-world env var naming
        Environment.SetEnvironmentVariable("GAMESESSION_SERVER_SALT", "production-salt");
        Environment.SetEnvironmentVariable("GAMESESSION_MAX_PLAYERS_PER_SESSION", "64");

        try
        {
            // Act
            var config = IServiceConfiguration.BuildConfiguration<Configuration_HyphenatedService>();

            // Assert - UPPER_SNAKE_CASE binds to PascalCase properties
            Assert.Equal("production-salt", config.ServerSalt);
            Assert.Equal(64, config.MaxPlayersPerSession);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GAMESESSION_SERVER_SALT", null);
            Environment.SetEnvironmentVariable("GAMESESSION_MAX_PLAYERS_PER_SESSION", null);
        }
    }

    [Fact]
    public void Configuration_PropertyNameAsEnvVar_AlsoBinds()
    {
        // Arrange - use property name directly (no underscores)
        Environment.SetEnvironmentVariable("GAMESESSION_SERVERSALT", "direct-property-name");

        try
        {
            // Act
            var config = IServiceConfiguration.BuildConfiguration<Configuration_HyphenatedService>();

            // Assert - single-word env var normalizes to matching property
            Assert.Equal("direct-property-name", config.ServerSalt);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GAMESESSION_SERVERSALT", null);
        }
    }

    #endregion
}
