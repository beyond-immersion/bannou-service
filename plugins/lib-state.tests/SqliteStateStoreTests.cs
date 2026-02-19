#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Data;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.EntityFrameworkCore;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Unit tests for SqliteStateStore using a real SQLite database (temp file per test).
/// Unlike MySqlStateStoreTests (limited by EF Core InMemory provider), SQLite supports
/// all SQL operations including FromSqlRaw, ExecuteDeleteAsync, json_extract(),
/// enabling comprehensive unit testing of every interface method.
/// </summary>
public class SqliteStateStoreTests : IDisposable
{
    private readonly Mock<ILogger<SqliteStateStore<TestEntity>>> _mockLogger;
    private readonly DbContextOptions<StateDbContext> _options;
    private readonly string _storeName;
    private readonly SqliteStateStore<TestEntity> _store;
    private readonly string _dbPath;

    /// <summary>
    /// Test entity for storage tests.
    /// </summary>
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
        public string? Category { get; set; }
        public List<string> Tags { get; set; } = new();
    }

    public SqliteStateStoreTests()
    {
        _mockLogger = new Mock<ILogger<SqliteStateStore<TestEntity>>>();
        _storeName = $"test-store-{Guid.NewGuid():N}";

        // Use a temp file for each test to ensure isolation
        _dbPath = Path.Combine(Path.GetTempPath(), $"bannou_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath};Mode=ReadWriteCreate;Cache=Shared;";

        _options = new DbContextOptionsBuilder<StateDbContext>()
            .UseSqlite(connectionString)
            .Options;

        // Create schema
        using var context = new StateDbContext(_options);
        context.Database.EnsureCreated();

        _store = new SqliteStateStore<TestEntity>(_options, _storeName, 10000, _mockLogger.Object);
    }

    public void Dispose()
    {
        // Clean up database file
        try
        {
            using var context = new StateDbContext(_options);
            context.Database.EnsureDeleted();
        }
        catch
        {
            // Best effort cleanup
        }

        // Delete the temp file
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }

            // SQLite may also create -wal and -shm files
            var walPath = _dbPath + "-wal";
            var shmPath = _dbPath + "-shm";
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    /// <summary>
    /// Helper to create a simple equality condition.
    /// </summary>
    private static QueryCondition Eq(string path, object value) => new()
    {
        Path = path,
        Operator = QueryOperator.Equals,
        Value = value
    };

    /// <summary>
    /// Helper to create a greater-than condition.
    /// </summary>
    private static QueryCondition Gt(string path, object value) => new()
    {
        Path = path,
        Operator = QueryOperator.GreaterThan,
        Value = value
    };

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        // SqliteStateStore is created by StateStoreFactory, not DI-registered directly.
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
        Assert.NotEqual(originalEtag, newEtag);

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

    [Fact]
    public async Task SaveAsync_WithTtl_ThrowsInvalidOperationException()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        var options = new StateOptions { Ttl = 300 };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.SaveAsync("key1", entity, options));
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
        Assert.Equal(42, retrieved.Value);
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
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Test", Value = 42 });

        // Act
        var deleted = await _store.DeleteAsync("key1");

        // Assert
        Assert.True(deleted);
        var result = await _store.GetAsync("key1");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Act
        var deleted = await _store.DeleteAsync("nonexistent");

        // Assert
        Assert.False(deleted);
    }

    #endregion

    #region ExistsAsync Tests

    [Fact]
    public async Task ExistsAsync_WithExistingKey_ReturnsTrue()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Test", Value = 42 });

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

    [Fact]
    public async Task DeleteBulkAsync_WithExistingKeys_DeletesAllAndReturnsCount()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "One", Value = 1 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Two", Value = 2 });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Three", Value = 3 });

        // Act
        var deleted = await _store.DeleteBulkAsync(new[] { "key1", "key2" });

        // Assert
        Assert.Equal(2, deleted);
        Assert.Null(await _store.GetAsync("key1"));
        Assert.Null(await _store.GetAsync("key2"));
        Assert.NotNull(await _store.GetAsync("key3"));
    }

    [Fact]
    public async Task DeleteBulkAsync_WithMixedKeys_ReturnsDeletedCount()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "One", Value = 1 });

        // Act
        var deleted = await _store.DeleteBulkAsync(new[] { "key1", "key2", "key3" });

        // Assert
        Assert.Equal(1, deleted);
    }

    [Fact]
    public async Task DeleteBulkAsync_WithEmptyKeys_ReturnsZero()
    {
        // Act
        var deleted = await _store.DeleteBulkAsync(Array.Empty<string>());

        // Assert
        Assert.Equal(0, deleted);
    }

    #endregion

    #region QueryAsync Tests

    [Fact]
    public async Task QueryAsync_WithMatchingPredicate_ReturnsFilteredResults()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Value = 10, Category = "A" });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Value = 20, Category = "B" });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Gamma", Value = 30, Category = "A" });

        // Act
        var results = await _store.QueryAsync(e => e.Category == "A");

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "Alpha");
        Assert.Contains(results, e => e.Name == "Gamma");
    }

    [Fact]
    public async Task QueryAsync_WithNoMatches_ReturnsEmptyList()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Value = 10 });

        // Act
        var results = await _store.QueryAsync(e => e.Category == "NonExistent");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_WithNumericComparison_ReturnsFilteredResults()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Value = 10 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Value = 20 });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Gamma", Value = 30 });

        // Act
        var results = await _store.QueryAsync(e => e.Value > 15);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, e => e.Name == "Beta");
        Assert.Contains(results, e => e.Name == "Gamma");
    }

    #endregion

    #region QueryPagedAsync Tests

    [Fact]
    public async Task QueryPagedAsync_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        for (int i = 1; i <= 10; i++)
        {
            await _store.SaveAsync($"key{i}", new TestEntity
            {
                Id = i.ToString(),
                Name = $"Entity{i}",
                Value = i,
                Category = "Test"
            });
        }

        // Act - Get page 1 (second page), page size 3
        var result = await _store.QueryPagedAsync(
            predicate: null,
            page: 1,
            pageSize: 3,
            orderBy: e => (object)e.Value);

        // Assert
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(10, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(3, result.PageSize);
        Assert.True(result.HasMore);
        Assert.Equal(4, result.TotalPages);
    }

    [Fact]
    public async Task QueryPagedAsync_WithPredicate_FiltersBeforePaging()
    {
        // Arrange
        for (int i = 1; i <= 10; i++)
        {
            await _store.SaveAsync($"key{i}", new TestEntity
            {
                Id = i.ToString(),
                Name = $"Entity{i}",
                Value = i,
                Category = i % 2 == 0 ? "Even" : "Odd"
            });
        }

        // Act - Get all even entries on first page
        var result = await _store.QueryPagedAsync(
            predicate: e => e.Category == "Even",
            page: 0,
            pageSize: 10);

        // Assert
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal("Even", item.Category));
    }

    #endregion

    #region CountAsync Tests

    [Fact]
    public async Task CountAsync_WithNoPredicate_ReturnsTotal()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "One", Value = 1 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Two", Value = 2 });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Three", Value = 3 });

        // Act
        var count = await _store.CountAsync();

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task CountAsync_WithPredicate_ReturnsFilteredCount()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "One", Value = 1, Category = "A" });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Two", Value = 2, Category = "B" });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Three", Value = 3, Category = "A" });

        // Act
        var count = await _store.CountAsync(e => e.Category == "A");

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CountAsync_WithEmptyStore_ReturnsZero()
    {
        // Act
        var count = await _store.CountAsync();

        // Assert
        Assert.Equal(0, count);
    }

    #endregion

    #region JsonQueryAsync Tests

    [Fact]
    public async Task JsonQueryAsync_WithStringEquality_ReturnsMatches()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Value = 10, Category = "A" });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Value = 20, Category = "B" });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Gamma", Value = 30, Category = "A" });

        // Act
        var conditions = new List<QueryCondition> { Eq("Category", "A") };
        var results = await _store.JsonQueryAsync(conditions);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Value.Name == "Alpha");
        Assert.Contains(results, r => r.Value.Name == "Gamma");
        Assert.All(results, r => Assert.NotNull(r.Key));
    }

    [Fact]
    public async Task JsonQueryAsync_WithNumericComparison_ReturnsMatches()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Value = 10 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Value = 20 });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Gamma", Value = 30 });

        // Act
        var conditions = new List<QueryCondition> { Gt("Value", 15) };
        var results = await _store.JsonQueryAsync(conditions);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Value.Name == "Beta");
        Assert.Contains(results, r => r.Value.Name == "Gamma");
    }

    [Fact]
    public async Task JsonQueryAsync_WithEmptyConditions_ReturnsAll()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Value = 10 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Value = 20 });

        // Act
        var results = await _store.JsonQueryAsync(new List<QueryCondition>());

        // Assert
        Assert.Equal(2, results.Count);
    }

    #endregion

    #region JsonQueryPagedAsync Tests

    [Fact]
    public async Task JsonQueryPagedAsync_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        for (int i = 1; i <= 10; i++)
        {
            await _store.SaveAsync($"key{i}", new TestEntity
            {
                Id = i.ToString(),
                Name = $"Entity{i}",
                Value = i,
                Category = "Test"
            });
        }

        // Act
        var result = await _store.JsonQueryPagedAsync(
            conditions: null,
            offset: 0,
            limit: 5,
            sortBy: new JsonSortSpec { Path = "Value" });

        // Assert
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(10, result.TotalCount);
        Assert.True(result.HasMore);
    }

    [Fact]
    public async Task JsonQueryPagedAsync_WithConditions_FiltersBeforePaging()
    {
        // Arrange
        for (int i = 1; i <= 10; i++)
        {
            await _store.SaveAsync($"key{i}", new TestEntity
            {
                Id = i.ToString(),
                Name = $"Entity{i}",
                Value = i,
                Category = i % 2 == 0 ? "Even" : "Odd"
            });
        }

        // Act
        var conditions = new List<QueryCondition> { Eq("Category", "Even") };
        var result = await _store.JsonQueryPagedAsync(conditions, offset: 0, limit: 10);

        // Assert
        Assert.Equal(5, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
    }

    #endregion

    #region JsonCountAsync Tests

    [Fact]
    public async Task JsonCountAsync_WithConditions_ReturnsFilteredCount()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Category = "A" });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Category = "B" });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Gamma", Category = "A" });

        // Act
        var count = await _store.JsonCountAsync(new List<QueryCondition> { Eq("Category", "A") });

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task JsonCountAsync_WithEmptyConditions_ReturnsTotal()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha" });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta" });

        // Act
        var count = await _store.JsonCountAsync(null);

        // Assert
        Assert.Equal(2, count);
    }

    #endregion

    #region JsonDistinctAsync Tests

    [Fact]
    public async Task JsonDistinctAsync_ReturnsUniqueValues()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Category = "A" });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Category = "B" });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Gamma", Category = "A" });
        await _store.SaveAsync("key4", new TestEntity { Id = "4", Name = "Delta", Category = "C" });

        // Act
        var distinct = await _store.JsonDistinctAsync("Category");

        // Assert
        Assert.Equal(3, distinct.Count);
        Assert.Contains("A", distinct.Select(v => v?.ToString()));
        Assert.Contains("B", distinct.Select(v => v?.ToString()));
        Assert.Contains("C", distinct.Select(v => v?.ToString()));
    }

    [Fact]
    public async Task JsonDistinctAsync_WithConditions_FiltersBeforeDistinct()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Value = 10, Category = "A" });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Value = 20, Category = "B" });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Gamma", Value = 30, Category = "A" });

        // Act
        var conditions = new List<QueryCondition> { Gt("Value", 15) };
        var distinct = await _store.JsonDistinctAsync("Category", conditions);

        // Assert
        Assert.Equal(2, distinct.Count);
        Assert.Contains("B", distinct.Select(v => v?.ToString()));
        Assert.Contains("A", distinct.Select(v => v?.ToString()));
    }

    #endregion

    #region JsonAggregateAsync Tests

    [Fact]
    public async Task JsonAggregateAsync_Sum_ReturnsSumOfValues()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Value = 10 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Value = 20 });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Gamma", Value = 30 });

        // Act
        var sum = await _store.JsonAggregateAsync("Value", JsonAggregation.Sum);

        // Assert
        Assert.NotNull(sum);
        Assert.Equal(60.0, Convert.ToDouble(sum));
    }

    [Fact]
    public async Task JsonAggregateAsync_Count_ReturnsCount()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Value = 10 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Value = 20 });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Gamma", Value = 30 });

        // Act
        var count = await _store.JsonAggregateAsync("Value", JsonAggregation.Count);

        // Assert
        Assert.NotNull(count);
        Assert.Equal(3, Convert.ToInt32(count));
    }

    [Fact]
    public async Task JsonAggregateAsync_Avg_ReturnsAverage()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Value = 10 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Value = 20 });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Gamma", Value = 30 });

        // Act
        var avg = await _store.JsonAggregateAsync("Value", JsonAggregation.Avg);

        // Assert
        Assert.NotNull(avg);
        Assert.Equal(20.0, Convert.ToDouble(avg));
    }

    [Fact]
    public async Task JsonAggregateAsync_Min_ReturnsMinimum()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Value = 10 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Value = 20 });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Gamma", Value = 30 });

        // Act
        var min = await _store.JsonAggregateAsync("Value", JsonAggregation.Min);

        // Assert
        Assert.NotNull(min);
        Assert.Equal(10.0, Convert.ToDouble(min));
    }

    [Fact]
    public async Task JsonAggregateAsync_Max_ReturnsMaximum()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Value = 10 });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Value = 20 });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Gamma", Value = 30 });

        // Act
        var max = await _store.JsonAggregateAsync("Value", JsonAggregation.Max);

        // Assert
        Assert.NotNull(max);
        Assert.Equal(30.0, Convert.ToDouble(max));
    }

    [Fact]
    public async Task JsonAggregateAsync_WithConditions_FiltersBeforeAggregation()
    {
        // Arrange
        await _store.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Value = 10, Category = "A" });
        await _store.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Value = 20, Category = "B" });
        await _store.SaveAsync("key3", new TestEntity { Id = "3", Name = "Gamma", Value = 30, Category = "A" });

        // Act
        var conditions = new List<QueryCondition> { Eq("Category", "A") };
        var sum = await _store.JsonAggregateAsync("Value", JsonAggregation.Sum, conditions);

        // Assert
        Assert.NotNull(sum);
        Assert.Equal(40.0, Convert.ToDouble(sum));
    }

    #endregion

    #region Store Isolation Tests

    [Fact]
    public async Task DifferentStoreNames_AreIsolated()
    {
        // Arrange
        var store1 = new SqliteStateStore<TestEntity>(_options, "store1", 10000, _mockLogger.Object);
        var store2 = new SqliteStateStore<TestEntity>(_options, "store2", 10000, _mockLogger.Object);

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

    [Fact]
    public async Task DifferentStoreNames_QueryIsolation()
    {
        // Arrange
        var store1 = new SqliteStateStore<TestEntity>(_options, "store1", 10000, _mockLogger.Object);
        var store2 = new SqliteStateStore<TestEntity>(_options, "store2", 10000, _mockLogger.Object);

        await store1.SaveAsync("key1", new TestEntity { Id = "1", Name = "Alpha", Category = "A" });
        await store2.SaveAsync("key2", new TestEntity { Id = "2", Name = "Beta", Category = "A" });

        // Act
        var results1 = await store1.QueryAsync(e => e.Category == "A");
        var results2 = await store2.QueryAsync(e => e.Category == "A");

        // Assert
        Assert.Single(results1);
        Assert.Equal("Alpha", results1[0].Name);
        Assert.Single(results2);
        Assert.Equal("Beta", results2[0].Name);
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
        var keys = Enumerable.Range(1, 50).Select(i => $"key{i}").ToArray();
        var results = await _store.GetBulkAsync(keys);
        Assert.Equal(50, results.Count);
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
