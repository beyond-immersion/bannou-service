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
        ServiceConstructorValidator.ValidateServiceConstructor<RedisStateStore<TestEntity>>();
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
        await _store.GetAsync("mykey");

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
        await _store.DeleteAsync("mykey");

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
        await _store.ExistsAsync("mykey");

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
        // Arrange
        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        // Act
        var result = await _store.GetAsync("nonexistent");

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

    [Fact]
    public async Task GetAsync_WithRedisConnectionException_RethrowsAndLogs()
    {
        // Arrange
        _mockDatabase
            .Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Test"));

        // Act & Assert
        await Assert.ThrowsAsync<RedisConnectionException>(() => _store.GetAsync("key1"));
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
        await Assert.ThrowsAsync<RedisTimeoutException>(() => _store.GetAsync("key1"));
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
        var (value, etag) = await _store.GetWithETagAsync("key1");

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
        var (value, etag) = await _store.GetWithETagAsync("key1");

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
        var (value, etag) = await _store.GetWithETagAsync("nonexistent");

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
        await _store.SaveAsync("key1", entity);

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
        var etag = await _store.SaveAsync("key1", entity);

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
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.SaveAsync("key1", entity));
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
        var result = await _store.TrySaveAsync("key1", entity, "");

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
        var result = await _store.TrySaveAsync("key1", entity, "");

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
        var result = await _store.TrySaveAsync("key1", entity, "3");

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
        var result = await _store.TrySaveAsync("key1", entity, "3");

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
        var result = await _store.DeleteAsync("key1");

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
        _mockDatabase
            .Setup(db => db.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        // Act
        var result = await _store.ExistsAsync("key1");

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
        var result = await _store.ExistsAsync("nonexistent");

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
        var results = await _store.GetBulkAsync(new[] { "key1", "key2" });

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
        var results = await _store.GetBulkAsync(new[] { "key1", "key2" });

        // Assert
        Assert.Single(results);
        Assert.True(results.ContainsKey("key1"));
        Assert.False(results.ContainsKey("key2"));
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
        var results = await _store.GetBulkAsync(new[] { "key1", "key2" });

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
        var etags = await _store.SaveBulkAsync(items);

        // Assert
        Assert.Equal(2, etags.Count);
        Assert.True(etags.ContainsKey("key1"));
        Assert.True(etags.ContainsKey("key2"));
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
        _mockDatabase
            .SetupSequence(db => db.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false)
            .ReturnsAsync(true);

        // Act
        var existing = await _store.ExistsBulkAsync(new[] { "key1", "key2", "key3" });

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
        var existing = await _store.ExistsBulkAsync(Array.Empty<string>());

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
        var count = await _store.DeleteBulkAsync(new[] { "key1", "key2" });

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task DeleteBulkAsync_WithEmptyKeys_ReturnsZero()
    {
        // Act
        var count = await _store.DeleteBulkAsync(Array.Empty<string>());

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
        await _store.AddToSetAsync("myset", new TestEntity { Id = "1" });

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
        var result = await _store.GetSetAsync<TestEntity>("myset");

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
        var result = await _store.GetSetAsync<TestEntity>("myset");

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
        var count = await _store.SetCountAsync("myset");

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
        await _store.SortedSetAddAsync("leaderboard", "player1", 100.0);

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
        var score = await _store.SortedSetScoreAsync("leaderboard", "player1");

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
        var rank = await _store.SortedSetRankAsync("leaderboard", "player1");

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
        var count = await _store.SortedSetCountAsync("leaderboard");

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
        await _store.IncrementAsync("mycounter");

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
        var value = await _store.GetCounterAsync("mycounter");

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
        var value = await _store.GetCounterAsync("nonexistent");

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
        var value = await _store.GetCounterAsync("mycounter");

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
        await _store.HashSetAsync("myhash", "field1", new TestEntity { Id = "1" });

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
        var result = await _store.HashGetAsync<TestEntity>("myhash", "field1");

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
        var result = await _store.HashGetAsync<TestEntity>("myhash", "field1");

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
        var count = await _store.HashCountAsync("myhash");

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
        var result = await _store.HashGetAllAsync<TestEntity>("myhash");

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
}
