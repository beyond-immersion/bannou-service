using BeyondImmersion.BannouService.Services;
using Xunit.Abstractions;

namespace BeyondImmersion.UnitTests;

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
        public string? ForceServiceID { get; set; }
        public string? TestProperty { get; set; }
    }

    [DaprService("ServiceTests.test_required")]
    private class TestService_Required : IDaprService { }

    [DaprService("ServiceTests.test_multiple_required")]
    private class TestService_MultipleRequired : IDaprService { }

    [ServiceConfiguration(typeof(TestService_Required))]
    private class TestConfiguration_RequiredProperty : IServiceConfiguration
    {
        public string? ForceServiceID { get; set; }

        [ConfigRequired(AllowEmptyStrings = false)]
        public string? TestProperty { get; set; }
    }

    [ServiceConfiguration(typeof(TestService_MultipleRequired), primary: true)]
    private class TestConfiguration_MultipleRequiredProperties_A : IServiceConfiguration
    {
        public string? ForceServiceID { get; set; }

        [ConfigRequired(AllowEmptyStrings = false)]
        public string? TestProperty_A { get; set; }
    }

    [ServiceConfiguration(typeof(TestService_MultipleRequired))]
    private class TestConfiguration_MultipleRequiredProperties_B : IServiceConfiguration
    {
        public string? ForceServiceID { get; set; }

        [ConfigRequired(AllowEmptyStrings = false)]
        public string? TestProperty_B { get; set; }
    }

    private Services(CollectionFixture collectionFixture)
    {
        TestCollectionContext = collectionFixture;
    }

    public Services(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Services>();
    }

    private void ResetENVs()
    {
        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_REQUIRED_SERVICE_ENABLED", null);
        Environment.SetEnvironmentVariable("SERVICETESTS.TESTSERVICEENABLED", null);
        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", null);
        Environment.SetEnvironmentVariable("SERVICETESTS.TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("SERVICETESTS.TESTPROPERTY_A", null);
        Environment.SetEnvironmentVariable("SERVICETESTS.TESTPROPERTY_B", null);
        Environment.SetEnvironmentVariable("SERVICETESTS.FORCESERVICEID", null);
        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_TESTPROPERTY", null);
        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_FORCESERVICEID", null);

        TestCollectionContext.ResetENVs();
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
        ResetENVs();
        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "true");
        Assert.True(IDaprService.IsAnyEnabled());
    }

    [Fact]
    public void ServiceEnabled_NoAttribute()
    {
        ResetENVs();
        IDaprService testService = new TestService();
        Assert.False(testService.IsEnabled());

        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "true");
        Assert.False(testService.IsEnabled());
    }

    [Fact]
    public void ServiceEnabled()
    {
        ResetENVs();
        IDaprService testService = new TestService_Attribute();
        Assert.False(testService.IsEnabled());

        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "true");
        Assert.True(testService.IsEnabled());
    }

#pragma warning disable CS0162 // Unreachable code detected
    [Fact]
    public void ServiceEnabled_Default()
    {
        ResetENVs();
        if (ServiceConstants.ENABLE_SERVICES_BY_DEFAULT)
            Assert.True(IDaprService.IsEnabled(typeof(TestService_Attribute)));
        else
            Assert.False(IDaprService.IsEnabled(typeof(TestService_Attribute)));
    }
#pragma warning restore CS0162 // Unreachable code detected

    [Fact]
    public void ServiceEnabled_BadType()
    {
        ResetENVs();
        Assert.Throws<InvalidCastException>(() => IDaprService.IsEnabled(typeof(TestService_Invalid)));
    }

    [Fact]
    public void ServiceEnabled_TestType()
    {
        ResetENVs();
        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "false");
        Assert.False(IDaprService.IsEnabled(typeof(TestService_Attribute)));

        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "true");
        Assert.True(IDaprService.IsEnabled(typeof(TestService_Attribute)));
    }

    [Fact]
    public void ServiceEnabled_TestType_Generic()
    {
        ResetENVs();
        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "false");
        Assert.False(IDaprService.IsEnabled<TestService_Attribute>());

        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "true");
        Assert.True(IDaprService.IsEnabled<TestService_Attribute>());
    }

    [Fact]
    public void ServiceEnabled_TestString()
    {
        ResetENVs();
        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "false");
        Assert.False(IDaprService.IsEnabled("ServiceTests.Test"));

        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_SERVICE_ENABLED", "true");
        Assert.True(IDaprService.IsEnabled("ServiceTests.Test"));
    }

    [Fact]
    public void GetConfigurationType()
    {
        IDaprService testService = new TestService();
        Assert.Equal(typeof(ServiceConfiguration), testService.GetConfigurationType());

        testService = new TestService_Attribute();
        Assert.Equal(typeof(TestConfiguration_Attribute_TestService), testService.GetConfigurationType());

        testService = new TestService_MultipleRequired();
        Assert.Equal(typeof(TestConfiguration_MultipleRequiredProperties_A), testService.GetConfigurationType());
        Assert.NotEqual(typeof(TestConfiguration_MultipleRequiredProperties_B), testService.GetConfigurationType());
    }

    [Fact]
    public void GetConfigurationType_ByServiceType()
    {
        Assert.Equal(typeof(ServiceConfiguration), IDaprService.GetConfigurationType(typeof(TestService)));
        Assert.Equal(typeof(TestConfiguration_Attribute_TestService), IDaprService.GetConfigurationType(typeof(TestService_Attribute)));
        Assert.Equal(typeof(TestConfiguration_MultipleRequiredProperties_A), IDaprService.GetConfigurationType(typeof(TestService_MultipleRequired)));
        Assert.NotEqual(typeof(TestConfiguration_MultipleRequiredProperties_B), IDaprService.GetConfigurationType(typeof(TestService_MultipleRequired)));
    }

    [Fact]
    public void BuildServiceConfiguration()
    {
        ResetENVs();
        var config = (TestConfiguration_Attribute_TestService?)IDaprService.BuildConfiguration(typeof(TestService_Attribute));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Environment.SetEnvironmentVariable("FORCESERVICEID", serviceID);
        config = (TestConfiguration_Attribute_TestService?)IDaprService.BuildConfiguration(typeof(TestService_Attribute));
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void BuildServiceConfiguration_Generic()
    {
        ResetENVs();
        var config = (TestConfiguration_Attribute_TestService?)IDaprService.BuildConfiguration<TestService_Attribute>();
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Environment.SetEnvironmentVariable("FORCESERVICEID", serviceID);
        config = (TestConfiguration_Attribute_TestService?)IDaprService.BuildConfiguration<TestService_Attribute>();
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void BuildServiceConfiguration_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = IDaprService.BuildConfiguration(typeof(TestService_Attribute),
                        args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);

        config = IDaprService.BuildConfiguration(typeof(TestService_Attribute),
                        args: new string[] { $"--forceserviceid={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void BuildServiceConfiguration_Generic_WithArgs()
    {
        ResetENVs();
        var serviceID = Guid.NewGuid().ToString().ToLower();
        var config = IDaprService.BuildConfiguration<TestService_Attribute>(
                        args: new string[] { $"--ForceServiceID={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);

        config = IDaprService.BuildConfiguration<TestService_Attribute>(
                        args: new string[] { $"--forceserviceid={serviceID}" });
        Assert.NotNull(config);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void BuildServiceConfiguration_FromService()
    {
        ResetENVs();
        IDaprService testService = new TestService_Attribute();
        var config = testService.BuildConfiguration() as TestConfiguration_Attribute_TestService;
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Environment.SetEnvironmentVariable("FORCESERVICEID", serviceID);
        config = testService.BuildConfiguration() as TestConfiguration_Attribute_TestService;
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void FindAll()
    {
        ResetENVs();

        Assert.DoesNotContain(IDaprService.FindHandlers(), t => t.Item1 == typeof(TestService));
        Assert.Contains(IDaprService.FindHandlers(), t => t.Item1 == typeof(TestService_Attribute));
        Assert.Contains(IDaprService.FindHandlers(), t => t.Item1 == typeof(TestService_Required));
        Assert.Contains(IDaprService.FindHandlers(), t => t.Item1 == typeof(TestService_MultipleRequired));

#pragma warning disable CS0162 // Unreachable code detected
        if (ServiceConstants.ENABLE_SERVICES_BY_DEFAULT)
        {
            Assert.Contains(IDaprService.FindHandlers(true), t => t.Item1 == typeof(TestService_Attribute));
            Assert.Contains(IDaprService.FindHandlers(true), t => t.Item1 == typeof(TestService_Required));
            Assert.Contains(IDaprService.FindHandlers(true), t => t.Item1 == typeof(TestService_MultipleRequired));
        }
        else
        {
            Assert.DoesNotContain(IDaprService.FindHandlers(true), t => t.Item1 == typeof(TestService_Attribute));
            Assert.DoesNotContain(IDaprService.FindHandlers(true), t => t.Item1 == typeof(TestService_Required));
            Assert.DoesNotContain(IDaprService.FindHandlers(true), t => t.Item1 == typeof(TestService_MultipleRequired));

            Environment.SetEnvironmentVariable("SERVICETESTS.TEST_REQUIRED_SERVICE_ENABLED", "true");
            Assert.Contains(IDaprService.FindHandlers(true), t => t.Item1 == typeof(TestService_Required));
        }
#pragma warning restore CS0162 // Unreachable code detected

    }

    [Fact]
    public void FindAll_TestOverride_MostDerivedType()
    {
        ResetENVs();
        var locateService = IDaprService.FindHandler("ServiceTests.OverrideTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(TestService_Override_2), locateService.Value.Item2);
    }

    [Fact]
    public void FindAll_TestOverride_MostDerivedType_NoAttribute()
    {
        ResetENVs();
        var locateService = IDaprService.FindHandler("ServiceTests.OverrideNoAttrTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(TestService_Override_NoAttribute_2), locateService.Value.Item2);
    }

    [Fact]
    public void FindAll_TestOverride_Priority()
    {
        ResetENVs();
        var locateService = IDaprService.FindHandler("ServiceTests.PriorityTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(TestService_Priority_2), locateService.Value.Item2);
    }

    [Fact]
    public void FindAll_TestOverride_PriorityOverMostDerivedType()
    {
        ResetENVs();
        var locateService = IDaprService.FindHandler("ServiceTests.PriorityOverrideTest");
        Assert.True(locateService.HasValue);
        Assert.Equal(typeof(TestService_PriorityAndOverride_1), locateService.Value.Item2);
    }

    [Fact]
    public void AllHaveRequiredConfiguration()
    {
        ResetENVs();

        Assert.True(IDaprService.AllHaveRequiredConfiguration());

        Environment.SetEnvironmentVariable("SERVICETESTS.TEST_REQUIRED_SERVICE_ENABLED", "true");
        Assert.False(IDaprService.AllHaveRequiredConfiguration());

        Environment.SetEnvironmentVariable("TESTPROPERTY", "something");
        Assert.True(IDaprService.AllHaveRequiredConfiguration());
    }

    [Fact]
    public void HasRequiredConfiguration()
    {
        ResetENVs();
        IDaprService testService = new TestService_Required();
        Assert.False(testService.HasRequiredConfiguration());

        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Assert.True(testService.HasRequiredConfiguration());
    }

    [Fact]
    public void HasRequiredConfiguration_MultipleTypes()
    {
        ResetENVs();
        IDaprService testService = new TestService_MultipleRequired();
        Assert.False(testService.HasRequiredConfiguration());

        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Assert.False(testService.HasRequiredConfiguration());

        Environment.SetEnvironmentVariable("TESTPROPERTY_A", "Test");
        Assert.True(testService.HasRequiredConfiguration());

        Environment.SetEnvironmentVariable("TESTPROPERTY_B", "Test");
        Assert.True(testService.HasRequiredConfiguration());
    }

    [Fact]
    public void HasRequiredConfiguration_ByType()
    {
        ResetENVs();
        Assert.False(IDaprService.HasRequiredConfiguration(typeof(TestService_Required)));

        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Assert.True(IDaprService.HasRequiredConfiguration(typeof(TestService_Required)));
    }

    [Fact]
    public void HasRequiredConfiguration_ByType_MultipleTypes()
    {
        ResetENVs();
        Assert.False(IDaprService.HasRequiredConfiguration(typeof(TestService_MultipleRequired)));

        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Assert.False(IDaprService.HasRequiredConfiguration(typeof(TestService_MultipleRequired)));

        Environment.SetEnvironmentVariable("TESTPROPERTY_A", "Test");
        Assert.False(IDaprService.HasRequiredConfiguration(typeof(TestService_MultipleRequired)));

        Environment.SetEnvironmentVariable("TESTPROPERTY_B", "Test");
        Assert.True(IDaprService.HasRequiredConfiguration(typeof(TestService_MultipleRequired)));
    }

    [Fact]
    public void HasRequiredConfiguration_ByType_Generic()
    {
        ResetENVs();
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_Required>());

        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Assert.True(IDaprService.HasRequiredConfiguration<TestService_Required>());
    }

    [Fact]
    public void HasRequiredConfiguration_ByType_MultipleTypes_Generic()
    {
        ResetENVs();
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());

        Environment.SetEnvironmentVariable("TESTPROPERTY", "Test");
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());

        Environment.SetEnvironmentVariable("TESTPROPERTY_A", "Test");
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());

        Environment.SetEnvironmentVariable("TESTPROPERTY_B", "Test");
        Assert.True(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());
    }
}
