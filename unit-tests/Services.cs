using BeyondImmersion.BannouService.Configuration;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.UnitTests;

[Collection("unit tests")]
public class Services : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    private class Service_Invalid { }

    [DaprService("ServiceTests.InvalidTest")]
    private class Service_Invalid_Attribute { }

    private class Service : IDaprService { }

    [DaprService("ServiceTests.Test")]
    private class Service_Attribute : IDaprService { }

    /// <summary>
    /// Service for testing priority overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.PriorityTest", interfaceType: typeof(Service_Priority_1))]
    private class Service_Priority_1 : IDaprService { }

    /// <summary>
    /// Service for testing priority overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.PriorityTest", interfaceType: typeof(Service_Priority_1), priority: true)]
    private class Service_Priority_2 : IDaprService { }

    /// <summary>
    /// Service for testing implicit overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.OverrideTest", interfaceType: typeof(Service_Override_1))]
    private class Service_Override_1 : IDaprService { }

    /// <summary>
    /// Service for testing implicit overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.OverrideTest", interfaceType: typeof(Service_Override_1))]
    private class Service_Override_2 : Service_Override_1 { }

    /// <summary>
    /// Service for testing implicit overrides without using attributes.
    /// </summary>
    [DaprService("ServiceTests.OverrideNoAttrTest", interfaceType: typeof(Service_Override_NoAttribute_1))]
    private class Service_Override_NoAttribute_1 : IDaprService { }

    /// <summary>
    /// Service for testing implicit overrides without using attributes.
    /// </summary>
    private class Service_Override_NoAttribute_2 : Service_Override_NoAttribute_1 { }

    /// <summary>
    /// Service for testing implicit overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.PriorityOverrideTest", interfaceType: typeof(Service_PriorityAndOverride_1), priority: true)]
    private class Service_PriorityAndOverride_1 : IDaprService { }

    /// <summary>
    /// Service for testing implicit overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.PriorityOverrideTest", interfaceType: typeof(Service_PriorityAndOverride_1))]
    private class Service_PriorityAndOverride_2 : Service_PriorityAndOverride_1 { }

    [ServiceConfiguration(typeof(Service_Attribute))]
    private class Configuration : IServiceConfiguration
    {
        public string? Force_Service_ID { get; set; }
        public bool? Service_Disabled { get; set; }
        public string? Property { get; set; }
    }

    [DaprService("ServiceTests.test_required")]
    private class Service_Required : IDaprService { }

    [DaprService("ServiceTests.test_multiple_required")]
    private class Service_MultipleRequired : IDaprService { }

    [ServiceConfiguration(typeof(Service_Required))]
    private class Configuration_Required : IServiceConfiguration
    {
        public string? Force_Service_ID { get; set; }
        public bool? Service_Disabled { get; set; }

        [ConfigRequired(AllowEmptyStrings = false)]
        public string? Property { get; set; }
    }

    [ServiceConfiguration(typeof(Service_MultipleRequired))]
    private class Configuration_MultipleRequired_A : IServiceConfiguration
    {
        public string? Force_Service_ID { get; set; }
        public bool? Service_Disabled { get; set; }

        [ConfigRequired(AllowEmptyStrings = false)]
        public string? Property_A { get; set; }
    }

    [ServiceConfiguration(typeof(Service_MultipleRequired))]
    private class Configuration_MultipleRequired_B : IServiceConfiguration
    {
        public string? Force_Service_ID { get; set; }
        public bool? Service_Disabled { get; set; }

        [ConfigRequired(AllowEmptyStrings = false)]
        public string? Property_B { get; set; }
    }

    public Services(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Services>();
    }

    [Fact]
    public void Services_GetServiceName()
    {
        Assert.Null(typeof(Service).GetServiceName());
        Assert.Equal("ServiceTests.Test", typeof(Service_Attribute).GetServiceName());
    }

    [Fact]
    public void Services_GetServiceName_FromService()
    {
        IDaprService testService = new Service();
        Assert.Null(testService.GetName());

        testService = new Service_Attribute();
        Assert.Equal("ServiceTests.Test", testService.GetName());
    }

    [Fact]
    public void Services_AnyServiceEnabled()
    {
        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_DISABLED", null);
            Assert.True(IDaprService.IsAnyEnabled());
        }
        finally
        {
        }
    }

    [Fact]
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
    public void Services_ServiceEnabled_Default()
    {
        Assert.False(IDaprService.IsDisabled(typeof(Service_Attribute)));
    }

    [Fact]
    public void Services_ServiceEnabled_BadType()
    {
        _ = Assert.Throws<InvalidCastException>(() => IDaprService.IsDisabled(typeof(Service_Invalid)));
    }

    [Fact]
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

    [Fact]
    public void Services_GetConfigurationType()
    {
        IDaprService testService = new Service();
        Assert.Equal(typeof(AppConfiguration), testService.GetConfigurationType());

        testService = new Service_Attribute();
        Assert.Equal(typeof(Configuration), testService.GetConfigurationType());

        testService = new Service_MultipleRequired();
        Assert.Equal(typeof(Configuration_MultipleRequired_A), testService.GetConfigurationType());
        Assert.NotEqual(typeof(Configuration_MultipleRequired_B), testService.GetConfigurationType());
    }

    [Fact]
    public void Services_GetConfigurationType_ByServiceType()
    {
        Assert.Equal(typeof(AppConfiguration), IDaprService.GetConfigurationType(typeof(Service)));
        Assert.Equal(typeof(Configuration), IDaprService.GetConfigurationType(typeof(Service_Attribute)));
        Assert.Equal(typeof(Configuration_MultipleRequired_A), IDaprService.GetConfigurationType(typeof(Service_MultipleRequired)));
        Assert.NotEqual(typeof(Configuration_MultipleRequired_B), IDaprService.GetConfigurationType(typeof(Service_MultipleRequired)));
    }

    [Fact]
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
    public void Services_FindAll_TestOverride_MostDerivedType()
    {
        (Type, Type, DaprServiceAttribute)? locateService = IDaprService.GetServiceInfo("ServiceTests.OverrideTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(Service_Override_2), locateService.Value.Item2);
    }

    [Fact]
    public void Services_FindAll_TestOverride_MostDerivedType_NoAttribute()
    {
        (Type, Type, DaprServiceAttribute)? locateService = IDaprService.GetServiceInfo("ServiceTests.OverrideNoAttrTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(Service_Override_NoAttribute_2), locateService.Value.Item2);
    }

    [Fact]
    public void Services_FindAll_TestOverride_Priority()
    {
        (Type, Type, DaprServiceAttribute)? locateService = IDaprService.GetServiceInfo("ServiceTests.PriorityTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(Service_Priority_2), locateService.Value.Item2);
    }

    [Fact]
    public void Services_FindAll_TestOverride_PriorityOverMostDerivedType()
    {
        (Type, Type, DaprServiceAttribute)? locateService = IDaprService.GetServiceInfo("ServiceTests.PriorityOverrideTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(Service_PriorityAndOverride_1), locateService.Value.Item2);
    }

    [Fact]
    public void Services_HasRequiredConfiguration()
    {
        IDaprService testService = new Service_Required();
        Assert.False(testService.HasRequiredConfiguration());

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_PROPERTY", "Test");
            Assert.True(testService.HasRequiredConfiguration());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_PROPERTY", null);
        }
    }

    [Fact]
    public void Services_HasRequiredConfiguration_MultipleTypes()
    {
        IDaprService testService = new Service_MultipleRequired();
        Assert.False(testService.HasRequiredConfiguration());

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY", "Test");
            Assert.False(testService.HasRequiredConfiguration());
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_A", "Test");
            Assert.True(testService.HasRequiredConfiguration());
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_B", "Test");
            Assert.True(testService.HasRequiredConfiguration());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_A", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_B", null);
        }
    }

    [Fact]
    public void Services_HasRequiredConfiguration_ByType()
    {
        Assert.False(IDaprService.HasRequiredConfiguration(typeof(Service_Required)));

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_PROPERTY", "Test");
            Assert.True(IDaprService.HasRequiredConfiguration(typeof(Service_Required)));
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_PROPERTY", null);
        }
    }

    [Fact]
    public void Services_HasRequiredConfiguration_ByType_MultipleTypes()
    {
        Assert.False(IDaprService.HasRequiredConfiguration(typeof(Service_MultipleRequired)));

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY", "Test");
            Assert.False(IDaprService.HasRequiredConfiguration(typeof(Service_MultipleRequired)));
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_A", "Test");
            Assert.False(IDaprService.HasRequiredConfiguration(typeof(Service_MultipleRequired)));
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_B", "Test");
            Assert.True(IDaprService.HasRequiredConfiguration(typeof(Service_MultipleRequired)));
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_A", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_B", null);
        }
    }

    [Fact]
    public void Services_HasRequiredConfiguration_ByType_Generic()
    {
        Assert.False(IDaprService.HasRequiredConfiguration<Service_Required>());

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_PROPERTY", "Test");
            Assert.True(IDaprService.HasRequiredConfiguration<Service_Required>());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_PROPERTY", null);
        }
    }

    [Fact]
    public void Services_HasRequiredConfiguration_ByType_MultipleTypes_Generic()
    {
        Assert.False(IDaprService.HasRequiredConfiguration<Service_MultipleRequired>());

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY", "Test");
            Assert.False(IDaprService.HasRequiredConfiguration<Service_MultipleRequired>());
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_A", "Test");
            Assert.False(IDaprService.HasRequiredConfiguration<Service_MultipleRequired>());
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_B", "Test");
            Assert.True(IDaprService.HasRequiredConfiguration<Service_MultipleRequired>());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_A", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_PROPERTY_B", null);
        }
    }
}
