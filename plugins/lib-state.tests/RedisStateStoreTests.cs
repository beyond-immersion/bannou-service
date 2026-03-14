#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.State.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Moq;
using StackExchange.Redis;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Unit tests for RedisStateStore using mocked IDatabase.
/// Tests core CRUD operations, key prefixing, JSON serialization, and error handling.
/// </summary>
/// <remarks>
/// These tests verify the RedisStateStore logic without requiring a live Redis connection.
/// Full integration testing is performed in infrastructure-tests.
/// </remarks>
public class RedisStateStoreTests
{
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<ILogger<RedisStateStore<TestEntity>>> _mockLogger;
    private readonly RedisStateStore<TestEntity> _store;
    private const string KeyPrefix = "test-store";

    /// <summary>
    /// Test entity for storage tests.
    /// </summary>
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public RedisStateStoreTests()
    {
        _mockDatabase = new Mock<IDatabase>();
        _mockLogger = new Mock<ILogger<RedisStateStore<TestEntity>>>();

        _store = new RedisStateStore<TestEntity>(
            _mockDatabase.Object,
            KeyPrefix,
            defaultTtl: null,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        // Note: RedisStateStore is not a DI-registered service - it's created by StateStoreFactory.
        // The factory controls constructor args including the optional error publisher callback.
        Assert.NotNull(_store);
    }

    #endregion

    #region Key Prefixing Tests

    [Fact]
    public async Task GetAsync_UsesCorrectKeyPrefix()
    {
        // Arrange
        string? capturedKey = null;
        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, CommandFlags>((key, flags) => capturedKey = key.ToString())
            .ReturnsAsync(RedisValue.Null);

        // Act
        await _store.GetAsync("mykey", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"{KeyPrefix}:mykey", capturedKey);
    }

    [Fact]
    public async Task DeleteAsync_UsesCorrectKeyPrefixes()
    {
        // Arrange
        var capturedKeys = new List<string>();
        _mockDatabase
            .Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, CommandFlags>((key, flags) => capturedKeys.Add(key.ToString()))
            .ReturnsAsync(true);

        // Act
        await _store.DeleteAsync("mykey", cancellationToken: TestContext.Current.CancellationToken);

        // Assert - should delete both value and meta keys
        Assert.Contains($"{KeyPrefix}:mykey", capturedKeys);
        Assert.Contains($"{KeyPrefix}:mykey:meta", capturedKeys);
    }

    [Fact]
    public async Task ExistsAsync_UsesCorrectKeyPrefix()
    {
        // Arrange
        string? capturedKey = null;
        _mockDatabase
            .Setup(db => db.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, CommandFlags>((key, flags) => capturedKey = key.ToString())
            .ReturnsAsync(true);

        // Act
        await _store.ExistsAsync("mykey", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"{KeyPrefix}:mykey", capturedKey);
    }

    #endregion

    #region GetAsync Tests

    [Fact]
    public async Task GetAsync_WithExistingKey_DeserializesAndReturnsValue()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        var json = BannouJson.Serialize(entity);
        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)json);

        // Act
        var result = await _store.GetAsync("key1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1", result.Id);
        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task GetAsync_WithNonExistentKey_ReturnsNull()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _store.GetAsync("nonexistent", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_WithCorruptedJson_ReturnsNullAndLogsError()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"{ invalid json");

        // Act
        var result = await _store.GetAsync("corrupt-key", cancellationToken: TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task GetAsync_WithRedisConnectionException_RethrowsAndLogs()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test"));

        // Act & Assert
        await Assert.ThrowsAsync<RedisConnectionException>(() => _store.GetAsync("key1", cancellationToken: TestContext.Current.CancellationToken));
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Redis connection failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAsync_WithRedisTimeoutException_RethrowsAndLogs()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisTimeoutException("Test", CommandStatus.Unknown));

        // Act & Assert
        await Assert.ThrowsAsync<RedisTimeoutException>(() => _store.GetAsync("key1", cancellationToken: TestContext.Current.CancellationToken));
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Redis timeout")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region GetWithETagAsync Tests

    [Fact]
    public async Task GetWithETagAsync_WithExistingKey_ReturnsValueAndVersion()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        var json = BannouJson.Serialize(entity);

        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)json);
        _mockDatabase
            .Setup(db => db.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"5");

        // Act
        var (value, etag) = await _store.GetWithETagAsync("key1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(value);
        Assert.Equal("1", value.Id);
        Assert.Equal("5", etag);
    }

    [Fact]
    public async Task GetWithETagAsync_WithNoVersion_ReturnsDefaultEtag()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        var json = BannouJson.Serialize(entity);

        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)json);
        _mockDatabase
            .Setup(db => db.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var (value, etag) = await _store.GetWithETagAsync("key1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(value);
        Assert.Equal("0", etag); // Default when no version exists
    }

    [Fact]
    public async Task GetWithETagAsync_WithNonExistentKey_ReturnsNulls()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        _mockDatabase
            .Setup(db => db.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var (value, etag) = await _store.GetWithETagAsync("nonexistent", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(value);
        Assert.Null(etag);
    }

    #endregion

    #region SaveAsync Tests

    /// <summary>
    /// Helper method to create a mock transaction with default behaviors.
    /// Moq DefaultValue.Mock allows all method calls to return default values.
    /// </summary>
    private static Mock<ITransaction> CreateMockTransaction(long hashIncrementResult = 1L, bool executeResult = true)
    {
        var mockTransaction = new Mock<ITransaction>(MockBehavior.Loose);
        mockTransaction.DefaultValue = DefaultValue.Mock;

        mockTransaction
            .Setup(t => t.HashIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .Returns(Task.FromResult(hashIncrementResult));
        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(executeResult);

        return mockTransaction;
    }

    [Fact]
    public async Task SaveAsync_CallsCreateTransaction()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        var mockTransaction = CreateMockTransaction();

        _mockDatabase
            .Setup(db => db.CreateTransaction(It.IsAny<object?>()))
            .Returns(mockTransaction.Object);

        // Act
        await _store.SaveAsync("key1", entity, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _mockDatabase.Verify(db => db.CreateTransaction(It.IsAny<object?>()), Times.Once);
    }

    [Fact]
    public async Task SaveAsync_ReturnsVersionAsEtag()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        var mockTransaction = CreateMockTransaction(hashIncrementResult: 7L);

        _mockDatabase
            .Setup(db => db.CreateTransaction(It.IsAny<object?>()))
            .Returns(mockTransaction.Object);

        // Act
        var etag = await _store.SaveAsync("key1", entity, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("7", etag);
    }

    [Fact]
    public async Task SaveAsync_WithTransactionFailure_ThrowsInvalidOperationException()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        var mockTransaction = CreateMockTransaction(executeResult: false);

        _mockDatabase
            .Setup(db => db.CreateTransaction(It.IsAny<object?>()))
            .Returns(mockTransaction.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.SaveAsync("key1", entity, cancellationToken: TestContext.Current.CancellationToken));
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("transaction unexpectedly failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region TrySaveAsync Tests

    [Fact]
    public async Task TrySaveAsync_WithEmptyEtagAndKeyNotExists_CreatesEntry()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        _mockDatabase
            .Setup(db => db.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var mockTransaction = new Mock<ITransaction>(MockBehavior.Loose);
        mockTransaction.DefaultValue = DefaultValue.Mock;
        mockTransaction.Setup(t => t.AddCondition(It.IsAny<Condition>()));
        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _mockDatabase
            .Setup(db => db.CreateTransaction(It.IsAny<object?>()))
            .Returns(mockTransaction.Object);

        // Act
        var result = await _store.TrySaveAsync("key1", entity, "", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("1", result);
    }

    [Fact]
    public async Task TrySaveAsync_WithEmptyEtagAndKeyExists_ReturnsNull()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        _mockDatabase
            .Setup(db => db.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true); // Key already exists

        // Act
        var result = await _store.TrySaveAsync("key1", entity, "", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task TrySaveAsync_WithMatchingEtag_UpdatesAndReturnsNewVersion()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        _mockDatabase
            .Setup(db => db.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"3");

        var mockTransaction = new Mock<ITransaction>(MockBehavior.Loose);
        mockTransaction.DefaultValue = DefaultValue.Mock;
        mockTransaction.Setup(t => t.AddCondition(It.IsAny<Condition>()));
        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _mockDatabase
            .Setup(db => db.CreateTransaction(It.IsAny<object?>()))
            .Returns(mockTransaction.Object);

        // Act
        var result = await _store.TrySaveAsync("key1", entity, "3", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("4", result);
    }

    [Fact]
    public async Task TrySaveAsync_WithMismatchedEtag_ReturnsNull()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };

        _mockDatabase
            .Setup(db => db.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"5"); // Different version

        // Act
        var result = await _store.TrySaveAsync("key1", entity, "3", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_WithExistingKey_ReturnsTrue()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.KeyDeleteAsync(It.Is<RedisKey>(k => !k.ToString().Contains(":meta")), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _mockDatabase
            .Setup(db => db.KeyDeleteAsync(It.Is<RedisKey>(k => k.ToString().Contains(":meta")), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await _store.DeleteAsync("key1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var result = await _store.DeleteAsync("nonexistent", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region ExistsAsync Tests

    [Fact]
    public async Task ExistsAsync_WithExistingKey_ReturnsTrue()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await _store.ExistsAsync("key1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ExistsAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var result = await _store.ExistsAsync("nonexistent", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetBulkAsync Tests

    [Fact]
    public async Task GetBulkAsync_WithExistingKeys_ReturnsAllValues()
    {
        // Arrange
        var entity1 = new TestEntity { Id = "1", Name = "One", Value = 1 };
        var entity2 = new TestEntity { Id = "2", Name = "Two", Value = 2 };

        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue[]
            {
                BannouJson.Serialize(entity1),
                BannouJson.Serialize(entity2)
            });

        // Act
        var results = await _store.GetBulkAsync(new[] { "key1", "key2" }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("One", results["key1"].Name);
        Assert.Equal("Two", results["key2"].Name);
    }

    [Fact]
    public async Task GetBulkAsync_WithMixedKeys_ReturnsOnlyExisting()
    {
        // Arrange
        var entity1 = new TestEntity { Id = "1", Name = "One", Value = 1 };

        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue[]
            {
                BannouJson.Serialize(entity1),
                RedisValue.Null  // Second key doesn't exist
            });

        // Act
        var results = await _store.GetBulkAsync(new[] { "key1", "key2" }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results);
        Assert.True(results.ContainsKey("key1"));
        Assert.False(results.ContainsKey("key2"));
    }

    [Fact]
    public async Task GetBulkAsync_WithEmptyKeys_ReturnsEmptyDictionary()
    {
        // Act
        var results = await _store.GetBulkAsync(Array.Empty<string>(), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetBulkAsync_WithCorruptedJson_SkipsCorruptedItems()
    {
        // Arrange
        var entity1 = new TestEntity { Id = "1", Name = "One", Value = 1 };

        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue[]
            {
                BannouJson.Serialize(entity1),
                "{ invalid json"  // Corrupted
            });

        // Act
        var results = await _store.GetBulkAsync(new[] { "key1", "key2" }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results);
        Assert.True(results.ContainsKey("key1"));
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

    #region SaveBulkAsync Tests

    [Fact]
    public async Task SaveBulkAsync_WithMultipleItems_SavesAll()
    {
        // Arrange
        var items = new Dictionary<string, TestEntity>
        {
            ["key1"] = new TestEntity { Id = "1", Name = "One", Value = 1 },
            ["key2"] = new TestEntity { Id = "2", Name = "Two", Value = 2 }
        };

        var version = 0L;
        var mockTransaction = new Mock<ITransaction>(MockBehavior.Loose);
        mockTransaction.DefaultValue = DefaultValue.Mock;
        mockTransaction
            .Setup(t => t.HashIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .Returns(() => Task.FromResult(++version));
        mockTransaction
            .Setup(t => t.ExecuteAsync(It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _mockDatabase
            .Setup(db => db.CreateTransaction(It.IsAny<object?>()))
            .Returns(mockTransaction.Object);

        // Act
        var etags = await _store.SaveBulkAsync(items, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, etags.Count);
        Assert.True(etags.ContainsKey("key1"));
        Assert.True(etags.ContainsKey("key2"));
    }

    [Fact]
    public async Task SaveBulkAsync_WithEmptyItems_ReturnsEmptyDictionary()
    {
        // Act
        var etags = await _store.SaveBulkAsync(new Dictionary<string, TestEntity>(), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(etags);
    }

    #endregion

    #region ExistsBulkAsync Tests

    [Fact]
    public async Task ExistsBulkAsync_WithMixedKeys_ReturnsExistingOnly()
    {
        // Arrange
        _mockDatabase
            .SetupSequence(db => db.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        // Act
        var existing = await _store.ExistsBulkAsync(new[] { "key1", "key2", "key3" }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, existing.Count);
        Assert.Contains("key1", existing);
        Assert.Contains("key3", existing);
        Assert.DoesNotContain("key2", existing);
    }

    [Fact]
    public async Task ExistsBulkAsync_WithEmptyKeys_ReturnsEmptySet()
    {
        // Act
        var existing = await _store.ExistsBulkAsync(Array.Empty<string>(), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(existing);
    }

    #endregion

    #region DeleteBulkAsync Tests

    [Fact]
    public async Task DeleteBulkAsync_WithExistingKeys_ReturnsDeletedCount()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(4); // 2 keys * 2 (value + meta)

        // Act
        var count = await _store.DeleteBulkAsync(new[] { "key1", "key2" }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task DeleteBulkAsync_WithEmptyKeys_ReturnsZero()
    {
        // Act
        var count = await _store.DeleteBulkAsync(Array.Empty<string>(), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, count);
    }

    #endregion

    #region Set Operations Tests

    [Fact]
    public async Task AddToSetAsync_UsesCorrectSetKeyPrefix()
    {
        // Arrange
        string? capturedKey = null;
        _mockDatabase
            .Setup(db => db.SetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, CommandFlags>((key, value, flags) => capturedKey = key.ToString())
            .ReturnsAsync(true);

        // Act
        await _store.AddToSetAsync("myset", new TestEntity { Id = "1" }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"{KeyPrefix}:set:myset", capturedKey);
    }

    [Fact]
    public async Task GetSetAsync_DeserializesAllMembers()
    {
        // Arrange
        var entity1 = new TestEntity { Id = "1", Name = "One", Value = 1 };
        var entity2 = new TestEntity { Id = "2", Name = "Two", Value = 2 };

        _mockDatabase
            .Setup(db => db.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue[]
            {
                BannouJson.Serialize(entity1),
                BannouJson.Serialize(entity2)
            });

        // Act
        var result = await _store.GetSetAsync<TestEntity>("myset", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Name == "One");
        Assert.Contains(result, e => e.Name == "Two");
    }

    [Fact]
    public async Task GetSetAsync_WithCorruptedMember_SkipsCorruptedItems()
    {
        // Arrange
        var entity1 = new TestEntity { Id = "1", Name = "One", Value = 1 };

        _mockDatabase
            .Setup(db => db.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue[]
            {
                BannouJson.Serialize(entity1),
                "{ corrupted }"
            });

        // Act
        var result = await _store.GetSetAsync<TestEntity>("myset", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.Equal("One", result[0].Name);
    }

    [Fact]
    public async Task SetCountAsync_ReturnsSetLength()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.SetLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(5);

        // Act
        var count = await _store.SetCountAsync("myset", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, count);
    }

    #endregion

    #region Sorted Set Operations Tests

    [Fact]
    public async Task SortedSetAddAsync_UsesCorrectKeyPrefix()
    {
        // Arrange
        string? capturedKey = null;
        _mockDatabase
            .Setup(db => db.SortedSetAddAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<double>(), It.IsAny<SortedSetWhen>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, double, SortedSetWhen, CommandFlags>((key, member, score, when, flags) => capturedKey = key.ToString())
            .ReturnsAsync(true);

        // Act
        await _store.SortedSetAddAsync("leaderboard", "player1", 100.0, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"{KeyPrefix}:zset:leaderboard", capturedKey);
    }

    [Fact]
    public async Task SortedSetScoreAsync_ReturnsScore()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.SortedSetScoreAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(100.5);

        // Act
        var score = await _store.SortedSetScoreAsync("leaderboard", "player1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(100.5, score);
    }

    [Fact]
    public async Task SortedSetRankAsync_ReturnsRank()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.SortedSetRankAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Order>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(5);

        // Act
        var rank = await _store.SortedSetRankAsync("leaderboard", "player1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, rank);
    }

    [Fact]
    public async Task SortedSetCountAsync_ReturnsLength()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.SortedSetLengthAsync(It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(10);

        // Act
        var count = await _store.SortedSetCountAsync("leaderboard", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(10, count);
    }

    #endregion

    #region Counter Operations Tests

    [Fact]
    public async Task IncrementAsync_UsesCorrectKeyPrefix()
    {
        // Arrange
        string? capturedKey = null;
        _mockDatabase
            .Setup(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, long, CommandFlags>((key, increment, flags) => capturedKey = key.ToString())
            .ReturnsAsync(1);

        // Act
        await _store.IncrementAsync("mycounter", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"{KeyPrefix}:counter:mycounter", capturedKey);
    }

    [Fact]
    public async Task GetCounterAsync_ReturnsValue()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"42");

        // Act
        var value = await _store.GetCounterAsync("mycounter", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(42, value);
    }

    [Fact]
    public async Task GetCounterAsync_WithNonExistentKey_ReturnsNull()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var value = await _store.GetCounterAsync("nonexistent", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(value);
    }

    [Fact]
    public async Task GetCounterAsync_WithNonNumericValue_ReturnsNullAndLogsWarning()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"not-a-number");

        // Act
        var value = await _store.GetCounterAsync("mycounter", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(value);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("non-numeric value")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Hash Operations Tests

    [Fact]
    public async Task HashSetAsync_UsesCorrectKeyPrefix()
    {
        // Arrange
        string? capturedKey = null;
        _mockDatabase
            .Setup(db => db.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, RedisValue, RedisValue, When, CommandFlags>((key, field, value, when, flags) => capturedKey = key.ToString())
            .ReturnsAsync(true);

        // Act
        await _store.HashSetAsync("myhash", "field1", new TestEntity { Id = "1" }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"{KeyPrefix}:hash:myhash", capturedKey);
    }

    [Fact]
    public async Task HashGetAsync_DeserializesValue()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Test", Value = 42 };
        _mockDatabase
            .Setup(db => db.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)BannouJson.Serialize(entity));

        // Act
        var result = await _store.HashGetAsync<TestEntity>("myhash", "field1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1", result.Id);
        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public async Task HashGetAsync_WithCorruptedValue_ReturnsDefaultAndLogsError()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)"{ corrupted }");

        // Act
        var result = await _store.HashGetAsync<TestEntity>("myhash", "field1", cancellationToken: TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task HashCountAsync_ReturnsLength()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.HashLengthAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(3);

        // Act
        var count = await _store.HashCountAsync("myhash", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task HashGetAllAsync_WithCorruptedFields_SkipsCorruptedAndLogsError()
    {
        // Arrange
        var entity = new TestEntity { Id = "1", Name = "Valid", Value = 1 };
        _mockDatabase
            .Setup(db => db.HashGetAllAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new HashEntry[]
            {
                new("field1", BannouJson.Serialize(entity)),
                new("field2", "{ corrupted }")
            });

        // Act
        var result = await _store.HashGetAllAsync<TestEntity>("myhash", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey("field1"));
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

    #region SaveBulkAsync Transaction Failure Tests (Gap 5)

    /// <summary>
    /// Verifies that SaveBulkAsync throws InvalidOperationException when
    /// the Redis transaction fails unexpectedly (ExecuteAsync returns false).
    /// </summary>
    [Fact]
    public async Task SaveBulkAsync_WithTransactionFailure_ThrowsInvalidOperationException()
    {
        // Arrange
        var items = new Dictionary<string, TestEntity>
        {
            ["key1"] = new TestEntity { Id = "1", Name = "One", Value = 1 },
            ["key2"] = new TestEntity { Id = "2", Name = "Two", Value = 2 }
        };

        var mockTransaction = CreateMockTransaction(executeResult: false);

        _mockDatabase
            .Setup(db => db.CreateTransaction(It.IsAny<object?>()))
            .Returns(mockTransaction.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _store.SaveBulkAsync(items, cancellationToken: TestContext.Current.CancellationToken));

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("bulk transaction unexpectedly failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region SortedSetRangeByScoreAsync Tests (Gap 6)

    /// <summary>
    /// Verifies ascending range query returns results in correct order
    /// with member-score tuples deserialized from SortedSetEntry.
    /// </summary>
    [Fact]
    public async Task SortedSetRangeByScoreAsync_Ascending_ReturnsResults()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.SortedSetRangeByScoreWithScoresAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<double>(),
                It.IsAny<double>(),
                Exclude.None,
                Order.Ascending,
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(new SortedSetEntry[]
            {
                new("player-a", 100.0),
                new("player-b", 200.0),
                new("player-c", 300.0)
            });

        // Act
        var results = await _store.SortedSetRangeByScoreAsync("leaderboard", 0, 500, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal("player-a", results[0].member);
        Assert.Equal(100.0, results[0].score);
        Assert.Equal("player-b", results[1].member);
        Assert.Equal(200.0, results[1].score);
        Assert.Equal("player-c", results[2].member);
        Assert.Equal(300.0, results[2].score);
    }

    /// <summary>
    /// Verifies descending range query uses Order.Descending.
    /// </summary>
    [Fact]
    public async Task SortedSetRangeByScoreAsync_Descending_UsesDescendingOrder()
    {
        // Arrange
        Order? capturedOrder = null;
        _mockDatabase
            .Setup(db => db.SortedSetRangeByScoreWithScoresAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<double>(),
                It.IsAny<double>(),
                It.IsAny<Exclude>(),
                It.IsAny<Order>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, double, double, Exclude, Order, long, long, CommandFlags>(
                (key, min, max, exclude, order, skip, take, flags) => capturedOrder = order)
            .ReturnsAsync(new SortedSetEntry[]
            {
                new("player-c", 300.0),
                new("player-b", 200.0)
            });

        // Act
        var results = await _store.SortedSetRangeByScoreAsync(
            "leaderboard", 0, 500, descending: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Order.Descending, capturedOrder);
        Assert.Equal(2, results.Count);
        Assert.Equal("player-c", results[0].member);
    }

    /// <summary>
    /// Verifies that offset and count parameters are passed through to Redis.
    /// </summary>
    [Fact]
    public async Task SortedSetRangeByScoreAsync_WithOffsetAndCount_PassesParameters()
    {
        // Arrange
        long? capturedOffset = null;
        long? capturedCount = null;
        _mockDatabase
            .Setup(db => db.SortedSetRangeByScoreWithScoresAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<double>(),
                It.IsAny<double>(),
                It.IsAny<Exclude>(),
                It.IsAny<Order>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, double, double, Exclude, Order, long, long, CommandFlags>(
                (key, min, max, exclude, order, skip, take, flags) =>
                {
                    capturedOffset = skip;
                    capturedCount = take;
                })
            .ReturnsAsync(Array.Empty<SortedSetEntry>());

        // Act
        await _store.SortedSetRangeByScoreAsync("leaderboard", 0, 1000, offset: 10, count: 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(10, capturedOffset);
        Assert.Equal(5, capturedCount);
    }

    /// <summary>
    /// Verifies the correct sorted set key prefix is used.
    /// </summary>
    [Fact]
    public async Task SortedSetRangeByScoreAsync_UsesCorrectKeyPrefix()
    {
        // Arrange
        string? capturedKey = null;
        _mockDatabase
            .Setup(db => db.SortedSetRangeByScoreWithScoresAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<double>(),
                It.IsAny<double>(),
                It.IsAny<Exclude>(),
                It.IsAny<Order>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, double, double, Exclude, Order, long, long, CommandFlags>(
                (key, min, max, exclude, order, skip, take, flags) => capturedKey = key.ToString())
            .ReturnsAsync(Array.Empty<SortedSetEntry>());

        // Act
        await _store.SortedSetRangeByScoreAsync("scores", 0, 100, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"{KeyPrefix}:zset:scores", capturedKey);
    }

    /// <summary>
    /// Documents that SortedSetRangeByScoreAsync always passes Exclude.None to Redis.
    /// The Exclude parameter is not exposed in the public API.
    /// </summary>
    [Fact]
    public async Task SortedSetRangeByScoreAsync_AlwaysUsesExcludeNone()
    {
        // Arrange
        Exclude? capturedExclude = null;
        _mockDatabase
            .Setup(db => db.SortedSetRangeByScoreWithScoresAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<double>(),
                It.IsAny<double>(),
                It.IsAny<Exclude>(),
                It.IsAny<Order>(),
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, double, double, Exclude, Order, long, long, CommandFlags>(
                (key, min, max, exclude, order, skip, take, flags) => capturedExclude = exclude)
            .ReturnsAsync(Array.Empty<SortedSetEntry>());

        // Act
        await _store.SortedSetRangeByScoreAsync("leaderboard", 0, 100, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — the public API does not expose an Exclude parameter;
        // the implementation always passes Exclude.None to Redis
        Assert.Equal(Exclude.None, capturedExclude);
    }

    #endregion

    #region HashSetManyAsync Tests (Gap 7)

    /// <summary>
    /// Verifies HashSetManyAsync serializes all fields and writes them to the correct hash key.
    /// Uses Capture Pattern to verify the HashEntry array passed to Redis.
    /// </summary>
    [Fact]
    public async Task HashSetManyAsync_WithMultipleFields_WritesAllToHash()
    {
        // Arrange
        HashEntry[]? capturedEntries = null;
        string? capturedKey = null;
        _mockDatabase
            .Setup(db => db.HashSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<HashEntry[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, HashEntry[], CommandFlags>(
                (key, entries, flags) =>
                {
                    capturedKey = key.ToString();
                    capturedEntries = entries;
                })
            .Returns(Task.CompletedTask);

        var fields = new Dictionary<string, TestEntity>
        {
            ["field-a"] = new TestEntity { Id = "1", Name = "Alpha", Value = 10 },
            ["field-b"] = new TestEntity { Id = "2", Name = "Beta", Value = 20 }
        };

        // Act
        await _store.HashSetManyAsync("myhash", fields, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"{KeyPrefix}:hash:myhash", capturedKey);
        Assert.NotNull(capturedEntries);
        Assert.Equal(2, capturedEntries.Length);

        // Verify field names are correct
        var fieldNames = capturedEntries.Select(e => e.Name.ToString()).ToList();
        Assert.Contains("field-a", fieldNames);
        Assert.Contains("field-b", fieldNames);
    }

    /// <summary>
    /// Verifies HashSetManyAsync with empty fields returns immediately without calling Redis.
    /// </summary>
    [Fact]
    public async Task HashSetManyAsync_WithEmptyFields_DoesNotCallRedis()
    {
        // Act
        await _store.HashSetManyAsync("myhash", new Dictionary<string, TestEntity>(), cancellationToken: TestContext.Current.CancellationToken);

        // Assert — no Redis calls should have been made
        _mockDatabase.Verify(
            db => db.HashSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<HashEntry[]>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies HashSetManyAsync applies TTL via KeyExpireAsync when StateOptions.Ttl is set.
    /// </summary>
    [Fact]
    public async Task HashSetManyAsync_WithTtl_AppliesKeyExpire()
    {
        // Arrange
        TimeSpan? capturedTtl = null;
        _mockDatabase
            .Setup(db => db.HashSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<HashEntry[]>(),
                It.IsAny<CommandFlags>()))
            .Returns(Task.CompletedTask);

        _mockDatabase
            .Setup(db => db.KeyExpireAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, TimeSpan?, ExpireWhen, CommandFlags>(
                (key, ttl, when, flags) => capturedTtl = ttl)
            .ReturnsAsync(true);

        var fields = new Dictionary<string, TestEntity>
        {
            ["field-a"] = new TestEntity { Id = "1", Name = "Alpha", Value = 10 }
        };

        // Act
        await _store.HashSetManyAsync("myhash", fields, new StateOptions { Ttl = 300 }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(capturedTtl);
        Assert.Equal(TimeSpan.FromSeconds(300), capturedTtl.Value);
    }

    #endregion

    #region RefreshSetTtlAsync Tests (Gap 8)

    /// <summary>
    /// Verifies RefreshSetTtlAsync calls KeyExpireAsync with the correct set key and TTL.
    /// </summary>
    [Fact]
    public async Task RefreshSetTtlAsync_CallsKeyExpireWithCorrectParameters()
    {
        // Arrange
        string? capturedKey = null;
        TimeSpan? capturedTtl = null;
        _mockDatabase
            .Setup(db => db.KeyExpireAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, TimeSpan?, ExpireWhen, CommandFlags>(
                (key, ttl, when, flags) =>
                {
                    capturedKey = key.ToString();
                    capturedTtl = ttl;
                })
            .ReturnsAsync(true);

        // Act
        var result = await _store.RefreshSetTtlAsync("myset", 120, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.Equal($"{KeyPrefix}:set:myset", capturedKey);
        Assert.Equal(TimeSpan.FromSeconds(120), capturedTtl);
    }

    /// <summary>
    /// Verifies RefreshSetTtlAsync returns false when the set key does not exist.
    /// </summary>
    [Fact]
    public async Task RefreshSetTtlAsync_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.KeyExpireAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var result = await _store.RefreshSetTtlAsync("nonexistent", 60, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region RefreshHashTtlAsync Tests (Gap 9)

    /// <summary>
    /// Verifies RefreshHashTtlAsync calls KeyExpireAsync with the correct hash key and TTL.
    /// </summary>
    [Fact]
    public async Task RefreshHashTtlAsync_CallsKeyExpireWithCorrectParameters()
    {
        // Arrange
        string? capturedKey = null;
        TimeSpan? capturedTtl = null;
        _mockDatabase
            .Setup(db => db.KeyExpireAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, TimeSpan?, ExpireWhen, CommandFlags>(
                (key, ttl, when, flags) =>
                {
                    capturedKey = key.ToString();
                    capturedTtl = ttl;
                })
            .ReturnsAsync(true);

        // Act
        var result = await _store.RefreshHashTtlAsync("myhash", 600, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.Equal($"{KeyPrefix}:hash:myhash", capturedKey);
        Assert.Equal(TimeSpan.FromSeconds(600), capturedTtl);
    }

    /// <summary>
    /// Verifies RefreshHashTtlAsync returns false when the hash key does not exist.
    /// </summary>
    [Fact]
    public async Task RefreshHashTtlAsync_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.KeyExpireAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var result = await _store.RefreshHashTtlAsync("nonexistent", 60, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region DeleteHashAsync Tests (Gap 10)

    /// <summary>
    /// Verifies DeleteHashAsync calls KeyDeleteAsync with the correct hash key prefix.
    /// </summary>
    [Fact]
    public async Task DeleteHashAsync_WithExistingHash_ReturnsTrueAndUsesCorrectKey()
    {
        // Arrange
        string? capturedKey = null;
        _mockDatabase
            .Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, CommandFlags>((key, flags) => capturedKey = key.ToString())
            .ReturnsAsync(true);

        // Act
        var result = await _store.DeleteHashAsync("myhash", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.Equal($"{KeyPrefix}:hash:myhash", capturedKey);
    }

    /// <summary>
    /// Verifies DeleteHashAsync returns false when the hash does not exist.
    /// </summary>
    [Fact]
    public async Task DeleteHashAsync_WithNonExistentHash_ReturnsFalse()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var result = await _store.DeleteHashAsync("nonexistent", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region DecrementAsync Tests (Gap 11)

    /// <summary>
    /// Verifies DecrementAsync calls StringDecrementAsync with the correct counter key and decrement value.
    /// </summary>
    [Fact]
    public async Task DecrementAsync_CallsStringDecrementWithCorrectParameters()
    {
        // Arrange
        string? capturedKey = null;
        long? capturedDecrement = null;
        _mockDatabase
            .Setup(db => db.StringDecrementAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<long>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, long, CommandFlags>(
                (key, decr, flags) =>
                {
                    capturedKey = key.ToString();
                    capturedDecrement = decr;
                })
            .ReturnsAsync(9);

        // Act
        var result = await _store.DecrementAsync("mycounter", 1, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(9, result);
        Assert.Equal($"{KeyPrefix}:counter:mycounter", capturedKey);
        Assert.Equal(1, capturedDecrement);
    }

    /// <summary>
    /// Verifies DecrementAsync with custom decrement value passes the value through.
    /// </summary>
    [Fact]
    public async Task DecrementAsync_WithCustomDecrement_PassesCorrectValue()
    {
        // Arrange
        long? capturedDecrement = null;
        _mockDatabase
            .Setup(db => db.StringDecrementAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<long>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, long, CommandFlags>(
                (key, decr, flags) => capturedDecrement = decr)
            .ReturnsAsync(5);

        // Act
        var result = await _store.DecrementAsync("mycounter", 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, result);
        Assert.Equal(5, capturedDecrement);
    }

    /// <summary>
    /// Verifies DecrementAsync applies TTL via KeyExpireAsync when StateOptions.Ttl is set.
    /// </summary>
    [Fact]
    public async Task DecrementAsync_WithTtl_AppliesKeyExpire()
    {
        // Arrange
        TimeSpan? capturedTtl = null;
        _mockDatabase
            .Setup(db => db.StringDecrementAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<long>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(9);

        _mockDatabase
            .Setup(db => db.KeyExpireAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(),
                It.IsAny<CommandFlags>()))
            .Callback<RedisKey, TimeSpan?, ExpireWhen, CommandFlags>(
                (key, ttl, when, flags) => capturedTtl = ttl)
            .ReturnsAsync(true);

        // Act
        await _store.DecrementAsync("mycounter", 1, new StateOptions { Ttl = 60 }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(capturedTtl);
        Assert.Equal(TimeSpan.FromSeconds(60), capturedTtl.Value);
    }

    #endregion

    #region SetCounterAsync Tests (Gap 12)

    /// <summary>
    /// Verifies SetCounterAsync calls StringSetAsync with the correct counter key and value.
    /// Uses Verify pattern since StringSetAsync has many overloads with optional parameters.
    /// </summary>
    [Fact]
    public async Task SetCounterAsync_SetsValueWithCorrectKey()
    {
        // Arrange — setup the Expiration-based overload (resolved by compiler for 2-arg calls)
        _mockDatabase
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<Expiration>(),
                It.IsAny<ValueCondition>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _store.SetCounterAsync("mycounter", 42, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — verify the correct key was used
        _mockDatabase.Verify(
            db => db.StringSetAsync(
                It.Is<RedisKey>(k => k.ToString() == $"{KeyPrefix}:counter:mycounter"),
                It.Is<RedisValue>(v => (long)v == 42),
                It.IsAny<Expiration>(),
                It.IsAny<ValueCondition>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies SetCounterAsync with TTL passes the expiry through to Redis.
    /// </summary>
    [Fact]
    public async Task SetCounterAsync_WithTtl_SetsValueWithExpiry()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<Expiration>(),
                It.IsAny<ValueCondition>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        await _store.SetCounterAsync("mycounter", 100, new StateOptions { Ttl = 300 }, cancellationToken: TestContext.Current.CancellationToken);

        // Assert — verify TTL was passed (Expiration wraps TimeSpan, displayed as "EX 300")
        _mockDatabase.Verify(
            db => db.StringSetAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.Is<Expiration>(e => e.ToString().Contains("300")),
                It.IsAny<ValueCondition>(),
                It.IsAny<CommandFlags>()),
            Times.Once);
    }

    #endregion

    #region DeleteCounterAsync Tests (Gap 13)

    /// <summary>
    /// Verifies DeleteCounterAsync calls KeyDeleteAsync with the correct counter key prefix.
    /// </summary>
    [Fact]
    public async Task DeleteCounterAsync_WithExistingCounter_ReturnsTrueAndUsesCorrectKey()
    {
        // Arrange
        string? capturedKey = null;
        _mockDatabase
            .Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey, CommandFlags>((key, flags) => capturedKey = key.ToString())
            .ReturnsAsync(true);

        // Act
        var result = await _store.DeleteCounterAsync("mycounter", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.Equal($"{KeyPrefix}:counter:mycounter", capturedKey);
    }

    /// <summary>
    /// Verifies DeleteCounterAsync returns false when the counter does not exist.
    /// </summary>
    [Fact]
    public async Task DeleteCounterAsync_WithNonExistentCounter_ReturnsFalse()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        // Act
        var result = await _store.DeleteCounterAsync("nonexistent", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    #endregion
}
