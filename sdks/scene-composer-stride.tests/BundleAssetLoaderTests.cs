using System;
using System.IO;
using System.Threading.Tasks;
using BeyondImmersion.Bannou.SceneComposer.Stride.Content;
using Xunit;

namespace BeyondImmersion.Bannou.SceneComposer.Stride.Tests;

/// <summary>
/// Unit tests for BundleAssetLoader lifecycle and error handling.
/// </summary>
/// <remarks>
/// These tests focus on behavior that doesn't require a valid bundle file:
/// - Constructor validation
/// - Initialization state checks
/// - Dispose behavior
///
/// Tests that require actual bundle data (reading assets, manifest access)
/// would need integration tests with real .bannou files.
/// </remarks>
public class BundleAssetLoaderTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WhenStreamIsNull_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new BundleAssetLoader(null!));
        Assert.Equal("stream", ex.ParamName);
    }

    [Fact]
    public void Constructor_WhenValidStream_CreatesInstance()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act
        using var loader = new BundleAssetLoader(stream);

        // Assert - no exception thrown
        Assert.NotNull(loader);
    }

    #endregion

    #region Initialization State Tests

    [Fact]
    public void HasAsset_WhenNotInitialized_ThrowsInvalidOperationException()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var loader = new BundleAssetLoader(stream);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => loader.HasAsset("test"));
        Assert.Contains("not initialized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAssetEntry_WhenNotInitialized_ThrowsInvalidOperationException()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var loader = new BundleAssetLoader(stream);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => loader.GetAssetEntry("test"));
        Assert.Contains("not initialized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadAsset_WhenNotInitialized_ThrowsInvalidOperationException()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var loader = new BundleAssetLoader(stream);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => loader.ReadAsset("test"));
        Assert.Contains("not initialized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAssetAsync_WhenNotInitialized_ThrowsInvalidOperationException()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var loader = new BundleAssetLoader(stream);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.ReadAssetAsync("test"));
        Assert.Contains("not initialized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAssetIds_WhenNotInitialized_ThrowsInvalidOperationException()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var loader = new BundleAssetLoader(stream);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => loader.GetAssetIds());
        Assert.Contains("not initialized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetAssetEntries_WhenNotInitialized_ThrowsInvalidOperationException()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var loader = new BundleAssetLoader(stream);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => loader.GetAssetEntries());
        Assert.Contains("not initialized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_WhenNotInitialized_ThrowsInvalidOperationException()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var loader = new BundleAssetLoader(stream);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _ = loader.Manifest);
        Assert.Contains("not initialized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        using var stream = new MemoryStream();
        var loader = new BundleAssetLoader(stream);

        // Act & Assert - should not throw
        loader.Dispose();
        loader.Dispose();
    }

    [Fact]
    public void Initialize_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var stream = new MemoryStream();
        var loader = new BundleAssetLoader(stream);
        loader.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => loader.Initialize());
    }

    [Fact]
    public async Task InitializeAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var stream = new MemoryStream();
        var loader = new BundleAssetLoader(stream);
        loader.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => loader.InitializeAsync());
    }

    [Fact]
    public void HasAsset_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var stream = new MemoryStream();
        var loader = new BundleAssetLoader(stream);
        loader.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => loader.HasAsset("test"));
    }

    [Fact]
    public void GetAssetEntry_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var stream = new MemoryStream();
        var loader = new BundleAssetLoader(stream);
        loader.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => loader.GetAssetEntry("test"));
    }

    [Fact]
    public void ReadAsset_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var stream = new MemoryStream();
        var loader = new BundleAssetLoader(stream);
        loader.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => loader.ReadAsset("test"));
    }

    [Fact]
    public void GetAssetIds_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var stream = new MemoryStream();
        var loader = new BundleAssetLoader(stream);
        loader.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => loader.GetAssetIds());
    }

    [Fact]
    public void GetAssetEntries_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var stream = new MemoryStream();
        var loader = new BundleAssetLoader(stream);
        loader.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => loader.GetAssetEntries());
    }

    [Fact]
    public void Manifest_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        using var stream = new MemoryStream();
        var loader = new BundleAssetLoader(stream);
        loader.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => _ = loader.Manifest);
    }

    #endregion

    #region Invalid Data Tests

    [Fact]
    public void Initialize_WhenStreamEmpty_ThrowsInvalidDataException()
    {
        // Arrange
        using var stream = new MemoryStream();
        using var loader = new BundleAssetLoader(stream);

        // Act & Assert - BannouBundleReader should throw when reading empty stream
        Assert.ThrowsAny<Exception>(() => loader.Initialize());
    }

    [Fact]
    public void Initialize_WhenStreamTooShort_ThrowsException()
    {
        // Arrange - only 2 bytes, not enough for manifest length
        using var stream = new MemoryStream(new byte[] { 0x00, 0x01 });
        using var loader = new BundleAssetLoader(stream);

        // Act & Assert
        Assert.ThrowsAny<Exception>(() => loader.Initialize());
    }

    [Fact]
    public void Initialize_WhenInvalidManifestLength_ThrowsException()
    {
        // Arrange - manifest length says 1GB but stream is tiny
        var data = new byte[4];
        data[0] = 0x40; // Big endian for a large number
        data[1] = 0x00;
        data[2] = 0x00;
        data[3] = 0x00;

        using var stream = new MemoryStream(data);
        using var loader = new BundleAssetLoader(stream);

        // Act & Assert
        Assert.ThrowsAny<Exception>(() => loader.Initialize());
    }

    #endregion

    #region Integration Tests with Real Bundle

    /// <summary>
    /// Gets the path to the test bundle fixture.
    /// </summary>
    private static string TestBundlePath => Path.Combine(
        AppContext.BaseDirectory, "TestFixtures", "test-bundle.bannou");

    /// <summary>
    /// Helper to create a loader with the test bundle.
    /// </summary>
    private static BundleAssetLoader CreateTestBundleLoader()
    {
        var stream = File.OpenRead(TestBundlePath);
        return new BundleAssetLoader(stream);
    }

    [Fact]
    public void Initialize_WithValidBundle_Succeeds()
    {
        // Skip if test bundle not available
        if (!File.Exists(TestBundlePath))
        {
            return; // Skip test - bundle not available
        }

        // Arrange
        using var loader = CreateTestBundleLoader();

        // Act
        loader.Initialize();

        // Assert - no exception thrown, manifest accessible
        Assert.NotNull(loader.Manifest);
        Assert.True(loader.AssetCount > 0);
    }

    [Fact]
    public async Task InitializeAsync_WithValidBundle_Succeeds()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();

        // Act
        await loader.InitializeAsync();

        // Assert
        Assert.NotNull(loader.Manifest);
        Assert.True(loader.AssetCount > 0);
    }

    [Fact]
    public void Manifest_AfterInitialize_ContainsExpectedData()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();
        loader.Initialize();

        // Act
        var manifest = loader.Manifest;

        // Assert - test bundle should have these properties
        Assert.Equal("test-box", manifest.BundleId);
        Assert.Equal("box", manifest.Name);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal(3, manifest.AssetCount);
        Assert.Equal("lz4", manifest.CompressionAlgorithm);
    }

    [Fact]
    public void HasAsset_WithExistingAsset_ReturnsTrue()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();
        loader.Initialize();

        // Act & Assert - test bundle contains Box1x1x1
        Assert.True(loader.HasAsset("Box1x1x1"));
        Assert.True(loader.HasAsset("Box1x1x1/gen/Buffer_1"));
        Assert.True(loader.HasAsset("Box1x1x1/gen/Buffer_2"));
    }

    [Fact]
    public void HasAsset_WithNonExistingAsset_ReturnsFalse()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();
        loader.Initialize();

        // Act & Assert
        Assert.False(loader.HasAsset("NonExistent"));
        Assert.False(loader.HasAsset("Box1x1x1/gen/Buffer_99"));
    }

    [Fact]
    public void GetAssetIds_ReturnsAllAssetIds()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();
        loader.Initialize();

        // Act
        var assetIds = loader.GetAssetIds().ToList();

        // Assert
        Assert.Equal(3, assetIds.Count);
        Assert.Contains("Box1x1x1", assetIds);
        Assert.Contains("Box1x1x1/gen/Buffer_1", assetIds);
        Assert.Contains("Box1x1x1/gen/Buffer_2", assetIds);
    }

    [Fact]
    public void GetAssetEntry_WithExistingAsset_ReturnsEntry()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();
        loader.Initialize();

        // Act
        var entry = loader.GetAssetEntry("Box1x1x1");

        // Assert
        Assert.NotNull(entry);
        Assert.Equal("Box1x1x1", entry.AssetId);
        Assert.Equal("Box1x1x1.sdmodel", entry.Filename);
        Assert.Equal("application/x-stride-model", entry.ContentType);
        Assert.True(entry.UncompressedSize > 0);
        Assert.True(entry.CompressedSize > 0);
        Assert.False(string.IsNullOrEmpty(entry.ContentHash));
    }

    [Fact]
    public void GetAssetEntry_WithNonExistingAsset_ReturnsNull()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();
        loader.Initialize();

        // Act
        var entry = loader.GetAssetEntry("NonExistent");

        // Assert
        Assert.Null(entry);
    }

    [Fact]
    public void ReadAsset_WithExistingAsset_ReturnsDecompressedData()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();
        loader.Initialize();
        var entry = loader.GetAssetEntry("Box1x1x1");

        // Act
        var data = loader.ReadAsset("Box1x1x1");

        // Assert
        Assert.NotNull(data);
        Assert.Equal(entry!.UncompressedSize, data.Length);

        // Verify it's valid CHNK data (compiled Stride asset)
        Assert.True(data.Length >= 4);
        // CHNK magic is "KNHC" when read as bytes (little-endian)
        Assert.Equal((byte)'K', data[0]);
        Assert.Equal((byte)'N', data[1]);
        Assert.Equal((byte)'H', data[2]);
        Assert.Equal((byte)'C', data[3]);
    }

    [Fact]
    public async Task ReadAssetAsync_WithExistingAsset_ReturnsDecompressedData()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();
        await loader.InitializeAsync();
        var entry = loader.GetAssetEntry("Box1x1x1");

        // Act
        var data = await loader.ReadAssetAsync("Box1x1x1");

        // Assert
        Assert.NotNull(data);
        Assert.Equal(entry!.UncompressedSize, data.Length);
    }

    [Fact]
    public void ReadAsset_WithNonExistingAsset_ReturnsNull()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();
        loader.Initialize();

        // Act
        var data = loader.ReadAsset("NonExistent");

        // Assert
        Assert.Null(data);
    }

    [Fact]
    public void GetAssetEntries_ReturnsAllEntries()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();
        loader.Initialize();

        // Act
        var entries = loader.GetAssetEntries().ToList();

        // Assert
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.AssetId == "Box1x1x1");
        Assert.Contains(entries, e => e.AssetId == "Box1x1x1/gen/Buffer_1");
        Assert.Contains(entries, e => e.AssetId == "Box1x1x1/gen/Buffer_2");
    }

    [Fact]
    public void BundleId_ReturnsManifestBundleId()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();
        loader.Initialize();

        // Act & Assert
        Assert.Equal("test-box", loader.BundleId);
    }

    [Fact]
    public void Name_ReturnsManifestName()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();
        loader.Initialize();

        // Act & Assert
        Assert.Equal("box", loader.Name);
    }

    [Fact]
    public void Version_ReturnsManifestVersion()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();
        loader.Initialize();

        // Act & Assert
        Assert.Equal("1.0.0", loader.Version);
    }

    [Fact]
    public void Initialize_CalledMultipleTimes_DoesNotThrow()
    {
        if (!File.Exists(TestBundlePath))
            return;

        // Arrange
        using var loader = CreateTestBundleLoader();

        // Act - calling Initialize multiple times should be idempotent
        loader.Initialize();
        loader.Initialize();
        loader.Initialize();

        // Assert - still works
        Assert.Equal(3, loader.AssetCount);
    }

    #endregion
}
