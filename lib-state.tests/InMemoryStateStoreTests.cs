using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Services;

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
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var store = new InMemoryStateStore<TestEntity>("test", _mockLogger.Object);

        // Assert
        Assert.NotNull(store);
    }

    [Fact]
    public void Constructor_WithNullStoreName_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryStateStore<TestEntity>(null!, _mockLogger.Object));
        Assert.Equal("storeName", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new InMemoryStateStore<TestEntity>("test", null!));
        Assert.Equal("logger", ex.ParamName);
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
    public async Task GetAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.GetAsync(null!));
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
    public async Task SaveAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.SaveAsync(null!, entity));
    }

    [Fact]
    public async Task SaveAsync_WithNullValue_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.SaveAsync("key1", null!));
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
        var success = await _store.TrySaveAsync("key1", entity, etag);

        // Assert
        Assert.True(success);
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
        var success = await _store.TrySaveAsync("key1", entity, "999"); // Wrong etag

        // Assert
        Assert.False(success);
        var retrieved = await _store.GetAsync("key1");
        Assert.NotNull(retrieved);
        Assert.Equal(42, retrieved.Value); // Original value preserved
    }

    [Fact]
    public async Task TrySaveAsync_WithInvalidETagFormat_ReturnsFalse()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        await _store.SaveAsync("key1", entity);

        // Act
        var success = await _store.TrySaveAsync("key1", entity, "not-a-number");

        // Assert
        Assert.False(success);
    }

    [Fact]
    public async Task TrySaveAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        // Act
        var success = await _store.TrySaveAsync("nonexistent", entity, "1");

        // Assert
        Assert.False(success);
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

        // Assert - Only one should succeed
        Assert.Equal(1, results.Count(r => r == true));
        Assert.Equal(9, results.Count(r => r == false));
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

    [Fact]
    public async Task DeleteAsync_WithNullKey_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.DeleteAsync(null!));
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
}
