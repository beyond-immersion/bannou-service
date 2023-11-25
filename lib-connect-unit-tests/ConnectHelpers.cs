using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.Connect.UnitTests;

[Collection("connect unit tests")]
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
        Assert.Equal("bannou/method/", baseDaprUri.MakeRelativeUri(serviceUri).ToString());
        Assert.Equal("../../", serviceUri.MakeRelativeUri(baseDaprUri).ToString());
    }

    [Fact]
    public void ValidateAndDecodeToken()
    {
        ConnectService.ValidateAndDecodeToken();
    }

    [Fact]
    public void GetMessageID()
    {
        ConnectService.GetMessageID();
    }

    [Fact]
    public void GetMessageChannel()
    {
        ConnectService.GetMessageChannel();
    }

    [Fact]
    public void GetServiceID()
    {
        ConnectService.GetServiceID();
    }

    [Fact]
    public void GetMessageContent()
    {
        ConnectService.GetMessageContent();
    }

    [Fact]
    public void GetMessageResponseCode()
    {
        ConnectService.GetMessageResponseCode();
    }

    [Fact]
    public void CreateResponseMessageBytes()
    {
        ConnectService.CreateResponseMessageBytes();
    }

    [Fact]
    public void CreateRPCMessageBytes()
    {
        ConnectService.CreateRPCMessageBytes();
    }
}
