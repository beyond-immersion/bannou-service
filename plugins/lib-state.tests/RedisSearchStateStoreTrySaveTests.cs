using BeyondImmersion.BannouService.State.Services;
using Moq;
using StackExchange.Redis;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Unit tests for RedisSearchStateStore.TrySaveAsync.
/// Tests the Lua script integration for atomic optimistic concurrency.
/// </summary>
public class RedisSearchStateStoreTrySaveTests
{
    private readonly Mock<IDatabase> _mockDatabase;
    private readonly Mock<ILogger<RedisSearchStateStore<TestDocument>>> _mockLogger;
    private readonly RedisSearchStateStore<TestDocument> _store;
    private const string KeyPrefix = "test-store";

    /// <summary>
    /// Test document for storage tests.
    /// </summary>
    public class TestDocument
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public RedisSearchStateStoreTrySaveTests()
    {
        _mockDatabase = new Mock<IDatabase>();
        _mockLogger = new Mock<ILogger<RedisSearchStateStore<TestDocument>>>();

        _store = new RedisSearchStateStore<TestDocument>(
            _mockDatabase.Object,
            KeyPrefix,
            defaultTtl: null,
            _mockLogger.Object);
    }

    #region TrySaveAsync with Empty ETag (Create) Tests

    [Fact]
    public async Task TrySaveAsync_WithEmptyEtag_CallsScriptEvaluateWithTryCreateScript()
    {
        // Arrange
        var document = new TestDocument { Id = "1", Name = "Test", Value = 42 };
        string? capturedScript = null;

        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((script, keys, values, flags) =>
            {
                capturedScript = script;
            })
            .ReturnsAsync(RedisResult.Create((long)1));

        // Act
        await _store.TrySaveAsync("key1", document, "");

        // Assert
        Assert.NotNull(capturedScript);
        Assert.Equal(RedisLuaScripts.TryCreate, capturedScript);
    }

    [Fact]
    public async Task TrySaveAsync_WithEmptyEtag_PassesCorrectKeys()
    {
        // Arrange
        var document = new TestDocument { Id = "1", Name = "Test", Value = 42 };
        RedisKey[]? capturedKeys = null;

        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((script, keys, values, flags) =>
            {
                capturedKeys = keys;
            })
            .ReturnsAsync(RedisResult.Create((long)1));

        // Act
        await _store.TrySaveAsync("key1", document, "");

        // Assert
        Assert.NotNull(capturedKeys);
        Assert.Equal(2, capturedKeys.Length);
        Assert.Equal($"{KeyPrefix}:key1", capturedKeys[0].ToString());
        Assert.Equal($"{KeyPrefix}:key1:meta", capturedKeys[1].ToString());
    }

    [Fact]
    public async Task TrySaveAsync_WithEmptyEtag_PassesJsonAndTimestamp()
    {
        // Arrange
        var document = new TestDocument { Id = "1", Name = "Test", Value = 42 };
        RedisValue[]? capturedValues = null;

        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((script, keys, values, flags) =>
            {
                capturedValues = values;
            })
            .ReturnsAsync(RedisResult.Create((long)1));

        // Act
        await _store.TrySaveAsync("key1", document, "");

        // Assert
        Assert.NotNull(capturedValues);
        Assert.Equal(2, capturedValues.Length);

        // First value should be JSON
        var json = capturedValues[0].ToString();
        Assert.Contains("\"Id\":\"1\"", json);
        Assert.Contains("\"Name\":\"Test\"", json);
        Assert.Contains("\"Value\":42", json);

        // Second value should be timestamp (numeric string)
        var timestamp = capturedValues[1].ToString();
        Assert.True(long.TryParse(timestamp, out var ts));
        Assert.True(ts > 0);
    }

    [Fact]
    public async Task TrySaveAsync_WithEmptyEtag_ReturnsVersion1OnSuccess()
    {
        // Arrange
        var document = new TestDocument { Id = "1", Name = "Test", Value = 42 };

        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((long)1));

        // Act
        var result = await _store.TrySaveAsync("key1", document, "");

        // Assert
        Assert.Equal("1", result);
    }

    [Fact]
    public async Task TrySaveAsync_WithEmptyEtag_ReturnsNullOnConflict()
    {
        // Arrange
        var document = new TestDocument { Id = "1", Name = "Test", Value = 42 };

        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((long)-1)); // Key already exists

        // Act
        var result = await _store.TrySaveAsync("key1", document, "");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region TrySaveAsync with ETag (Update) Tests

    [Fact]
    public async Task TrySaveAsync_WithEtag_CallsScriptEvaluateWithTryUpdateScript()
    {
        // Arrange
        var document = new TestDocument { Id = "1", Name = "Test", Value = 42 };
        string? capturedScript = null;

        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((script, keys, values, flags) =>
            {
                capturedScript = script;
            })
            .ReturnsAsync(RedisResult.Create((long)2));

        // Act
        await _store.TrySaveAsync("key1", document, "1");

        // Assert
        Assert.NotNull(capturedScript);
        Assert.Equal(RedisLuaScripts.TryUpdate, capturedScript);
    }

    [Fact]
    public async Task TrySaveAsync_WithEtag_PassesCorrectKeys()
    {
        // Arrange
        var document = new TestDocument { Id = "1", Name = "Test", Value = 42 };
        RedisKey[]? capturedKeys = null;

        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((script, keys, values, flags) =>
            {
                capturedKeys = keys;
            })
            .ReturnsAsync(RedisResult.Create((long)2));

        // Act
        await _store.TrySaveAsync("key1", document, "1");

        // Assert
        Assert.NotNull(capturedKeys);
        Assert.Equal(2, capturedKeys.Length);
        Assert.Equal($"{KeyPrefix}:key1", capturedKeys[0].ToString());
        Assert.Equal($"{KeyPrefix}:key1:meta", capturedKeys[1].ToString());
    }

    [Fact]
    public async Task TrySaveAsync_WithEtag_PassesEtagJsonAndTimestamp()
    {
        // Arrange
        var document = new TestDocument { Id = "1", Name = "Test", Value = 42 };
        RedisValue[]? capturedValues = null;

        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((script, keys, values, flags) =>
            {
                capturedValues = values;
            })
            .ReturnsAsync(RedisResult.Create((long)2));

        // Act
        await _store.TrySaveAsync("key1", document, "1");

        // Assert
        Assert.NotNull(capturedValues);
        Assert.Equal(3, capturedValues.Length);

        // First value should be etag
        Assert.Equal("1", capturedValues[0].ToString());

        // Second value should be JSON
        var json = capturedValues[1].ToString();
        Assert.Contains("\"Id\":\"1\"", json);

        // Third value should be timestamp
        var timestamp = capturedValues[2].ToString();
        Assert.True(long.TryParse(timestamp, out var ts));
        Assert.True(ts > 0);
    }

    [Fact]
    public async Task TrySaveAsync_WithEtag_ReturnsNewVersionOnSuccess()
    {
        // Arrange
        var document = new TestDocument { Id = "1", Name = "Test", Value = 42 };

        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((long)5)); // Version 4 -> 5

        // Act
        var result = await _store.TrySaveAsync("key1", document, "4");

        // Assert
        Assert.Equal("5", result);
    }

    [Fact]
    public async Task TrySaveAsync_WithEtag_ReturnsNullOnVersionMismatch()
    {
        // Arrange
        var document = new TestDocument { Id = "1", Name = "Test", Value = 42 };

        _mockDatabase
            .Setup(db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((long)-1)); // Version mismatch

        // Act
        var result = await _store.TrySaveAsync("key1", document, "1");

        // Assert
        Assert.Null(result);
    }

    #endregion
}
