#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Data;
using BeyondImmersion.BannouService.State.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.EntityFrameworkCore;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Unit tests for MySqlStateStore using EF Core InMemory provider.
/// Tests query patterns, ETag handling, and error scenarios.
/// </summary>
public class MySqlStateStoreTests : IDisposable
{
    private readonly Mock<ILogger<MySqlStateStore<TestEntity>>> _mockLogger;
    private readonly DbContextOptions<StateDbContext> _options;
    private readonly string _storeName;
    private readonly MySqlStateStore<TestEntity> _store;

    /// <summary>
    /// Test entity for storage tests.
    /// </summary>
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public string? Category { get; set; }
    }

    public MySqlStateStoreTests()
    {
        _mockLogger = new Mock<ILogger<MySqlStateStore<TestEntity>>>();
        _storeName = $"test-store-{Guid.NewGuid():N}";

        // Use unique database name per test to ensure isolation
        _options = new DbContextOptionsBuilder<StateDbContext>()
            .UseInMemoryDatabase(databaseName: $"TestDb_{Guid.NewGuid():N}")
            .Options;

        _store = new MySqlStateStore<TestEntity>(_options, _storeName, 10000, _mockLogger.Object);
    }

    public void Dispose()
    {
        // Clean up database
        using var context = new StateDbContext(_options);
        context.Database.EnsureDeleted();
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<MySqlStateStore<TestEntity>>();
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
    public async Task GetAsync_WithCorruptedJson_ReturnsNullAndLogsError()
    {
        // Arrange - Insert corrupted JSON directly
        using (var context = new StateDbContext(_options))
        {
            context.StateEntries.Add(new StateEntry
            {
                StoreName = _storeName,
                Key = "corrupt-key",
                ValueJson = "{ invalid json",
                ETag = "test-etag",
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Act
        var result = await _store.GetAsync("corrupt-key");

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("JSON deserialization failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region GetWithETagAsync Tests

    [Fact]
    public async Task GetWithETagAsync_WithExistingKey_ReturnsValueAndETag()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        var savedEtag = await _store.SaveAsync("key1", entity);

        // Act
        var (value, etag) = await _store.GetWithETagAsync("key1");

        // Assert
        Assert.NotNull(value);
        Assert.Equal("1", value.Id);
        Assert.NotNull(etag);
        Assert.Equal(savedEtag, etag);
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

    #endregion

    #region SaveAsync Tests

    [Fact]
    public async Task SaveAsync_WithNewKey_CreatesEntry()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        // Act
        var etag = await _store.SaveAsync("key1", entity);

        // Assert
        Assert.NotNull(etag);
        Assert.True(etag.Length > 0);

        var retrieved = await _store.GetAsync("key1");
        Assert.NotNull(retrieved);
        Assert.Equal("Test", retrieved.Name);
    }

    [Fact]
    public async Task SaveAsync_WithExistingKey_UpdatesEntry()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        var originalEtag = await _store.SaveAsync("key1", entity);

        // Act
        entity.Value = 100;
        var newEtag = await _store.SaveAsync("key1", entity);

        // Assert
        Assert.NotNull(newEtag);
        Assert.NotEqual(originalEtag, newEtag); // ETag should change

        var retrieved = await _store.GetAsync("key1");
        Assert.NotNull(retrieved);
        Assert.Equal(100, retrieved.Value);
    }

    [Fact]
    public async Task SaveAsync_SameContentDifferentKeys_DifferentETags()
    {
        // Arrange - Same content for two different keys
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        // Act
        var etag1 = await _store.SaveAsync("key1", entity);
        var etag2 = await _store.SaveAsync("key2", entity);

        // Assert - ETags should be different because key is included in hash
        Assert.NotEqual(etag1, etag2);
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
        var newEtag = await _store.TrySaveAsync("key1", entity, "wrong-etag");

        // Assert
        Assert.Null(newEtag);
        var retrieved = await _store.GetAsync("key1");
        Assert.NotNull(retrieved);
        Assert.Equal(42, retrieved.Value); // Original value preserved
    }

    [Fact]
    public async Task TrySaveAsync_WithNonExistentKey_ReturnsNull()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        // Act
        var newEtag = await _store.TrySaveAsync("nonexistent", entity, "any-etag");

        // Assert
        Assert.Null(newEtag);
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
    public async Task GetBulkAsync_WithEmptyKeys_ReturnsEmptyDictionary()
    {
        // Act
        var results = await _store.GetBulkAsync(Array.Empty<string>());

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region SaveBulkAsync Tests

    [Fact]
    public async Task SaveBulkAsync_WithMultipleItems_SavesAll()
    {
        // Arrange
        var items = new Dictionary<string, TestEntity>
        {
            ["key1"] = new TestEntity { Id = "1", Name = "One", Value = 1 },
            ["key2"] = new TestEntity { Id = "2", Name = "Two", Value = 2 },
            ["key3"] = new TestEntity { Id = "3", Name = "Three", Value = 3 }
        };

        // Act
        var etags = await _store.SaveBulkAsync(items);

        // Assert
        Assert.Equal(3, etags.Count);
        Assert.True(etags.ContainsKey("key1"));
        Assert.True(etags.ContainsKey("key2"));
        Assert.True(etags.ContainsKey("key3"));

        // Verify all were saved
        var results = await _store.GetBulkAsync(new[] { "key1", "key2", "key3" });
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task SaveBulkAsync_WithEmptyItems_ReturnsEmptyDictionary()
    {
        // Act
        var etags = await _store.SaveBulkAsync(new Dictionary<string, TestEntity>());

        // Assert
        Assert.Empty(etags);
    }

    #endregion

    #region ExistsBulkAsync Tests

    [Fact]
    public async Task ExistsBulkAsync_WithMixedKeys_ReturnsExistingOnly()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "One", Value = 1 });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Three", Value = 3 });

        // Act
        var existing = await _store.ExistsBulkAsync(new[] { "key1", "key2", "key3", "key4" });

        // Assert
        Assert.Equal(2, existing.Count);
        Assert.Contains("key1", existing);
        Assert.Contains("key3", existing);
        Assert.DoesNotContain("key2", existing);
        Assert.DoesNotContain("key4", existing);
    }

    #endregion

    #region DeleteBulkAsync Tests

    // NOTE: DeleteBulkAsync requires MySQL ExecuteDeleteAsync which is not supported by InMemory provider.
    // These tests would require a real MySQL database for integration testing.

    #endregion

    #region QueryAsync Tests

    // NOTE: QueryAsync uses raw SQL queries (FromSqlRaw) which are not supported by InMemory provider.
    // These tests would require a real MySQL database for integration testing.
    // The query functionality is tested via infrastructure tests.

    #endregion

    #region QueryPagedAsync Tests

    // NOTE: QueryPagedAsync uses SqlQueryRaw which is not supported by InMemory provider.
    // These tests would require a real MySQL database for integration testing.
    // The pagination functionality is tested via infrastructure tests.

    #endregion

    #region CountAsync Tests

    [Fact]
    public async Task CountAsync_WithNoFilter_ReturnsTotal()
    {
        // Arrange
        for (int i = 1; i <= 15; i++)
        {
            await _store.SaveAsync($"key{i}", new TestEntity { Id = i.ToString(), Name = $"Entity{i}", Value = i });
        }

        // Act
        var count = await _store.CountAsync();

        // Assert
        Assert.Equal(15, count);
    }

    // NOTE: CountAsync with filter uses SqlQueryRaw which is not supported by InMemory provider.

    [Fact]
    public async Task CountAsync_WithEmptyStore_ReturnsZero()
    {
        // Act
        var count = await _store.CountAsync();

        // Assert
        Assert.Equal(0, count);
    }

    #endregion

    #region Store Isolation Tests

    [Fact]
    public async Task DifferentStoreNames_AreIsolated()
    {
        // Arrange
        var store1 = new MySqlStateStore<TestEntity>(_options, "store1", 10000, _mockLogger.Object);
        var store2 = new MySqlStateStore<TestEntity>(_options, "store2", 10000, _mockLogger.Object);

        await store1.SaveAsync("key1", new TestEntity { Id = "1", Name = "Store1Data", Value = 1 });
        await store2.SaveAsync("key1", new TestEntity { Id = "2", Name = "Store2Data", Value = 2 });

        // Act
        var result1 = await store1.GetAsync("key1");
        var result2 = await store2.GetAsync("key1");

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal("Store1Data", result1.Name);
        Assert.Equal("Store2Data", result2.Name);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task ConcurrentSaves_AllSucceed()
    {
        // Arrange & Act
        var tasks = Enumerable.Range(1, 50).Select(async i =>
        {
            var entity = new TestEntity { Id = i.ToString(), Name = $"Entity{i}", Value = i };
            await _store.SaveAsync($"key{i}", entity);
        });

        await Task.WhenAll(tasks);

        // Assert
        var count = await _store.CountAsync();
        Assert.Equal(50, count);
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
