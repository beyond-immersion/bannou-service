using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.UnitTests;

[Collection("core tests")]
public class Controllers : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    [DaprService("test")]
    public class TestController : Controller, IDaprController { }

    [DaprService("test")]
    public class TestDaprController : Controller, IDaprController { }

    public Controllers(CollectionFixture collectionContext)
    {
        TestCollectionContext = collectionContext;
    }

    [Fact]
    public void GetServiceName()
    {
        Assert.Equal("test", typeof(TestController).GetServiceName());
        Assert.Equal("test", typeof(TestDaprController).GetServiceName());
    }
}
