using Microsoft.AspNetCore.Mvc;
using Xunit.Abstractions;

namespace BeyondImmersion.UnitTests;

[Collection("unit tests")]
public class Controllers : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public class TestController_NoAttribute : ControllerBase, IDaprController { }

    [DaprController("/", serviceType: typeof(TestService_Attribute))]
    public class TestController_Attribute : ControllerBase, IDaprController { }

    [DaprService("ControllerTests.Test")]
    public class TestService_Attribute : IDaprService { }

    private Controllers(CollectionFixture collectionContext)
    {
        TestCollectionContext = collectionContext;
    }

    public Controllers(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Controllers>();
    }
}
