using BeyondImmersion.BannouService.Puppetmaster.Watches;

namespace BeyondImmersion.BannouService.Puppetmaster.Tests;

/// <summary>
/// Unit tests for WatchRegistry dual-indexed registry operations.
/// </summary>
public class WatchRegistryTests
{
    private readonly WatchRegistry _registry;

    public WatchRegistryTests()
    {
        _registry = new WatchRegistry();
    }

    #region AddWatch Tests

    [Fact]
    public void AddWatch_SingleWatch_ReturnsTrue()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        // Act
        var result = _registry.AddWatch(actorId, "character", resourceId, null, null);

        // Assert
        Assert.True(result);
        Assert.Equal(1, _registry.TotalWatchCount);
    }

    [Fact]
    public void AddWatch_DuplicateWatch_ReturnsFalse()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        _registry.AddWatch(actorId, "character", resourceId, null, null);

        // Act
        var result = _registry.AddWatch(actorId, "character", resourceId, null, null);

        // Assert
        Assert.False(result);
        Assert.Equal(1, _registry.TotalWatchCount);
    }

    [Fact]
    public void AddWatch_SameActorDifferentResources_AddsAll()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var resourceId1 = Guid.NewGuid();
        var resourceId2 = Guid.NewGuid();

        // Act
        _registry.AddWatch(actorId, "character", resourceId1, null, null);
        _registry.AddWatch(actorId, "character", resourceId2, null, null);

        // Assert
        Assert.Equal(2, _registry.TotalWatchCount);
        Assert.Equal(1, _registry.WatchingActorCount);
    }

    [Fact]
    public void AddWatch_DifferentActorsSameResource_AddsAll()
    {
        // Arrange
        var actorId1 = Guid.NewGuid();
        var actorId2 = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        // Act
        _registry.AddWatch(actorId1, "character", resourceId, null, null);
        _registry.AddWatch(actorId2, "character", resourceId, null, null);

        // Assert
        Assert.Equal(2, _registry.TotalWatchCount);
        Assert.Equal(2, _registry.WatchingActorCount);
        Assert.Equal(1, _registry.WatchedResourceCount);
    }

    [Fact]
    public void AddWatch_WithSources_StoresSources()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var sources = new List<string> { "character-personality", "character-history" };

        // Act
        _registry.AddWatch(actorId, "character", resourceId, sources, null);

        // Assert
        Assert.True(_registry.HasMatchingWatch(actorId, "character", resourceId, "character-personality"));
        Assert.True(_registry.HasMatchingWatch(actorId, "character", resourceId, "character-history"));
        Assert.False(_registry.HasMatchingWatch(actorId, "character", resourceId, "character-encounter"));
    }

    [Fact]
    public void AddWatch_WithNullSources_MatchesAnything()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        // Act
        _registry.AddWatch(actorId, "character", resourceId, null, null);

        // Assert - null sources means match any source type
        Assert.True(_registry.HasMatchingWatch(actorId, "character", resourceId, "character-personality"));
        Assert.True(_registry.HasMatchingWatch(actorId, "character", resourceId, "anything"));
    }

    #endregion

    #region RemoveWatch Tests

    [Fact]
    public void RemoveWatch_ExistingWatch_ReturnsTrue()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        _registry.AddWatch(actorId, "character", resourceId, null, null);

        // Act
        var result = _registry.RemoveWatch(actorId, "character", resourceId);

        // Assert
        Assert.True(result);
        Assert.Equal(0, _registry.TotalWatchCount);
    }

    [Fact]
    public void RemoveWatch_NonexistentWatch_ReturnsFalse()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        // Act
        var result = _registry.RemoveWatch(actorId, "character", resourceId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RemoveWatch_UpdatesBothIndexes()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        _registry.AddWatch(actorId, "character", resourceId, null, null);

        // Act
        _registry.RemoveWatch(actorId, "character", resourceId);

        // Assert
        Assert.Empty(_registry.GetWatchers("character", resourceId));
        Assert.Equal(0, _registry.WatchingActorCount);
        Assert.Equal(0, _registry.WatchedResourceCount);
    }

    #endregion

    #region RemoveAllWatches Tests

    [Fact]
    public void RemoveAllWatches_WithWatches_ReturnsCount()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        _registry.AddWatch(actorId, "character", Guid.NewGuid(), null, null);
        _registry.AddWatch(actorId, "character", Guid.NewGuid(), null, null);
        _registry.AddWatch(actorId, "realm", Guid.NewGuid(), null, null);

        // Act
        var removedCount = _registry.RemoveAllWatches(actorId);

        // Assert
        Assert.Equal(3, removedCount);
        Assert.Equal(0, _registry.TotalWatchCount);
    }

    [Fact]
    public void RemoveAllWatches_NoWatches_ReturnsZero()
    {
        // Arrange
        var actorId = Guid.NewGuid();

        // Act
        var removedCount = _registry.RemoveAllWatches(actorId);

        // Assert
        Assert.Equal(0, removedCount);
    }

    [Fact]
    public void RemoveAllWatches_DoesNotAffectOtherActors()
    {
        // Arrange
        var actorId1 = Guid.NewGuid();
        var actorId2 = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        _registry.AddWatch(actorId1, "character", resourceId, null, null);
        _registry.AddWatch(actorId2, "character", resourceId, null, null);

        // Act
        _registry.RemoveAllWatches(actorId1);

        // Assert
        Assert.Equal(1, _registry.TotalWatchCount);
        var watchers = _registry.GetWatchers("character", resourceId);
        Assert.Single(watchers);
        Assert.Contains(actorId2, watchers);
    }

    #endregion

    #region GetWatchers Tests

    [Fact]
    public void GetWatchers_WithWatchers_ReturnsActorIds()
    {
        // Arrange
        var actorId1 = Guid.NewGuid();
        var actorId2 = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        _registry.AddWatch(actorId1, "character", resourceId, null, null);
        _registry.AddWatch(actorId2, "character", resourceId, null, null);

        // Act
        var watchers = _registry.GetWatchers("character", resourceId);

        // Assert
        Assert.Equal(2, watchers.Count);
        Assert.Contains(actorId1, watchers);
        Assert.Contains(actorId2, watchers);
    }

    [Fact]
    public void GetWatchers_NoWatchers_ReturnsEmpty()
    {
        // Arrange
        var resourceId = Guid.NewGuid();

        // Act
        var watchers = _registry.GetWatchers("character", resourceId);

        // Assert
        Assert.Empty(watchers);
    }

    [Fact]
    public void GetWatchers_DifferentResourceType_ReturnsEmpty()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        _registry.AddWatch(actorId, "character", resourceId, null, null);

        // Act
        var watchers = _registry.GetWatchers("realm", resourceId);

        // Assert
        Assert.Empty(watchers);
    }

    #endregion

    #region HasMatchingWatch Tests

    [Fact]
    public void HasMatchingWatch_NoWatch_ReturnsFalse()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();

        // Act
        var result = _registry.HasMatchingWatch(actorId, "character", resourceId, "character-personality");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasMatchingWatch_MatchingSource_ReturnsTrue()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var sources = new List<string> { "character-personality" };
        _registry.AddWatch(actorId, "character", resourceId, sources, null);

        // Act
        var result = _registry.HasMatchingWatch(actorId, "character", resourceId, "character-personality");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasMatchingWatch_NonMatchingSource_ReturnsFalse()
    {
        // Arrange
        var actorId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var sources = new List<string> { "character-personality" };
        _registry.AddWatch(actorId, "character", resourceId, sources, null);

        // Act
        var result = _registry.HasMatchingWatch(actorId, "character", resourceId, "character-history");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Diagnostic Properties Tests

    [Fact]
    public void DiagnosticProperties_EmptyRegistry_AllZero()
    {
        // Assert
        Assert.Equal(0, _registry.TotalWatchCount);
        Assert.Equal(0, _registry.WatchingActorCount);
        Assert.Equal(0, _registry.WatchedResourceCount);
    }

    [Fact]
    public void DiagnosticProperties_AfterOperations_Accurate()
    {
        // Arrange
        var actor1 = Guid.NewGuid();
        var actor2 = Guid.NewGuid();
        var resource1 = Guid.NewGuid();
        var resource2 = Guid.NewGuid();

        // Act
        _registry.AddWatch(actor1, "character", resource1, null, null);
        _registry.AddWatch(actor1, "character", resource2, null, null);
        _registry.AddWatch(actor2, "character", resource1, null, null);

        // Assert
        Assert.Equal(3, _registry.TotalWatchCount);
        Assert.Equal(2, _registry.WatchingActorCount);
        Assert.Equal(2, _registry.WatchedResourceCount);
    }

    #endregion
}
