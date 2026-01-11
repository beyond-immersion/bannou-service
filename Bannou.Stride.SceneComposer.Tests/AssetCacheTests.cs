using BeyondImmersion.Bannou.Stride.SceneComposer.Caching;
using Xunit;

namespace BeyondImmersion.Bannou.Stride.SceneComposer.Tests;

/// <summary>
/// Unit tests for the AssetCache LRU cache implementation.
/// </summary>
public class AssetCacheTests
{
    #region Basic Operations

    [Fact]
    public void Add_WhenAssetAdded_CanBeRetrieved()
    {
        // Arrange
        using var cache = new AssetCache();
        var asset = new TestAsset("test-data");

        // Act
        cache.Add("asset1", asset, 100);
        var found = cache.TryGet<TestAsset>("asset1", out var retrieved);

        // Assert
        Assert.True(found);
        Assert.Same(asset, retrieved);
    }

    [Fact]
    public void TryGet_WhenAssetNotInCache_ReturnsFalse()
    {
        // Arrange
        using var cache = new AssetCache();

        // Act
        var found = cache.TryGet<TestAsset>("nonexistent", out var retrieved);

        // Assert
        Assert.False(found);
        Assert.Null(retrieved);
    }

    [Fact]
    public void TryGet_WhenWrongType_ReturnsFalseAndNull()
    {
        // Arrange
        using var cache = new AssetCache();
        cache.Add("asset1", new TestAsset("data"), 100);

        // Act
        var found = cache.TryGet<string>("asset1", out var retrieved);

        // Assert
        Assert.False(found);
        Assert.Null(retrieved);
    }

    [Fact]
    public void Contains_WhenAssetExists_ReturnsTrue()
    {
        // Arrange
        using var cache = new AssetCache();
        cache.Add("asset1", new TestAsset("data"), 100);

        // Act & Assert
        Assert.True(cache.Contains("asset1"));
    }

    [Fact]
    public void Contains_WhenAssetNotExists_ReturnsFalse()
    {
        // Arrange
        using var cache = new AssetCache();

        // Act & Assert
        Assert.False(cache.Contains("nonexistent"));
    }

    #endregion

    #region Size Tracking

    [Fact]
    public void CurrentSize_WhenAssetsAdded_TracksCorrectly()
    {
        // Arrange
        using var cache = new AssetCache();

        // Act
        cache.Add("asset1", new TestAsset("a"), 100);
        cache.Add("asset2", new TestAsset("b"), 200);
        cache.Add("asset3", new TestAsset("c"), 300);

        // Assert
        Assert.Equal(600, cache.CurrentSize);
        Assert.Equal(3, cache.Count);
    }

    [Fact]
    public void CurrentSize_WhenAssetRemoved_DecreasesCorrectly()
    {
        // Arrange
        using var cache = new AssetCache();
        cache.Add("asset1", new TestAsset("a"), 100);
        cache.Add("asset2", new TestAsset("b"), 200);

        // Act
        cache.Remove("asset1");

        // Assert
        Assert.Equal(200, cache.CurrentSize);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void MaxSize_ReturnsConfiguredValue()
    {
        // Arrange
        using var cache = new AssetCache(maxSizeBytes: 1024);

        // Assert
        Assert.Equal(1024, cache.MaxSize);
    }

    #endregion

    #region LRU Eviction

    [Fact]
    public void Add_WhenExceedsMaxSize_EvictsLeastRecentlyUsed()
    {
        // Arrange
        using var cache = new AssetCache(maxSizeBytes: 250);
        cache.Add("asset1", new TestAsset("first"), 100);
        cache.Add("asset2", new TestAsset("second"), 100);

        // Act - This should evict asset1 to make room
        cache.Add("asset3", new TestAsset("third"), 100);

        // Assert
        Assert.False(cache.Contains("asset1")); // Evicted
        Assert.True(cache.Contains("asset2"));
        Assert.True(cache.Contains("asset3"));
        Assert.True(cache.CurrentSize <= cache.MaxSize);
    }

    [Fact]
    public void TryGet_UpdatesLRUOrder()
    {
        // Arrange
        using var cache = new AssetCache(maxSizeBytes: 250);
        cache.Add("asset1", new TestAsset("first"), 100);
        cache.Add("asset2", new TestAsset("second"), 100);

        // Access asset1 to make it recently used
        cache.TryGet<TestAsset>("asset1", out _);

        // Act - This should evict asset2 (now least recently used)
        cache.Add("asset3", new TestAsset("third"), 100);

        // Assert
        Assert.True(cache.Contains("asset1")); // Still there (was accessed)
        Assert.False(cache.Contains("asset2")); // Evicted
        Assert.True(cache.Contains("asset3"));
    }

    [Fact]
    public void Add_WhenSameKeyExists_UpdatesAsset()
    {
        // Arrange
        using var cache = new AssetCache();
        var original = new TestAsset("original");
        var updated = new TestAsset("updated");

        cache.Add("asset1", original, 100);

        // Act
        cache.Add("asset1", updated, 150);

        // Assert
        cache.TryGet<TestAsset>("asset1", out var retrieved);
        Assert.Same(updated, retrieved);
        Assert.Equal(150, cache.CurrentSize); // Size updated
        Assert.Equal(1, cache.Count); // Still only one entry
    }

    [Fact]
    public void Add_WhenMultipleEvictionsNeeded_EvictsEnough()
    {
        // Arrange
        using var cache = new AssetCache(maxSizeBytes: 500);
        cache.Add("asset1", new TestAsset("1"), 100);
        cache.Add("asset2", new TestAsset("2"), 100);
        cache.Add("asset3", new TestAsset("3"), 100);
        cache.Add("asset4", new TestAsset("4"), 100);

        // Act - Need to evict multiple assets
        cache.Add("large", new TestAsset("large"), 350);

        // Assert
        Assert.True(cache.Contains("large"));
        Assert.True(cache.CurrentSize <= cache.MaxSize);
    }

    #endregion

    #region Clear Operation

    [Fact]
    public void Clear_RemovesAllAssets()
    {
        // Arrange
        using var cache = new AssetCache();
        cache.Add("asset1", new TestAsset("a"), 100);
        cache.Add("asset2", new TestAsset("b"), 200);

        // Act
        cache.Clear();

        // Assert
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.CurrentSize);
        Assert.False(cache.Contains("asset1"));
        Assert.False(cache.Contains("asset2"));
    }

    [Fact]
    public void Clear_DisposesDisposableAssets()
    {
        // Arrange
        using var cache = new AssetCache();
        var disposable1 = new DisposableAsset();
        var disposable2 = new DisposableAsset();

        cache.Add("d1", disposable1, 100);
        cache.Add("d2", disposable2, 100);

        // Act
        cache.Clear();

        // Assert
        Assert.True(disposable1.IsDisposed);
        Assert.True(disposable2.IsDisposed);
    }

    #endregion

    #region Remove Operation

    [Fact]
    public void Remove_WhenAssetExists_ReturnsTrue()
    {
        // Arrange
        using var cache = new AssetCache();
        cache.Add("asset1", new TestAsset("a"), 100);

        // Act
        var removed = cache.Remove("asset1");

        // Assert
        Assert.True(removed);
        Assert.False(cache.Contains("asset1"));
    }

    [Fact]
    public void Remove_WhenAssetNotExists_ReturnsFalse()
    {
        // Arrange
        using var cache = new AssetCache();

        // Act
        var removed = cache.Remove("nonexistent");

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public void Remove_DisposesDisposableAsset()
    {
        // Arrange
        using var cache = new AssetCache();
        var disposable = new DisposableAsset();
        cache.Add("d1", disposable, 100);

        // Act
        cache.Remove("d1");

        // Assert
        Assert.True(disposable.IsDisposed);
    }

    #endregion

    #region Dispose Behavior

    [Fact]
    public void Dispose_DisposesAllDisposableAssets()
    {
        // Arrange
        var disposable1 = new DisposableAsset();
        var disposable2 = new DisposableAsset();
        var nonDisposable = new TestAsset("non-disposable");

        var cache = new AssetCache();
        cache.Add("d1", disposable1, 100);
        cache.Add("d2", disposable2, 100);
        cache.Add("nd", nonDisposable, 100);

        // Act
        cache.Dispose();

        // Assert
        Assert.True(disposable1.IsDisposed);
        Assert.True(disposable2.IsDisposed);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var cache = new AssetCache();
        cache.Add("asset1", new TestAsset("a"), 100);

        // Act & Assert - Should not throw
        cache.Dispose();
        cache.Dispose();
    }

    [Fact]
    public void Operations_AfterDispose_ThrowObjectDisposedException()
    {
        // Arrange
        var cache = new AssetCache();
        cache.Dispose();

        // Act & Assert - All operations should throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => cache.Add("asset", new TestAsset("a"), 100));
        Assert.Throws<ObjectDisposedException>(() => cache.TryGet<TestAsset>("asset", out _));
        Assert.Throws<ObjectDisposedException>(() => cache.Remove("asset"));
        Assert.Throws<ObjectDisposedException>(() => cache.Clear());
        Assert.Throws<ObjectDisposedException>(() => _ = cache.Contains("asset"));
        Assert.Throws<ObjectDisposedException>(() => _ = cache.Count);
        Assert.Throws<ObjectDisposedException>(() => _ = cache.CurrentSize);
    }

    #endregion

    #region Thread Safety

    [Fact]
    public async Task ConcurrentOperations_DoNotCorruptState()
    {
        // Arrange
        using var cache = new AssetCache(maxSizeBytes: 10000);
        var tasks = new List<Task>();
        var random = new Random(42);

        // Act - Concurrent reads and writes
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    var key = $"asset-{index}-{j}";
                    cache.Add(key, new TestAsset($"data-{index}-{j}"), 10);
                    cache.TryGet<TestAsset>(key, out _);

                    if (j % 3 == 0)
                    {
                        cache.Remove(key);
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - Cache should be in valid state
        Assert.True(cache.CurrentSize >= 0);
        Assert.True(cache.CurrentSize <= cache.MaxSize);
        Assert.True(cache.Count >= 0);
    }

    #endregion

    #region Test Helpers

    private class TestAsset
    {
        public string Data { get; }

        public TestAsset(string data)
        {
            Data = data;
        }
    }

    private class DisposableAsset : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    #endregion
}
