using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.UnitTests;

public class ServiceTests
{
    [DaprService("test")]
    public class TestService : IDaprService { }

    [DaprService("test")]
    public class TestDaprService : IDaprService { }

    [DaprService("test")]
    public class TestController : Controller, IDaprService { }

    [DaprService("test")]
    public class TestDaprController : Controller, IDaprService { }

    public ServiceTests()
    {
    }

    [Fact]
    public void GetServiceName()
    {
        Assert.Equal("Test", typeof(TestService).GetServiceName());
        Assert.Equal("Test", typeof(TestController).GetServiceName());
        Assert.Equal("Test", typeof(TestDaprService).GetServiceName());
        Assert.Equal("Test", typeof(TestDaprController).GetServiceName());
    }
}
