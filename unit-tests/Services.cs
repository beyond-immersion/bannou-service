using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.UnitTests;

[Collection("core tests")]
public class Services : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    private class TestService_Invalid { }

    [DaprService("InvalidTest")]
    private class TestService_Invalid_Attribute { }

    private class TestService : IDaprService { }

    private class TestDaprService : IDaprService { }

    [DaprService("Test")]
    private class TestService_Attribute : IDaprService { }

    private class TestService_NoConvention_NoAttribute : IDaprService { }

    [DaprService("Test")]
    private class TestService_NoConvention_Attribute : IDaprService { }

    [ServiceConfiguration(typeof(TestService_Attribute))]
    private class TestConfiguration_Attribute_TestService : IServiceConfiguration
    {
        public string? ForceServiceID { get; set; }
        public string? TestProperty { get; set; }
    }

    [DaprService("test")]
    private class TestService_Required : IDaprService { }

    [DaprService("test")]
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

    public Services(CollectionFixture collectionContext)
    {
        TestCollectionContext = collectionContext;
    }

    [Fact]
    public void GetServiceName()
    {
        Assert.Equal("Test", typeof(TestService).GetServiceName());
        Assert.Equal("Test", typeof(TestDaprService).GetServiceName());
        Assert.Equal("Test", typeof(TestService_Attribute).GetServiceName());
        Assert.Equal("Test", typeof(TestService_NoConvention_Attribute).GetServiceName());
        Assert.Equal(nameof(TestService_NoConvention_NoAttribute), typeof(TestService_NoConvention_NoAttribute).GetServiceName());
    }

    [Fact]
    public void GetServiceName_FromService()
    {
        IDaprService testService = new TestService_NoConvention_NoAttribute();
        Assert.Equal(nameof(TestService_NoConvention_NoAttribute), testService.GetName());
    }

    [Fact]
    public void AnyServiceEnabled()
    {
        TestCollectionContext.ResetENVs();
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(IDaprService.IsAnyEnabled());
    }

    [Fact]
    public void ServiceEnabled()
    {
        TestCollectionContext.ResetENVs();
        IDaprService testService = new TestService();
        Assert.False(testService.IsEnabled());

        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(testService.IsEnabled());
    }

#pragma warning disable CS0162 // Unreachable code detected
    [Fact]
    public void ServiceEnabled_Default()
    {
        TestCollectionContext.ResetENVs();
        if (ServiceConstants.ENABLE_SERVICES_BY_DEFAULT)
            Assert.True(IDaprService.IsEnabled(typeof(TestService_Attribute)));
        else
            Assert.False(IDaprService.IsEnabled(typeof(TestService_Attribute)));
    }
#pragma warning restore CS0162 // Unreachable code detected

    [Fact]
    public void ServiceEnabled_BadType()
    {
        TestCollectionContext.ResetENVs();
        Assert.Throws<InvalidCastException>(() => IDaprService.IsEnabled(typeof(TestService_Invalid)));
    }

    [Fact]
    public void ServiceEnabled_TestType()
    {
        TestCollectionContext.ResetENVs();
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "false");
        Assert.False(IDaprService.IsEnabled(typeof(TestService_Attribute)));

        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(IDaprService.IsEnabled(typeof(TestService_Attribute)));
    }

    [Fact]
    public void ServiceEnabled_TestType_Generic()
    {
        TestCollectionContext.ResetENVs();
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "false");
        Assert.False(IDaprService.IsEnabled<TestService_Attribute>());

        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(IDaprService.IsEnabled<TestService_Attribute>());
    }

    [Fact]
    public void ServiceEnabled_TestString()
    {
        TestCollectionContext.ResetENVs();
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "false");
        Assert.False(IDaprService.IsEnabled("TestService"));

        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(IDaprService.IsEnabled("TestService"));
    }

    [Fact]
    public void ServiceEnabled_TestString_Lower()
    {
        TestCollectionContext.ResetENVs();
        Environment.SetEnvironmentVariable("test_service_enabled", "false");
        Assert.False(IDaprService.IsEnabled("testservice"));

        Environment.SetEnvironmentVariable("test_service_enabled", "true");
        Assert.True(IDaprService.IsEnabled("testservice"));
    }

    [Fact]
    public void GetConfigurationType()
    {
        IDaprService testService = new TestService_NoConvention_NoAttribute();
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
        Assert.Equal(typeof(ServiceConfiguration), IDaprService.GetConfigurationType(typeof(TestService_NoConvention_NoAttribute)));
        Assert.Equal(typeof(TestConfiguration_Attribute_TestService), IDaprService.GetConfigurationType(typeof(TestService_Attribute)));
        Assert.Equal(typeof(TestConfiguration_MultipleRequiredProperties_A), IDaprService.GetConfigurationType(typeof(TestService_MultipleRequired)));
        Assert.NotEqual(typeof(TestConfiguration_MultipleRequiredProperties_B), IDaprService.GetConfigurationType(typeof(TestService_MultipleRequired)));
    }

    [Fact]
    public void BuildServiceConfiguration()
    {
        TestCollectionContext.ResetENVs();
        var config = (TestConfiguration_Attribute_TestService?)IDaprService.BuildConfiguration(typeof(TestService_Attribute));
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = (TestConfiguration_Attribute_TestService?)IDaprService.BuildConfiguration(typeof(TestService_Attribute));
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void BuildServiceConfiguration_Generic()
    {
        TestCollectionContext.ResetENVs();
        var config = (TestConfiguration_Attribute_TestService?)IDaprService.BuildConfiguration<TestService_Attribute>();
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = (TestConfiguration_Attribute_TestService?)IDaprService.BuildConfiguration<TestService_Attribute>();
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void BuildServiceConfiguration_WithArgs()
    {
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
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
        TestCollectionContext.ResetENVs();
        IDaprService testService = new TestService_Attribute();
        var config = testService.BuildConfiguration() as TestConfiguration_Attribute_TestService;
        Assert.NotNull(config);
        Assert.Null(config.TestProperty);
        Assert.Null(config.ForceServiceID);

        var serviceID = Guid.NewGuid().ToString().ToLower();
        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Environment.SetEnvironmentVariable("ForceServiceID", serviceID);
        config = testService.BuildConfiguration() as TestConfiguration_Attribute_TestService;
        Assert.NotNull(config);
        Assert.Equal("Test", config.TestProperty);
        Assert.Equal(serviceID, config.ForceServiceID);
    }

    [Fact]
    public void HasRequiredConfiguration()
    {
        TestCollectionContext.ResetENVs();
        IDaprService testService = new TestService_Required();
        Assert.False(testService.HasRequiredConfiguration());

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.True(testService.HasRequiredConfiguration());
    }

    [Fact]
    public void HasRequiredConfiguration_MultipleTypes()
    {
        TestCollectionContext.ResetENVs();
        IDaprService testService = new TestService_MultipleRequired();
        Assert.False(testService.HasRequiredConfiguration());

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.False(testService.HasRequiredConfiguration());

        Environment.SetEnvironmentVariable("TestProperty_A", "Test");
        Assert.True(testService.HasRequiredConfiguration());

        Environment.SetEnvironmentVariable("TestProperty_B", "Test");
        Assert.True(testService.HasRequiredConfiguration());
    }

    [Fact]
    public void HasRequiredConfiguration_ByType()
    {
        TestCollectionContext.ResetENVs();
        Assert.False(IDaprService.HasRequiredConfiguration(typeof(TestService_Required)));

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.True(IDaprService.HasRequiredConfiguration(typeof(TestService_Required)));
    }

    [Fact]
    public void HasRequiredConfiguration_ByType_MultipleTypes()
    {
        TestCollectionContext.ResetENVs();
        Assert.False(IDaprService.HasRequiredConfiguration(typeof(TestService_MultipleRequired)));

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.False(IDaprService.HasRequiredConfiguration(typeof(TestService_MultipleRequired)));

        Environment.SetEnvironmentVariable("TestProperty_A", "Test");
        Assert.False(IDaprService.HasRequiredConfiguration(typeof(TestService_MultipleRequired)));

        Environment.SetEnvironmentVariable("TestProperty_B", "Test");
        Assert.True(IDaprService.HasRequiredConfiguration(typeof(TestService_MultipleRequired)));
    }

    [Fact]
    public void HasRequiredConfiguration_ByType_Generic()
    {
        TestCollectionContext.ResetENVs();
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_Required>());

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.True(IDaprService.HasRequiredConfiguration<TestService_Required>());
    }

    [Fact]
    public void HasRequiredConfiguration_ByType_MultipleTypes_Generic()
    {
        TestCollectionContext.ResetENVs();
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());

        Environment.SetEnvironmentVariable("TestProperty_A", "Test");
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());

        Environment.SetEnvironmentVariable("TestProperty_B", "Test");
        Assert.True(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());
    }
}
