using BeyondImmersion.Bannou.AssetLoader.Cache;
using Xunit;

namespace BeyondImmersion.Bannou.AssetLoader.Tests.Cache;

/// <summary>
/// Unit tests for MemoryAssetCache.
/// Verifies LRU eviction, hit/miss tracking, and size management.
/// </summary>
public class MemoryAssetCacheTests : IAsyncLifetime
{
    private MemoryAssetCache _cache = null!;

    public Task InitializeAsync()
    {
        _cache = new MemoryAssetCache(maxSizeBytes: 1024 * 1024); // 1MB default
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _cache.ClearAsync();
    }

    #region Constructor Tests

    /// <summary>
    /// Verifies that default max size is set correctly.
    /// </summary>
    [Fact]
    public void Constructor_DefaultMaxSize_Is256MB()
    {
        // Arrange & Act
        var cache = new MemoryAssetCache();

        // Assert
        Assert.Equal(256 * 1024 * 1024, cache.MaxSizeBytes);
    }

    /// <summary>
    /// Verifies that custom max size is set correctly.
    /// </summary>
    [Fact]
    public void Constructor_CustomMaxSize_IsRespected()
    {
        // Arrange & Act
        var cache = new MemoryAssetCache(maxSizeBytes: 1024);

        // Assert
        Assert.Equal(1024, cache.MaxSizeBytes);
    }

    #endregion

    #region StoreBundleAsync Tests

    /// <summary>
    /// Verifies that storing a bundle makes it available.
    /// </summary>
    [Fact]
    public async Task StoreBundleAsync_MakesBundleAvailable()
    {
        // Arrange
        var bundleId = "test-bundle";
        var data = CreateTestData(100);
        var hash = "abc123";

        // Act
        await _cache.StoreBundleAsync(bundleId, new MemoryStream(data), hash);

        // Assert
        Assert.True(await _cache.HasBundleAsync(bundleId));
    }

    /// <summary>
    /// Verifies that stored data can be retrieved intact.
    /// </summary>
    [Fact]
    public async Task StoreBundleAsync_DataIsRetrievableIntact()
    {
        // Arrange
        var bundleId = "test-bundle";
        var originalData = CreateTestData(256);
        var hash = "hash123";

        // Act
        await _cache.StoreBundleAsync(bundleId, new MemoryStream(originalData), hash);
        var stream = await _cache.GetBundleStreamAsync(bundleId);

        // Assert
        Assert.NotNull(stream);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.Equal(originalData, ms.ToArray());
    }

    /// <summary>
    /// Verifies that storing with null bundleId throws.
    /// </summary>
    [Fact]
    public async Task StoreBundleAsync_NullBundleId_Throws()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _cache.StoreBundleAsync(null!, new MemoryStream(), "hash"));
    }

    /// <summary>
    /// Verifies that storing with empty bundleId throws.
    /// </summary>
    [Fact]
    public async Task StoreBundleAsync_EmptyBundleId_Throws()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _cache.StoreBundleAsync("", new MemoryStream(), "hash"));
    }

    /// <summary>
    /// Verifies that storing with null data throws.
    /// </summary>
    [Fact]
    public async Task StoreBundleAsync_NullData_Throws()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _cache.StoreBundleAsync("bundle", null!, "hash"));
    }

    /// <summary>
    /// Verifies that storing with empty hash throws.
    /// </summary>
    [Fact]
    public async Task StoreBundleAsync_EmptyHash_Throws()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _cache.StoreBundleAsync("bundle", new MemoryStream(), ""));
    }

    #endregion

    #region HasBundleAsync Tests

    /// <summary>
    /// Verifies that HasBundleAsync returns false for non-existent bundle.
    /// </summary>
    [Fact]
    public async Task HasBundleAsync_NonExistent_ReturnsFalse()
    {
        // Act
        var result = await _cache.HasBundleAsync("non-existent");

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that HasBundleAsync returns true for existing bundle.
    /// </summary>
    [Fact]
    public async Task HasBundleAsync_Existing_ReturnsTrue()
    {
        // Arrange
        await _cache.StoreBundleAsync("bundle-1", new MemoryStream(CreateTestData(100)), "hash1");

        // Act
        var result = await _cache.HasBundleAsync("bundle-1");

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Verifies that HasBundleAsync with matching hash returns true.
    /// </summary>
    [Fact]
    public async Task HasBundleAsync_MatchingHash_ReturnsTrue()
    {
        // Arrange
        await _cache.StoreBundleAsync("bundle-1", new MemoryStream(CreateTestData(100)), "correct-hash");

        // Act
        var result = await _cache.HasBundleAsync("bundle-1", "correct-hash");

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Verifies that HasBundleAsync with mismatched hash returns false.
    /// </summary>
    [Fact]
    public async Task HasBundleAsync_MismatchedHash_ReturnsFalse()
    {
        // Arrange
        await _cache.StoreBundleAsync("bundle-1", new MemoryStream(CreateTestData(100)), "original-hash");

        // Act
        var result = await _cache.HasBundleAsync("bundle-1", "different-hash");

        // Assert
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that hash comparison is case-insensitive.
    /// </summary>
    [Fact]
    public async Task HasBundleAsync_HashComparison_IsCaseInsensitive()
    {
        // Arrange
        await _cache.StoreBundleAsync("bundle-1", new MemoryStream(CreateTestData(100)), "ABC123");

        // Act
        var result = await _cache.HasBundleAsync("bundle-1", "abc123");

        // Assert
        Assert.True(result);
    }

    #endregion

    #region GetBundleStreamAsync Tests

    /// <summary>
    /// Verifies that GetBundleStreamAsync returns null for non-existent bundle.
    /// </summary>
    [Fact]
    public async Task GetBundleStreamAsync_NonExistent_ReturnsNull()
    {
        // Act
        var result = await _cache.GetBundleStreamAsync("non-existent");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that GetBundleStreamAsync returns independent streams.
    /// </summary>
    [Fact]
    public async Task GetBundleStreamAsync_ReturnsIndependentStreams()
    {
        // Arrange
        await _cache.StoreBundleAsync("bundle-1", new MemoryStream(CreateTestData(100)), "hash1");

        // Act
        var stream1 = await _cache.GetBundleStreamAsync("bundle-1");
        var stream2 = await _cache.GetBundleStreamAsync("bundle-1");

        // Assert - streams should be independent
        Assert.NotNull(stream1);
        Assert.NotNull(stream2);

        // Reading from one should not affect the other
        var buffer = new byte[50];
        _ = await stream1!.ReadAsync(buffer);
        Assert.Equal(0, stream2!.Position);

        stream1.Dispose();
        stream2.Dispose();
    }

    /// <summary>
    /// Verifies that accessing updates last access time for LRU.
    /// </summary>
    [Fact]
    public async Task GetBundleStreamAsync_UpdatesAccessTime()
    {
        // Arrange
        await _cache.StoreBundleAsync("bundle-1", new MemoryStream(CreateTestData(100)), "hash1");
        var initialStats = _cache.GetStats();

        // Act
        var stream = await _cache.GetBundleStreamAsync("bundle-1");
        stream?.Dispose();

        // Assert - hit count should increase
        var afterStats = _cache.GetStats();
        Assert.Equal(initialStats.HitCount + 1, afterStats.HitCount);
    }

    #endregion

    #region RemoveBundleAsync Tests

    /// <summary>
    /// Verifies that RemoveBundleAsync removes the bundle.
    /// </summary>
    [Fact]
    public async Task RemoveBundleAsync_RemovesBundle()
    {
        // Arrange
        await _cache.StoreBundleAsync("bundle-1", new MemoryStream(CreateTestData(100)), "hash1");

        // Act
        await _cache.RemoveBundleAsync("bundle-1");

        // Assert
        Assert.False(await _cache.HasBundleAsync("bundle-1"));
    }

    /// <summary>
    /// Verifies that RemoveBundleAsync for non-existent is a no-op.
    /// </summary>
    [Fact]
    public async Task RemoveBundleAsync_NonExistent_DoesNotThrow()
    {
        // Act - should not throw
        await _cache.RemoveBundleAsync("non-existent");
    }

    #endregion

    #region GetStats Tests

    /// <summary>
    /// Verifies that GetStats returns correct size information.
    /// </summary>
    [Fact]
    public async Task GetStats_ReportsCorrectSize()
    {
        // Arrange
        var data1 = CreateTestData(100);
        var data2 = CreateTestData(200);
        await _cache.StoreBundleAsync("bundle-1", new MemoryStream(data1), "hash1");
        await _cache.StoreBundleAsync("bundle-2", new MemoryStream(data2), "hash2");

        // Act
        var stats = _cache.GetStats();

        // Assert
        Assert.Equal(300, stats.TotalBytes);
        Assert.Equal(2, stats.BundleCount);
    }

    /// <summary>
    /// Verifies that GetStats tracks hit count.
    /// </summary>
    [Fact]
    public async Task GetStats_TracksHitCount()
    {
        // Arrange
        await _cache.StoreBundleAsync("bundle-1", new MemoryStream(CreateTestData(100)), "hash1");

        // Act
        (await _cache.GetBundleStreamAsync("bundle-1"))?.Dispose();
        (await _cache.GetBundleStreamAsync("bundle-1"))?.Dispose();
        (await _cache.GetBundleStreamAsync("bundle-1"))?.Dispose();

        // Assert
        var stats = _cache.GetStats();
        Assert.Equal(3, stats.HitCount);
    }

    /// <summary>
    /// Verifies that GetStats tracks miss count.
    /// </summary>
    [Fact]
    public async Task GetStats_TracksMissCount()
    {
        // Act
        await _cache.GetBundleStreamAsync("non-existent-1");
        await _cache.GetBundleStreamAsync("non-existent-2");

        // Assert
        var stats = _cache.GetStats();
        Assert.Equal(2, stats.MissCount);
    }

    #endregion

    #region LRU Eviction Tests

    /// <summary>
    /// Verifies that LRU eviction removes least recently accessed items.
    /// </summary>
    [Fact]
    public async Task EvictToSizeAsync_RemovesLeastRecentlyAccessed()
    {
        // Arrange
        _cache = new MemoryAssetCache(maxSizeBytes: 1000);

        // Add three bundles
        await _cache.StoreBundleAsync("old", new MemoryStream(CreateTestData(100)), "hash1");
        await Task.Delay(10); // Ensure different timestamps
        await _cache.StoreBundleAsync("middle", new MemoryStream(CreateTestData(100)), "hash2");
        await Task.Delay(10);
        await _cache.StoreBundleAsync("new", new MemoryStream(CreateTestData(100)), "hash3");

        // Access "old" to make it recently used
        (await _cache.GetBundleStreamAsync("old"))?.Dispose();

        // Act - evict to make room
        await _cache.EvictToSizeAsync(150);

        // Assert - "middle" should be evicted (least recently accessed)
        Assert.True(await _cache.HasBundleAsync("old"), "Old bundle should remain (recently accessed)");
        Assert.False(await _cache.HasBundleAsync("middle"), "Middle bundle should be evicted (least recently accessed)");
        Assert.True(await _cache.HasBundleAsync("new"), "New bundle should remain (recently created)");
    }

    /// <summary>
    /// Verifies that storing large bundle triggers eviction.
    /// </summary>
    [Fact]
    public async Task StoreBundleAsync_TriggerEviction_WhenExceedsMaxSize()
    {
        // Arrange
        _cache = new MemoryAssetCache(maxSizeBytes: 300);
        await _cache.StoreBundleAsync("bundle-1", new MemoryStream(CreateTestData(100)), "hash1");
        await _cache.StoreBundleAsync("bundle-2", new MemoryStream(CreateTestData(100)), "hash2");

        // Act - add bundle that would exceed max size
        await _cache.StoreBundleAsync("bundle-3", new MemoryStream(CreateTestData(150)), "hash3");

        // Assert - some bundles should be evicted
        var stats = _cache.GetStats();
        Assert.True(stats.TotalBytes <= 300, "Total size should not exceed max");
        Assert.True(await _cache.HasBundleAsync("bundle-3"), "New bundle should be present");
    }

    /// <summary>
    /// Verifies that EvictToSizeAsync with negative target throws.
    /// </summary>
    [Fact]
    public async Task EvictToSizeAsync_NegativeTarget_Throws()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _cache.EvictToSizeAsync(-1));
    }

    /// <summary>
    /// Verifies that EvictToSizeAsync does nothing when under target.
    /// </summary>
    [Fact]
    public async Task EvictToSizeAsync_UnderTarget_DoesNothing()
    {
        // Arrange
        await _cache.StoreBundleAsync("bundle-1", new MemoryStream(CreateTestData(100)), "hash1");

        // Act
        await _cache.EvictToSizeAsync(1000);

        // Assert
        Assert.True(await _cache.HasBundleAsync("bundle-1"));
    }

    #endregion

    #region ClearAsync Tests

    /// <summary>
    /// Verifies that ClearAsync removes all bundles.
    /// </summary>
    [Fact]
    public async Task ClearAsync_RemovesAllBundles()
    {
        // Arrange
        await _cache.StoreBundleAsync("bundle-1", new MemoryStream(CreateTestData(100)), "hash1");
        await _cache.StoreBundleAsync("bundle-2", new MemoryStream(CreateTestData(100)), "hash2");

        // Act
        await _cache.ClearAsync();

        // Assert
        var stats = _cache.GetStats();
        Assert.Equal(0, stats.BundleCount);
        Assert.Equal(0, stats.TotalBytes);
    }

    /// <summary>
    /// Verifies that ClearAsync resets stats.
    /// </summary>
    [Fact]
    public async Task ClearAsync_ResetsStats()
    {
        // Arrange
        await _cache.StoreBundleAsync("bundle-1", new MemoryStream(CreateTestData(100)), "hash1");
        (await _cache.GetBundleStreamAsync("bundle-1"))?.Dispose();
        await _cache.GetBundleStreamAsync("non-existent");

        // Act
        await _cache.ClearAsync();

        // Assert
        var stats = _cache.GetStats();
        Assert.Equal(0, stats.HitCount);
        Assert.Equal(0, stats.MissCount);
    }

    #endregion

    #region Helper Methods

    private static byte[] CreateTestData(int size)
    {
        var data = new byte[size];
        Random.Shared.NextBytes(data);
        return data;
    }

    #endregion
}
