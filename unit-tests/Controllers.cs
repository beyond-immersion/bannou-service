using Microsoft.AspNetCore.Mvc;
using Xunit.Abstractions;

namespace BeyondImmersion.UnitTests;

[Collection("unit tests")]
public class Controllers : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public class TestController : ControllerBase, IDaprController { }

    public class TestDaprController : ControllerBase, IDaprController { }

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
        Assert.Equal("Test", typeof(TestController).GetServiceName());
        Assert.Equal("Test", typeof(TestDaprController).GetServiceName());
    }
}
