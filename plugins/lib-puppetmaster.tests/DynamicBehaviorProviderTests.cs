using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.BannouService.Puppetmaster.Caching;
using BeyondImmersion.BannouService.Puppetmaster.Providers;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Puppetmaster.Tests;

/// <summary>
/// Unit tests for DynamicBehaviorProvider.
/// </summary>
public class DynamicBehaviorProviderTests
{
    private readonly Mock<BehaviorDocumentCache> _mockCache;
    private readonly DynamicBehaviorProvider _provider;

    public DynamicBehaviorProviderTests()
    {
        var mockScopeFactory = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockCacheLogger = new Mock<ILogger<BehaviorDocumentCache>>();
        var configuration = new PuppetmasterServiceConfiguration();

        _mockCache = new Mock<BehaviorDocumentCache>(
            mockScopeFactory.Object,
            mockHttpClientFactory.Object,
            mockCacheLogger.Object,
            configuration);

        _provider = new DynamicBehaviorProvider(_mockCache.Object);
    }

    #region Priority Tests

    [Fact]
    public void Priority_Returns100()
    {
        // Priority 100 = highest, checked before seeded providers
        Assert.Equal(100, _provider.Priority);
    }

    #endregion

    #region CanProvide Tests

    [Fact]
    public void CanProvide_WithValidGuid_ReturnsTrue()
    {
        // Arrange
        var behaviorRef = Guid.NewGuid().ToString();

        // Act
        var result = _provider.CanProvide(behaviorRef);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanProvide_WithNull_ReturnsFalse()
    {
        // Act
        var result = _provider.CanProvide(null!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanProvide_WithEmptyString_ReturnsFalse()
    {
        // Act
        var result = _provider.CanProvide("");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanProvide_WithWhitespace_ReturnsFalse()
    {
        // Act
        var result = _provider.CanProvide("   ");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanProvide_WithNonGuid_ReturnsFalse()
    {
        // Arrange - seeded behaviors use non-GUID refs like "humanoid_base"
        var behaviorRef = "humanoid_base";

        // Act
        var result = _provider.CanProvide(behaviorRef);

        // Assert - DynamicBehaviorProvider only handles asset GUIDs
        Assert.False(result);
    }

    [Fact]
    public void CanProvide_WithInvalidGuid_ReturnsFalse()
    {
        // Arrange
        var behaviorRef = "not-a-valid-guid-12345";

        // Act
        var result = _provider.CanProvide(behaviorRef);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetDocumentAsync Tests

    [Fact]
    public async Task GetDocumentAsync_DelegatesToCache()
    {
        // Arrange
        var behaviorRef = Guid.NewGuid().ToString();
        var expectedDoc = new AbmlDocument
        {
            Version = "2.0",
            Metadata = new DocumentMetadata { Id = "test-doc" }
        };
        _mockCache
            .Setup(c => c.GetOrLoadAsync(behaviorRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedDoc);

        // Act
        var result = await _provider.GetDocumentAsync(behaviorRef, CancellationToken.None);

        // Assert
        Assert.Same(expectedDoc, result);
        _mockCache.Verify(c => c.GetOrLoadAsync(behaviorRef, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDocumentAsync_WhenCacheReturnsNull_ReturnsNull()
    {
        // Arrange
        var behaviorRef = Guid.NewGuid().ToString();
        _mockCache
            .Setup(c => c.GetOrLoadAsync(behaviorRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AbmlDocument?)null);

        // Act
        var result = await _provider.GetDocumentAsync(behaviorRef, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Invalidate Tests

    [Fact]
    public void Invalidate_DelegatesToCache()
    {
        // Arrange
        var behaviorRef = Guid.NewGuid().ToString();
        _mockCache.Setup(c => c.Invalidate(behaviorRef)).Returns(true);

        // Act
        _provider.Invalidate(behaviorRef);

        // Assert
        _mockCache.Verify(c => c.Invalidate(behaviorRef), Times.Once);
    }

    [Fact]
    public void InvalidateAll_DelegatesToCache()
    {
        // Arrange
        _mockCache.Setup(c => c.InvalidateAll()).Returns(5);

        // Act
        _provider.InvalidateAll();

        // Assert
        _mockCache.Verify(c => c.InvalidateAll(), Times.Once);
    }

    #endregion
}
