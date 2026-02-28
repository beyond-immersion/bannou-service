using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Seed.Caching;
using BeyondImmersion.BannouService.Seed.Providers;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Seed.Tests.Providers;

/// <summary>
/// Unit tests for SeedProviderFactory.
/// </summary>
public class SeedProviderFactoryTests
{
    [Fact]
    public void ProviderName_ReturnsSeed()
    {
        var mockCache = new Mock<ISeedDataCache>();
        var factory = new SeedProviderFactory(mockCache.Object, new NullTelemetryProvider());
        Assert.Equal("seed", factory.ProviderName);
    }

    [Fact]
    public async Task CreateAsync_NullEntityId_ReturnsEmptyProvider()
    {
        var mockCache = new Mock<ISeedDataCache>();
        var factory = new SeedProviderFactory(mockCache.Object, new NullTelemetryProvider());

        var provider = await factory.CreateAsync(null, Guid.NewGuid(), null, CancellationToken.None);

        Assert.Same(SeedProvider.Empty, provider);
        mockCache.Verify(c => c.GetSeedDataOrLoadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithEntityId_LoadsFromCache()
    {
        var characterId = Guid.NewGuid();
        var mockCache = new Mock<ISeedDataCache>();
        mockCache.Setup(c => c.GetSeedDataOrLoadAsync(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CachedSeedData.Empty);

        var factory = new SeedProviderFactory(mockCache.Object, new NullTelemetryProvider());

        var provider = await factory.CreateAsync(characterId, Guid.NewGuid(), null, CancellationToken.None);

        Assert.NotNull(provider);
        Assert.Equal("seed", provider.Name);
        mockCache.Verify(c => c.GetSeedDataOrLoadAsync(characterId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WithEntityId_EmptyData_ReturnsProviderWithZeroActiveCount()
    {
        var characterId = Guid.NewGuid();
        var mockCache = new Mock<ISeedDataCache>();
        mockCache.Setup(c => c.GetSeedDataOrLoadAsync(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CachedSeedData.Empty);

        var factory = new SeedProviderFactory(mockCache.Object, new NullTelemetryProvider());

        var provider = await factory.CreateAsync(characterId, Guid.NewGuid(), null, CancellationToken.None);
        var activeCount = provider.GetValue(new[] { "active_count" }.AsSpan());

        Assert.Equal(0, activeCount);
    }

    [Fact]
    public async Task CreateAsync_WithEntityId_PopulatedData_ReturnsProviderWithSeeds()
    {
        var characterId = Guid.NewGuid();
        var seedId = Guid.NewGuid();
        var mockCache = new Mock<ISeedDataCache>();

        var seed = new SeedResponse
        {
            SeedId = seedId,
            OwnerId = characterId,
            OwnerType = EntityType.Character,
            SeedTypeCode = "guardian",
            GameServiceId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            GrowthPhase = "seedling",
            TotalGrowth = 5.0f,
            DisplayName = "Test Seed",
            Status = SeedStatus.Active
        };

        var data = new CachedSeedData(
            new[] { seed },
            new Dictionary<Guid, GrowthResponse>(),
            new Dictionary<Guid, CapabilityManifestResponse>());

        mockCache.Setup(c => c.GetSeedDataOrLoadAsync(characterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(data);

        var factory = new SeedProviderFactory(mockCache.Object, new NullTelemetryProvider());

        var provider = await factory.CreateAsync(characterId, Guid.NewGuid(), null, CancellationToken.None);
        var activeCount = provider.GetValue(new[] { "active_count" }.AsSpan());
        var phase = provider.GetValue(new[] { "guardian", "phase" }.AsSpan());

        Assert.Equal(1, activeCount);
        Assert.Equal("seedling", phase);
    }
}
