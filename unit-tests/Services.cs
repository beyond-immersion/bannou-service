using BeyondImmersion.BannouService.Configuration;
using Xunit.Abstractions;

namespace BeyondImmersion.UnitTests.Services;

[Collection("unit tests")]
public class Services : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    private class TestService_Invalid { }

    [DaprService("ServiceTests.InvalidTest")]
    private class TestService_Invalid_Attribute { }

    private class TestService : IDaprService { }

    [DaprService("ServiceTests.Test")]
    private class TestService_Attribute : IDaprService { }

    /// <summary>
    /// Service for testing priority overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.PriorityTest", type: typeof(TestService_Priority_1))]
    private class TestService_Priority_1 : IDaprService { }

    /// <summary>
    /// Service for testing priority overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.PriorityTest", type: typeof(TestService_Priority_1), priority: true)]
    private class TestService_Priority_2 : IDaprService { }

    /// <summary>
    /// Service for testing implicit overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.OverrideTest", type: typeof(TestService_Override_1))]
    private class TestService_Override_1 : IDaprService { }

    /// <summary>
    /// Service for testing implicit overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.OverrideTest", type: typeof(TestService_Override_1))]
    private class TestService_Override_2 : TestService_Override_1 { }

    /// <summary>
    /// Service for testing implicit overrides without using attributes.
    /// </summary>
    [DaprService("ServiceTests.OverrideNoAttrTest", type: typeof(TestService_Override_NoAttribute_1))]
    private class TestService_Override_NoAttribute_1 : IDaprService { }

    /// <summary>
    /// Service for testing implicit overrides without using attributes.
    /// </summary>
    private class TestService_Override_NoAttribute_2 : TestService_Override_NoAttribute_1 { }

    /// <summary>
    /// Service for testing implicit overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.PriorityOverrideTest", type: typeof(TestService_PriorityAndOverride_1), priority: true)]
    private class TestService_PriorityAndOverride_1 : IDaprService { }

    /// <summary>
    /// Service for testing implicit overrides using attributes.
    /// </summary>
    [DaprService("ServiceTests.PriorityOverrideTest", type: typeof(TestService_PriorityAndOverride_1))]
    private class TestService_PriorityAndOverride_2 : TestService_PriorityAndOverride_1 { }

    [ServiceConfiguration(typeof(TestService_Attribute))]
    private class TestConfiguration_Attribute_TestService : IServiceConfiguration
    {
        public string? Force_Service_ID { get; set; }
        public string? TestProperty { get; set; }
    }

    [DaprService("ServiceTests.test_required")]
    private class TestService_Required : IDaprService { }

    [DaprService("ServiceTests.test_multiple_required")]
    private class TestService_MultipleRequired : IDaprService { }

    [ServiceConfiguration(typeof(TestService_Required))]
    private class TestConfiguration_RequiredProperty : IServiceConfiguration
    {
        public string? Force_Service_ID { get; set; }

        [ConfigRequired(AllowEmptyStrings = false)]
        public string? TestProperty { get; set; }
    }

    [ServiceConfiguration(typeof(TestService_MultipleRequired))]
    private class TestConfiguration_MultipleRequiredProperties_A : IServiceConfiguration
    {
        public string? Force_Service_ID { get; set; }

        [ConfigRequired(AllowEmptyStrings = false)]
        public string? TestProperty_A { get; set; }
    }

    [ServiceConfiguration(typeof(TestService_MultipleRequired))]
    private class TestConfiguration_MultipleRequiredProperties_B : IServiceConfiguration
    {
        public string? Force_Service_ID { get; set; }

        [ConfigRequired(AllowEmptyStrings = false)]
        public string? TestProperty_B { get; set; }
    }

    public Services(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Services>();
    }

    [Fact]
    public void GetServiceName()
    {
        Assert.Null(typeof(TestService).GetServiceName());
        Assert.Equal("ServiceTests.Test", typeof(TestService_Attribute).GetServiceName());
    }

    [Fact]
    public void GetServiceName_FromService()
    {
        IDaprService testService = new TestService();
        Assert.Null(testService.GetName());

        testService = new TestService_Attribute();
        Assert.Equal("ServiceTests.Test", testService.GetName());
    }

    [Fact]
    public void AnyServiceEnabled()
    {
        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "true");
            Assert.True(IDaprService.IsAnyEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", null);
        }
    }

    [Fact]
    public void ServiceEnabled_NoAttribute()
    {
        IDaprService testService = new TestService();
        Assert.False(testService.IsEnabled());

        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "true");
            Assert.False(testService.IsEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", null);
        }
    }

    [Fact]
    public void ServiceEnabled()
    {
        IDaprService testService = new TestService_Attribute();
        Assert.False(testService.IsEnabled());

        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "true");
            Assert.True(testService.IsEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", null);
        }
    }

    [Fact]
    public void ServiceEnabled_Default()
    {
        Assert.False(IDaprService.IsEnabled(typeof(TestService_Attribute)));
    }

    [Fact]
    public void ServiceEnabled_BadType()
    {
        _ = Assert.Throws<InvalidCastException>(() => IDaprService.IsEnabled(typeof(TestService_Invalid)));
    }

    [Fact]
    public void ServiceEnabled_TestType()
    {
        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "false");
            Assert.False(IDaprService.IsEnabled(typeof(TestService_Attribute)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", null);
        }

        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "true");
            Assert.True(IDaprService.IsEnabled(typeof(TestService_Attribute)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", null);
        }
    }

    [Fact]
    public void ServiceEnabled_TestType_Generic()
    {
        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "false");
            Assert.False(IDaprService.IsEnabled<TestService_Attribute>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", null);
        }

        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "true");
            Assert.True(IDaprService.IsEnabled<TestService_Attribute>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", null);
        }
    }

    [Fact]
    public void ServiceEnabled_TestString()
    {
        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "false");
            Assert.False(IDaprService.IsEnabled("ServiceTests.Test"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", null);
        }

        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "true");
            Assert.True(IDaprService.IsEnabled("ServiceTests.Test"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", null);
        }
    }

    [Fact]
    public void GetConfigurationType()
    {
        IDaprService testService = new TestService();
        Assert.Equal(typeof(AppConfiguration), testService.GetConfigurationType());

        testService = new TestService_Attribute();
        Assert.Equal(typeof(TestConfiguration_Attribute_TestService), testService.GetConfigurationType());

        testService = new TestService_MultipleRequired();
        Assert.Equal(typeof(TestConfiguration_MultipleRequiredProperties_A), testService.GetConfigurationType());
        Assert.NotEqual(typeof(TestConfiguration_MultipleRequiredProperties_B), testService.GetConfigurationType());
    }

    [Fact]
    public void GetConfigurationType_ByServiceType()
    {
        Assert.Equal(typeof(AppConfiguration), IDaprService.GetConfigurationType(typeof(TestService)));
        Assert.Equal(typeof(TestConfiguration_Attribute_TestService), IDaprService.GetConfigurationType(typeof(TestService_Attribute)));
        Assert.Equal(typeof(TestConfiguration_MultipleRequiredProperties_A), IDaprService.GetConfigurationType(typeof(TestService_MultipleRequired)));
        Assert.NotEqual(typeof(TestConfiguration_MultipleRequiredProperties_B), IDaprService.GetConfigurationType(typeof(TestService_MultipleRequired)));
    }

    [Fact]
    public void BuildServiceConfiguration()
    {
        var config = (TestConfiguration_Attribute_TestService?)IDaprService.BuildConfiguration(typeof(TestService_Attribute));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.Test".ToUpper()}_TESTPROPERTY", "Test");
            Environment.SetEnvironmentVariable($"{"ServiceTests.Test".ToUpper()}_FORCE_SERVICE_ID", serviceID);
            config = (TestConfiguration_Attribute_TestService?)IDaprService.BuildConfiguration(typeof(TestService_Attribute));
            Assert.NotNull(config);
            Assert.Equal("Test", config.TestProperty);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.Test".ToUpper()}_TESTPROPERTY", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.Test".ToUpper()}_FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void BuildServiceConfiguration_Generic()
    {
        var config = (TestConfiguration_Attribute_TestService?)IDaprService.BuildConfiguration<TestService_Attribute>();
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.Test".ToUpper()}_TESTPROPERTY", "Test");
            Environment.SetEnvironmentVariable($"{"ServiceTests.Test".ToUpper()}_FORCE_SERVICE_ID", serviceID);
            config = (TestConfiguration_Attribute_TestService?)IDaprService.BuildConfiguration<TestService_Attribute>();
            Assert.NotNull(config);
            Assert.Equal("Test", config.TestProperty);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.Test".ToUpper()}_TESTPROPERTY", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.Test".ToUpper()}_FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void BuildServiceConfiguration_WithArgs()
    {
        var serviceID = Guid.NewGuid().ToString().ToLower();
        IServiceConfiguration? config = IDaprService.BuildConfiguration(typeof(TestService_Attribute),
                        args: new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.Force_Service_ID);

        config = IDaprService.BuildConfiguration(typeof(TestService_Attribute),
                        args: new string[] { $"--force-service-id={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void BuildServiceConfiguration_Generic_WithArgs()
    {
        var serviceID = Guid.NewGuid().ToString().ToLower();
        IServiceConfiguration config = IDaprService.BuildConfiguration<TestService_Attribute>(
                        args: new string[] { $"--Force-Service-ID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.Force_Service_ID);

        config = IDaprService.BuildConfiguration<TestService_Attribute>(
                        args: new string[] { $"--force-service-id={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.Force_Service_ID);
    }

    [Fact]
    public void BuildServiceConfiguration_FromService()
    {
        IDaprService testService = new TestService_Attribute();
        var config = testService.BuildConfiguration() as TestConfiguration_Attribute_TestService;
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.Force_Service_ID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.Test".ToUpper()}_TESTPROPERTY", "Test");
            Environment.SetEnvironmentVariable($"{"ServiceTests.Test".ToUpper()}_FORCE_SERVICE_ID", serviceID);
            config = testService.BuildConfiguration() as TestConfiguration_Attribute_TestService;
            Assert.NotNull(config);
            Assert.Equal("Test", config.TestProperty);
            Assert.Equal(serviceID, config.Force_Service_ID);
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.Test".ToUpper()}_TESTPROPERTY", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.Test".ToUpper()}_FORCE_SERVICE_ID", null);
        }
    }

    [Fact]
    public void FindAll()
    {
        Assert.DoesNotContain(IDaprService.GetAllServiceInfo(), t => t.Item1 == typeof(TestService));
        Assert.Contains(IDaprService.GetAllServiceInfo(), t => t.Item1 == typeof(TestService_Attribute));
        Assert.Contains(IDaprService.GetAllServiceInfo(), t => t.Item1 == typeof(TestService_Required));
        Assert.Contains(IDaprService.GetAllServiceInfo(), t => t.Item1 == typeof(TestService_MultipleRequired));

        Assert.DoesNotContain(IDaprService.GetAllServiceInfo(true), t => t.Item1 == typeof(TestService_Attribute));
        Assert.DoesNotContain(IDaprService.GetAllServiceInfo(true), t => t.Item1 == typeof(TestService_Required));
        Assert.DoesNotContain(IDaprService.GetAllServiceInfo(true), t => t.Item1 == typeof(TestService_MultipleRequired));

        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_REQUIRED_SERVICE_ENABLED", "true");
            Assert.Contains(IDaprService.GetAllServiceInfo(true), t => t.Item1 == typeof(TestService_Required));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_REQUIRED_SERVICE_ENABLED", null);
        }
    }

    [Fact]
    public void FindAll_TestOverride_MostDerivedType()
    {
        (Type, Type, DaprServiceAttribute)? locateService = IDaprService.GetServiceInfo("ServiceTests.OverrideTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(TestService_Override_2), locateService.Value.Item2);
    }

    [Fact]
    public void FindAll_TestOverride_MostDerivedType_NoAttribute()
    {
        (Type, Type, DaprServiceAttribute)? locateService = IDaprService.GetServiceInfo("ServiceTests.OverrideNoAttrTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(TestService_Override_NoAttribute_2), locateService.Value.Item2);
    }

    [Fact]
    public void FindAll_TestOverride_Priority()
    {
        (Type, Type, DaprServiceAttribute)? locateService = IDaprService.GetServiceInfo("ServiceTests.PriorityTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(TestService_Priority_2), locateService.Value.Item2);
    }

    [Fact]
    public void FindAll_TestOverride_PriorityOverMostDerivedType()
    {
        (Type, Type, DaprServiceAttribute)? locateService = IDaprService.GetServiceInfo("ServiceTests.PriorityOverrideTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(TestService_PriorityAndOverride_1), locateService.Value.Item2);
    }

    [Fact]
    public void AllHaveRequiredConfiguration()
    {
        Assert.True(IDaprService.AllHaveRequiredConfiguration(IDaprService.GetAllServiceInfo(enabledOnly: true)));

        try
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_REQUIRED_SERVICE_ENABLED", "true");
            Assert.False(IDaprService.AllHaveRequiredConfiguration(IDaprService.GetAllServiceInfo(enabledOnly: true)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_REQUIRED_SERVICE_ENABLED", null);
        }

        try
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", "something");
            Assert.True(IDaprService.AllHaveRequiredConfiguration(IDaprService.GetAllServiceInfo(enabledOnly: true)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TESTPROPERTY", null);
        }
    }

    [Fact]
    public void HasRequiredConfiguration()
    {
        IDaprService testService = new TestService_Required();
        Assert.False(testService.HasRequiredConfiguration());

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_TESTPROPERTY", "Test");
            Assert.True(testService.HasRequiredConfiguration());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_TESTPROPERTY", null);
        }
    }

    [Fact]
    public void HasRequiredConfiguration_MultipleTypes()
    {
        IDaprService testService = new TestService_MultipleRequired();
        Assert.False(testService.HasRequiredConfiguration());

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY", "Test");
            Assert.False(testService.HasRequiredConfiguration());
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY_A", "Test");
            Assert.True(testService.HasRequiredConfiguration());
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY_B", "Test");
            Assert.True(testService.HasRequiredConfiguration());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY_A", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY_B", null);
        }
    }

    [Fact]
    public void HasRequiredConfiguration_ByType()
    {
        Assert.False(IDaprService.HasRequiredConfiguration(typeof(TestService_Required)));

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_TESTPROPERTY", "Test");
            Assert.True(IDaprService.HasRequiredConfiguration(typeof(TestService_Required)));
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_TESTPROPERTY", null);
        }
    }

    [Fact]
    public void HasRequiredConfiguration_ByType_MultipleTypes()
    {
        Assert.False(IDaprService.HasRequiredConfiguration(typeof(TestService_MultipleRequired)));

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY", "Test");
            Assert.False(IDaprService.HasRequiredConfiguration(typeof(TestService_MultipleRequired)));
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY_A", "Test");
            Assert.False(IDaprService.HasRequiredConfiguration(typeof(TestService_MultipleRequired)));
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY_B", "Test");
            Assert.True(IDaprService.HasRequiredConfiguration(typeof(TestService_MultipleRequired)));
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY_A", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY_B", null);
        }
    }

    [Fact]
    public void HasRequiredConfiguration_ByType_Generic()
    {
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_Required>());

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_TESTPROPERTY", "Test");
            Assert.True(IDaprService.HasRequiredConfiguration<TestService_Required>());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_required".ToUpper()}_TESTPROPERTY", null);
        }
    }

    [Fact]
    public void HasRequiredConfiguration_ByType_MultipleTypes_Generic()
    {
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());

        try
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY", "Test");
            Assert.False(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY_A", "Test");
            Assert.False(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY_B", "Test");
            Assert.True(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY_A", null);
            Environment.SetEnvironmentVariable($"{"ServiceTests.test_multiple_required".ToUpper()}_TESTPROPERTY_B", null);
        }
    }
}
