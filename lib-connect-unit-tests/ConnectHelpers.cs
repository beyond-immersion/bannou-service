using BeyondImmersion.BannouService.UnitTests;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.Connect.UnitTests;

[Collection("unit tests")]
public class ConnectHelpers : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public ConnectHelpers(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<ConnectHelpers>();
    }

    [Fact]
    public void GenerateServiceRequestUri()
    {
        var serviceUri = ConnectService.GenerateServiceRequestUrl("bannou");
        var baseDaprUri = new Uri("http://127.0.0.1:80/v1.0/invoke/", UriKind.Absolute);

        Assert.True(serviceUri?.IsAbsoluteUri ?? false);
        Assert.Equal(80, serviceUri.Port);
        Assert.True(serviceUri.IsLoopback);
        Assert.Equal("bannou/method/", serviceUri.MakeRelativeUri(baseDaprUri).PathAndQuery);
    }
}
