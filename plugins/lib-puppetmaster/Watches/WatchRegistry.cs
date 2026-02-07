// =============================================================================
// Watch Registry
// Dual-indexed registry mapping actors to watched resources and vice versa.
// =============================================================================

using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Puppetmaster.Watches;

/// <summary>
/// Dual-indexed registry mapping actors to watched resources and vice versa.
/// Thread-safe, ephemeral (in-memory only - watches are lost on restart).
/// </summary>
/// <remarks>
/// <para>
/// This registry supports the ABML <c>watch:</c> action by tracking which actors
/// are watching which resources. When resource change events arrive, the registry
/// enables efficient lookup of all actors that need to be notified.
/// </para>
/// <para>
/// Per IMPLEMENTATION TENETS (multi-instance safety), this registry is instance-local.
/// Watches are ephemeral and tied to the actor's lifetime - when an actor stops,
/// all its watches are automatically cleaned up.
/// </para>
/// </remarks>
public sealed class WatchRegistry
{
    // actorId -> { resourceKey -> WatchEntry }
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, WatchEntry>> _actorWatches = new();

    // resourceKey -> { actorId -> dummy byte }
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>> _resourceWatchers = new();

    /// <summary>
    /// Creates a composite key for a resource.
    /// </summary>
    private static string MakeResourceKey(string resourceType, Guid resourceId)
        => $"{resourceType}:{resourceId}";

    /// <summary>
    /// Register a watch for an actor on a resource.
    /// </summary>
    /// <param name="actorId">The actor registering the watch.</param>
    /// <param name="resourceType">Resource type (e.g., "character").</param>
    /// <param name="resourceId">Resource GUID.</param>
    /// <param name="sources">Optional list of source types to filter (null = all sources).</param>
    public void AddWatch(Guid actorId, string resourceType, Guid resourceId, IReadOnlyList<string>? sources)
    {
        var resourceKey = MakeResourceKey(resourceType, resourceId);
        var entry = new WatchEntry(resourceType, resourceId, sources);

        // Add to actor's watch set
        var actorWatches = _actorWatches.GetOrAdd(actorId, _ => new ConcurrentDictionary<string, WatchEntry>());
        actorWatches[resourceKey] = entry;

        // Add to resource's watcher set
        var resourceWatchers = _resourceWatchers.GetOrAdd(resourceKey, _ => new ConcurrentDictionary<Guid, byte>());
        resourceWatchers[actorId] = 0;
    }

    /// <summary>
    /// Remove a specific watch.
    /// </summary>
    /// <param name="actorId">The actor removing the watch.</param>
    /// <param name="resourceType">Resource type.</param>
    /// <param name="resourceId">Resource GUID.</param>
    /// <returns>True if the watch was found and removed.</returns>
    public bool RemoveWatch(Guid actorId, string resourceType, Guid resourceId)
    {
        var resourceKey = MakeResourceKey(resourceType, resourceId);

        // Remove from actor's watch set
        if (_actorWatches.TryGetValue(actorId, out var actorWatches))
        {
            actorWatches.TryRemove(resourceKey, out _);

            // Clean up empty actor entry
            if (actorWatches.IsEmpty)
            {
                _actorWatches.TryRemove(actorId, out _);
            }
        }

        // Remove from resource's watcher set
        if (_resourceWatchers.TryGetValue(resourceKey, out var resourceWatchers))
        {
            resourceWatchers.TryRemove(actorId, out _);

            // Clean up empty resource entry
            if (resourceWatchers.IsEmpty)
            {
                _resourceWatchers.TryRemove(resourceKey, out _);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Remove all watches for an actor (called on actor stop).
    /// </summary>
    /// <param name="actorId">The actor being stopped.</param>
    /// <returns>Number of watches removed.</returns>
    public int RemoveAllWatches(Guid actorId)
    {
        if (!_actorWatches.TryRemove(actorId, out var actorWatches))
        {
            return 0;
        }

        var count = 0;
        foreach (var kvp in actorWatches)
        {
            var resourceKey = kvp.Key;

            // Remove actor from resource's watcher set
            if (_resourceWatchers.TryGetValue(resourceKey, out var resourceWatchers))
            {
                resourceWatchers.TryRemove(actorId, out _);

                // Clean up empty resource entry
                if (resourceWatchers.IsEmpty)
                {
                    _resourceWatchers.TryRemove(resourceKey, out _);
                }
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Get all actor IDs watching a specific resource.
    /// </summary>
    /// <param name="resourceType">Resource type.</param>
    /// <param name="resourceId">Resource GUID.</param>
    /// <returns>List of watching actor IDs.</returns>
    public IReadOnlyList<Guid> GetWatchers(string resourceType, Guid resourceId)
    {
        var resourceKey = MakeResourceKey(resourceType, resourceId);

        if (_resourceWatchers.TryGetValue(resourceKey, out var watchers))
        {
            return watchers.Keys.ToList();
        }

        return [];
    }

    /// <summary>
    /// Check if an actor has a watch with matching sources.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="resourceType">Resource type.</param>
    /// <param name="resourceId">Resource GUID.</param>
    /// <param name="sourceType">The source type that changed.</param>
    /// <returns>True if the actor should be notified about this source change.</returns>
    public bool HasMatchingWatch(Guid actorId, string resourceType, Guid resourceId, string sourceType)
    {
        var resourceKey = MakeResourceKey(resourceType, resourceId);

        if (!_actorWatches.TryGetValue(actorId, out var actorWatches))
        {
            return false;
        }

        if (!actorWatches.TryGetValue(resourceKey, out var entry))
        {
            return false;
        }

        // Null sources means watch all sources
        if (entry.Sources == null || entry.Sources.Count == 0)
        {
            return true;
        }

        // Check if sourceType is in the filter list
        return entry.Sources.Contains(sourceType);
    }

    /// <summary>
    /// Get the watch entry for a specific actor and resource.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="resourceType">Resource type.</param>
    /// <param name="resourceId">Resource GUID.</param>
    /// <returns>The watch entry if found, null otherwise.</returns>
    public WatchEntry? GetWatchEntry(Guid actorId, string resourceType, Guid resourceId)
    {
        var resourceKey = MakeResourceKey(resourceType, resourceId);

        if (_actorWatches.TryGetValue(actorId, out var actorWatches) &&
            actorWatches.TryGetValue(resourceKey, out var entry))
        {
            return entry;
        }

        return null;
    }

    /// <summary>
    /// Get all watches for an actor.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <returns>List of watch entries.</returns>
    public IReadOnlyList<WatchEntry> GetWatchesForActor(Guid actorId)
    {
        if (_actorWatches.TryGetValue(actorId, out var actorWatches))
        {
            return actorWatches.Values.ToList();
        }

        return [];
    }

    /// <summary>
    /// Get the total number of active watches.
    /// </summary>
    public int TotalWatchCount => _actorWatches.Values.Sum(w => w.Count);

    /// <summary>
    /// Get the number of unique actors with watches.
    /// </summary>
    public int WatchingActorCount => _actorWatches.Count;

    /// <summary>
    /// Get the number of unique resources being watched.
    /// </summary>
    public int WatchedResourceCount => _resourceWatchers.Count;
}

/// <summary>
/// A single watch entry representing an actor's subscription to a resource.
/// </summary>
/// <param name="ResourceType">Resource type (e.g., "character", "realm").</param>
/// <param name="ResourceId">Resource GUID.</param>
/// <param name="Sources">Optional filter - only notify for changes from these source types.</param>
public sealed record WatchEntry(
    string ResourceType,
    Guid ResourceId,
    IReadOnlyList<string>? Sources);
