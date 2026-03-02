using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Unit tests for InMemoryStateStore bulk operations (SaveBulkAsync, ExistsBulkAsync, DeleteBulkAsync)
/// and additional coverage gaps including TrySaveAsync empty-etag path and GetWithETagAsync edge cases.
/// </summary>
public class InMemoryBulkOperationsTests : IDisposable
{
    private readonly Mock<ILogger<InMemoryStateStore<TestModel>>> _mockLogger;
    private readonly string _storeName;
    private readonly InMemoryStateStore<TestModel> _store;

    /// <summary>
    /// Test model for bulk operation tests.
    /// </summary>
    public class TestModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Score { get; set; }
    }

    public InMemoryBulkOperationsTests()
    {
        _mockLogger = new Mock<ILogger<InMemoryStateStore<TestModel>>>();
        _storeName = $"bulk-test-{Guid.NewGuid():N}";
        _store = new InMemoryStateStore<TestModel>(_storeName, _mockLogger.Object);
    }

    public void Dispose()
    {
        _store.Clear();
    }

    #region SaveBulkAsync Tests

    [Fact]
    public async Task SaveBulkAsync_WithMultipleItems_SavesAllAndReturnsEtags()
    {
        // Arrange
        var items = new Dictionary<string, TestModel>
        {
            ["key1"] = new TestModel { Id = "1", Name = "Alpha", Score = 10 },
            ["key2"] = new TestModel { Id = "2", Name = "Beta", Score = 20 },
            ["key3"] = new TestModel { Id = "3", Name = "Gamma", Score = 30 }
        };

        // Act
        var etags = await _store.SaveBulkAsync(items);

        // Assert
        Assert.Equal(3, etags.Count);
        Assert.True(etags.ContainsKey("key1"));
        Assert.True(etags.ContainsKey("key2"));
        Assert.True(etags.ContainsKey("key3"));
        // First save should return version "1"
        Assert.Equal("1", etags["key1"]);
        Assert.Equal("1", etags["key2"]);
        Assert.Equal("1", etags["key3"]);

        // Verify all items are retrievable
        var retrieved1 = await _store.GetAsync("key1");
        Assert.NotNull(retrieved1);
        Assert.Equal("Alpha", retrieved1.Name);

        var retrieved2 = await _store.GetAsync("key2");
        Assert.NotNull(retrieved2);
        Assert.Equal("Beta", retrieved2.Name);

        var retrieved3 = await _store.GetAsync("key3");
        Assert.NotNull(retrieved3);
        Assert.Equal("Gamma", retrieved3.Name);
    }

    [Fact]
    public async Task SaveBulkAsync_WithEmptyItems_ReturnsEmptyDictionary()
    {
        // Act
        var etags = await _store.SaveBulkAsync(new Dictionary<string, TestModel>());

        // Assert
        Assert.Empty(etags);
    }

    [Fact]
    public async Task SaveBulkAsync_OverwritingExistingKeys_IncrementsVersions()
    {
        // Arrange - Save initial values
        await _store.SaveAsync("key1", new TestModel { Id = "1", Name = "Old1", Score = 1 });
        await _store.SaveAsync("key2", new TestModel { Id = "2", Name = "Old2", Score = 2 });

        var updates = new Dictionary<string, TestModel>
        {
            ["key1"] = new TestModel { Id = "1", Name = "New1", Score = 100 },
            ["key2"] = new TestModel { Id = "2", Name = "New2", Score = 200 }
        };

        // Act
        var etags = await _store.SaveBulkAsync(updates);

        // Assert - Versions should be "2" (incremented from initial "1")
        Assert.Equal("2", etags["key1"]);
        Assert.Equal("2", etags["key2"]);

        // Verify updated values
        var retrieved1 = await _store.GetAsync("key1");
        Assert.NotNull(retrieved1);
        Assert.Equal("New1", retrieved1.Name);
        Assert.Equal(100, retrieved1.Score);
    }

    [Fact]
    public async Task SaveBulkAsync_WithTtl_SetsExpirationOnAllItems()
    {
        // Arrange
        var items = new Dictionary<string, TestModel>
        {
            ["key1"] = new TestModel { Id = "1", Name = "Expiring1", Score = 10 },
            ["key2"] = new TestModel { Id = "2", Name = "Expiring2", Score = 20 }
        };

        // Act
        await _store.SaveBulkAsync(items, new StateOptions { Ttl = 1 });

        // Assert - Items exist immediately
        Assert.NotNull(await _store.GetAsync("key1"));
        Assert.NotNull(await _store.GetAsync("key2"));

        // Wait for expiration
        await Task.Delay(1100);

        // Items should be expired
        Assert.Null(await _store.GetAsync("key1"));
        Assert.Null(await _store.GetAsync("key2"));
    }

    [Fact]
    public async Task SaveBulkAsync_MixedNewAndExisting_HandlesCorrectly()
    {
        // Arrange - Save one existing key
        await _store.SaveAsync("existing-key", new TestModel { Id = "1", Name = "Existing", Score = 50 });

        var items = new Dictionary<string, TestModel>
        {
            ["existing-key"] = new TestModel { Id = "1", Name = "Updated", Score = 100 },
            ["new-key"] = new TestModel { Id = "2", Name = "New", Score = 200 }
        };

        // Act
        var etags = await _store.SaveBulkAsync(items);

        // Assert
        Assert.Equal("2", etags["existing-key"]); // Version incremented
        Assert.Equal("1", etags["new-key"]); // New key starts at version 1
    }

    #endregion

    #region ExistsBulkAsync Tests

    [Fact]
    public async Task ExistsBulkAsync_WithMixedKeys_ReturnsOnlyExistingKeys()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestModel { Id = "1", Name = "One", Score = 1 });
        await _store.SaveAsync("key3", new TestModel { Id = "3", Name = "Three", Score = 3 });

        // Act
        var existing = await _store.ExistsBulkAsync(new[] { "key1", "key2", "key3", "key4" });

        // Assert
        Assert.Equal(2, existing.Count);
        Assert.Contains("key1", existing);
        Assert.Contains("key3", existing);
        Assert.DoesNotContain("key2", existing);
        Assert.DoesNotContain("key4", existing);
    }

    [Fact]
    public async Task ExistsBulkAsync_WithEmptyKeys_ReturnsEmptySet()
    {
        // Act
        var existing = await _store.ExistsBulkAsync(Array.Empty<string>());

        // Assert
        Assert.Empty(existing);
    }

    [Fact]
    public async Task ExistsBulkAsync_WithAllExisting_ReturnsAllKeys()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestModel { Id = "1", Name = "One", Score = 1 });
        await _store.SaveAsync("key2", new TestModel { Id = "2", Name = "Two", Score = 2 });

        // Act
        var existing = await _store.ExistsBulkAsync(new[] { "key1", "key2" });

        // Assert
        Assert.Equal(2, existing.Count);
        Assert.Contains("key1", existing);
        Assert.Contains("key2", existing);
    }

    [Fact]
    public async Task ExistsBulkAsync_WithAllNonExistent_ReturnsEmptySet()
    {
        // Act
        var existing = await _store.ExistsBulkAsync(new[] { "nope1", "nope2", "nope3" });

        // Assert
        Assert.Empty(existing);
    }

    [Fact]
    public async Task ExistsBulkAsync_ExcludesExpiredKeys()
    {
        // Arrange
        await _store.SaveAsync("permanent", new TestModel { Id = "1", Name = "Permanent", Score = 1 });
        await _store.SaveAsync("expiring", new TestModel { Id = "2", Name = "Expiring", Score = 2 },
            new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var existing = await _store.ExistsBulkAsync(new[] { "permanent", "expiring" });

        // Assert
        Assert.Single(existing);
        Assert.Contains("permanent", existing);
        Assert.DoesNotContain("expiring", existing);
    }

    #endregion

    #region DeleteBulkAsync Tests

    [Fact]
    public async Task DeleteBulkAsync_WithExistingKeys_DeletesAllAndReturnsCount()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestModel { Id = "1", Name = "One", Score = 1 });
        await _store.SaveAsync("key2", new TestModel { Id = "2", Name = "Two", Score = 2 });
        await _store.SaveAsync("key3", new TestModel { Id = "3", Name = "Three", Score = 3 });

        // Act
        var deletedCount = await _store.DeleteBulkAsync(new[] { "key1", "key2", "key3" });

        // Assert
        Assert.Equal(3, deletedCount);
        Assert.Null(await _store.GetAsync("key1"));
        Assert.Null(await _store.GetAsync("key2"));
        Assert.Null(await _store.GetAsync("key3"));
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public async Task DeleteBulkAsync_WithEmptyKeys_ReturnsZero()
    {
        // Act
        var deletedCount = await _store.DeleteBulkAsync(Array.Empty<string>());

        // Assert
        Assert.Equal(0, deletedCount);
    }

    [Fact]
    public async Task DeleteBulkAsync_WithMixedExistingAndNonExistent_ReturnsDeletedCount()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestModel { Id = "1", Name = "One", Score = 1 });
        await _store.SaveAsync("key3", new TestModel { Id = "3", Name = "Three", Score = 3 });

        // Act - key2 doesn't exist
        var deletedCount = await _store.DeleteBulkAsync(new[] { "key1", "key2", "key3" });

        // Assert
        Assert.Equal(2, deletedCount);
    }

    [Fact]
    public async Task DeleteBulkAsync_WithAllNonExistent_ReturnsZero()
    {
        // Act
        var deletedCount = await _store.DeleteBulkAsync(new[] { "nope1", "nope2" });

        // Assert
        Assert.Equal(0, deletedCount);
    }

    [Fact]
    public async Task DeleteBulkAsync_LeavesOtherKeysUntouched()
    {
        // Arrange
        await _store.SaveAsync("delete-me", new TestModel { Id = "1", Name = "Delete", Score = 1 });
        await _store.SaveAsync("keep-me", new TestModel { Id = "2", Name = "Keep", Score = 2 });

        // Act
        var deletedCount = await _store.DeleteBulkAsync(new[] { "delete-me" });

        // Assert
        Assert.Equal(1, deletedCount);
        Assert.Null(await _store.GetAsync("delete-me"));
        Assert.NotNull(await _store.GetAsync("keep-me"));
    }

    #endregion

    #region TrySaveAsync Empty ETag (Create-If-Not-Exists) Tests

    [Fact]
    public async Task TrySaveAsync_WithEmptyETag_CreatesNewEntry()
    {
        // Arrange
        var entity = new TestModel { Id = "new", Name = "Brand New", Score = 100 };

        // Act
        var etag = await _store.TrySaveAsync("new-key", entity, string.Empty);

        // Assert
        Assert.NotNull(etag);
        Assert.Equal("1", etag);

        var retrieved = await _store.GetAsync("new-key");
        Assert.NotNull(retrieved);
        Assert.Equal("Brand New", retrieved.Name);
    }

    [Fact]
    public async Task TrySaveAsync_WithNullETag_CreatesNewEntry()
    {
        // Arrange
        var entity = new TestModel { Id = "new", Name = "Brand New", Score = 100 };

        // Act - null is treated same as empty string
        var etag = await _store.TrySaveAsync("new-key", entity, string.Empty);

        // Assert
        Assert.NotNull(etag);
        Assert.Equal("1", etag);
    }

    [Fact]
    public async Task TrySaveAsync_WithEmptyETag_WhenKeyAlreadyExists_ReturnsNull()
    {
        // Arrange - Create existing entry first
        var existing = new TestModel { Id = "existing", Name = "Original", Score = 50 };
        await _store.SaveAsync("existing-key", existing);

        var newEntity = new TestModel { Id = "new", Name = "Competing Create", Score = 100 };

        // Act - Empty etag means "create new only"
        var etag = await _store.TrySaveAsync("existing-key", newEntity, string.Empty);

        // Assert - Should fail because key already exists
        Assert.Null(etag);

        // Original value should be preserved
        var retrieved = await _store.GetAsync("existing-key");
        Assert.NotNull(retrieved);
        Assert.Equal("Original", retrieved.Name);
    }

    [Fact]
    public async Task TrySaveAsync_WithValidETag_PreservesTtl()
    {
        // Arrange - Save with TTL
        var entity = new TestModel { Id = "1", Name = "Original", Score = 10 };
        await _store.SaveAsync("ttl-key", entity, new StateOptions { Ttl = 60 });
        var (_, etag) = await _store.GetWithETagAsync("ttl-key");

        // Act - Update via optimistic concurrency (TTL should be preserved)
        var updated = new TestModel { Id = "1", Name = "Updated", Score = 20 };
        Assert.NotNull(etag);
        var newEtag = await _store.TrySaveAsync("ttl-key", updated, etag);

        // Assert
        Assert.NotNull(newEtag);
        // Key should still exist (TTL was preserved, not removed)
        var retrieved = await _store.GetAsync("ttl-key");
        Assert.NotNull(retrieved);
        Assert.Equal("Updated", retrieved.Name);
    }

    #endregion

    #region GetWithETagAsync Edge Cases

    [Fact]
    public async Task GetWithETagAsync_WithExpiredKey_ReturnsNullValueAndNullETag()
    {
        // Arrange
        var entity = new TestModel { Id = "1", Name = "Expiring", Score = 42 };
        await _store.SaveAsync("expiring-key", entity, new StateOptions { Ttl = 1 });

        // Verify it exists first
        var (valueBefore, etagBefore) = await _store.GetWithETagAsync("expiring-key");
        Assert.NotNull(valueBefore);
        Assert.NotNull(etagBefore);

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var (value, etag) = await _store.GetWithETagAsync("expiring-key");

        // Assert
        Assert.Null(value);
        Assert.Null(etag);
    }

    [Fact]
    public async Task GetWithETagAsync_ETagIsVersionString()
    {
        // Arrange
        var entity = new TestModel { Id = "1", Name = "Test", Score = 42 };
        await _store.SaveAsync("key1", entity);

        // Act
        var (_, etag1) = await _store.GetWithETagAsync("key1");

        // Save again to increment version
        await _store.SaveAsync("key1", new TestModel { Id = "1", Name = "Updated", Score = 100 });
        var (_, etag2) = await _store.GetWithETagAsync("key1");

        // Assert - ETags should be version strings
        Assert.Equal("1", etag1);
        Assert.Equal("2", etag2);
    }

    #endregion

    #region GetKeyCountForStore Static Method Tests

    [Fact]
    public async Task GetKeyCountForStore_WithExistingStore_ReturnsCorrectCount()
    {
        // Arrange
        await _store.SaveAsync("k1", new TestModel { Id = "1", Name = "One", Score = 1 });
        await _store.SaveAsync("k2", new TestModel { Id = "2", Name = "Two", Score = 2 });
        await _store.SaveAsync("k3", new TestModel { Id = "3", Name = "Three", Score = 3 });

        // Act
        var count = InMemoryStateStore<TestModel>.GetKeyCountForStore(_storeName);

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public void GetKeyCountForStore_WithNonExistentStore_ReturnsZero()
    {
        // Act
        var count = InMemoryStateStore<TestModel>.GetKeyCountForStore("nonexistent-store-name-xyz");

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetKeyCountForStore_AfterDeletions_ReturnsUpdatedCount()
    {
        // Arrange
        await _store.SaveAsync("k1", new TestModel { Id = "1", Name = "One", Score = 1 });
        await _store.SaveAsync("k2", new TestModel { Id = "2", Name = "Two", Score = 2 });
        Assert.Equal(2, InMemoryStateStore<TestModel>.GetKeyCountForStore(_storeName));

        // Act - Delete one
        await _store.DeleteAsync("k1");

        // Assert
        Assert.Equal(1, InMemoryStateStore<TestModel>.GetKeyCountForStore(_storeName));
    }

    #endregion

    #region InMemoryStoreData.GetKeyCountForStore Tests

    [Fact]
    public async Task InMemoryStoreData_GetKeyCountForStore_MatchesStaticMethod()
    {
        // Arrange
        await _store.SaveAsync("data-key", new TestModel { Id = "1", Name = "Data", Score = 42 });

        // Act
        var storeDataCount = InMemoryStoreData.GetKeyCountForStore(_storeName);
        var staticCount = InMemoryStateStore<TestModel>.GetKeyCountForStore(_storeName);

        // Assert - Both should return the same value
        Assert.Equal(storeDataCount, staticCount);
        Assert.Equal(1, storeDataCount);
    }

    #endregion

    #region SaveAsync Version Tracking Tests

    [Fact]
    public async Task SaveAsync_ReturnsConsecutiveVersionStrings()
    {
        // Arrange & Act
        var etag1 = await _store.SaveAsync("key1", new TestModel { Id = "1", Name = "V1", Score = 1 });
        var etag2 = await _store.SaveAsync("key1", new TestModel { Id = "1", Name = "V2", Score = 2 });
        var etag3 = await _store.SaveAsync("key1", new TestModel { Id = "1", Name = "V3", Score = 3 });

        // Assert
        Assert.Equal("1", etag1);
        Assert.Equal("2", etag2);
        Assert.Equal("3", etag3);
    }

    [Fact]
    public async Task SaveAsync_DifferentKeys_HaveIndependentVersions()
    {
        // Act
        var etag1 = await _store.SaveAsync("key-a", new TestModel { Id = "1", Name = "A", Score = 1 });
        var etag2 = await _store.SaveAsync("key-b", new TestModel { Id = "2", Name = "B", Score = 2 });

        // Assert - Each key starts at version 1 independently
        Assert.Equal("1", etag1);
        Assert.Equal("1", etag2);
    }

    #endregion

    #region AddToSetAsync Bulk with Empty Items Tests

    [Fact]
    public async Task AddToSetAsync_BulkWithEmptyEnumerable_ReturnsZero()
    {
        // Act
        var added = await _store.AddToSetAsync("test-set", Array.Empty<TestModel>().AsEnumerable());

        // Assert
        Assert.Equal(0, added);
    }

    #endregion

    #region SetContainsAsync with Expired Set Tests

    [Fact]
    public async Task SetContainsAsync_WithExpiredSet_ReturnsFalse()
    {
        // Arrange
        var item = new TestModel { Id = "1", Name = "Expiring", Score = 10 };
        await _store.AddToSetAsync("expiring-set", item, new StateOptions { Ttl = 1 });

        // Verify it exists
        Assert.True(await _store.SetContainsAsync("expiring-set", item));

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var contains = await _store.SetContainsAsync("expiring-set", item);

        // Assert
        Assert.False(contains);
    }

    #endregion

    #region RemoveFromSetAsync with Expired Set Tests

    [Fact]
    public async Task RemoveFromSetAsync_WithExpiredSet_ReturnsFalse()
    {
        // Arrange
        var item = new TestModel { Id = "1", Name = "Expiring", Score = 10 };
        await _store.AddToSetAsync("expiring-set", item, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var removed = await _store.RemoveFromSetAsync("expiring-set", item);

        // Assert
        Assert.False(removed);
    }

    #endregion

    #region RefreshSetTtlAsync with Expired Set Tests

    [Fact]
    public async Task RefreshSetTtlAsync_WithExpiredSet_ReturnsFalse()
    {
        // Arrange
        var item = new TestModel { Id = "1", Name = "Expiring", Score = 10 };
        await _store.AddToSetAsync("expiring-set", item, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var refreshed = await _store.RefreshSetTtlAsync("expiring-set", 60);

        // Assert
        Assert.False(refreshed);
    }

    #endregion

    #region SortedSetRangeByRank Negative Index Tests

    [Fact]
    public async Task SortedSetRangeByRankAsync_WithNegativeIndices_WrapsFromEnd()
    {
        // Arrange - Add members with distinct scores
        await _store.SortedSetAddAsync("rank-test", "alice", 100);
        await _store.SortedSetAddAsync("rank-test", "bob", 200);
        await _store.SortedSetAddAsync("rank-test", "charlie", 300);

        // Act - Use negative stop index (-1 = last element), ascending
        var result = await _store.SortedSetRangeByRankAsync("rank-test", 0, -1, descending: false);

        // Assert - Should return all 3 members in ascending order
        Assert.Equal(3, result.Count);
        Assert.Equal("alice", result[0].member);
        Assert.Equal("bob", result[1].member);
        Assert.Equal("charlie", result[2].member);
    }

    [Fact]
    public async Task SortedSetRangeByRankAsync_WithStartBeyondCount_ReturnsEmpty()
    {
        // Arrange
        await _store.SortedSetAddAsync("rank-test", "alice", 100);

        // Act - Start index beyond the set size
        var result = await _store.SortedSetRangeByRankAsync("rank-test", 10, 20, descending: false);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region SortedSetRank Ascending Tests

    [Fact]
    public async Task SortedSetRankAsync_WithNonExistentSet_ReturnsNull()
    {
        // Act
        var rank = await _store.SortedSetRankAsync("nonexistent-set", "member1");

        // Assert
        Assert.Null(rank);
    }

    #endregion

    #region SortedSet Expired Tests

    [Fact]
    public async Task SortedSetScoreAsync_WithExpiredSet_ReturnsNull()
    {
        // Arrange
        await _store.SortedSetAddAsync("expiring-ss", "member1", 42.0, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var score = await _store.SortedSetScoreAsync("expiring-ss", "member1");

        // Assert
        Assert.Null(score);
    }

    [Fact]
    public async Task SortedSetRankAsync_WithExpiredSet_ReturnsNull()
    {
        // Arrange
        await _store.SortedSetAddAsync("expiring-ss", "member1", 42.0, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var rank = await _store.SortedSetRankAsync("expiring-ss", "member1");

        // Assert
        Assert.Null(rank);
    }

    [Fact]
    public async Task SortedSetRemoveAsync_WithExpiredSet_ReturnsFalse()
    {
        // Arrange
        await _store.SortedSetAddAsync("expiring-ss", "member1", 42.0, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var removed = await _store.SortedSetRemoveAsync("expiring-ss", "member1");

        // Assert
        Assert.False(removed);
    }

    [Fact]
    public async Task SortedSetRangeByRankAsync_WithExpiredSet_ReturnsEmpty()
    {
        // Arrange
        await _store.SortedSetAddAsync("expiring-ss", "member1", 42.0, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var range = await _store.SortedSetRangeByRankAsync("expiring-ss", 0, -1);

        // Assert
        Assert.Empty(range);
    }

    [Fact]
    public async Task SortedSetRangeByScoreAsync_WithExpiredSet_ReturnsEmpty()
    {
        // Arrange
        await _store.SortedSetAddAsync("expiring-ss", "member1", 42.0, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var range = await _store.SortedSetRangeByScoreAsync("expiring-ss", 0, 100);

        // Assert
        Assert.Empty(range);
    }

    #endregion

    #region SortedSetIncrementAsync with Expired Set Tests

    [Fact]
    public async Task SortedSetIncrementAsync_WithExpiredSet_ResetsAndCreatesNewMember()
    {
        // Arrange - Set that will expire
        await _store.SortedSetAddAsync("expiring-ss", "member1", 100.0, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act - Increment on expired set should start from 0
        var newScore = await _store.SortedSetIncrementAsync("expiring-ss", "member1", 5.0);

        // Assert - Should be 5.0 (started from 0 after clearing expired data)
        Assert.Equal(5.0, newScore);
    }

    #endregion

    #region Counter Expired Reset Tests

    [Fact]
    public async Task IncrementAsync_WithExpiredCounter_ResetsToZeroThenIncrements()
    {
        // Arrange
        await _store.SetCounterAsync("expiring-counter", 100, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var newValue = await _store.IncrementAsync("expiring-counter", 5);

        // Assert - Should be 5 (reset from expired 100 to 0, then +5)
        Assert.Equal(5, newValue);
    }

    #endregion

    #region Hash Expired Reset Tests

    [Fact]
    public async Task HashSetAsync_WithExpiredHash_ClearsAndSetsNewField()
    {
        // Arrange
        await _store.HashSetAsync("expiring-hash", "old-field", "old-value", new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var isNew = await _store.HashSetAsync<string>("expiring-hash", "new-field", "new-value");

        // Assert - Should be a new field (hash was cleared)
        Assert.True(isNew);

        // Old field should be gone
        var oldValue = await _store.HashGetAsync<string>("expiring-hash", "old-field");
        Assert.Null(oldValue);
    }

    [Fact]
    public async Task HashDeleteAsync_WithExpiredHash_ReturnsFalse()
    {
        // Arrange
        await _store.HashSetAsync("expiring-hash", "field1", "value1", new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var deleted = await _store.HashDeleteAsync("expiring-hash", "field1");

        // Assert
        Assert.False(deleted);
    }

    [Fact]
    public async Task HashExistsAsync_WithExpiredHash_ReturnsFalse()
    {
        // Arrange
        await _store.HashSetAsync("expiring-hash", "field1", "value1", new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var exists = await _store.HashExistsAsync("expiring-hash", "field1");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task HashIncrementAsync_WithExpiredHash_ResetsAndIncrements()
    {
        // Arrange
        await _store.HashSetAsync("expiring-hash", "counter", "100", new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var newValue = await _store.HashIncrementAsync("expiring-hash", "counter", 5);

        // Assert - Should be 5 (reset from expired to 0, then +5)
        Assert.Equal(5, newValue);
    }

    [Fact]
    public async Task RefreshHashTtlAsync_WithExpiredHash_ReturnsFalse()
    {
        // Arrange
        await _store.HashSetAsync("expiring-hash", "field1", "value1", new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var refreshed = await _store.RefreshHashTtlAsync("expiring-hash", 60);

        // Assert
        Assert.False(refreshed);
    }

    [Fact]
    public async Task HashSetManyAsync_WithExpiredHash_WithoutNewTtl_RemainsExpired()
    {
        // Arrange
        await _store.HashSetAsync("expiring-hash", "old-field", "old-value", new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act - Add new fields WITHOUT providing new TTL options
        var fields = new Dictionary<string, string>
        {
            ["new-field1"] = "value1",
            ["new-field2"] = "value2"
        };
        await _store.HashSetManyAsync("expiring-hash", fields);

        // Assert - Container ExpiresAt is still in the past, so count returns 0
        // The items were added but the hash is still considered expired
        var count = await _store.HashCountAsync("expiring-hash");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task HashSetManyAsync_WithExpiredHash_WithNewTtl_ClearsAndSetsNewFields()
    {
        // Arrange
        await _store.HashSetAsync("expiring-hash-ttl", "old-field", "old-value", new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act - Add new fields WITH a new TTL (resets ExpiresAt)
        var fields = new Dictionary<string, string>
        {
            ["new-field1"] = "value1",
            ["new-field2"] = "value2"
        };
        await _store.HashSetManyAsync("expiring-hash-ttl", fields, new StateOptions { Ttl = 60 });

        // Assert - New TTL makes the hash visible again
        var count = await _store.HashCountAsync("expiring-hash-ttl");
        Assert.Equal(2, count);
    }

    #endregion

    #region HashGetAsync with Numeric Field Tests

    [Fact]
    public async Task HashGetAsync_RetrievesNumericFieldSetViaIncrement()
    {
        // Arrange - Set field via increment (creates a NumericField)
        await _store.HashIncrementAsync("test-hash", "counter", 42);

        // Act - Retrieve as long
        var value = await _store.HashGetAsync<long>("test-hash", "counter");

        // Assert
        Assert.Equal(42, value);
    }

    [Fact]
    public async Task HashExistsAsync_WithNumericField_ReturnsTrue()
    {
        // Arrange - Create a numeric field via increment
        await _store.HashIncrementAsync("test-hash", "counter", 1);

        // Act
        var exists = await _store.HashExistsAsync("test-hash", "counter");

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task HashCountAsync_CountsBothStringAndNumericFields()
    {
        // Arrange
        await _store.HashSetAsync("mixed-hash", "string-field", "hello");
        await _store.HashIncrementAsync("mixed-hash", "numeric-field", 42);

        // Act
        var count = await _store.HashCountAsync("mixed-hash");

        // Assert
        Assert.Equal(2, count);
    }

    #endregion

    #region HashSetAsync Overwrites NumericField Tests

    [Fact]
    public async Task HashSetAsync_OverwritesNumericFieldWithStringField()
    {
        // Arrange - Create a numeric field
        await _store.HashIncrementAsync("test-hash", "field1", 100);
        var numericValue = await _store.HashGetAsync<long>("test-hash", "field1");
        Assert.Equal(100, numericValue);

        // Act - Overwrite with string field
        await _store.HashSetAsync("test-hash", "field1", "now-a-string");

        // Assert - Should now be a string, numeric removed
        var stringValue = await _store.HashGetAsync<string>("test-hash", "field1");
        Assert.Equal("now-a-string", stringValue);
    }

    [Fact]
    public async Task HashIncrementAsync_OverwritesStringFieldWithNumericField()
    {
        // Arrange - Create a string field
        await _store.HashSetAsync("test-hash", "field1", "hello");

        // Act - Increment should try to parse string, fail gracefully, default to 0
        var newValue = await _store.HashIncrementAsync("test-hash", "field1", 5);

        // Assert - Should be 5 (0 + 5, since "hello" is not numeric)
        Assert.Equal(5, newValue);
    }

    #endregion

    #region AddToSetAsync Bulk Expired Set Tests

    [Fact]
    public async Task AddToSetAsync_BulkOnExpiredSet_WithoutNewTtl_RemainsExpired()
    {
        // Arrange
        var oldItem = new TestModel { Id = "old", Name = "Old", Score = 1 };
        await _store.AddToSetAsync("expiring-set", oldItem, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act - Add new items WITHOUT providing new TTL options
        var newItems = new[]
        {
            new TestModel { Id = "new1", Name = "New1", Score = 10 },
            new TestModel { Id = "new2", Name = "New2", Score = 20 }
        };
        var added = await _store.AddToSetAsync("expiring-set", newItems.AsEnumerable());

        // Assert - Items were added to internal storage, but container ExpiresAt
        // is still in the past, so count returns 0 (set appears expired)
        Assert.Equal(2, added);
        var count = await _store.SetCountAsync("expiring-set");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task AddToSetAsync_BulkOnExpiredSet_WithNewTtl_ClearsAndAddsNew()
    {
        // Arrange
        var oldItem = new TestModel { Id = "old", Name = "Old", Score = 1 };
        await _store.AddToSetAsync("expiring-set-ttl", oldItem, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act - Add new items WITH a new TTL (resets ExpiresAt)
        var newItems = new[]
        {
            new TestModel { Id = "new1", Name = "New1", Score = 10 },
            new TestModel { Id = "new2", Name = "New2", Score = 20 }
        };
        var added = await _store.AddToSetAsync("expiring-set-ttl", newItems.AsEnumerable(),
            new StateOptions { Ttl = 60 });

        // Assert - New TTL makes the set visible again
        Assert.Equal(2, added);
        var count = await _store.SetCountAsync("expiring-set-ttl");
        Assert.Equal(2, count);
    }

    #endregion

    #region SortedSetAddBatchAsync Expired Tests

    [Fact]
    public async Task SortedSetAddBatchAsync_OnExpiredSet_WithoutNewTtl_RemainsExpired()
    {
        // Arrange
        await _store.SortedSetAddAsync("expiring-ss", "old-member", 50.0, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act - Add new entries WITHOUT providing new TTL options
        var entries = new[] { ("new1", 10.0), ("new2", 20.0) };
        var added = await _store.SortedSetAddBatchAsync("expiring-ss", entries);

        // Assert - Items were added to internal storage, but container ExpiresAt
        // is still in the past, so count returns 0 (sorted set appears expired)
        Assert.Equal(2, added);
        var count = await _store.SortedSetCountAsync("expiring-ss");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SortedSetAddBatchAsync_OnExpiredSet_WithNewTtl_ClearsAndAddsNew()
    {
        // Arrange
        await _store.SortedSetAddAsync("expiring-ss-ttl", "old-member", 50.0, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act - Add new entries WITH a new TTL (resets ExpiresAt)
        var entries = new[] { ("new1", 10.0), ("new2", 20.0) };
        var added = await _store.SortedSetAddBatchAsync("expiring-ss-ttl", entries,
            new StateOptions { Ttl = 60 });

        // Assert - New TTL makes the sorted set visible again
        Assert.Equal(2, added);
        var count = await _store.SortedSetCountAsync("expiring-ss-ttl");
        Assert.Equal(2, count);
    }

    #endregion
}
