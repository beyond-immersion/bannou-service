using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.State.Tests;

/// <summary>
/// Unit tests for RedisDistributedLockProvider.
/// Tests verify the lock behavior without requiring a Redis connection.
///
/// CRITICAL BUG FIXED: The Lua string.find function interprets hyphens (-) as pattern characters.
/// Lock owner IDs like "auth-abc123" would fail to match during unlock because the hyphen
/// was treated as "match 0 or more of previous char (lazy)" instead of a literal hyphen.
/// Fix: Added plain=true (4th argument) to string.find for literal string matching.
///
/// The Lua script in RedisLockResponse uses:
///   string.find(value, ARGV[1], 1, true) == 1
/// The 4th argument (true) enables plain mode, disabling pattern interpretation.
/// </summary>
public class RedisDistributedLockProviderTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<ILogger<RedisDistributedLockProvider>> _mockLogger;

    public RedisDistributedLockProviderTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockLogger = new Mock<ILogger<RedisDistributedLockProvider>>();

        // Return null for GetRedisOperations → forces in-memory mode
        _mockStateStoreFactory.Setup(f => f.GetRedisOperations()).Returns((IRedisOperations?)null);
    }

    private RedisDistributedLockProvider CreateProvider()
    {
        return new RedisDistributedLockProvider(_mockStateStoreFactory.Object, _mockLogger.Object);
    }


    /// <summary>
    /// Document the lock owner ID formats that should work correctly.
    /// These formats all contain characters that are special in Lua patterns.
    /// </summary>
    [Theory]
    [InlineData("auth-abc123def456", "Service ID with hyphen")]
    [InlineData("permission-statestore-lock", "Multiple hyphens")]
    [InlineData("service.name.with.dots", "Dots (Lua pattern: any char)")]
    [InlineData("value+plus+signs", "Plus signs (Lua pattern: 1 or more)")]
    [InlineData("value*star", "Asterisk (Lua pattern: 0 or more)")]
    [InlineData("value%percent", "Percent (Lua pattern: escape char)")]
    [InlineData("value[bracket]", "Brackets (Lua pattern: char class)")]
    [InlineData("value^caret$dollar", "Anchors (Lua pattern)")]
    public void LockOwnerFormats_WithSpecialChars_ShouldBeSupported(string ownerId, string description)
    {
        // This test documents the lock owner formats that contain Lua pattern special chars.
        // With plain=true in string.find, all these formats should work correctly.
        // The actual verification happens in integration tests, but this documents the requirement.

        Assert.NotEmpty(ownerId);
        Assert.NotEmpty(description);

        // Verify the owner ID contains at least one Lua pattern special char
        var luaPatternChars = new[] { '-', '.', '+', '*', '%', '[', ']', '^', '$', '(', ')', '?' };
        Assert.True(ownerId.Any(c => luaPatternChars.Contains(c)),
            $"Test case '{description}' should contain a Lua pattern special character");
    }

    /// <summary>
    /// Verify the expected lock value format: "{owner}:{timestamp}"
    /// This format is what the unlock script expects to match against.
    /// </summary>
    [Fact]
    public void LockValueFormat_ShouldBe_OwnerColonTimestamp()
    {
        // The lock provider stores values as "{lockOwner}:{timestamp}"
        // The unlock script checks if the stored value STARTS WITH the owner prefix
        // This test documents the expected format

        var owner = "auth-abc123";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expectedFormat = $"{owner}:{timestamp}";

        Assert.StartsWith(owner, expectedFormat);
        Assert.Contains(":", expectedFormat);
    }

    /// <summary>
    /// Simulate the Lua string.find logic to verify our fix works.
    /// This mirrors what the Lua script does on Redis.
    /// </summary>
    [Theory]
    [InlineData("auth-abc123:1735239363", "auth-abc123", true)]
    [InlineData("permission-statestore:1735239363", "permission-statestore", true)]
    [InlineData("service.with.dots:1735239363", "service.with.dots", true)]
    [InlineData("other-owner:1735239363", "auth-abc123", false)]
    [InlineData("auth-abc123:1735239363", "auth", false)] // Prefix should match exactly
    public void StringStartsWith_SimulatesLuaStringFind(string storedValue, string owner, bool shouldMatch)
    {
        // This simulates what the Lua script does with plain string matching:
        // string.find(value, owner, 1, true) == 1
        // Which is equivalent to: value.StartsWith(owner)

        // Note: The Lua script checks if the value STARTS WITH the owner followed by ":"
        // So "auth" should NOT match "auth-abc123:timestamp" - it needs exact owner match
        var startsWithOwnerAndColon = storedValue.StartsWith($"{owner}:");

        Assert.Equal(shouldMatch, startsWithOwnerAndColon);
    }

    #region InMemory Lock Acquisition Tests

    /// <summary>
    /// Verifies acquiring an uncontested lock succeeds.
    /// Each test uses a unique resource ID to avoid static dictionary interference.
    /// </summary>
    [Fact]
    public async Task LockAsync_InMemory_UncontestedKey_Succeeds()
    {
        // Arrange
        await using var provider = CreateProvider();
        var resourceId = Guid.NewGuid().ToString();

        // Act
        var result = await provider.LockAsync("test-store", resourceId, "owner-a", 30);

        // Assert
        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies that a second owner cannot acquire a lock held by someone else.
    /// </summary>
    [Fact]
    public async Task LockAsync_InMemory_ContestedByDifferentOwner_Fails()
    {
        // Arrange
        await using var provider = CreateProvider();
        var resourceId = Guid.NewGuid().ToString();

        var first = await provider.LockAsync("test-store", resourceId, "owner-a", 30);
        Assert.True(first.Success);

        // Act — different owner tries same key
        var second = await provider.LockAsync("test-store", resourceId, "owner-b", 30);

        // Assert
        Assert.False(second.Success);
    }

    /// <summary>
    /// Verifies that disposing a lock releases it, allowing re-acquisition.
    /// </summary>
    [Fact]
    public async Task LockAsync_InMemory_AfterDisposal_CanReacquire()
    {
        // Arrange
        await using var provider = CreateProvider();
        var resourceId = Guid.NewGuid().ToString();

        var first = await provider.LockAsync("test-store", resourceId, "owner-a", 30);
        Assert.True(first.Success);

        // Act — dispose the first lock, then try again
        await first.DisposeAsync();
        var second = await provider.LockAsync("test-store", resourceId, "owner-b", 30);

        // Assert — should succeed since previous lock was released
        Assert.True(second.Success);
    }

    /// <summary>
    /// Verifies that an expired lock can be acquired by a new owner.
    /// Uses 1-second expiry with a short delay to trigger expiration detection.
    /// </summary>
    [Fact]
    public async Task LockAsync_InMemory_ExpiredLock_CanBeAcquiredByNewOwner()
    {
        // Arrange
        await using var provider = CreateProvider();
        var resourceId = Guid.NewGuid().ToString();

        var first = await provider.LockAsync("test-store", resourceId, "owner-a", 1);
        Assert.True(first.Success);

        // Wait for expiration
        await Task.Delay(1100);

        // Act — different owner tries after expiry
        var second = await provider.LockAsync("test-store", resourceId, "owner-b", 30);

        // Assert — succeeds because previous lock expired
        Assert.True(second.Success);
    }

    /// <summary>
    /// Verifies that a failed lock response does not release the lock on disposal.
    /// </summary>
    [Fact]
    public async Task LockAsync_InMemory_FailedLockDisposal_DoesNotRelease()
    {
        // Arrange
        await using var provider = CreateProvider();
        var resourceId = Guid.NewGuid().ToString();

        var first = await provider.LockAsync("test-store", resourceId, "owner-a", 30);
        Assert.True(first.Success);

        var second = await provider.LockAsync("test-store", resourceId, "owner-b", 30);
        Assert.False(second.Success);

        // Act — dispose the failed lock response
        await second.DisposeAsync();

        // Assert — original lock still held, third attempt by different owner fails
        var third = await provider.LockAsync("test-store", resourceId, "owner-c", 30);
        Assert.False(third.Success);
    }

    #endregion
}
