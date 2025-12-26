using BeyondImmersion.BannouService.Configuration;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.UnitTests;

[Collection("unit tests")]
public class Services : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    private class Service_Invalid { }

    [DaprService("ServiceTests.InvalidTest")]
    [Obsolete]
    private class Service_Invalid_Attribute { }

    [Obsolete]
    private class Service : IDaprService { }

    [DaprService("ServiceTests.Test")]
    [Obsolete]
    private class Service_Attribute : IDaprService { }

    /// <summary>
    /// Service for testing priority overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.PriorityTest", interfaceType: typeof(Service_Priority_1))]
    [Obsolete]
    private class Service_Priority_1 : IDaprService { }

    /// <summary>
    /// Service for testing priority overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.PriorityTest", interfaceType: typeof(Service_Priority_1), priority: true)]
    [Obsolete]
    private class Service_Priority_2 : IDaprService { }

    /// <summary>
    /// Service for testing implicit overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.OverrideTest", interfaceType: typeof(Service_Override_1))]
    [Obsolete]
    private class Service_Override_1 : IDaprService { }

    /// <summary>
    /// Service for testing implicit overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.OverrideTest", interfaceType: typeof(Service_Override_1))]
    [Obsolete]
    private class Service_Override_2 : Service_Override_1 { }

    /// <summary>
    /// Service for testing implicit overrides without using attributes.
    /// </summary>
    [DaprService("ServiceTests.OverrideNoAttrTest", interfaceType: typeof(Service_Override_NoAttribute_1))]
    [Obsolete]
    private class Service_Override_NoAttribute_1 : IDaprService { }

    /// <summary>
    /// Service for testing implicit overrides without using attributes.
    /// </summary>
    [Obsolete]
    private class Service_Override_NoAttribute_2 : Service_Override_NoAttribute_1 { }

    /// <summary>
    /// Service for testing implicit overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.PriorityOverrideTest", interfaceType: typeof(Service_PriorityAndOverride_1), priority: true)]
    [Obsolete]
    private class Service_PriorityAndOverride_1 : IDaprService { }

    /// <summary>
    /// Service for testing implicit overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.PriorityOverrideTest", interfaceType: typeof(Service_PriorityAndOverride_1))]
    [Obsolete]
    private class Service_PriorityAndOverride_2 : Service_PriorityAndOverride_1 { }

    [ServiceConfiguration(typeof(Service_Attribute))]
    [Obsolete]
    private class Configuration : BaseServiceConfiguration
    {
        public string? Property { get; set; }
    }

    [DaprService("ServiceTests.test_required")]
    [Obsolete]
    private class Service_Required : IDaprService { }

    [DaprService("ServiceTests.test_multiple_required")]
    [Obsolete]
    private class Service_MultipleRequired : IDaprService { }

    [ServiceConfiguration(typeof(Service_Required))]
    [Obsolete]
    private class Configuration_Required : BaseServiceConfiguration
    {
        [ConfigRequired(AllowEmptyStrings = false)]
        public string? Property { get; set; }
    }

    [ServiceConfiguration(typeof(Service_MultipleRequired))]
    [Obsolete]
    private class Configuration_MultipleRequired_A : BaseServiceConfiguration
    {
        // PropertyA binds from PROPERTY_A env var (normalized to PascalCase)
        [ConfigRequired(AllowEmptyStrings = false)]
        public string? PropertyA { get; set; }
    }

    [ServiceConfiguration(typeof(Service_MultipleRequired))]
    [Obsolete]
    private class Configuration_MultipleRequired_B : BaseServiceConfiguration
    {
        // PropertyB binds from PROPERTY_B env var (normalized to PascalCase)
        [ConfigRequired(AllowEmptyStrings = false)]
        public string? PropertyB { get; set; }
    }

    [Obsolete]
    public Services(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Services>();
    }

    [Fact]
    [Obsolete]
    public void Services_GetServiceName()
    {
        Assert.Null(typeof(Service).GetServiceName());
        Assert.Equal("ServiceTests.Test", typeof(Service_Attribute).GetServiceName());
    }

    [Fact]
    [Obsolete]
    public void Services_GetServiceName_FromService()
    {
        IDaprService testService = new Service();
        Assert.Null(testService.GetName());

        testService = new Service_Attribute();
        Assert.Equal("ServiceTests.Test", testService.GetName());
    }

    [Fact]
    [Obsolete]
    public void Services_AnyServiceEnabled()
    {
        // Test that at least one service is available - use Services property to check
        Assert.True(IDaprService.Services.Length > 0, "At least one service should be available");
    }

    [Fact]
    [Obsolete]
    public void Services_ServiceEnabled_NoAttribute()
    {
        IDaprService testService = new Service();
        Assert.False(testService.IsDisabled());

        try
        {
            Environment.SetEnvironmentVariable("TEST_SERVICE_DISABLED", "true");
            Environment.SetEnvironmentVariable("SERVICE_DISABLED", "true");
            Environment.SetEnvironmentVariable("SERVICETESTS.SERVICE_DISABLED", "true");
            Assert.False(testService.IsDisabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("SERVICETESTS.SERVICE_DISABLED", null);
        }
    }

    [Fact]
    [Obsolete]
    public void Services_ServiceEnabled()
    {
        IDaprService testService = new Service_Attribute();
        Assert.False(testService.IsDisabled());

        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", "true");
            Assert.True(testService.IsDisabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", null);
        }
    }

    [Fact]
    [Obsolete]
    public void Services_ServiceEnabled_Default()
    {
        Assert.False(IDaprService.IsDisabled(typeof(Service_Attribute)));
    }

    [Fact]
    [Obsolete]
    public void Services_ServiceEnabled_BadType()
    {
        _ = Assert.Throws<InvalidCastException>(() => IDaprService.IsDisabled(typeof(Service_Invalid)));
    }

    [Fact]
    [Obsolete]
    public void Services_ServiceEnabled_TestType()
    {
        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", "false");
            Assert.False(IDaprService.IsDisabled(typeof(Service_Attribute)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", null);
        }

        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", "true");
            Assert.True(IDaprService.IsDisabled(typeof(Service_Attribute)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", null);
        }
    }

    [Fact]
    [Obsolete]
    public void Services_ServiceEnabled_TestType_Generic()
    {
        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", "false");
            Assert.False(IDaprService.IsDisabled<Service_Attribute>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", null);
        }

        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", "true");
            Assert.True(IDaprService.IsDisabled<Service_Attribute>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", null);
        }
    }

    [Fact]
    [Obsolete]
    public void Services_ServiceEnabled_TestString()
    {
        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", "false");
            Assert.False(IDaprService.IsDisabled("ServiceTests.Test"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", null);
        }

        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", "true");
            Assert.True(IDaprService.IsDisabled("ServiceTests.Test"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", null);
        }
    }

    // Removed GetConfigurationType tests - this was an internal implementation detail
    // Configuration type discovery is handled internally by the plugin system

    [Fact]
    [Obsolete]
    public void Services_FindAll()
    {
        try
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_REQUIRED_SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_MULTIPLEREQUIRED_SERVICE_DISABLED", null);

            Assert.DoesNotContain(IDaprService.Services, t => t.Item1 == typeof(Service));
            Assert.Contains(IDaprService.Services, t => t.Item1 == typeof(Service_Attribute));
            Assert.Contains(IDaprService.Services, t => t.Item1 == typeof(Service_Required));
            Assert.Contains(IDaprService.Services, t => t.Item1 == typeof(Service_MultipleRequired));

            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "true");
            Program.Configuration.Services_Enabled = true;
            Assert.DoesNotContain(IDaprService.EnabledServices, t => t.Item1 == typeof(Service));
            Assert.Contains(IDaprService.EnabledServices, t => t.Item1 == typeof(Service_Attribute));
            Assert.Contains(IDaprService.EnabledServices, t => t.Item1 == typeof(Service_Required));
            Assert.Contains(IDaprService.EnabledServices, t => t.Item1 == typeof(Service_MultipleRequired));

            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "false");
            Program.Configuration.Services_Enabled = false;
            Assert.DoesNotContain(IDaprService.EnabledServices, t => t.Item1 == typeof(Service));
            Assert.DoesNotContain(IDaprService.EnabledServices, t => t.Item1 == typeof(Service_Attribute));
            Assert.DoesNotContain(IDaprService.EnabledServices, t => t.Item1 == typeof(Service_Required));
            Assert.DoesNotContain(IDaprService.EnabledServices, t => t.Item1 == typeof(Service_MultipleRequired));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Program.Configuration.Services_Enabled = true;
        }

        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", "true");
            Assert.DoesNotContain(IDaprService.EnabledServices, t => t.Item1 == typeof(Service));
            Assert.DoesNotContain(IDaprService.EnabledServices, t => t.Item1 == typeof(Service_Attribute));
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", "false");
            Assert.DoesNotContain(IDaprService.EnabledServices, t => t.Item1 == typeof(Service));
            Assert.Contains(IDaprService.EnabledServices, t => t.Item1 == typeof(Service_Attribute));

            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_REQUIRED_SERVICE_DISABLED", "true");
            Assert.DoesNotContain(IDaprService.EnabledServices, t => t.Item1 == typeof(Service_Required));
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_REQUIRED_SERVICE_DISABLED", "false");
            Assert.Contains(IDaprService.EnabledServices, t => t.Item1 == typeof(Service_Required));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_ATTRIBUTE_SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_REQUIRED_SERVICE_DISABLED", null);
        }
    }

    [Fact]
    [Obsolete]
    public void Services_FindAll_TestOverride_MostDerivedType()
    {
        (Type, Type, BannouServiceAttribute)? locateService = IBannouService.GetServiceInfo("ServiceTests.OverrideTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(Service_Override_2), locateService.Value.Item2);
    }

    [Fact]
    [Obsolete]
    public void Services_FindAll_TestOverride_MostDerivedType_NoAttribute()
    {
        (Type, Type, BannouServiceAttribute)? locateService = IBannouService.GetServiceInfo("ServiceTests.OverrideNoAttrTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(Service_Override_NoAttribute_2), locateService.Value.Item2);
    }

    [Fact]
    [Obsolete]
    public void Services_FindAll_TestOverride_Priority()
    {
        (Type, Type, BannouServiceAttribute)? locateService = IBannouService.GetServiceInfo("ServiceTests.PriorityTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(Service_Priority_2), locateService.Value.Item2);
    }

    [Fact]
    [Obsolete]
    public void Services_FindAll_TestOverride_PriorityOverMostDerivedType()
    {
        (Type, Type, BannouServiceAttribute)? locateService = IBannouService.GetServiceInfo("ServiceTests.PriorityOverrideTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(Service_PriorityAndOverride_1), locateService.Value.Item2);
    }

    [Fact]
    [Obsolete]
    public void Services_HasRequiredConfiguration()
    {
        // Test configuration requirements using IServiceConfiguration.HasRequired() instead of the removed method
        try
        {
            // Without required environment variable, configuration should not have required properties
            var config = IServiceConfiguration.BuildConfiguration<Configuration_Required>();
            Assert.False(((IServiceConfiguration)config).HasRequired());

            // With required environment variable, configuration should have required properties
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_PROPERTY", "Test");
            var configWithRequired = IServiceConfiguration.BuildConfiguration<Configuration_Required>();
            Assert.True(((IServiceConfiguration)configWithRequired).HasRequired());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_PROPERTY", null);
        }
    }

    [Fact]
    [Obsolete]
    public void Services_HasRequiredConfiguration_MultipleTypes()
    {
        // Test multiple configuration requirements using IServiceConfiguration.HasRequired()
        try
        {
            // No required properties set - neither configuration should be satisfied
            var configA = IServiceConfiguration.BuildConfiguration<Configuration_MultipleRequired_A>();
            var configB = IServiceConfiguration.BuildConfiguration<Configuration_MultipleRequired_B>();
            Assert.False(((IServiceConfiguration)configA).HasRequired());
            Assert.False(((IServiceConfiguration)configB).HasRequired());

            // Set property A - only config A should be satisfied
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_A", "Test");
            var configAWithRequired = IServiceConfiguration.BuildConfiguration<Configuration_MultipleRequired_A>();
            var configBStillMissing = IServiceConfiguration.BuildConfiguration<Configuration_MultipleRequired_B>();
            Assert.True(((IServiceConfiguration)configAWithRequired).HasRequired());
            Assert.False(((IServiceConfiguration)configBStillMissing).HasRequired());

            // Set property B - both configurations should now be satisfied
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_B", "Test");
            var configAStillSatisfied = IServiceConfiguration.BuildConfiguration<Configuration_MultipleRequired_A>();
            var configBNowSatisfied = IServiceConfiguration.BuildConfiguration<Configuration_MultipleRequired_B>();
            Assert.True(((IServiceConfiguration)configAStillSatisfied).HasRequired());
            Assert.True(((IServiceConfiguration)configBNowSatisfied).HasRequired());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_A", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_B", null);
        }
    }

    [Fact]
    [Obsolete]
    public void Services_HasRequiredConfiguration_ByType()
    {
        // Test configuration requirements by type using IServiceConfiguration.HasRequiredForType()
        Assert.False(IServiceConfiguration.HasRequiredForType<Configuration_Required>());

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_PROPERTY", "Test");
            Assert.True(IServiceConfiguration.HasRequiredForType<Configuration_Required>());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_PROPERTY", null);
        }
    }

    [Fact]
    [Obsolete]
    public void Services_HasRequiredConfiguration_ByType_MultipleTypes()
    {
        // Test multiple configuration requirements by type
        Assert.False(IServiceConfiguration.HasRequiredForType<Configuration_MultipleRequired_A>());
        Assert.False(IServiceConfiguration.HasRequiredForType<Configuration_MultipleRequired_B>());

        try
        {
            // Set property A - only config A should be satisfied
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_A", "Test");
            Assert.True(IServiceConfiguration.HasRequiredForType<Configuration_MultipleRequired_A>());
            Assert.False(IServiceConfiguration.HasRequiredForType<Configuration_MultipleRequired_B>());

            // Set property B - both configurations should be satisfied
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_B", "Test");
            Assert.True(IServiceConfiguration.HasRequiredForType<Configuration_MultipleRequired_A>());
            Assert.True(IServiceConfiguration.HasRequiredForType<Configuration_MultipleRequired_B>());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_A", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_B", null);
        }
    }

    [Fact]
    [Obsolete]
    public void Services_HasRequiredConfiguration_ByType_Generic()
    {
        // Test generic configuration requirements
        Assert.False(IServiceConfiguration.HasRequiredForType<Configuration_Required>());

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_PROPERTY", "Test");
            Assert.True(IServiceConfiguration.HasRequiredForType<Configuration_Required>());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_PROPERTY", null);
        }
    }

    [Fact]
    [Obsolete]
    public void Services_HasRequiredConfiguration_ByType_MultipleTypes_Generic()
    {
        // Test generic multiple configuration requirements
        Assert.False(IServiceConfiguration.HasRequiredForType<Configuration_MultipleRequired_A>());
        Assert.False(IServiceConfiguration.HasRequiredForType<Configuration_MultipleRequired_B>());

        try
        {
            // Set property A - only config A should be satisfied
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_A", "Test");
            Assert.True(IServiceConfiguration.HasRequiredForType<Configuration_MultipleRequired_A>());
            Assert.False(IServiceConfiguration.HasRequiredForType<Configuration_MultipleRequired_B>());

            // Set property B - both configurations should be satisfied
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_B", "Test");
            Assert.True(IServiceConfiguration.HasRequiredForType<Configuration_MultipleRequired_A>());
            Assert.True(IServiceConfiguration.HasRequiredForType<Configuration_MultipleRequired_B>());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_A", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_B", null);
        }
    }
}
