using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Services;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Unit tests for InMemoryStateStore.
/// Tests thread-safe in-memory state storage with TTL and optimistic concurrency support.
/// </summary>
public class InMemoryStateStoreTests : IDisposable
{
    private readonly Mock<ILogger<InMemoryStateStore<TestEntity>>> _mockLogger;
    private readonly string _storeName;
    private readonly InMemoryStateStore<TestEntity> _store;

    /// <summary>
    /// Test entity for storage tests.
    /// </summary>
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public InMemoryStateStoreTests()
    {
        _mockLogger = new Mock<ILogger<InMemoryStateStore<TestEntity>>>();
        _storeName = $"test-store-{Guid.NewGuid():N}"; // Unique store per test
        _store = new InMemoryStateStore<TestEntity>(_storeName, _mockLogger.Object);
    }

    public void Dispose()
    {
        _store.Clear();
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        // Note: InMemoryStateStore is not a DI-registered service - it's created by StateStoreFactory.
        // The factory controls constructor args including the optional error publisher callback.
        Assert.NotNull(_store);
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_WithExistingKey_ReturnsValue()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        await _store.SaveAsync("key1", entity);

        // Act
        var result = await _store.GetAsync("key1");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1", result.Id);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task GetAsync_WithNonExistentKey_ReturnsNull()
    {
        // Act
        var result = await _store.GetAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithExpiredKey_ReturnsNullAndRemovesEntry()
    {
        // Arrange - Save with 1 second TTL
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        await _store.SaveAsync("expiring-key", entity, new StateOptions { Ttl = 1 });

        // Verify it exists first
        var beforeExpiry = await _store.GetAsync("expiring-key");
        Assert.NotNull(beforeExpiry);

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var result = await _store.GetAsync("expiring-key");

        // Assert
        Assert.Null(result);
        Assert.Equal(0, _store.Count);
    }

    #endregion

    #region GetWithETagAsync Tests

    [Fact]
    public async Task GetWithETagAsync_WithExistingKey_ReturnsValueAndETag()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        await _store.SaveAsync("key1", entity);

        // Act
        var (value, etag) = await _store.GetWithETagAsync("key1");

        // Assert
        Assert.NotNull(value);
        Assert.Equal("1", value.Id);
        Assert.NotNull(etag);
        Assert.Equal("1", etag); // First save = version 1
    }

    [Fact]
    public async Task GetWithETagAsync_WithNonExistentKey_ReturnsNullValueAndNullETag()
    {
        // Act
        var (value, etag) = await _store.GetWithETagAsync("nonexistent");

        // Assert
        Assert.Null(value);
        Assert.Null(etag);
    }

    [Fact]
    public async Task GetWithETagAsync_AfterMultipleSaves_ReturnsIncrementedETag()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        await _store.SaveAsync("key1", entity);
        await _store.SaveAsync("key1", entity);
        await _store.SaveAsync("key1", entity);

        // Act
        var (_, etag) = await _store.GetWithETagAsync("key1");

        // Assert
        Assert.Equal("3", etag); // Third save = version 3
    }

    #endregion

    #region SaveAsync Tests

    [Fact]
    public async Task SaveAsync_WithNewKey_CreatesEntryWithVersion1()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        // Act
        var etag = await _store.SaveAsync("key1", entity);

        // Assert
        Assert.Equal("1", etag);
        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public async Task SaveAsync_WithExistingKey_IncrementsVersion()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        await _store.SaveAsync("key1", entity);

        // Act
        entity.Value = 100;
        var etag = await _store.SaveAsync("key1", entity);

        // Assert
        Assert.Equal("2", etag);
        var retrieved = await _store.GetAsync("key1");
        Assert.NotNull(retrieved);
        Assert.Equal(100, retrieved.Value);
    }

    [Fact]
    public async Task SaveAsync_WithTtl_SetsExpiration()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        // Act
        await _store.SaveAsync("ttl-key", entity, new StateOptions { Ttl = 2 });

        // Assert - Should exist immediately
        Assert.True(await _store.ExistsAsync("ttl-key"));

        // Wait for expiration
        await Task.Delay(2100);

        // Should be expired now
        Assert.False(await _store.ExistsAsync("ttl-key"));
    }

    #endregion

    #region TrySaveAsync (Optimistic Concurrency) Tests

    [Fact]
    public async Task TrySaveAsync_WithCorrectETag_SucceedsAndUpdatesValue()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        var etag = await _store.SaveAsync("key1", entity);

        // Act
        entity.Value = 100;
        var newEtag = await _store.TrySaveAsync("key1", entity, etag);

        // Assert
        Assert.NotNull(newEtag);
        var retrieved = await _store.GetAsync("key1");
        Assert.NotNull(retrieved);
        Assert.Equal(100, retrieved.Value);
    }

    [Fact]
    public async Task TrySaveAsync_WithIncorrectETag_FailsAndPreservesValue()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        await _store.SaveAsync("key1", entity);

        // Act
        entity.Value = 100;
        var newEtag = await _store.TrySaveAsync("key1", entity, "999"); // Wrong etag

        // Assert
        Assert.Null(newEtag);
        var retrieved = await _store.GetAsync("key1");
        Assert.NotNull(retrieved);
        Assert.Equal(42, retrieved.Value); // Original value preserved
    }

    [Fact]
    public async Task TrySaveAsync_WithInvalidETagFormat_ReturnsNull()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        await _store.SaveAsync("key1", entity);

        // Act
        var newEtag = await _store.TrySaveAsync("key1", entity, "not-a-number");

        // Assert
        Assert.Null(newEtag);
    }

    [Fact]
    public async Task TrySaveAsync_WithNonExistentKey_ReturnsNull()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        // Act
        var newEtag = await _store.TrySaveAsync("nonexistent", entity, "1");

        // Assert
        Assert.Null(newEtag);
    }

    [Fact]
    public async Task TrySaveAsync_ConcurrentUpdates_OnlyOneSucceeds()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 0 };
        var etag = await _store.SaveAsync("key1", entity);

        // Act - Simulate concurrent updates with same etag
        var tasks = Enumerable.Range(1, 10).Select(i =>
        {
            var update = new TestEntity { Id = "1", Name = "Test", Value = i };
            return _store.TrySaveAsync("key1", update, etag);
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert - Only one should succeed (non-null etag)
        Assert.Equal(1, results.Count(r => r != null));
        Assert.Equal(9, results.Count(r => r == null));
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithExistingKey_ReturnsTrueAndRemovesEntry()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        await _store.SaveAsync("key1", entity);

        // Act
        var result = await _store.DeleteAsync("key1");

        // Assert
        Assert.True(result);
        Assert.Null(await _store.GetAsync("key1"));
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Act
        var result = await _store.DeleteAsync("nonexistent");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region ExistsAsync Tests

    [Fact]
    public async Task ExistsAsync_WithExistingKey_ReturnsTrue()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        await _store.SaveAsync("key1", entity);

        // Act
        var exists = await _store.ExistsAsync("key1");

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Act
        var exists = await _store.ExistsAsync("nonexistent");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_WithExpiredKey_ReturnsFalseAndRemovesEntry()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        await _store.SaveAsync("expiring-key", entity, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var exists = await _store.ExistsAsync("expiring-key");

        // Assert
        Assert.False(exists);
    }

    #endregion

    #region GetBulkAsync Tests

    [Fact]
    public async Task GetBulkAsync_WithExistingKeys_ReturnsAllValues()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "One", Value = 1 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Two", Value = 2 });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Three", Value = 3 });

        // Act
        var results = await _store.GetBulkAsync(new[] { "key1", "key2", "key3" });

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal("One", results["key1"].Name);
        Assert.Equal("Two", results["key2"].Name);
        Assert.Equal("Three", results["key3"].Name);
    }

    [Fact]
    public async Task GetBulkAsync_WithMixedExistingAndNonExistent_ReturnsOnlyExisting()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "One", Value = 1 });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Three", Value = 3 });

        // Act
        var results = await _store.GetBulkAsync(new[] { "key1", "key2", "key3" });

        // Assert
        Assert.Equal(2, results.Count);
        Assert.True(results.ContainsKey("key1"));
        Assert.False(results.ContainsKey("key2"));
        Assert.True(results.ContainsKey("key3"));
    }

    [Fact]
    public async Task GetBulkAsync_WithAllNonExistent_ReturnsEmptyDictionary()
    {
        // Act
        var results = await _store.GetBulkAsync(new[] { "nonexistent1", "nonexistent2" });

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetBulkAsync_WithEmptyKeys_ReturnsEmptyDictionary()
    {
        // Act
        var results = await _store.GetBulkAsync(Array.Empty<string>());

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetBulkAsync_ExcludesExpiredEntries()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "One", Value = 1 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Two", Value = 2 }, new StateOptions { Ttl = 1 });

        // Wait for key2 to expire
        await Task.Delay(1100);

        // Act
        var results = await _store.GetBulkAsync(new[] { "key1", "key2" });

        // Assert
        Assert.Single(results);
        Assert.True(results.ContainsKey("key1"));
        Assert.False(results.ContainsKey("key2"));
    }

    #endregion

    #region Clear and Count Tests

    [Fact]
    public async Task Clear_RemovesAllEntries()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "One", Value = 1 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Two", Value = 2 });
        Assert.Equal(2, _store.Count);

        // Act
        _store.Clear();

        // Assert
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public async Task Count_ReturnsNumberOfNonExpiredEntries()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "One", Value = 1 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Two", Value = 2 });
        await _store.SaveAsync("expiring", new TestEntity { Id = "3", Name = "Three", Value = 3 }, new StateOptions { Ttl = 1 });

        Assert.Equal(3, _store.Count);

        // Wait for expiration
        await Task.Delay(1100);

        // Act & Assert
        Assert.Equal(2, _store.Count);
    }

    #endregion

    #region Shared Store Tests

    [Fact]
    public async Task MultipleInstances_WithSameStoreName_ShareData()
    {
        // Arrange
        var sharedStoreName = $"shared-{Guid.NewGuid():N}";
        var store1 = new InMemoryStateStore<TestEntity>(sharedStoreName, _mockLogger.Object);
        var store2 = new InMemoryStateStore<TestEntity>(sharedStoreName, _mockLogger.Object);

        // Act - Save via store1
        await store1.SaveAsync("shared-key", new TestEntity { Id = "1", Name = "Shared", Value = 42 });

        // Assert - Retrieve via store2
        var result = await store2.GetAsync("shared-key");
        Assert.NotNull(result);
        Assert.Equal("Shared", result.Name);

        // Cleanup
        store1.Clear();
    }

    [Fact]
    public async Task MultipleInstances_WithDifferentStoreNames_DoNotShareData()
    {
        // Arrange
        var store1 = new InMemoryStateStore<TestEntity>($"store-a-{Guid.NewGuid():N}", _mockLogger.Object);
        var store2 = new InMemoryStateStore<TestEntity>($"store-b-{Guid.NewGuid():N}", _mockLogger.Object);

        // Act - Save via store1
        await store1.SaveAsync("key", new TestEntity { Id = "1", Name = "Store1", Value = 1 });

        // Assert - store2 should not see it
        var result = await store2.GetAsync("key");
        Assert.Null(result);

        // Cleanup
        store1.Clear();
        store2.Clear();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentSaves_AllSucceed()
    {
        // Arrange
        var tasks = Enumerable.Range(1, 100).Select(async i =>
        {
            var entity = new TestEntity { Id = i.ToString(), Name = $"Entity{i}", Value = i };
            await _store.SaveAsync($"key{i}", entity);
        });

        // Act
        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(100, _store.Count);
    }

    [Fact]
    public async Task ConcurrentReads_AllSucceed()
    {
        // Arrange
        for (int i = 1; i <= 10; i++)
        {
            await _store.SaveAsync($"key{i}", new TestEntity { Id = i.ToString(), Name = $"Entity{i}", Value = i });
        }

        // Act - 100 concurrent reads
        var tasks = Enumerable.Range(1, 100).Select(async i =>
        {
            var keyNum = (i % 10) + 1;
            return await _store.GetAsync($"key{keyNum}");
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, r => Assert.NotNull(r));
    }

    #endregion

    #region Set Operation Tests

    [Fact]
    public async Task AddToSetAsync_WithSingleItem_ReturnsTrue()
    {
        // Arrange
        var item = new TestEntity { Id = "1", Name = "Item1", Value = 10 };

        // Act
        var result = await _store.AddToSetAsync("test-set", item);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AddToSetAsync_WithDuplicateItem_ReturnsFalse()
    {
        // Arrange
        var item = new TestEntity { Id = "1", Name = "Item1", Value = 10 };
        await _store.AddToSetAsync("test-set", item);

        // Act - Add same item again
        var result = await _store.AddToSetAsync("test-set", item);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task AddToSetAsync_WithMultipleItems_ReturnsCount()
    {
        // Arrange
        var items = new[]
        {
            new TestEntity { Id = "1", Name = "Item1", Value = 10 },
            new TestEntity { Id = "2", Name = "Item2", Value = 20 },
            new TestEntity { Id = "3", Name = "Item3", Value = 30 }
        };

        // Act - Use AsEnumerable() to ensure bulk overload is selected
        var result = await _store.AddToSetAsync("test-set", items.AsEnumerable());

        // Assert
        Assert.Equal(3, result);
    }

    [Fact]
    public async Task AddToSetAsync_WithPartialDuplicates_ReturnsNewItemCount()
    {
        // Arrange
        var item1 = new TestEntity { Id = "1", Name = "Item1", Value = 10 };
        await _store.AddToSetAsync("test-set", item1);

        var items = new[]
        {
            item1, // Duplicate
            new TestEntity { Id = "2", Name = "Item2", Value = 20 },
            new TestEntity { Id = "3", Name = "Item3", Value = 30 }
        };

        // Act - Use AsEnumerable() to ensure bulk overload is selected
        var result = await _store.AddToSetAsync("test-set", items.AsEnumerable());

        // Assert
        Assert.Equal(2, result); // Only 2 new items
    }

    [Fact]
    public async Task AddToSetAsync_WithTtl_SetsExpiration()
    {
        // Arrange
        var item = new TestEntity { Id = "1", Name = "Item1", Value = 10 };

        // Act
        await _store.AddToSetAsync("expiring-set", item, new StateOptions { Ttl = 1 });

        // Assert - Should exist immediately
        var countBefore = await _store.SetCountAsync("expiring-set");
        Assert.Equal(1, countBefore);

        // Wait for expiration
        await Task.Delay(1100);

        // Should be expired now
        var countAfter = await _store.SetCountAsync("expiring-set");
        Assert.Equal(0, countAfter);
    }

    [Fact]
    public async Task RemoveFromSetAsync_WithExistingItem_ReturnsTrue()
    {
        // Arrange
        var item = new TestEntity { Id = "1", Name = "Item1", Value = 10 };
        await _store.AddToSetAsync("test-set", item);

        // Act
        var result = await _store.RemoveFromSetAsync("test-set", item);

        // Assert
        Assert.True(result);
        Assert.Equal(0, await _store.SetCountAsync("test-set"));
    }

    [Fact]
    public async Task RemoveFromSetAsync_WithNonExistentItem_ReturnsFalse()
    {
        // Arrange
        var item1 = new TestEntity { Id = "1", Name = "Item1", Value = 10 };
        var item2 = new TestEntity { Id = "2", Name = "Item2", Value = 20 };
        await _store.AddToSetAsync("test-set", item1);

        // Act
        var result = await _store.RemoveFromSetAsync("test-set", item2);

        // Assert
        Assert.False(result);
        Assert.Equal(1, await _store.SetCountAsync("test-set"));
    }

    [Fact]
    public async Task RemoveFromSetAsync_WithNonExistentSet_ReturnsFalse()
    {
        // Arrange
        var item = new TestEntity { Id = "1", Name = "Item1", Value = 10 };

        // Act
        var result = await _store.RemoveFromSetAsync("nonexistent-set", item);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetSetAsync_WithExistingSet_ReturnsAllItems()
    {
        // Arrange
        var item1 = new TestEntity { Id = "1", Name = "Item1", Value = 10 };
        var item2 = new TestEntity { Id = "2", Name = "Item2", Value = 20 };
        var item3 = new TestEntity { Id = "3", Name = "Item3", Value = 30 };

        await _store.AddToSetAsync("test-set", new[] { item1, item2, item3 }.AsEnumerable());

        // Act
        var result = await _store.GetSetAsync<TestEntity>("test-set");

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains(result, i => i.Id == "1" && i.Name == "Item1");
        Assert.Contains(result, i => i.Id == "2" && i.Name == "Item2");
        Assert.Contains(result, i => i.Id == "3" && i.Name == "Item3");
    }

    [Fact]
    public async Task GetSetAsync_WithNonExistentSet_ReturnsEmptyList()
    {
        // Act
        var result = await _store.GetSetAsync<TestEntity>("nonexistent-set");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSetAsync_WithExpiredSet_ReturnsEmptyList()
    {
        // Arrange
        var item = new TestEntity { Id = "1", Name = "Item1", Value = 10 };
        await _store.AddToSetAsync("expiring-set", item, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var result = await _store.GetSetAsync<TestEntity>("expiring-set");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task SetContainsAsync_WithExistingItem_ReturnsTrue()
    {
        // Arrange
        var item = new TestEntity { Id = "1", Name = "Item1", Value = 10 };
        await _store.AddToSetAsync("test-set", item);

        // Act
        var result = await _store.SetContainsAsync("test-set", item);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SetContainsAsync_WithNonExistentItem_ReturnsFalse()
    {
        // Arrange
        var item1 = new TestEntity { Id = "1", Name = "Item1", Value = 10 };
        var item2 = new TestEntity { Id = "2", Name = "Item2", Value = 20 };
        await _store.AddToSetAsync("test-set", item1);

        // Act
        var result = await _store.SetContainsAsync("test-set", item2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SetContainsAsync_WithNonExistentSet_ReturnsFalse()
    {
        // Arrange
        var item = new TestEntity { Id = "1", Name = "Item1", Value = 10 };

        // Act
        var result = await _store.SetContainsAsync("nonexistent-set", item);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SetCountAsync_WithExistingSet_ReturnsCount()
    {
        // Arrange
        var items = new[]
        {
            new TestEntity { Id = "1", Name = "Item1", Value = 10 },
            new TestEntity { Id = "2", Name = "Item2", Value = 20 },
            new TestEntity { Id = "3", Name = "Item3", Value = 30 }
        };
        await _store.AddToSetAsync("test-set", items.AsEnumerable());

        // Act
        var result = await _store.SetCountAsync("test-set");

        // Assert
        Assert.Equal(3, result);
    }

    [Fact]
    public async Task SetCountAsync_WithNonExistentSet_ReturnsZero()
    {
        // Act
        var result = await _store.SetCountAsync("nonexistent-set");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task DeleteSetAsync_WithExistingSet_ReturnsTrueAndRemovesSet()
    {
        // Arrange
        var item = new TestEntity { Id = "1", Name = "Item1", Value = 10 };
        await _store.AddToSetAsync("test-set", item);

        // Act
        var result = await _store.DeleteSetAsync("test-set");

        // Assert
        Assert.True(result);
        Assert.Equal(0, await _store.SetCountAsync("test-set"));
    }

    [Fact]
    public async Task DeleteSetAsync_WithNonExistentSet_ReturnsFalse()
    {
        // Act
        var result = await _store.DeleteSetAsync("nonexistent-set");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RefreshSetTtlAsync_WithExistingSet_RefreshesTtl()
    {
        // Arrange
        var item = new TestEntity { Id = "1", Name = "Item1", Value = 10 };
        await _store.AddToSetAsync("test-set", item, new StateOptions { Ttl = 1 });

        // Act - Refresh with longer TTL before expiration
        await Task.Delay(500);
        var result = await _store.RefreshSetTtlAsync("test-set", 3);

        // Assert
        Assert.True(result);

        // Wait past original TTL
        await Task.Delay(700);

        // Should still exist due to refreshed TTL
        Assert.Equal(1, await _store.SetCountAsync("test-set"));
    }

    [Fact]
    public async Task RefreshSetTtlAsync_WithNonExistentSet_ReturnsFalse()
    {
        // Act
        var result = await _store.RefreshSetTtlAsync("nonexistent-set", 60);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SetOperations_ConcurrentAccess_ThreadSafe()
    {
        // Arrange & Act - Concurrent adds and removes
        var tasks = Enumerable.Range(1, 50).Select(async i =>
        {
            var item = new TestEntity { Id = i.ToString(), Name = $"Item{i}", Value = i };
            await _store.AddToSetAsync("concurrent-set", item);

            // Immediately remove half of them
            if (i % 2 == 0)
            {
                await _store.RemoveFromSetAsync("concurrent-set", item);
            }
        });

        await Task.WhenAll(tasks);

        // Assert - Should have 25 items (odd numbers only)
        var count = await _store.SetCountAsync("concurrent-set");
        Assert.Equal(25, count);
    }

    [Fact]
    public async Task SetOperations_MultipleStoresWithSameName_ShareSetData()
    {
        // Arrange
        var sharedStoreName = $"shared-set-store-{Guid.NewGuid():N}";
        var store1 = new InMemoryStateStore<TestEntity>(sharedStoreName, _mockLogger.Object);
        var store2 = new InMemoryStateStore<TestEntity>(sharedStoreName, _mockLogger.Object);

        var item = new TestEntity { Id = "1", Name = "Shared", Value = 42 };

        // Act - Add via store1
        await store1.AddToSetAsync("shared-set", item);

        // Assert - Retrieve via store2
        var result = await store2.GetSetAsync<TestEntity>("shared-set");
        Assert.Single(result);
        Assert.Equal("Shared", result.First().Name);

        // Cleanup
        await store1.DeleteSetAsync("shared-set");
    }

    #endregion
}
