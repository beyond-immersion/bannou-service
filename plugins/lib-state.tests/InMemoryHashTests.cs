using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Services;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Unit tests for InMemoryStateStore hash operations.
/// Tests the ICacheableStateStore hash implementation for in-memory backend.
/// </summary>
public class InMemoryHashTests : IDisposable
{
    private readonly Mock<ILogger<InMemoryStateStore<TestEntity>>> _mockLogger;
    private readonly string _storeName;
    private readonly InMemoryStateStore<TestEntity> _store;

    /// <summary>
    /// Test entity for state store tests.
    /// </summary>
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public InMemoryHashTests()
    {
        _mockLogger = new Mock<ILogger<InMemoryStateStore<TestEntity>>>();
        _storeName = $"test-hash-store-{Guid.NewGuid():N}";
        _store = new InMemoryStateStore<TestEntity>(_storeName, _mockLogger.Object);
    }

    public void Dispose()
    {
        _store.Clear();
    }

    #region HashSetAsync Tests

    [Fact]
    public async Task HashSetAsync_WithNewField_ReturnsTrueAndSetsValue()
    {
        // Act
        var result = await _store.HashSetAsync("user:123", "name", "Alice");

        // Assert
        Assert.True(result);
        var value = await _store.HashGetAsync<string>("user:123", "name");
        Assert.Equal("Alice", value);
    }

    [Fact]
    public async Task HashSetAsync_WithExistingField_ReturnsFalseAndUpdatesValue()
    {
        // Arrange
        await _store.HashSetAsync("user:123", "name", "Alice");

        // Act
        var result = await _store.HashSetAsync("user:123", "name", "Bob");

        // Assert
        Assert.False(result); // Field already existed
        var value = await _store.HashGetAsync<string>("user:123", "name");
        Assert.Equal("Bob", value);
    }

    [Fact]
    public async Task HashSetAsync_WithDifferentTypes_Works()
    {
        // Act
        await _store.HashSetAsync("user:123", "name", "Alice");
        await _store.HashSetAsync("user:123", "age", 30);
        await _store.HashSetAsync("user:123", "score", 99.5);
        await _store.HashSetAsync("user:123", "active", true);

        // Assert
        Assert.Equal("Alice", await _store.HashGetAsync<string>("user:123", "name"));
        Assert.Equal(30, await _store.HashGetAsync<int>("user:123", "age"));
        Assert.Equal(99.5, await _store.HashGetAsync<double>("user:123", "score"));
        Assert.True(await _store.HashGetAsync<bool>("user:123", "active"));
    }

    [Fact]
    public async Task HashSetAsync_WithTtl_SetsExpiration()
    {
        // Arrange
        await _store.HashSetAsync("expiring:hash", "field", "value", new StateOptions { Ttl = 1 });

        // Assert - Should exist immediately
        var valueBefore = await _store.HashGetAsync<string>("expiring:hash", "field");
        Assert.Equal("value", valueBefore);

        // Wait for expiration
        await Task.Delay(1100);

        // Should be expired now
        var valueAfter = await _store.HashGetAsync<string>("expiring:hash", "field");
        Assert.Null(valueAfter);
    }

    [Fact]
    public async Task HashSetAsync_WithComplexObject_SerializesAndDeserializes()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        // Act
        await _store.HashSetAsync("objects", "entity1", entity);

        // Assert
        var result = await _store.HashGetAsync<TestEntity>("objects", "entity1");
        Assert.NotNull(result);
        Assert.Equal("1", result.Id);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Value);
    }

    #endregion

    #region HashSetManyAsync Tests

    [Fact]
    public async Task HashSetManyAsync_WithMultipleFields_SetsAll()
    {
        // Arrange
        var fields = new Dictionary<string, string>
        {
            ["name"] = "Alice",
            ["email"] = "alice@example.com",
            ["role"] = "admin"
        };

        // Act
        await _store.HashSetManyAsync("user:123", fields);

        // Assert
        Assert.Equal("Alice", await _store.HashGetAsync<string>("user:123", "name"));
        Assert.Equal("alice@example.com", await _store.HashGetAsync<string>("user:123", "email"));
        Assert.Equal("admin", await _store.HashGetAsync<string>("user:123", "role"));
    }

    [Fact]
    public async Task HashSetManyAsync_WithExistingFields_UpdatesAll()
    {
        // Arrange
        await _store.HashSetAsync("user:123", "name", "Alice");
        await _store.HashSetAsync("user:123", "email", "old@example.com");

        var updates = new Dictionary<string, string>
        {
            ["name"] = "Bob",
            ["email"] = "new@example.com"
        };

        // Act
        await _store.HashSetManyAsync("user:123", updates);

        // Assert
        Assert.Equal("Bob", await _store.HashGetAsync<string>("user:123", "name"));
        Assert.Equal("new@example.com", await _store.HashGetAsync<string>("user:123", "email"));
    }

    [Fact]
    public async Task HashSetManyAsync_WithEmpty_DoesNothing()
    {
        // Act
        await _store.HashSetManyAsync("user:123", new Dictionary<string, string>());

        // Assert
        var count = await _store.HashCountAsync("user:123");
        Assert.Equal(0, count);
    }

    #endregion

    #region HashGetAsync Tests

    [Fact]
    public async Task HashGetAsync_WithExistingField_ReturnsValue()
    {
        // Arrange
        await _store.HashSetAsync("user:123", "name", "Alice");

        // Act
        var result = await _store.HashGetAsync<string>("user:123", "name");

        // Assert
        Assert.Equal("Alice", result);
    }

    [Fact]
    public async Task HashGetAsync_WithNonExistentField_ReturnsDefault()
    {
        // Arrange
        await _store.HashSetAsync("user:123", "name", "Alice");

        // Act
        var result = await _store.HashGetAsync<string>("user:123", "nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task HashGetAsync_WithNonExistentHash_ReturnsDefault()
    {
        // Act
        var result = await _store.HashGetAsync<string>("nonexistent", "field");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task HashGetAsync_WithWrongType_ReturnsDefaultAndLogsError()
    {
        // Arrange - Store a string
        await _store.HashSetAsync("user:123", "data", "not-an-int");

        // Act - Try to read as int returns default instead of throwing
        // (IMPLEMENTATION TENETS: Deserialize failures log error and return default)
        var result = await _store.HashGetAsync<int>("user:123", "data");

        // Assert
        Assert.Equal(default, result);
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

    #region HashGetAllAsync Tests

    [Fact]
    public async Task HashGetAllAsync_WithExistingHash_ReturnsAllFields()
    {
        // Arrange
        await _store.HashSetAsync("user:123", "name", "Alice");
        await _store.HashSetAsync("user:123", "email", "alice@example.com");
        await _store.HashSetAsync("user:123", "role", "admin");

        // Act
        var result = await _store.HashGetAllAsync<string>("user:123");

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("Alice", result["name"]);
        Assert.Equal("alice@example.com", result["email"]);
        Assert.Equal("admin", result["role"]);
    }

    [Fact]
    public async Task HashGetAllAsync_WithNonExistentHash_ReturnsEmpty()
    {
        // Act
        var result = await _store.HashGetAllAsync<string>("nonexistent");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task HashGetAllAsync_WithExpiredHash_ReturnsEmpty()
    {
        // Arrange
        await _store.HashSetAsync("expiring:hash", "field", "value", new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var result = await _store.HashGetAllAsync<string>("expiring:hash");

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region HashDeleteAsync Tests

    [Fact]
    public async Task HashDeleteAsync_WithExistingField_ReturnsTrueAndDeletes()
    {
        // Arrange
        await _store.HashSetAsync("user:123", "name", "Alice");
        await _store.HashSetAsync("user:123", "email", "alice@example.com");

        // Act
        var result = await _store.HashDeleteAsync("user:123", "name");

        // Assert
        Assert.True(result);
        Assert.Null(await _store.HashGetAsync<string>("user:123", "name"));
        Assert.Equal("alice@example.com", await _store.HashGetAsync<string>("user:123", "email"));
    }

    [Fact]
    public async Task HashDeleteAsync_WithNonExistentField_ReturnsFalse()
    {
        // Arrange
        await _store.HashSetAsync("user:123", "name", "Alice");

        // Act
        var result = await _store.HashDeleteAsync("user:123", "nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HashDeleteAsync_WithNonExistentHash_ReturnsFalse()
    {
        // Act
        var result = await _store.HashDeleteAsync("nonexistent", "field");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region HashExistsAsync Tests

    [Fact]
    public async Task HashExistsAsync_WithExistingField_ReturnsTrue()
    {
        // Arrange
        await _store.HashSetAsync("user:123", "name", "Alice");

        // Act
        var result = await _store.HashExistsAsync("user:123", "name");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HashExistsAsync_WithNonExistentField_ReturnsFalse()
    {
        // Arrange
        await _store.HashSetAsync("user:123", "name", "Alice");

        // Act
        var result = await _store.HashExistsAsync("user:123", "nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HashExistsAsync_WithNonExistentHash_ReturnsFalse()
    {
        // Act
        var result = await _store.HashExistsAsync("nonexistent", "field");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region HashIncrementAsync Tests

    [Fact]
    public async Task HashIncrementAsync_WithNewField_CreatesWithValue()
    {
        // Act
        var result = await _store.HashIncrementAsync("counters", "views", 1);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task HashIncrementAsync_WithExistingField_Increments()
    {
        // Arrange
        await _store.HashIncrementAsync("counters", "views", 10);

        // Act
        var result = await _store.HashIncrementAsync("counters", "views", 5);

        // Assert
        Assert.Equal(15, result);
    }

    [Fact]
    public async Task HashIncrementAsync_WithNegativeIncrement_Decrements()
    {
        // Arrange
        await _store.HashIncrementAsync("counters", "views", 100);

        // Act
        var result = await _store.HashIncrementAsync("counters", "views", -30);

        // Assert
        Assert.Equal(70, result);
    }

    [Fact]
    public async Task HashIncrementAsync_WithDefaultIncrement_IncrementsByOne()
    {
        // Act
        var result1 = await _store.HashIncrementAsync("counters", "views");
        var result2 = await _store.HashIncrementAsync("counters", "views");
        var result3 = await _store.HashIncrementAsync("counters", "views");

        // Assert
        Assert.Equal(1, result1);
        Assert.Equal(2, result2);
        Assert.Equal(3, result3);
    }

    [Fact]
    public async Task HashIncrementAsync_WithNonNumericField_DefaultsToZeroAndLogsError()
    {
        // Arrange - Store a non-numeric value
        await _store.HashSetAsync("counters", "name", "Alice");

        // Act - Increment on non-numeric field defaults to 0 and increments from there
        // (IMPLEMENTATION TENETS: Deserialize failures log error and default to 0)
        var result = await _store.HashIncrementAsync("counters", "name", 5);

        // Assert
        Assert.Equal(5, result); // 0 + 5 = 5
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

    #region HashCountAsync Tests

    [Fact]
    public async Task HashCountAsync_WithExistingHash_ReturnsFieldCount()
    {
        // Arrange
        await _store.HashSetAsync("user:123", "name", "Alice");
        await _store.HashSetAsync("user:123", "email", "alice@example.com");
        await _store.HashSetAsync("user:123", "role", "admin");

        // Act
        var count = await _store.HashCountAsync("user:123");

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task HashCountAsync_WithNonExistentHash_ReturnsZero()
    {
        // Act
        var count = await _store.HashCountAsync("nonexistent");

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task HashCountAsync_WithExpiredHash_ReturnsZero()
    {
        // Arrange
        await _store.HashSetAsync("expiring:hash", "field", "value", new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var count = await _store.HashCountAsync("expiring:hash");

        // Assert
        Assert.Equal(0, count);
    }

    #endregion

    #region DeleteHashAsync Tests

    [Fact]
    public async Task DeleteHashAsync_WithExistingHash_ReturnsTrueAndDeletesAll()
    {
        // Arrange
        await _store.HashSetAsync("user:123", "name", "Alice");
        await _store.HashSetAsync("user:123", "email", "alice@example.com");

        // Act
        var result = await _store.DeleteHashAsync("user:123");

        // Assert
        Assert.True(result);
        Assert.Equal(0, await _store.HashCountAsync("user:123"));
    }

    [Fact]
    public async Task DeleteHashAsync_WithNonExistentHash_ReturnsFalse()
    {
        // Act
        var result = await _store.DeleteHashAsync("nonexistent");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region RefreshHashTtlAsync Tests

    [Fact]
    public async Task RefreshHashTtlAsync_WithExistingHash_RefreshesTtl()
    {
        // Arrange
        await _store.HashSetAsync("user:123", "name", "Alice", new StateOptions { Ttl = 1 });

        // Act - Refresh with longer TTL before expiration
        await Task.Delay(500);
        var result = await _store.RefreshHashTtlAsync("user:123", 3);

        // Assert
        Assert.True(result);

        // Wait past original TTL
        await Task.Delay(700);

        // Should still exist due to refreshed TTL
        var value = await _store.HashGetAsync<string>("user:123", "name");
        Assert.Equal("Alice", value);
    }

    [Fact]
    public async Task RefreshHashTtlAsync_WithNonExistentHash_ReturnsFalse()
    {
        // Act
        var result = await _store.RefreshHashTtlAsync("nonexistent", 60);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task HashOperations_ConcurrentSets_AllSucceed()
    {
        // Arrange & Act
        var tasks = Enumerable.Range(1, 100).Select(i =>
            _store.HashSetAsync("concurrent:hash", $"field{i}", $"value{i}"));

        await Task.WhenAll(tasks);

        // Assert
        var count = await _store.HashCountAsync("concurrent:hash");
        Assert.Equal(100, count);
    }

    [Fact]
    public async Task HashIncrementAsync_ConcurrentIncrements_AllApply()
    {
        // Arrange & Act - 100 concurrent increments
        var tasks = Enumerable.Range(1, 100).Select(_ =>
            _store.HashIncrementAsync("concurrent:hash", "counter", 1));

        await Task.WhenAll(tasks);

        // Assert
        var value = await _store.HashGetAsync<long>("concurrent:hash", "counter");
        Assert.Equal(100, value);
    }

    [Fact]
    public async Task HashOperations_MultipleStoresWithSameName_ShareData()
    {
        // Arrange
        var sharedStoreName = $"shared-hash-{Guid.NewGuid():N}";
        var store1 = new InMemoryStateStore<TestEntity>(sharedStoreName, _mockLogger.Object);
        var store2 = new InMemoryStateStore<TestEntity>(sharedStoreName, _mockLogger.Object);

        // Act - Set via store1
        await store1.HashSetAsync("user:123", "name", "Alice");

        // Assert - Read via store2
        var value = await store2.HashGetAsync<string>("user:123", "name");
        Assert.Equal("Alice", value);

        // Cleanup
        await store1.DeleteHashAsync("user:123");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task HashSetAsync_WithEmptyStringField_Works()
    {
        // Act
        await _store.HashSetAsync("user:123", "", "empty-field-name");

        // Assert
        var value = await _store.HashGetAsync<string>("user:123", "");
        Assert.Equal("empty-field-name", value);
    }

    [Fact]
    public async Task HashSetAsync_WithNullValue_StoresNull()
    {
        // Act
        await _store.HashSetAsync<string?>("user:123", "nullable", null);

        // Assert
        var value = await _store.HashGetAsync<string?>("user:123", "nullable");
        Assert.Null(value);
    }

    [Fact]
    public async Task HashOperations_WithSpecialCharacters_Works()
    {
        // Arrange
        var specialKey = "user:123:with:colons";
        var specialField = "field.with.dots";
        var specialValue = "value with spaces\nand\nnewlines";

        // Act
        await _store.HashSetAsync(specialKey, specialField, specialValue);

        // Assert
        var value = await _store.HashGetAsync<string>(specialKey, specialField);
        Assert.Equal(specialValue, value);
    }

    #endregion
}
