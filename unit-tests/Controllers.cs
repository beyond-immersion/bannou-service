using Microsoft.AspNetCore.Mvc;
using Xunit.Abstractions;

namespace BeyondImmersion.UnitTests;

public class Controllers : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    [DaprService("test")]
    public class TestController : Controller, IDaprController { }

    [DaprService("test")]
    public class TestDaprController : Controller, IDaprController { }

    private Controllers(CollectionFixture collectionContext)
    {
        TestCollectionContext = collectionContext;
    }

    public Controllers(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Controllers>();
    }

    [Fact]
    public void GetServiceName()
    {
        Assert.Equal("test", typeof(TestController).GetServiceName());
        Assert.Equal("test", typeof(TestDaprController).GetServiceName());
    }
}
