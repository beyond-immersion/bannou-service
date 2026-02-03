using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Services;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Unit tests for InMemoryStateStore atomic counter operations.
/// Tests the ICacheableStateStore counter implementation for in-memory backend.
/// </summary>
public class InMemoryCounterTests : IDisposable
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

    public InMemoryCounterTests()
    {
        _mockLogger = new Mock<ILogger<InMemoryStateStore<TestEntity>>>();
        _storeName = $"test-counter-store-{Guid.NewGuid():N}";
        _store = new InMemoryStateStore<TestEntity>(_storeName, _mockLogger.Object);
    }

    public void Dispose()
    {
        _store.Clear();
    }

    #region IncrementAsync Tests

    [Fact]
    public async Task IncrementAsync_WithNewKey_CreatesCounterWithValue()
    {
        // Act
        var result = await _store.IncrementAsync("page-views", 1);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public async Task IncrementAsync_WithExistingKey_IncrementsValue()
    {
        // Arrange
        await _store.IncrementAsync("page-views", 10);

        // Act
        var result = await _store.IncrementAsync("page-views", 5);

        // Assert
        Assert.Equal(15, result);
    }

    [Fact]
    public async Task IncrementAsync_WithDefaultIncrement_IncrementsByOne()
    {
        // Act
        var result1 = await _store.IncrementAsync("counter");
        var result2 = await _store.IncrementAsync("counter");
        var result3 = await _store.IncrementAsync("counter");

        // Assert
        Assert.Equal(1, result1);
        Assert.Equal(2, result2);
        Assert.Equal(3, result3);
    }

    [Fact]
    public async Task IncrementAsync_WithLargeIncrement_Works()
    {
        // Act
        var result = await _store.IncrementAsync("large-counter", 1_000_000);

        // Assert
        Assert.Equal(1_000_000, result);
    }

    [Fact]
    public async Task IncrementAsync_WithTtl_SetsExpiration()
    {
        // Arrange
        await _store.IncrementAsync("expiring-counter", 100, new StateOptions { Ttl = 1 });

        // Assert - Should exist immediately
        var valueBefore = await _store.GetCounterAsync("expiring-counter");
        Assert.Equal(100, valueBefore);

        // Wait for expiration
        await Task.Delay(1100);

        // Should be expired now
        var valueAfter = await _store.GetCounterAsync("expiring-counter");
        Assert.Null(valueAfter);
    }

    [Fact]
    public async Task IncrementAsync_WithNegativeIncrement_Decrements()
    {
        // Arrange
        await _store.IncrementAsync("counter", 100);

        // Act
        var result = await _store.IncrementAsync("counter", -30);

        // Assert
        Assert.Equal(70, result);
    }

    #endregion

    #region DecrementAsync Tests

    [Fact]
    public async Task DecrementAsync_WithNewKey_CreatesNegativeCounter()
    {
        // Act
        var result = await _store.DecrementAsync("counter", 5);

        // Assert
        Assert.Equal(-5, result);
    }

    [Fact]
    public async Task DecrementAsync_WithExistingKey_DecrementsValue()
    {
        // Arrange
        await _store.IncrementAsync("counter", 100);

        // Act
        var result = await _store.DecrementAsync("counter", 30);

        // Assert
        Assert.Equal(70, result);
    }

    [Fact]
    public async Task DecrementAsync_WithDefaultDecrement_DecrementsByOne()
    {
        // Arrange
        await _store.IncrementAsync("counter", 10);

        // Act
        var result1 = await _store.DecrementAsync("counter");
        var result2 = await _store.DecrementAsync("counter");

        // Assert
        Assert.Equal(9, result1);
        Assert.Equal(8, result2);
    }

    [Fact]
    public async Task DecrementAsync_BelowZero_AllowsNegativeValues()
    {
        // Arrange
        await _store.IncrementAsync("counter", 5);

        // Act
        var result = await _store.DecrementAsync("counter", 10);

        // Assert
        Assert.Equal(-5, result);
    }

    [Fact]
    public async Task DecrementAsync_WithTtl_SetsExpiration()
    {
        // Arrange
        await _store.DecrementAsync("expiring-counter", 10, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var value = await _store.GetCounterAsync("expiring-counter");

        // Assert
        Assert.Null(value);
    }

    #endregion

    #region GetCounterAsync Tests

    [Fact]
    public async Task GetCounterAsync_WithExistingKey_ReturnsValue()
    {
        // Arrange
        await _store.IncrementAsync("counter", 42);

        // Act
        var result = await _store.GetCounterAsync("counter");

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task GetCounterAsync_WithNonExistentKey_ReturnsNull()
    {
        // Act
        var result = await _store.GetCounterAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCounterAsync_WithExpiredKey_ReturnsNull()
    {
        // Arrange
        await _store.IncrementAsync("expiring-counter", 100, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var result = await _store.GetCounterAsync("expiring-counter");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCounterAsync_WithNegativeValue_ReturnsNegative()
    {
        // Arrange
        await _store.DecrementAsync("counter", 50);

        // Act
        var result = await _store.GetCounterAsync("counter");

        // Assert
        Assert.Equal(-50, result);
    }

    #endregion

    #region SetCounterAsync Tests

    [Fact]
    public async Task SetCounterAsync_WithNewKey_SetsValue()
    {
        // Act
        await _store.SetCounterAsync("counter", 100);

        // Assert
        var result = await _store.GetCounterAsync("counter");
        Assert.Equal(100, result);
    }

    [Fact]
    public async Task SetCounterAsync_WithExistingKey_OverwritesValue()
    {
        // Arrange
        await _store.IncrementAsync("counter", 50);

        // Act
        await _store.SetCounterAsync("counter", 200);

        // Assert
        var result = await _store.GetCounterAsync("counter");
        Assert.Equal(200, result);
    }

    [Fact]
    public async Task SetCounterAsync_WithZero_SetsToZero()
    {
        // Arrange
        await _store.IncrementAsync("counter", 100);

        // Act
        await _store.SetCounterAsync("counter", 0);

        // Assert
        var result = await _store.GetCounterAsync("counter");
        Assert.Equal(0, result);
    }

    [Fact]
    public async Task SetCounterAsync_WithNegativeValue_SetsNegative()
    {
        // Act
        await _store.SetCounterAsync("counter", -100);

        // Assert
        var result = await _store.GetCounterAsync("counter");
        Assert.Equal(-100, result);
    }

    [Fact]
    public async Task SetCounterAsync_WithTtl_SetsExpiration()
    {
        // Act
        await _store.SetCounterAsync("expiring-counter", 100, new StateOptions { Ttl = 1 });

        // Assert - Should exist immediately
        var valueBefore = await _store.GetCounterAsync("expiring-counter");
        Assert.Equal(100, valueBefore);

        // Wait for expiration
        await Task.Delay(1100);

        // Should be expired now
        var valueAfter = await _store.GetCounterAsync("expiring-counter");
        Assert.Null(valueAfter);
    }

    #endregion

    #region DeleteCounterAsync Tests

    [Fact]
    public async Task DeleteCounterAsync_WithExistingKey_ReturnsTrueAndDeletes()
    {
        // Arrange
        await _store.IncrementAsync("counter", 100);

        // Act
        var result = await _store.DeleteCounterAsync("counter");

        // Assert
        Assert.True(result);
        var value = await _store.GetCounterAsync("counter");
        Assert.Null(value);
    }

    [Fact]
    public async Task DeleteCounterAsync_WithNonExistentKey_ReturnsFalse()
    {
        // Act
        var result = await _store.DeleteCounterAsync("nonexistent");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task IncrementAsync_ConcurrentIncrements_AllApply()
    {
        // Arrange & Act - 100 concurrent increments
        var tasks = Enumerable.Range(1, 100).Select(_ =>
            _store.IncrementAsync("concurrent-counter", 1));

        await Task.WhenAll(tasks);

        // Assert
        var value = await _store.GetCounterAsync("concurrent-counter");
        Assert.Equal(100, value);
    }

    [Fact]
    public async Task IncrementDecrement_ConcurrentMixed_CorrectFinalValue()
    {
        // Arrange
        await _store.SetCounterAsync("mixed-counter", 100);

        // Act - 50 increments and 50 decrements concurrently
        var incrementTasks = Enumerable.Range(1, 50).Select(_ =>
            _store.IncrementAsync("mixed-counter", 2));
        var decrementTasks = Enumerable.Range(1, 50).Select(_ =>
            _store.DecrementAsync("mixed-counter", 1));

        await Task.WhenAll(incrementTasks.Concat(decrementTasks));

        // Assert - 100 + (50*2) - (50*1) = 100 + 100 - 50 = 150
        var value = await _store.GetCounterAsync("mixed-counter");
        Assert.Equal(150, value);
    }

    [Fact]
    public async Task CounterOperations_MultipleStoresWithSameName_ShareData()
    {
        // Arrange
        var sharedStoreName = $"shared-counter-{Guid.NewGuid():N}";
        var store1 = new InMemoryStateStore<TestEntity>(sharedStoreName, _mockLogger.Object);
        var store2 = new InMemoryStateStore<TestEntity>(sharedStoreName, _mockLogger.Object);

        // Act - Increment via store1
        await store1.IncrementAsync("counter", 50);

        // Assert - Read via store2
        var value = await store2.GetCounterAsync("counter");
        Assert.Equal(50, value);

        // Act - Decrement via store2
        await store2.DecrementAsync("counter", 20);

        // Assert - Read via store1
        var finalValue = await store1.GetCounterAsync("counter");
        Assert.Equal(30, finalValue);

        // Cleanup
        await store1.DeleteCounterAsync("counter");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task IncrementAsync_WithMaxLong_HandlesOverflow()
    {
        // Arrange
        await _store.SetCounterAsync("max-counter", long.MaxValue - 1);

        // Act
        var result = await _store.IncrementAsync("max-counter", 1);

        // Assert
        Assert.Equal(long.MaxValue, result);
    }

    [Fact]
    public async Task DecrementAsync_WithMinLong_HandlesUnderflow()
    {
        // Arrange
        await _store.SetCounterAsync("min-counter", long.MinValue + 1);

        // Act
        var result = await _store.DecrementAsync("min-counter", 1);

        // Assert
        Assert.Equal(long.MinValue, result);
    }

    #endregion
}
