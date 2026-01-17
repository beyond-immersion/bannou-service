using BeyondImmersion.Bannou.AssetLoader.Cache;
using Xunit;

namespace BeyondImmersion.Bannou.AssetLoader.Tests.Cache;

/// <summary>
/// Unit tests for MemoryAssetCache.
/// Verifies LRU eviction, hit/miss tracking, and size management.
/// </summary>
public class MemoryAssetCacheTests : IAsyncLifetime
{
    private MemoryAssetCache? _cache;

    public Task InitializeAsync()
    {
        _cache = new MemoryAssetCache(maxSizeBytes: 1024 * 1024); // 1MB default
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the cache, throwing if not initialized.
    /// </summary>
    private MemoryAssetCache Cache => _cache ?? throw new InvalidOperationException("Cache not initialized - InitializeAsync must be called first");

    public async Task DisposeAsync()
    {
        if (_cache != null)
            await Cache.ClearAsync();
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
        using var stream = new MemoryStream(data);
        await Cache.StoreBundleAsync(bundleId, stream, hash);

        // Assert
        Assert.True(await Cache.HasBundleAsync(bundleId));
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
        using var inputStream = new MemoryStream(originalData);
        await Cache.StoreBundleAsync(bundleId, inputStream, hash);
        using var stream = await Cache.GetBundleStreamAsync(bundleId);

        // Assert
        Assert.NotNull(stream);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.Equal(originalData, ms.ToArray());
    }

    /// <summary>
    /// Verifies that storing with empty bundleId throws.
    /// </summary>
    [Fact]
    public async Task StoreBundleAsync_EmptyBundleId_Throws()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Cache.StoreBundleAsync("", stream, "hash"));
    }

    /// <summary>
    /// Verifies that storing with empty hash throws.
    /// </summary>
    [Fact]
    public async Task StoreBundleAsync_EmptyHash_Throws()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Cache.StoreBundleAsync("bundle", stream, ""));
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
        var result = await Cache.HasBundleAsync("non-existent");

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
        using var stream = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("bundle-1", stream, "hash1");

        // Act
        var result = await Cache.HasBundleAsync("bundle-1");

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
        using var stream = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("bundle-1", stream, "correct-hash");

        // Act
        var result = await Cache.HasBundleAsync("bundle-1", "correct-hash");

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
        using var stream = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("bundle-1", stream, "original-hash");

        // Act
        var result = await Cache.HasBundleAsync("bundle-1", "different-hash");

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
        using var stream = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("bundle-1", stream, "ABC123");

        // Act
        var result = await Cache.HasBundleAsync("bundle-1", "abc123");

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
        var result = await Cache.GetBundleStreamAsync("non-existent");

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
        using var inputStream = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("bundle-1", inputStream, "hash1");

        // Act
        using var stream1 = await Cache.GetBundleStreamAsync("bundle-1");
        using var stream2 = await Cache.GetBundleStreamAsync("bundle-1");

        // Assert - streams should be independent
        Assert.NotNull(stream1);
        Assert.NotNull(stream2);

        // Reading from one should not affect the other
        var buffer = new byte[50];
        _ = await stream1!.ReadAsync(buffer);
        Assert.Equal(0, stream2!.Position);
    }

    /// <summary>
    /// Verifies that accessing updates last access time for LRU.
    /// </summary>
    [Fact]
    public async Task GetBundleStreamAsync_UpdatesAccessTime()
    {
        // Arrange
        using var inputStream = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("bundle-1", inputStream, "hash1");
        var initialStats = Cache.GetStats();

        // Act
        using var stream = await Cache.GetBundleStreamAsync("bundle-1");

        // Assert - hit count should increase
        var afterStats = Cache.GetStats();
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
        using var stream = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("bundle-1", stream, "hash1");

        // Act
        await Cache.RemoveBundleAsync("bundle-1");

        // Assert
        Assert.False(await Cache.HasBundleAsync("bundle-1"));
    }

    /// <summary>
    /// Verifies that RemoveBundleAsync for non-existent is a no-op.
    /// </summary>
    [Fact]
    public async Task RemoveBundleAsync_NonExistent_DoesNotThrow()
    {
        // Act - should not throw
        await Cache.RemoveBundleAsync("non-existent");
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
        using var stream1 = new MemoryStream(data1);
        using var stream2 = new MemoryStream(data2);
        await Cache.StoreBundleAsync("bundle-1", stream1, "hash1");
        await Cache.StoreBundleAsync("bundle-2", stream2, "hash2");

        // Act
        var stats = Cache.GetStats();

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
        using var inputStream = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("bundle-1", inputStream, "hash1");

        // Act - call GetBundleStreamAsync three times to register hits
        (await Cache.GetBundleStreamAsync("bundle-1"))?.Dispose();
        (await Cache.GetBundleStreamAsync("bundle-1"))?.Dispose();
        (await Cache.GetBundleStreamAsync("bundle-1"))?.Dispose();

        // Assert
        var stats = Cache.GetStats();
        Assert.Equal(3, stats.HitCount);
    }

    /// <summary>
    /// Verifies that GetStats tracks miss count.
    /// </summary>
    [Fact]
    public async Task GetStats_TracksMissCount()
    {
        // Act
        await Cache.GetBundleStreamAsync("non-existent-1");
        await Cache.GetBundleStreamAsync("non-existent-2");

        // Assert
        var stats = Cache.GetStats();
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

        // Add three bundles with different timestamps
        using var streamOld = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("old", streamOld, "hash1");
        await Task.Delay(10); // Ensure different timestamps
        using var streamMiddle = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("middle", streamMiddle, "hash2");
        await Task.Delay(10);
        using var streamNew = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("new", streamNew, "hash3");

        // Access "old" to make it recently used (updates LastAccessedAt)
        // Order after access: middle (oldest), new (middle), old (newest)
        (await Cache.GetBundleStreamAsync("old"))?.Dispose();

        // Act - evict to 200 bytes (need to remove 100 bytes from 300 total)
        // Only "middle" should be evicted as it has the oldest LastAccessedAt
        await Cache.EvictToSizeAsync(200);

        // Assert - "middle" should be evicted (least recently accessed)
        Assert.True(await Cache.HasBundleAsync("old"), "Old bundle should remain (recently accessed)");
        Assert.False(await Cache.HasBundleAsync("middle"), "Middle bundle should be evicted (least recently accessed)");
        Assert.True(await Cache.HasBundleAsync("new"), "New bundle should remain (more recently created than middle)");
    }

    /// <summary>
    /// Verifies that storing large bundle triggers eviction.
    /// </summary>
    [Fact]
    public async Task StoreBundleAsync_TriggerEviction_WhenExceedsMaxSize()
    {
        // Arrange
        _cache = new MemoryAssetCache(maxSizeBytes: 300);
        using var stream1 = new MemoryStream(CreateTestData(100));
        using var stream2 = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("bundle-1", stream1, "hash1");
        await Cache.StoreBundleAsync("bundle-2", stream2, "hash2");

        // Act - add bundle that would exceed max size
        using var stream3 = new MemoryStream(CreateTestData(150));
        await Cache.StoreBundleAsync("bundle-3", stream3, "hash3");

        // Assert - some bundles should be evicted
        var stats = Cache.GetStats();
        Assert.True(stats.TotalBytes <= 300, "Total size should not exceed max");
        Assert.True(await Cache.HasBundleAsync("bundle-3"), "New bundle should be present");
    }

    /// <summary>
    /// Verifies that EvictToSizeAsync with negative target throws.
    /// </summary>
    [Fact]
    public async Task EvictToSizeAsync_NegativeTarget_Throws()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Cache.EvictToSizeAsync(-1));
    }

    /// <summary>
    /// Verifies that EvictToSizeAsync does nothing when under target.
    /// </summary>
    [Fact]
    public async Task EvictToSizeAsync_UnderTarget_DoesNothing()
    {
        // Arrange
        using var stream = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("bundle-1", stream, "hash1");

        // Act
        await Cache.EvictToSizeAsync(1000);

        // Assert
        Assert.True(await Cache.HasBundleAsync("bundle-1"));
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
        using var stream1 = new MemoryStream(CreateTestData(100));
        using var stream2 = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("bundle-1", stream1, "hash1");
        await Cache.StoreBundleAsync("bundle-2", stream2, "hash2");

        // Act
        await Cache.ClearAsync();

        // Assert
        var stats = Cache.GetStats();
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
        using var inputStream = new MemoryStream(CreateTestData(100));
        await Cache.StoreBundleAsync("bundle-1", inputStream, "hash1");
        (await Cache.GetBundleStreamAsync("bundle-1"))?.Dispose();
        await Cache.GetBundleStreamAsync("non-existent");

        // Act
        await Cache.ClearAsync();

        // Assert
        var stats = Cache.GetStats();
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
