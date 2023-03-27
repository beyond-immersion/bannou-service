namespace BeyondImmersion.UnitTests;

[Collection("core tests")]
public class Services : IClassFixture<Fixture>
{
    private Fixture TestFixture { get; }

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

    [ServiceConfiguration(typeof(TestService_MultipleRequired))]
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

    public Services(Fixture fixture)
    {
        TestFixture = fixture;
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

#pragma warning disable CS0162 // Unreachable code detected
    [Fact]
    public void ServiceEnabled_Default()
    {
        TestFixture.ResetENVs();
        if (ServiceConstants.ENABLE_SERVICES_BY_DEFAULT)
            Assert.True(IDaprService.IsEnabled(typeof(TestService_Attribute)));
        else
            Assert.False(IDaprService.IsEnabled(typeof(TestService_Attribute)));
    }
#pragma warning restore CS0162 // Unreachable code detected

    [Fact]
    public void ServiceEnabled_BadType()
    {
        TestFixture.ResetENVs();
        Assert.Throws<InvalidCastException>(() => IDaprService.IsEnabled(typeof(TestService_Invalid)));
    }

    [Fact]
    public void ServiceEnabled_TestType()
    {
        TestFixture.ResetENVs();
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "false");
        Assert.False(IDaprService.IsEnabled(typeof(TestService_Attribute)));

        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(IDaprService.IsEnabled(typeof(TestService_Attribute)));
    }

    [Fact]
    public void ServiceEnabled_TestType_Generic()
    {
        TestFixture.ResetENVs();
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "false");
        Assert.False(IDaprService.IsEnabled<TestService_Attribute>());

        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(IDaprService.IsEnabled<TestService_Attribute>());
    }

    [Fact]
    public void ServiceEnabled_TestString()
    {
        TestFixture.ResetENVs();
        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "false");
        Assert.False(IDaprService.IsEnabled("TestService"));

        Environment.SetEnvironmentVariable("TEST_SERVICE_ENABLED", "true");
        Assert.True(IDaprService.IsEnabled("TestService"));
    }

    [Fact]
    public void ServiceEnabled_TestString_Lower()
    {
        TestFixture.ResetENVs();
        Environment.SetEnvironmentVariable("test_service_enabled", "false");
        Assert.False(IDaprService.IsEnabled("testservice"));

        Environment.SetEnvironmentVariable("test_service_enabled", "true");
        Assert.True(IDaprService.IsEnabled("testservice"));
    }

    [Fact]
    public void BuildServiceConfiguration()
    {
        TestFixture.ResetENVs();
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
        TestFixture.ResetENVs();
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
        TestFixture.ResetENVs();
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
        TestFixture.ResetENVs();
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
    public void HasRequiredConfiguration()
    {
        TestFixture.ResetENVs();
        IDaprService testService = new TestService_Required();
        Assert.False(testService.HasRequiredConfiguration());

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.True(testService.HasRequiredConfiguration());
    }

    [Fact]
    public void HasRequiredConfiguration_MultipleTypes()
    {
        TestFixture.ResetENVs();
        IDaprService testService = new TestService_MultipleRequired();
        Assert.False(testService.HasRequiredConfiguration());

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.False(testService.HasRequiredConfiguration());

        Environment.SetEnvironmentVariable("TestProperty_A", "Test");
        Assert.False(testService.HasRequiredConfiguration());

        Environment.SetEnvironmentVariable("TestProperty_B", "Test");
        Assert.True(testService.HasRequiredConfiguration());
    }

    [Fact]
    public void HasRequiredConfiguration_ByType()
    {
        TestFixture.ResetENVs();
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_Required>());

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.True(IDaprService.HasRequiredConfiguration<TestService_Required>());
    }

    [Fact]
    public void HasRequiredConfiguration_ByType_MultipleTypes()
    {
        TestFixture.ResetENVs();
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());

        Environment.SetEnvironmentVariable("TestProperty", "Test");
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());

        Environment.SetEnvironmentVariable("TestProperty_A", "Test");
        Assert.False(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());

        Environment.SetEnvironmentVariable("TestProperty_B", "Test");
        Assert.True(IDaprService.HasRequiredConfiguration<TestService_MultipleRequired>());
    }
}
