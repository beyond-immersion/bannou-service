using BeyondImmersion.BannouService.Resource;

namespace BeyondImmersion.BannouService.Resource.Tests;

/// <summary>
/// Unit tests for ResourceReferenceEntry custom equality and hash code.
/// Correctness is critical since these objects are used as HashSet members
/// for reference counting.
/// </summary>
public class ResourceReferenceEntryTests
{
    #region Equals Tests

    /// <summary>
    /// Verifies that two entries with the same SourceType and SourceId are equal.
    /// </summary>
    [Fact]
    public void Equals_SameSourceTypeAndId_ReturnsTrue()
    {
        var a = new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" };
        var b = new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" };

        Assert.True(a.Equals(b));
        Assert.True(b.Equals(a));
    }

    /// <summary>
    /// Verifies that entries with different SourceType are not equal.
    /// </summary>
    [Fact]
    public void Equals_DifferentSourceType_ReturnsFalse()
    {
        var a = new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" };
        var b = new ResourceReferenceEntry { SourceType = "character", SourceId = "abc-123" };

        Assert.False(a.Equals(b));
    }

    /// <summary>
    /// Verifies that entries with different SourceId are not equal.
    /// </summary>
    [Fact]
    public void Equals_DifferentSourceId_ReturnsFalse()
    {
        var a = new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" };
        var b = new ResourceReferenceEntry { SourceType = "actor", SourceId = "xyz-789" };

        Assert.False(a.Equals(b));
    }

    /// <summary>
    /// Verifies that RegisteredAt does NOT affect equality (only SourceType and SourceId matter).
    /// </summary>
    [Fact]
    public void Equals_DifferentRegisteredAt_StillEqual()
    {
        var a = new ResourceReferenceEntry
        {
            SourceType = "actor",
            SourceId = "abc-123",
            RegisteredAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        var b = new ResourceReferenceEntry
        {
            SourceType = "actor",
            SourceId = "abc-123",
            RegisteredAt = DateTimeOffset.UtcNow
        };

        Assert.True(a.Equals(b));
    }

    /// <summary>
    /// Verifies that Equals returns false for non-ResourceReferenceEntry objects.
    /// </summary>
    [Fact]
    public void Equals_DifferentType_ReturnsFalse()
    {
        var entry = new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" };

        Assert.False(entry.Equals("not a reference entry"));
        Assert.False(entry.Equals(42));
        Assert.False(entry.Equals(null));
    }

    #endregion

    #region GetHashCode Tests

    /// <summary>
    /// Verifies that equal entries produce the same hash code.
    /// </summary>
    [Fact]
    public void GetHashCode_EqualEntries_SameHashCode()
    {
        var a = new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" };
        var b = new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" };

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// Verifies that different entries produce different hash codes (not guaranteed but likely).
    /// </summary>
    [Fact]
    public void GetHashCode_DifferentEntries_DifferentHashCodes()
    {
        var a = new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" };
        var b = new ResourceReferenceEntry { SourceType = "character", SourceId = "xyz-789" };

        // Hash collisions are theoretically possible but extremely unlikely for distinct strings
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    #endregion

    #region HashSet Behavior Tests

    /// <summary>
    /// Verifies that ResourceReferenceEntry works correctly as a HashSet member,
    /// which is its primary use case in reference counting.
    /// </summary>
    [Fact]
    public void HashSet_DuplicateEntries_AreDeduped()
    {
        var set = new HashSet<ResourceReferenceEntry>
        {
            new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" },
            new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" }, // duplicate
            new ResourceReferenceEntry { SourceType = "character", SourceId = "abc-123" }, // different type
        };

        Assert.Equal(2, set.Count);
    }

    /// <summary>
    /// Verifies that Contains works correctly with the custom equality.
    /// </summary>
    [Fact]
    public void HashSet_Contains_FindsMatchingEntry()
    {
        var set = new HashSet<ResourceReferenceEntry>
        {
            new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" },
        };

        // New instance with same values should be found
        var lookup = new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" };
        Assert.Contains(lookup, set);
    }

    /// <summary>
    /// Verifies that Remove works correctly with the custom equality.
    /// </summary>
    [Fact]
    public void HashSet_Remove_RemovesMatchingEntry()
    {
        var set = new HashSet<ResourceReferenceEntry>
        {
            new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" },
            new ResourceReferenceEntry { SourceType = "character", SourceId = "xyz-789" },
        };

        // Remove using a new instance with matching values
        var toRemove = new ResourceReferenceEntry { SourceType = "actor", SourceId = "abc-123" };
        var removed = set.Remove(toRemove);

        Assert.True(removed);
        Assert.Single(set);
    }

    #endregion
}
