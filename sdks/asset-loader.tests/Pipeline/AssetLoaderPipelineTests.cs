using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.Bannou.AssetLoader.Models;
using BeyondImmersion.Bannou.AssetLoader.Tests.TestHelpers;
using Moq;
using Xunit;

namespace BeyondImmersion.Bannou.AssetLoader.Tests.Pipeline;

/// <summary>
/// Tests for AssetLoader orchestration.
/// Tests the full Resolve → Download → Cache → Load workflow.
/// </summary>
public class AssetLoaderPipelineTests : IDisposable
{
    private readonly string _tempDir;

    public AssetLoaderPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"asset-loader-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region EnsureAssetsAvailableAsync Tests

    [Fact]
    public async Task EnsureAssetsAvailableAsync_AllAssetsAlreadyLoaded_ReturnsImmediately()
    {
        // Arrange
        var bundleId = "test/bundle";
        var source = CreateMockSource();

        // No cache = file:// URLs load directly
        await using var loader = new AssetLoader(source.Object, cache: null);

        // Pre-load a bundle
        var bundleStream = TestBundleFactory.CreateBundleStream(bundleId,
            TestBundleFactory.TextAsset("asset1"),
            TestBundleFactory.TextAsset("asset2"));
        var bundlePath = await SaveBundleToFile(bundleStream, "preload.bannou");

        await loader.LoadBundleAsync(bundleId, new Uri($"file://{bundlePath}"));

        // Act
        var result = await loader.EnsureAssetsAvailableAsync(new[] { "asset1", "asset2" });

        // Assert
        Assert.Empty(result.DownloadedBundleIds);
        Assert.Empty(result.UnresolvedAssetIds);
        Assert.Equal(2, result.RequestedAssetIds.Count);

        // Source.ResolveBundlesAsync should NOT be called
        source.Verify(s => s.ResolveBundlesAsync(
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<IReadOnlyList<string>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsureAssetsAvailableAsync_MissingAssets_ResolvesAndDownloads()
    {
        // Arrange
        var source = CreateMockSource();

        var bundleStream = TestBundleFactory.CreateBundleStream("resolved/bundle",
            TestBundleFactory.TextAsset("asset1"),
            TestBundleFactory.TextAsset("asset2"));
        var bundlePath = await SaveBundleToFile(bundleStream, "resolved.bannou");

        source.Setup(s => s.ResolveBundlesAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BundleResolutionResult
            {
                Bundles = new List<ResolvedBundleInfo>
                {
                    new()
                    {
                        BundleId = "resolved/bundle",
                        DownloadUrl = new Uri($"file://{bundlePath}"),
                        SizeBytes = 1024,
                        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                        IncludedAssetIds = new[] { "asset1", "asset2" }
                    }
                },
                StandaloneAssets = Array.Empty<ResolvedAssetInfo>()
            });

        await using var loader = new AssetLoader(source.Object, cache: null);

        // Act
        var result = await loader.EnsureAssetsAvailableAsync(new[] { "asset1", "asset2" });

        // Assert
        Assert.Single(result.DownloadedBundleIds);
        Assert.Contains("resolved/bundle", result.DownloadedBundleIds);
        Assert.Empty(result.UnresolvedAssetIds);
    }

    [Fact]
    public async Task EnsureAssetsAvailableAsync_UnresolvedAssets_ReportsUnresolved()
    {
        // Arrange
        var source = CreateMockSource();

        source.Setup(s => s.ResolveBundlesAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BundleResolutionResult
            {
                Bundles = Array.Empty<ResolvedBundleInfo>(),
                StandaloneAssets = Array.Empty<ResolvedAssetInfo>(),
                UnresolvedAssetIds = new[] { "missing1", "missing2" }
            });

        await using var loader = new AssetLoader(source.Object, cache: null);

        // Act
        var result = await loader.EnsureAssetsAvailableAsync(new[] { "missing1", "missing2" });

        // Assert
        Assert.Empty(result.DownloadedBundleIds);
        Assert.Equal(2, result.UnresolvedAssetIds.Count);
        Assert.Contains("missing1", result.UnresolvedAssetIds);
        Assert.Contains("missing2", result.UnresolvedAssetIds);
    }

    #endregion

    #region LoadBundleAsync Tests

    [Fact]
    public async Task LoadBundleAsync_FromLocalFile_LoadsSuccessfully()
    {
        // Arrange
        var bundleId = "test/local";
        var bundleStream = TestBundleFactory.CreateBundleStream(bundleId,
            TestBundleFactory.TextAsset("asset1", "content1"));
        var bundlePath = await SaveBundleToFile(bundleStream, "local.bannou");

        var source = CreateMockSource();
        // No cache = file:// URLs load directly without going through download
        await using var loader = new AssetLoader(source.Object, cache: null);

        // Act
        var result = await loader.LoadBundleAsync(bundleId, new Uri($"file://{bundlePath}"));

        // Assert
        Assert.Equal(BundleLoadStatus.Success, result.Status);
        Assert.Equal(bundleId, result.BundleId);
        Assert.Equal(1, result.AssetCount);
        Assert.True(loader.Registry.HasBundle(bundleId));
        Assert.True(loader.Registry.HasAsset("asset1"));
    }

    [Fact]
    public async Task LoadBundleAsync_AlreadyLoaded_ReturnsAlreadyLoaded()
    {
        // Arrange
        var bundleId = "test/already";
        var bundleStream = TestBundleFactory.CreateBundleStream(bundleId,
            TestBundleFactory.TextAsset("asset1"));
        var bundlePath = await SaveBundleToFile(bundleStream, "already.bannou");

        var source = CreateMockSource();
        await using var loader = new AssetLoader(source.Object, cache: null);

        // First load
        await loader.LoadBundleAsync(bundleId, new Uri($"file://{bundlePath}"));

        // Act - second load
        var result = await loader.LoadBundleAsync(bundleId, new Uri($"file://{bundlePath}"));

        // Assert
        Assert.Equal(BundleLoadStatus.AlreadyLoaded, result.Status);
    }

    [Fact]
    public async Task LoadBundleAsync_FileNotFound_ReturnsFailed()
    {
        // Arrange
        var source = CreateMockSource();
        await using var loader = new AssetLoader(source.Object, cache: null);

        // Act
        var result = await loader.LoadBundleAsync("missing/bundle",
            new Uri($"file://{_tempDir}/nonexistent.bannou"));

        // Assert
        Assert.Equal(BundleLoadStatus.Failed, result.Status);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task LoadBundleAsync_FromCache_UsesCachedData()
    {
        // Arrange
        var bundleId = "test/cached";
        var bundleStream = TestBundleFactory.CreateBundleStream(bundleId,
            TestBundleFactory.TextAsset("asset1"));

        var source = CreateMockSource();
        var cache = new Mock<IAssetCache>();

        // Set up cache to return the bundle
        cache.Setup(c => c.GetBundleStreamAsync(bundleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                bundleStream.Position = 0;
                var copy = new MemoryStream();
                bundleStream.CopyTo(copy);
                copy.Position = 0;
                return copy;
            });
        cache.Setup(c => c.GetStats()).Returns(new CacheStats
        {
            TotalBytes = 0,
            BundleCount = 0,
            MaxBytes = 100 * 1024 * 1024,
            HitCount = 0,
            MissCount = 0
        });

        var options = new AssetLoaderOptions { PreferCache = true };
        await using var loader = new AssetLoader(source.Object, cache.Object, options);

        // Act
        var result = await loader.LoadBundleAsync(bundleId, new Uri("http://example.com/bundle.bannou"));

        // Assert
        Assert.Equal(BundleLoadStatus.Success, result.Status);
        Assert.True(result.FromCache);
        cache.Verify(c => c.GetBundleStreamAsync(bundleId, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetAssetBytesAsync Tests

    [Fact]
    public async Task GetAssetBytesAsync_ExistingAsset_ReturnsBytes()
    {
        // Arrange
        var bundleId = "test/bytes";
        var expectedContent = "test content for asset";
        var bundleStream = TestBundleFactory.CreateBundleStream(bundleId,
            TestBundleFactory.TextAsset("asset1", expectedContent));
        var bundlePath = await SaveBundleToFile(bundleStream, "bytes.bannou");

        var source = CreateMockSource();
        // Disable validation to avoid hash mismatch (TestBundleFactory doesn't set correct hashes)
        var options = new AssetLoaderOptions { ValidateBundles = false };
        await using var loader = new AssetLoader(source.Object, cache: null, options);
        await loader.LoadBundleAsync(bundleId, new Uri($"file://{bundlePath}"));

        // Act
        var bytes = await loader.GetAssetBytesAsync("asset1");

        // Assert
        Assert.NotNull(bytes);
        var content = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Equal(expectedContent, content);
    }

    [Fact]
    public async Task GetAssetBytesAsync_NonexistentAsset_ReturnsNull()
    {
        // Arrange
        var bundleId = "test/no-asset";
        var bundleStream = TestBundleFactory.CreateBundleStream(bundleId,
            TestBundleFactory.TextAsset("asset1"));
        var bundlePath = await SaveBundleToFile(bundleStream, "no-asset.bannou");

        var source = CreateMockSource();
        var options = new AssetLoaderOptions { ValidateBundles = false };
        await using var loader = new AssetLoader(source.Object, cache: null, options);
        await loader.LoadBundleAsync(bundleId, new Uri($"file://{bundlePath}"));

        // Act
        var bytes = await loader.GetAssetBytesAsync("nonexistent");

        // Assert
        Assert.Null(bytes);
    }

    #endregion

    #region GetAssetEntry Tests

    [Fact]
    public async Task GetAssetEntry_ExistingAsset_ReturnsEntry()
    {
        // Arrange
        var bundleId = "test/entry";
        var bundleStream = TestBundleFactory.CreateBundleStream(bundleId,
            TestBundleFactory.TextAsset("asset1"));
        var bundlePath = await SaveBundleToFile(bundleStream, "entry.bannou");

        var source = CreateMockSource();
        var options = new AssetLoaderOptions { ValidateBundles = false };
        await using var loader = new AssetLoader(source.Object, cache: null, options);
        await loader.LoadBundleAsync(bundleId, new Uri($"file://{bundlePath}"));

        // Act
        var entry = loader.GetAssetEntry("asset1");

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("asset1", entry.AssetId);
        Assert.Equal("application/octet-stream", entry.ContentType);
    }

    [Fact]
    public async Task GetAssetEntry_NonexistentAsset_ReturnsNull()
    {
        // Arrange
        var source = CreateMockSource();
        await using var loader = new AssetLoader(source.Object, cache: null);

        // Act
        var entry = loader.GetAssetEntry("nonexistent");

        // Assert
        Assert.Null(entry);
    }

    #endregion

    #region UnloadBundle Tests

    [Fact]
    public async Task UnloadBundle_LoadedBundle_RemovesFromRegistry()
    {
        // Arrange
        var bundleId = "test/unload";
        var bundleStream = TestBundleFactory.CreateBundleStream(bundleId,
            TestBundleFactory.TextAsset("asset1"));
        var bundlePath = await SaveBundleToFile(bundleStream, "unload.bannou");

        var source = CreateMockSource();
        var options = new AssetLoaderOptions { ValidateBundles = false };
        await using var loader = new AssetLoader(source.Object, cache: null, options);
        await loader.LoadBundleAsync(bundleId, new Uri($"file://{bundlePath}"));

        Assert.True(loader.Registry.HasBundle(bundleId));
        Assert.True(loader.Registry.HasAsset("asset1"));

        // Act
        loader.UnloadBundle(bundleId);

        // Assert
        Assert.False(loader.Registry.HasBundle(bundleId));
        Assert.False(loader.Registry.HasAsset("asset1"));
    }

    [Fact]
    public async Task UnloadAllBundles_MultipleBundles_ClearsAll()
    {
        // Arrange
        var source = CreateMockSource();
        var options = new AssetLoaderOptions { ValidateBundles = false };
        await using var loader = new AssetLoader(source.Object, cache: null, options);

        // Load multiple bundles
        for (var i = 0; i < 3; i++)
        {
            var bundleId = $"test/bundle{i}";
            var bundleStream = TestBundleFactory.CreateBundleStream(bundleId,
                TestBundleFactory.TextAsset($"asset{i}"));
            var bundlePath = await SaveBundleToFile(bundleStream, $"bundle{i}.bannou");
            await loader.LoadBundleAsync(bundleId, new Uri($"file://{bundlePath}"));
        }

        Assert.Equal(3, loader.Registry.GetLoadedBundleIds().Count());

        // Act
        loader.UnloadAllBundles();

        // Assert
        Assert.Empty(loader.Registry.GetLoadedBundleIds());
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task LoadBundle_ConcurrentLoadsSameBundle_AllComplete()
    {
        // Arrange
        var bundleId = "test/concurrent";
        var bundleStream = TestBundleFactory.CreateBundleStream(bundleId,
            TestBundleFactory.TextAsset("asset1"));
        var bundlePath = await SaveBundleToFile(bundleStream, "concurrent.bannou");

        var source = CreateMockSource();
        var options = new AssetLoaderOptions { ValidateBundles = false };
        await using var loader = new AssetLoader(source.Object, cache: null, options);

        var bundleUri = new Uri($"file://{bundlePath}");

        // Act - load concurrently
        var tasks = new[]
        {
            loader.LoadBundleAsync(bundleId, bundleUri),
            loader.LoadBundleAsync(bundleId, bundleUri),
            loader.LoadBundleAsync(bundleId, bundleUri)
        };

        var results = await Task.WhenAll(tasks);

        // Assert - all should complete without failure, bundle should be registered
        Assert.All(results, r =>
            Assert.True(r.Status == BundleLoadStatus.Success || r.Status == BundleLoadStatus.AlreadyLoaded));
        Assert.True(loader.Registry.HasBundle(bundleId));
        Assert.True(loader.Registry.HasAsset("asset1"));
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task FullWorkflow_ResolveDownloadLoad_Success()
    {
        // Arrange - create a bundle and set up source to resolve it
        var bundleId = "workflow/test";
        var bundleStream = TestBundleFactory.CreateBundleStream(bundleId,
            TestBundleFactory.TextAsset("asset1", "content1"),
            TestBundleFactory.TextAsset("asset2", "content2"));
        var bundlePath = await SaveBundleToFile(bundleStream, "workflow.bannou");

        var source = CreateMockSource();
        source.Setup(s => s.ResolveBundlesAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BundleResolutionResult
            {
                Bundles = new List<ResolvedBundleInfo>
                {
                    new()
                    {
                        BundleId = bundleId,
                        DownloadUrl = new Uri($"file://{bundlePath}"),
                        SizeBytes = 1024,
                        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                        IncludedAssetIds = new[] { "asset1", "asset2" }
                    }
                },
                StandaloneAssets = Array.Empty<ResolvedAssetInfo>()
            });

        var options = new AssetLoaderOptions { ValidateBundles = false };
        await using var loader = new AssetLoader(source.Object, cache: null, options);

        // Act - full workflow: ensure assets available, then read them
        var availResult = await loader.EnsureAssetsAvailableAsync(new[] { "asset1", "asset2" });

        // Assert - resolve and download succeeded
        Assert.Single(availResult.DownloadedBundleIds);
        Assert.Contains(bundleId, availResult.DownloadedBundleIds);
        Assert.Empty(availResult.UnresolvedAssetIds);

        // Verify assets are accessible
        var bytes1 = await loader.GetAssetBytesAsync("asset1");
        var bytes2 = await loader.GetAssetBytesAsync("asset2");

        Assert.NotNull(bytes1);
        Assert.NotNull(bytes2);
        Assert.Equal("content1", System.Text.Encoding.UTF8.GetString(bytes1));
        Assert.Equal("content2", System.Text.Encoding.UTF8.GetString(bytes2));
    }

    #endregion

    #region Helper Methods

    private static Mock<IAssetSource> CreateMockSource()
    {
        var source = new Mock<IAssetSource>();
        source.Setup(s => s.RequiresAuthentication).Returns(false);
        source.Setup(s => s.IsAvailable).Returns(true);
        return source;
    }

    private async Task<string> SaveBundleToFile(MemoryStream bundleStream, string filename)
    {
        var path = Path.Combine(_tempDir, filename);
        bundleStream.Position = 0;
        await using var fileStream = File.Create(path);
        await bundleStream.CopyToAsync(fileStream);
        bundleStream.Position = 0;
        return path;
    }

    #endregion
}
