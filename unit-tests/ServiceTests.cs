using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.UnitTests;

public class ServiceTests
{
    [DaprService(template: "test")]
    public class TestService : Controller, IDaprService { }

    [DaprService(template: "test")]
    public class TestDaprService : Controller, IDaprService { }

    [DaprService(template: "test")]
    public class TestController : Controller, IDaprService { }

    [DaprService(template: "test")]
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
