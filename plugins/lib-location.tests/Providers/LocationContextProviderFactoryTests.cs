using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Location.Caching;
using BeyondImmersion.BannouService.Location.Providers;

namespace BeyondImmersion.BannouService.Location.Tests.Providers;

/// <summary>
/// Unit tests for LocationContextProviderFactory.
/// </summary>
public class LocationContextProviderFactoryTests
{
    [Fact]
    public void ProviderName_ReturnsLocation()
    {
        var mockCache = new Mock<ILocationDataCache>();
        var factory = new LocationContextProviderFactory(mockCache.Object);
        Assert.Equal("location", factory.ProviderName);
    }

    [Fact]
    public async Task CreateAsync_NullEntityId_ReturnsEmptyProvider()
    {
        var mockCache = new Mock<ILocationDataCache>();
        var factory = new LocationContextProviderFactory(mockCache.Object);

        var provider = await factory.CreateAsync(null, CancellationToken.None);

        Assert.Same(LocationContextProvider.Empty, provider);
        mockCache.Verify(
            c => c.GetOrLoadLocationContextAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithEntityId_LoadsFromCache()
    {
        var characterId = Guid.NewGuid();
        var contextData = new LocationContextData(
            Zone: "MARKET_DISTRICT",
            Name: "Market District",
            Region: "CENTRAL_REGION",
            Type: LocationType.DISTRICT,
            Depth: 3,
            Realm: "ARCADIA",
            NearbyPois: new List<string> { "TEMPLE_DISTRICT" },
            EntityCount: 15);

        var mockCache = new Mock<ILocationDataCache>();
        mockCache.Setup(c => c.GetOrLoadLocationContextAsync(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(contextData);

        var factory = new LocationContextProviderFactory(mockCache.Object);

        var provider = await factory.CreateAsync(characterId, CancellationToken.None);

        Assert.NotNull(provider);
        Assert.Equal("location", provider.Name);
        Assert.Equal("MARKET_DISTRICT", provider.GetValue(new[] { "zone" }.AsSpan()));
        mockCache.Verify(
            c => c.GetOrLoadLocationContextAsync(characterId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithEntityId_NullData_ReturnsProviderWithNullValues()
    {
        var characterId = Guid.NewGuid();
        var mockCache = new Mock<ILocationDataCache>();
        mockCache.Setup(c => c.GetOrLoadLocationContextAsync(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationContextData?)null);

        var factory = new LocationContextProviderFactory(mockCache.Object);

        var provider = await factory.CreateAsync(characterId, CancellationToken.None);

        Assert.NotNull(provider);
        Assert.Equal("location", provider.Name);
        // Provider wraps null data — all paths return null
        Assert.Null(provider.GetValue(new[] { "zone" }.AsSpan()));
        Assert.Null(provider.GetRootValue());
    }
}
