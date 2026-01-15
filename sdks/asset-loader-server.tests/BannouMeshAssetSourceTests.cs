using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.BannouService.Asset;
using Moq;
using Xunit;

namespace BeyondImmersion.Bannou.AssetLoader.Server.Tests;

/// <summary>
/// Unit tests for BannouMeshAssetSource.
/// Uses mocked IAssetClient to test behavior without live server.
/// </summary>
public class BannouMeshAssetSourceTests
{
    private readonly Mock<IAssetClient> _mockAssetClient;
    private readonly BannouMeshAssetSource _source;

    public BannouMeshAssetSourceTests()
    {
        _mockAssetClient = new Mock<IAssetClient>(MockBehavior.Strict);
        _source = new BannouMeshAssetSource(_mockAssetClient.Object);
    }

    #region Constructor Tests

    /// <summary>
    /// Verifies that constructor throws when client is null.
    /// </summary>
    [Fact]
    public void Constructor_NullClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new BannouMeshAssetSource(null!));
    }

    /// <summary>
    /// Verifies that constructor accepts valid client.
    /// </summary>
    [Fact]
    public void Constructor_ValidClient_CreatesInstance()
    {
        // Act
        var source = new BannouMeshAssetSource(_mockAssetClient.Object);

        // Assert
        Assert.NotNull(source);
    }

    /// <summary>
    /// Verifies that constructor accepts custom realm.
    /// </summary>
    [Fact]
    public void Constructor_WithCustomRealm_CreatesInstance()
    {
        // Act
        var source = new BannouMeshAssetSource(_mockAssetClient.Object, Realm.Fantasia);

        // Assert
        Assert.NotNull(source);
    }

    #endregion

    #region Property Tests

    /// <summary>
    /// Verifies that RequiresAuthentication is false (mesh handles auth).
    /// </summary>
    [Fact]
    public void RequiresAuthentication_ReturnsFalse()
    {
        // Assert
        Assert.False(_source.RequiresAuthentication);
    }

    /// <summary>
    /// Verifies that IsAvailable is true (mesh is always available).
    /// </summary>
    [Fact]
    public void IsAvailable_ReturnsTrue()
    {
        // Assert
        Assert.True(_source.IsAvailable);
    }

    #endregion

    #region ResolveBundlesAsync Tests

    /// <summary>
    /// Verifies that ResolveBundlesAsync validates null input.
    /// </summary>
    [Fact]
    public async Task ResolveBundlesAsync_NullAssetIds_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _source.ResolveBundlesAsync(null!));
    }

    /// <summary>
    /// Verifies that ResolveBundlesAsync returns resolved bundles.
    /// </summary>
    [Fact]
    public async Task ResolveBundlesAsync_WithAssets_ReturnsBundles()
    {
        // Arrange
        var response = new ResolveBundlesResponse
        {
            Bundles = new List<ResolvedBundle>
            {
                new ResolvedBundle
                {
                    BundleId = "bundle-1",
                    DownloadUrl = new Uri("https://example.com/bundle-1.bannou"),
                    Size = 1024,
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    BundleType = BundleType.Source,
                    AssetsProvided = new List<string> { "asset-1", "asset-2" }
                }
            },
            StandaloneAssets = new List<ResolvedAsset>()
        };

        _mockAssetClient
            .Setup(c => c.ResolveBundlesAsync(It.IsAny<ResolveBundlesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _source.ResolveBundlesAsync(new[] { "asset-1", "asset-2" });

        // Assert
        Assert.Single(result.Bundles);
        Assert.Equal("bundle-1", result.Bundles[0].BundleId);
        Assert.Equal(1024, result.Bundles[0].SizeBytes);
    }

    /// <summary>
    /// Verifies that ResolveBundlesAsync correctly maps metabundle type.
    /// </summary>
    [Fact]
    public async Task ResolveBundlesAsync_WithMetabundle_SetsIsMetabundleTrue()
    {
        // Arrange
        var response = new ResolveBundlesResponse
        {
            Bundles = new List<ResolvedBundle>
            {
                new ResolvedBundle
                {
                    BundleId = "metabundle-1",
                    DownloadUrl = new Uri("https://example.com/metabundle.bannou"),
                    Size = 2048,
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    BundleType = BundleType.Metabundle,
                    AssetsProvided = new List<string> { "asset-1" }
                }
            },
            StandaloneAssets = new List<ResolvedAsset>()
        };

        _mockAssetClient
            .Setup(c => c.ResolveBundlesAsync(It.IsAny<ResolveBundlesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _source.ResolveBundlesAsync(new[] { "asset-1" });

        // Assert
        Assert.True(result.Bundles[0].IsMetabundle);
    }

    /// <summary>
    /// Verifies that ResolveBundlesAsync handles standalone assets.
    /// </summary>
    [Fact]
    public async Task ResolveBundlesAsync_WithStandaloneAssets_ReturnsStandaloneAssets()
    {
        // Arrange
        var response = new ResolveBundlesResponse
        {
            Bundles = new List<ResolvedBundle>(),
            StandaloneAssets = new List<ResolvedAsset>
            {
                new ResolvedAsset
                {
                    AssetId = "standalone-1",
                    DownloadUrl = new Uri("https://example.com/asset.bin"),
                    Size = 512,
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
                }
            }
        };

        _mockAssetClient
            .Setup(c => c.ResolveBundlesAsync(It.IsAny<ResolveBundlesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _source.ResolveBundlesAsync(new[] { "standalone-1" });

        // Assert
        Assert.Empty(result.Bundles);
        Assert.Single(result.StandaloneAssets);
        Assert.Equal("standalone-1", result.StandaloneAssets[0].AssetId);
    }

    /// <summary>
    /// Verifies that ResolveBundlesAsync passes PreferMetabundles correctly.
    /// </summary>
    [Fact]
    public async Task ResolveBundlesAsync_SendsPreferMetabundles()
    {
        // Arrange
        ResolveBundlesRequest? capturedRequest = null;
        var response = new ResolveBundlesResponse
        {
            Bundles = new List<ResolvedBundle>(),
            StandaloneAssets = new List<ResolvedAsset>()
        };

        _mockAssetClient
            .Setup(c => c.ResolveBundlesAsync(It.IsAny<ResolveBundlesRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ResolveBundlesRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(response);

        // Act
        await _source.ResolveBundlesAsync(new[] { "asset-1" });

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest.PreferMetabundles);
        Assert.True(capturedRequest.IncludeStandalone);
    }

    #endregion

    #region GetBundleDownloadInfoAsync Tests

    /// <summary>
    /// Verifies that GetBundleDownloadInfoAsync validates null input.
    /// </summary>
    [Fact]
    public async Task GetBundleDownloadInfoAsync_NullBundleId_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _source.GetBundleDownloadInfoAsync(null!));
    }

    /// <summary>
    /// Verifies that GetBundleDownloadInfoAsync validates empty input.
    /// </summary>
    [Fact]
    public async Task GetBundleDownloadInfoAsync_EmptyBundleId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _source.GetBundleDownloadInfoAsync(""));
    }

    /// <summary>
    /// Verifies that GetBundleDownloadInfoAsync returns bundle info.
    /// </summary>
    [Fact]
    public async Task GetBundleDownloadInfoAsync_ExistingBundle_ReturnsInfo()
    {
        // Arrange
        var response = new BundleWithDownloadUrl
        {
            BundleId = "bundle-1",
            Version = "1.0.0",
            DownloadUrl = new Uri("https://example.com/bundle.bannou"),
            Size = 2048,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Format = BundleFormat.Bannou,
            AssetCount = 5,
            FromCache = false
        };

        _mockAssetClient
            .Setup(c => c.GetBundleAsync(It.IsAny<GetBundleRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _source.GetBundleDownloadInfoAsync("bundle-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("bundle-1", result.BundleId);
        Assert.Equal(2048, result.SizeBytes);
        Assert.Equal(new Uri("https://example.com/bundle.bannou"), result.DownloadUrl);
    }

    /// <summary>
    /// Verifies that GetBundleDownloadInfoAsync returns null for 404.
    /// </summary>
    [Fact]
    public async Task GetBundleDownloadInfoAsync_NotFound_ReturnsNull()
    {
        // Arrange
        _mockAssetClient
            .Setup(c => c.GetBundleAsync(It.IsAny<GetBundleRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BeyondImmersion.Bannou.Core.ApiException(
                "Not found",
                404,
                null,
                new Dictionary<string, IEnumerable<string>>(),
                null));

        // Act
        var result = await _source.GetBundleDownloadInfoAsync("non-existent");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetAssetDownloadInfoAsync Tests

    /// <summary>
    /// Verifies that GetAssetDownloadInfoAsync validates null input.
    /// </summary>
    [Fact]
    public async Task GetAssetDownloadInfoAsync_NullAssetId_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _source.GetAssetDownloadInfoAsync(null!));
    }

    /// <summary>
    /// Verifies that GetAssetDownloadInfoAsync validates empty input.
    /// </summary>
    [Fact]
    public async Task GetAssetDownloadInfoAsync_EmptyAssetId_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _source.GetAssetDownloadInfoAsync(""));
    }

    /// <summary>
    /// Verifies that GetAssetDownloadInfoAsync returns asset info.
    /// </summary>
    [Fact]
    public async Task GetAssetDownloadInfoAsync_ExistingAsset_ReturnsInfo()
    {
        // Arrange
        var response = new AssetWithDownloadUrl
        {
            AssetId = "asset-1",
            VersionId = "v1",
            DownloadUrl = new Uri("https://example.com/asset.bin"),
            Size = 1024,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            ContentType = "application/octet-stream",
            ContentHash = "abc123"
        };

        _mockAssetClient
            .Setup(c => c.GetAssetAsync(It.IsAny<GetAssetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _source.GetAssetDownloadInfoAsync("asset-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("asset-1", result.AssetId);
        Assert.Equal(1024, result.SizeBytes);
        Assert.Equal("application/octet-stream", result.ContentType);
    }

    /// <summary>
    /// Verifies that GetAssetDownloadInfoAsync returns null when no download URL.
    /// </summary>
    [Fact]
    public async Task GetAssetDownloadInfoAsync_NoDownloadUrl_ReturnsNull()
    {
        // Arrange
        var response = new AssetWithDownloadUrl
        {
            AssetId = "asset-1",
            VersionId = "v1",
            DownloadUrl = null, // No download URL
            Size = 1024,
            ContentType = "application/octet-stream",
            ContentHash = "abc123"
        };

        _mockAssetClient
            .Setup(c => c.GetAssetAsync(It.IsAny<GetAssetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _source.GetAssetDownloadInfoAsync("asset-1");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that GetAssetDownloadInfoAsync returns null for 404.
    /// </summary>
    [Fact]
    public async Task GetAssetDownloadInfoAsync_NotFound_ReturnsNull()
    {
        // Arrange
        _mockAssetClient
            .Setup(c => c.GetAssetAsync(It.IsAny<GetAssetRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BeyondImmersion.Bannou.Core.ApiException(
                "Not found",
                404,
                null,
                new Dictionary<string, IEnumerable<string>>(),
                null));

        // Act
        var result = await _source.GetAssetDownloadInfoAsync("non-existent");

        // Assert
        Assert.Null(result);
    }

    #endregion
}
