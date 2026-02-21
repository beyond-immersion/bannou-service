using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Services;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Unit tests for InMemoryStateStore sorted set operations.
/// Tests the ICacheableStateStore sorted set implementation for in-memory backend.
/// </summary>
public class InMemorySortedSetTests : IDisposable
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

    public InMemorySortedSetTests()
    {
        _mockLogger = new Mock<ILogger<InMemoryStateStore<TestEntity>>>();
        _storeName = $"test-sortedset-store-{Guid.NewGuid():N}";
        _store = new InMemoryStateStore<TestEntity>(_storeName, _mockLogger.Object);
    }

    public void Dispose()
    {
        _store.Clear();
    }

    #region SortedSetAddAsync Tests

    [Fact]
    public async Task SortedSetAddAsync_WithNewMember_ReturnsTrue()
    {
        // Act
        var result = await _store.SortedSetAddAsync("leaderboard", "player1", 100.0);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task SortedSetAddAsync_WithExistingMember_ReturnsFalseAndUpdatesScore()
    {
        // Arrange
        await _store.SortedSetAddAsync("leaderboard", "player1", 100.0);

        // Act
        var result = await _store.SortedSetAddAsync("leaderboard", "player1", 200.0);

        // Assert
        Assert.False(result); // Already exists
        var score = await _store.SortedSetScoreAsync("leaderboard", "player1");
        Assert.Equal(200.0, score); // Score updated
    }

    [Fact]
    public async Task SortedSetAddAsync_WithTtl_SetsExpiration()
    {
        // Arrange
        await _store.SortedSetAddAsync("expiring-leaderboard", "player1", 100.0, new StateOptions { Ttl = 1 });

        // Assert - Should exist immediately
        var scoreBefore = await _store.SortedSetScoreAsync("expiring-leaderboard", "player1");
        Assert.NotNull(scoreBefore);

        // Wait for expiration
        await Task.Delay(1100);

        // Should be expired now
        var scoreAfter = await _store.SortedSetScoreAsync("expiring-leaderboard", "player1");
        Assert.Null(scoreAfter);
    }

    #endregion

    #region SortedSetAddBatchAsync Tests

    [Fact]
    public async Task SortedSetAddBatchAsync_WithNewMembers_ReturnsCount()
    {
        // Arrange
        var entries = new[]
        {
            ("player1", 100.0),
            ("player2", 200.0),
            ("player3", 150.0)
        };

        // Act
        var result = await _store.SortedSetAddBatchAsync("leaderboard", entries);

        // Assert
        Assert.Equal(3, result);
    }

    [Fact]
    public async Task SortedSetAddBatchAsync_WithPartialExisting_ReturnsNewCount()
    {
        // Arrange
        await _store.SortedSetAddAsync("leaderboard", "player1", 100.0);

        var entries = new[]
        {
            ("player1", 150.0), // Existing
            ("player2", 200.0), // New
            ("player3", 250.0)  // New
        };

        // Act
        var result = await _store.SortedSetAddBatchAsync("leaderboard", entries);

        // Assert
        Assert.Equal(2, result); // Only 2 new members

        // Verify player1's score was updated
        var score = await _store.SortedSetScoreAsync("leaderboard", "player1");
        Assert.Equal(150.0, score);
    }

    [Fact]
    public async Task SortedSetAddBatchAsync_WithEmptyEntries_ReturnsZero()
    {
        // Act
        var result = await _store.SortedSetAddBatchAsync("leaderboard", Array.Empty<(string, double)>());

        // Assert
        Assert.Equal(0, result);
    }

    #endregion

    #region SortedSetRemoveAsync Tests

    [Fact]
    public async Task SortedSetRemoveAsync_WithExistingMember_ReturnsTrue()
    {
        // Arrange
        await _store.SortedSetAddAsync("leaderboard", "player1", 100.0);

        // Act
        var result = await _store.SortedSetRemoveAsync("leaderboard", "player1");

        // Assert
        Assert.True(result);
        var score = await _store.SortedSetScoreAsync("leaderboard", "player1");
        Assert.Null(score);
    }

    [Fact]
    public async Task SortedSetRemoveAsync_WithNonExistentMember_ReturnsFalse()
    {
        // Arrange
        await _store.SortedSetAddAsync("leaderboard", "player1", 100.0);

        // Act
        var result = await _store.SortedSetRemoveAsync("leaderboard", "player2");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SortedSetRemoveAsync_WithNonExistentSet_ReturnsFalse()
    {
        // Act
        var result = await _store.SortedSetRemoveAsync("nonexistent", "player1");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region SortedSetScoreAsync Tests

    [Fact]
    public async Task SortedSetScoreAsync_WithExistingMember_ReturnsScore()
    {
        // Arrange
        await _store.SortedSetAddAsync("leaderboard", "player1", 100.5);

        // Act
        var score = await _store.SortedSetScoreAsync("leaderboard", "player1");

        // Assert
        Assert.Equal(100.5, score);
    }

    [Fact]
    public async Task SortedSetScoreAsync_WithNonExistentMember_ReturnsNull()
    {
        // Arrange
        await _store.SortedSetAddAsync("leaderboard", "player1", 100.0);

        // Act
        var score = await _store.SortedSetScoreAsync("leaderboard", "player2");

        // Assert
        Assert.Null(score);
    }

    [Fact]
    public async Task SortedSetScoreAsync_WithNonExistentSet_ReturnsNull()
    {
        // Act
        var score = await _store.SortedSetScoreAsync("nonexistent", "player1");

        // Assert
        Assert.Null(score);
    }

    #endregion

    #region SortedSetRankAsync Tests

    [Fact]
    public async Task SortedSetRankAsync_Descending_ReturnsCorrectRank()
    {
        // Arrange
        await _store.SortedSetAddBatchAsync("leaderboard", new[]
        {
            ("player1", 100.0),
            ("player2", 200.0),
            ("player3", 150.0)
        });

        // Act - Descending (highest score = rank 0)
        var rank = await _store.SortedSetRankAsync("leaderboard", "player2", descending: true);

        // Assert
        Assert.Equal(0, rank); // player2 has highest score (200)
    }

    [Fact]
    public async Task SortedSetRankAsync_Ascending_ReturnsCorrectRank()
    {
        // Arrange
        await _store.SortedSetAddBatchAsync("leaderboard", new[]
        {
            ("player1", 100.0),
            ("player2", 200.0),
            ("player3", 150.0)
        });

        // Act - Ascending (lowest score = rank 0)
        var rank = await _store.SortedSetRankAsync("leaderboard", "player1", descending: false);

        // Assert
        Assert.Equal(0, rank); // player1 has lowest score (100)
    }

    [Fact]
    public async Task SortedSetRankAsync_WithNonExistentMember_ReturnsNull()
    {
        // Arrange
        await _store.SortedSetAddAsync("leaderboard", "player1", 100.0);

        // Act
        var rank = await _store.SortedSetRankAsync("leaderboard", "player2");

        // Assert
        Assert.Null(rank);
    }

    [Fact]
    public async Task SortedSetRankAsync_WithDuplicateScores_HandlesCorrectly()
    {
        // Arrange - players with same score
        await _store.SortedSetAddBatchAsync("leaderboard", new[]
        {
            ("player1", 100.0),
            ("player2", 100.0),
            ("player3", 200.0)
        });

        // Act
        var rank1 = await _store.SortedSetRankAsync("leaderboard", "player1", descending: true);
        var rank2 = await _store.SortedSetRankAsync("leaderboard", "player2", descending: true);
        var rank3 = await _store.SortedSetRankAsync("leaderboard", "player3", descending: true);

        // Assert
        Assert.Equal(0, rank3); // Highest score
        // player1 and player2 have same score, ranks depend on stable ordering
        Assert.True(rank1 >= 1 && rank1 <= 2);
        Assert.True(rank2 >= 1 && rank2 <= 2);
    }

    #endregion

    #region SortedSetRangeByRankAsync Tests

    [Fact]
    public async Task SortedSetRangeByRankAsync_Descending_ReturnsTopPlayers()
    {
        // Arrange
        await _store.SortedSetAddBatchAsync("leaderboard", new[]
        {
            ("player1", 100.0),
            ("player2", 200.0),
            ("player3", 150.0),
            ("player4", 50.0)
        });

        // Act - Get top 3 (descending)
        var result = await _store.SortedSetRangeByRankAsync("leaderboard", 0, 2, descending: true);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("player2", result[0].member);
        Assert.Equal(200.0, result[0].score);
        Assert.Equal("player3", result[1].member);
        Assert.Equal(150.0, result[1].score);
        Assert.Equal("player1", result[2].member);
        Assert.Equal(100.0, result[2].score);
    }

    [Fact]
    public async Task SortedSetRangeByRankAsync_Ascending_ReturnsBottomPlayers()
    {
        // Arrange
        await _store.SortedSetAddBatchAsync("leaderboard", new[]
        {
            ("player1", 100.0),
            ("player2", 200.0),
            ("player3", 150.0)
        });

        // Act - Get bottom 2 (ascending)
        var result = await _store.SortedSetRangeByRankAsync("leaderboard", 0, 1, descending: false);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("player1", result[0].member);
        Assert.Equal(100.0, result[0].score);
        Assert.Equal("player3", result[1].member);
        Assert.Equal(150.0, result[1].score);
    }

    [Fact]
    public async Task SortedSetRangeByRankAsync_WithNonExistentSet_ReturnsEmpty()
    {
        // Act
        var result = await _store.SortedSetRangeByRankAsync("nonexistent", 0, 9);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task SortedSetRangeByRankAsync_WithOutOfBoundsRange_ReturnsAvailable()
    {
        // Arrange
        await _store.SortedSetAddBatchAsync("leaderboard", new[]
        {
            ("player1", 100.0),
            ("player2", 200.0)
        });

        // Act - Request more than available
        var result = await _store.SortedSetRangeByRankAsync("leaderboard", 0, 99);

        // Assert
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region SortedSetRangeByScoreAsync Tests

    [Fact]
    public async Task SortedSetRangeByScoreAsync_ReturnsPlayersInScoreRange()
    {
        // Arrange
        await _store.SortedSetAddBatchAsync("leaderboard", new[]
        {
            ("player1", 100.0),
            ("player2", 200.0),
            ("player3", 150.0),
            ("player4", 250.0)
        });

        // Act - Get players with score 100-200 (inclusive)
        var result = await _store.SortedSetRangeByScoreAsync("leaderboard", 100.0, 200.0);

        // Assert
        Assert.Equal(3, result.Count);
        // Ascending by default
        Assert.Equal("player1", result[0].member);
        Assert.Equal("player3", result[1].member);
        Assert.Equal("player2", result[2].member);
    }

    [Fact]
    public async Task SortedSetRangeByScoreAsync_Descending_ReturnsReversed()
    {
        // Arrange
        await _store.SortedSetAddBatchAsync("leaderboard", new[]
        {
            ("player1", 100.0),
            ("player2", 200.0),
            ("player3", 150.0)
        });

        // Act
        var result = await _store.SortedSetRangeByScoreAsync("leaderboard", 100.0, 200.0, descending: true);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("player2", result[0].member); // Highest first
        Assert.Equal("player3", result[1].member);
        Assert.Equal("player1", result[2].member); // Lowest last
    }

    [Fact]
    public async Task SortedSetRangeByScoreAsync_WithOffsetAndCount_ReturnsPaginated()
    {
        // Arrange
        await _store.SortedSetAddBatchAsync("leaderboard", new[]
        {
            ("player1", 100.0),
            ("player2", 200.0),
            ("player3", 150.0),
            ("player4", 175.0)
        });

        // Act - Skip first, take 2
        var result = await _store.SortedSetRangeByScoreAsync("leaderboard", 100.0, 200.0, offset: 1, count: 2);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("player3", result[0].member); // 150
        Assert.Equal("player4", result[1].member); // 175
    }

    [Fact]
    public async Task SortedSetRangeByScoreAsync_WithNoMatchingRange_ReturnsEmpty()
    {
        // Arrange
        await _store.SortedSetAddBatchAsync("leaderboard", new[]
        {
            ("player1", 100.0),
            ("player2", 200.0)
        });

        // Act - Score range with no matches
        var result = await _store.SortedSetRangeByScoreAsync("leaderboard", 300.0, 400.0);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region SortedSetCountAsync Tests

    [Fact]
    public async Task SortedSetCountAsync_ReturnsCorrectCount()
    {
        // Arrange
        await _store.SortedSetAddBatchAsync("leaderboard", new[]
        {
            ("player1", 100.0),
            ("player2", 200.0),
            ("player3", 150.0)
        });

        // Act
        var count = await _store.SortedSetCountAsync("leaderboard");

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task SortedSetCountAsync_WithNonExistentSet_ReturnsZero()
    {
        // Act
        var count = await _store.SortedSetCountAsync("nonexistent");

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SortedSetCountAsync_WithExpiredSet_ReturnsZero()
    {
        // Arrange
        await _store.SortedSetAddAsync("expiring", "player1", 100.0, new StateOptions { Ttl = 1 });

        // Wait for expiration
        await Task.Delay(1100);

        // Act
        var count = await _store.SortedSetCountAsync("expiring");

        // Assert
        Assert.Equal(0, count);
    }

    #endregion

    #region SortedSetIncrementAsync Tests

    [Fact]
    public async Task SortedSetIncrementAsync_WithExistingMember_IncrementsScore()
    {
        // Arrange
        await _store.SortedSetAddAsync("leaderboard", "player1", 100.0);

        // Act
        var newScore = await _store.SortedSetIncrementAsync("leaderboard", "player1", 50.0);

        // Assert
        Assert.Equal(150.0, newScore);

        var storedScore = await _store.SortedSetScoreAsync("leaderboard", "player1");
        Assert.Equal(150.0, storedScore);
    }

    [Fact]
    public async Task SortedSetIncrementAsync_WithNewMember_CreatesMemberWithIncrement()
    {
        // Act
        var newScore = await _store.SortedSetIncrementAsync("leaderboard", "player1", 100.0);

        // Assert
        Assert.Equal(100.0, newScore);
    }

    [Fact]
    public async Task SortedSetIncrementAsync_WithNegativeIncrement_DecrementsScore()
    {
        // Arrange
        await _store.SortedSetAddAsync("leaderboard", "player1", 100.0);

        // Act
        var newScore = await _store.SortedSetIncrementAsync("leaderboard", "player1", -30.0);

        // Assert
        Assert.Equal(70.0, newScore);
    }

    #endregion

    #region SortedSetDeleteAsync Tests

    [Fact]
    public async Task SortedSetDeleteAsync_WithExistingSet_ReturnsTrueAndDeletesAll()
    {
        // Arrange
        await _store.SortedSetAddBatchAsync("leaderboard", new[]
        {
            ("player1", 100.0),
            ("player2", 200.0)
        });

        // Act
        var result = await _store.SortedSetDeleteAsync("leaderboard");

        // Assert
        Assert.True(result);

        var count = await _store.SortedSetCountAsync("leaderboard");
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SortedSetDeleteAsync_WithNonExistentSet_ReturnsFalse()
    {
        // Act
        var result = await _store.SortedSetDeleteAsync("nonexistent");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async Task SortedSetOperations_ConcurrentAdds_AllSucceed()
    {
        // Arrange & Act
        var tasks = Enumerable.Range(1, 100).Select(i =>
            _store.SortedSetAddAsync("concurrent-leaderboard", $"player{i}", i * 10.0));

        await Task.WhenAll(tasks);

        // Assert
        var count = await _store.SortedSetCountAsync("concurrent-leaderboard");
        Assert.Equal(100, count);
    }

    [Fact]
    public async Task SortedSetOperations_ConcurrentIncrements_AllApply()
    {
        // Arrange
        await _store.SortedSetAddAsync("increment-test", "player1", 0.0);

        // Act - 100 concurrent increments of 1
        var tasks = Enumerable.Range(1, 100).Select(_ =>
            _store.SortedSetIncrementAsync("increment-test", "player1", 1.0));

        await Task.WhenAll(tasks);

        // Assert
        var score = await _store.SortedSetScoreAsync("increment-test", "player1");
        Assert.Equal(100.0, score);
    }

    [Fact]
    public async Task SortedSetOperations_MultipleStoresWithSameName_ShareData()
    {
        // Arrange
        var sharedStoreName = $"shared-sortedset-{Guid.NewGuid():N}";
        var store1 = new InMemoryStateStore<TestEntity>(sharedStoreName, _mockLogger.Object);
        var store2 = new InMemoryStateStore<TestEntity>(sharedStoreName, _mockLogger.Object);

        // Act - Add via store1
        await store1.SortedSetAddAsync("leaderboard", "player1", 100.0);

        // Assert - Retrieve via store2
        var score = await store2.SortedSetScoreAsync("leaderboard", "player1");
        Assert.Equal(100.0, score);

        // Cleanup
        await store1.SortedSetDeleteAsync("leaderboard");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task SortedSetAddAsync_WithNegativeScore_Works()
    {
        // Act
        await _store.SortedSetAddAsync("leaderboard", "player1", -100.0);

        // Assert
        var score = await _store.SortedSetScoreAsync("leaderboard", "player1");
        Assert.Equal(-100.0, score);
    }

    [Fact]
    public async Task SortedSetAddAsync_WithVeryLargeScore_Works()
    {
        // Act
        await _store.SortedSetAddAsync("leaderboard", "player1", double.MaxValue / 2);

        // Assert
        var score = await _store.SortedSetScoreAsync("leaderboard", "player1");
        Assert.Equal(double.MaxValue / 2, score);
    }

    [Fact]
    public async Task SortedSetAddAsync_WithZeroScore_Works()
    {
        // Act
        await _store.SortedSetAddAsync("leaderboard", "player1", 0.0);

        // Assert
        var score = await _store.SortedSetScoreAsync("leaderboard", "player1");
        Assert.Equal(0.0, score);
    }

    #endregion
}
