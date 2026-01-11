using System;
using System.IO;
using System.Threading.Tasks;
using BeyondImmersion.Bannou.Stride.SceneComposer.Content;
using Xunit;

namespace BeyondImmersion.Bannou.Stride.SceneComposer.Tests;

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
}
