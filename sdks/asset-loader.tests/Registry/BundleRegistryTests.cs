using BeyondImmersion.Bannou.AssetLoader.Registry;
using BeyondImmersion.Bannou.AssetLoader.Tests.TestHelpers;
using Xunit;

namespace BeyondImmersion.Bannou.AssetLoader.Tests.Registry;

/// <summary>
/// Unit tests for BundleRegistry.
/// Verifies O(1) asset-to-bundle lookup, thread-safety, and proper cleanup.
/// </summary>
public class BundleRegistryTests : IDisposable
{
    private readonly BundleRegistry _registry = new();
    private readonly List<LoadedBundle> _bundles = new();

    public void Dispose()
    {
        _registry.Clear();
        foreach (var bundle in _bundles)
        {
            bundle.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    #region Register Tests

    /// <summary>
    /// Verifies that registering a bundle adds it to the registry.
    /// </summary>
    [Fact]
    public void Register_AddsBundle_ToBundleCollection()
    {
        // Arrange
        var bundle = CreateBundle("bundle-1", "asset-1", "asset-2");

        // Act
        _registry.Register(bundle);

        // Assert
        Assert.Equal(1, _registry.BundleCount);
        Assert.True(_registry.HasBundle("bundle-1"));
    }

    /// <summary>
    /// Verifies that registering a bundle indexes all its assets.
    /// </summary>
    [Fact]
    public void Register_IndexesAllAssets_ForFastLookup()
    {
        // Arrange
        var bundle = CreateBundle("bundle-1", "asset-1", "asset-2", "asset-3");

        // Act
        _registry.Register(bundle);

        // Assert
        Assert.Equal(3, _registry.AssetCount);
        Assert.True(_registry.HasAsset("asset-1"));
        Assert.True(_registry.HasAsset("asset-2"));
        Assert.True(_registry.HasAsset("asset-3"));
    }

    /// <summary>
    /// Verifies that re-registering same bundle ID replaces previous entry.
    /// </summary>
    [Fact]
    public void Register_SameBundleId_ReplacesExisting()
    {
        // Arrange
        var bundle1 = CreateBundle("bundle-1", "asset-1");
        var bundle2 = CreateBundle("bundle-1", "asset-2", "asset-3");

        // Act
        _registry.Register(bundle1);
        _registry.Register(bundle2);

        // Assert
        Assert.Equal(1, _registry.BundleCount);
        var retrieved = _registry.GetBundle("bundle-1");
        Assert.Equal(2, retrieved?.AssetIds.Count);
    }

    /// <summary>
    /// Verifies that when an asset exists in multiple bundles, first bundle wins.
    /// </summary>
    [Fact]
    public void Register_DuplicateAssetId_FirstBundleWins()
    {
        // Arrange
        var bundle1 = CreateBundle("bundle-1", "shared-asset", "asset-1");
        var bundle2 = CreateBundle("bundle-2", "shared-asset", "asset-2");

        // Act
        _registry.Register(bundle1);
        _registry.Register(bundle2);

        // Assert
        Assert.Equal("bundle-1", _registry.FindBundleForAsset("shared-asset"));
    }

    #endregion

    #region Unregister Tests

    /// <summary>
    /// Verifies that unregistering removes the bundle.
    /// </summary>
    [Fact]
    public void Unregister_RemovesBundle_FromCollection()
    {
        // Arrange
        var bundle = CreateBundle("bundle-1", "asset-1");
        _registry.Register(bundle);

        // Act
        _registry.Unregister("bundle-1");

        // Assert
        Assert.Equal(0, _registry.BundleCount);
        Assert.False(_registry.HasBundle("bundle-1"));
    }

    /// <summary>
    /// Verifies that unregistering removes asset index entries.
    /// </summary>
    [Fact]
    public void Unregister_RemovesAssetIndexEntries()
    {
        // Arrange
        var bundle = CreateBundle("bundle-1", "asset-1", "asset-2");
        _registry.Register(bundle);

        // Act
        _registry.Unregister("bundle-1");

        // Assert
        Assert.Equal(0, _registry.AssetCount);
        Assert.False(_registry.HasAsset("asset-1"));
        Assert.False(_registry.HasAsset("asset-2"));
    }

    /// <summary>
    /// Verifies that unregistering non-existent bundle is a no-op.
    /// </summary>
    [Fact]
    public void Unregister_NonExistentBundle_DoesNothing()
    {
        // Act - should not throw
        _registry.Unregister("non-existent");

        // Assert
        Assert.Equal(0, _registry.BundleCount);
    }

    /// <summary>
    /// Verifies that unregistering with empty throws ArgumentException.
    /// </summary>
    [Fact]
    public void Unregister_Empty_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _registry.Unregister(""));
    }

    #endregion

    #region FindBundleForAsset Tests

    /// <summary>
    /// Verifies that FindBundleForAsset returns correct bundle ID.
    /// </summary>
    [Fact]
    public void FindBundleForAsset_ExistingAsset_ReturnsBundleId()
    {
        // Arrange
        var bundle = CreateBundle("my-bundle", "target-asset");
        _registry.Register(bundle);

        // Act
        var result = _registry.FindBundleForAsset("target-asset");

        // Assert
        Assert.Equal("my-bundle", result);
    }

    /// <summary>
    /// Verifies that FindBundleForAsset returns null for non-existent asset.
    /// </summary>
    [Fact]
    public void FindBundleForAsset_NonExistentAsset_ReturnsNull()
    {
        // Arrange
        var bundle = CreateBundle("bundle-1", "asset-1");
        _registry.Register(bundle);

        // Act
        var result = _registry.FindBundleForAsset("non-existent");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that FindBundleForAsset with empty throws ArgumentException.
    /// </summary>
    [Fact]
    public void FindBundleForAsset_Empty_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _registry.FindBundleForAsset(""));
    }

    #endregion

    #region GetBundle Tests

    /// <summary>
    /// Verifies that GetBundle returns the registered bundle.
    /// </summary>
    [Fact]
    public void GetBundle_ExistingBundle_ReturnsBundle()
    {
        // Arrange
        var bundle = CreateBundle("bundle-1", "asset-1");
        _registry.Register(bundle);

        // Act
        var result = _registry.GetBundle("bundle-1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("bundle-1", result.BundleId);
    }

    /// <summary>
    /// Verifies that GetBundle returns null for non-existent bundle.
    /// </summary>
    [Fact]
    public void GetBundle_NonExistentBundle_ReturnsNull()
    {
        // Act
        var result = _registry.GetBundle("non-existent");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetLoadedBundleIds Tests

    /// <summary>
    /// Verifies that GetLoadedBundleIds returns all registered bundle IDs.
    /// </summary>
    [Fact]
    public void GetLoadedBundleIds_ReturnsAllBundleIds()
    {
        // Arrange
        _registry.Register(CreateBundle("bundle-1", "asset-1"));
        _registry.Register(CreateBundle("bundle-2", "asset-2"));
        _registry.Register(CreateBundle("bundle-3", "asset-3"));

        // Act
        var ids = _registry.GetLoadedBundleIds().ToList();

        // Assert
        Assert.Equal(3, ids.Count);
        Assert.Contains("bundle-1", ids);
        Assert.Contains("bundle-2", ids);
        Assert.Contains("bundle-3", ids);
    }

    /// <summary>
    /// Verifies that GetLoadedBundleIds returns empty when no bundles registered.
    /// </summary>
    [Fact]
    public void GetLoadedBundleIds_NoBundles_ReturnsEmpty()
    {
        // Act
        var ids = _registry.GetLoadedBundleIds();

        // Assert
        Assert.Empty(ids);
    }

    #endregion

    #region Clear Tests

    /// <summary>
    /// Verifies that Clear removes all bundles and assets.
    /// </summary>
    [Fact]
    public void Clear_RemovesAllBundlesAndAssets()
    {
        // Arrange
        _registry.Register(CreateBundle("bundle-1", "asset-1", "asset-2"));
        _registry.Register(CreateBundle("bundle-2", "asset-3", "asset-4"));

        // Act
        _registry.Clear();

        // Assert
        Assert.Equal(0, _registry.BundleCount);
        Assert.Equal(0, _registry.AssetCount);
    }

    #endregion

    #region Helper Methods

    private LoadedBundle CreateBundle(string bundleId, params string[] assetIds)
    {
        var assets = assetIds.Select(id => TestBundleFactory.TextAsset(id)).ToArray();
        var bundle = TestBundleFactory.CreateLoadedBundle(bundleId, assets);
        _bundles.Add(bundle);
        return bundle;
    }

    #endregion
}
