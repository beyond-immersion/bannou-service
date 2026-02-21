// =============================================================================
// Resource Snapshot Cache Interface
// Caches resource snapshots for Event Brain actors.
// =============================================================================

namespace BeyondImmersion.BannouService.Puppetmaster.Caching;

/// <summary>
/// Cache interface for resource snapshots used by Event Brain actors.
/// </summary>
/// <remarks>
/// <para>
/// Resource snapshots provide point-in-time views of entity data (characters, quests, etc.)
/// that Event Brain actors use for decision-making. Unlike Character Brain actors that
/// access their own live data via Variable Providers, Event Brain actors observe and
/// orchestrate by accessing snapshots of arbitrary entities.
/// </para>
/// <para>
/// <b>Cache Behavior</b>:
/// <list type="bullet">
///   <item>Snapshots are cached by resourceType + resourceId</item>
///   <item>Cache entries have configurable TTL (default 5 minutes)</item>
///   <item>Expired entries are lazily evicted on next access</item>
///   <item>Prefetch operations populate cache ahead of iteration</item>
/// </list>
/// </para>
/// </remarks>
public interface IResourceSnapshotCache
{
    /// <summary>
    /// Gets or loads a resource snapshot from cache or Resource service.
    /// </summary>
    /// <param name="resourceType">The resource type (e.g., "character").</param>
    /// <param name="resourceId">The resource ID.</param>
    /// <param name="filterSourceTypes">Optional filter for specific source types.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The snapshot data, or null if the resource doesn't exist.</returns>
    Task<ResourceSnapshotData?> GetOrLoadAsync(
        string resourceType,
        Guid resourceId,
        IReadOnlyList<string>? filterSourceTypes,
        CancellationToken ct);

    /// <summary>
    /// Prefetches multiple snapshots into cache for batch operations.
    /// </summary>
    /// <param name="resourceType">The resource type for all resources.</param>
    /// <param name="resourceIds">The resource IDs to prefetch.</param>
    /// <param name="filterSourceTypes">Optional filter for specific source types.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of snapshots successfully prefetched.</returns>
    Task<int> PrefetchAsync(
        string resourceType,
        IReadOnlyList<Guid> resourceIds,
        IReadOnlyList<string>? filterSourceTypes,
        CancellationToken ct);

    /// <summary>
    /// Invalidates a cached snapshot, forcing reload on next access.
    /// </summary>
    /// <param name="resourceType">The resource type.</param>
    /// <param name="resourceId">The resource ID.</param>
    void Invalidate(string resourceType, Guid resourceId);

    /// <summary>
    /// Invalidates all cached snapshots.
    /// </summary>
    void InvalidateAll();
}

/// <summary>
/// Represents cached resource snapshot data.
/// </summary>
/// <param name="ResourceType">The resource type (e.g., "character").</param>
/// <param name="ResourceId">The resource ID.</param>
/// <param name="Entries">The snapshot entries keyed by source type.</param>
/// <param name="LoadedAt">When the snapshot was loaded.</param>
public sealed record ResourceSnapshotData(
    string ResourceType,
    Guid ResourceId,
    IReadOnlyDictionary<string, ResourceSnapshotEntry> Entries,
    DateTimeOffset LoadedAt);

/// <summary>
/// Represents a single entry in a resource snapshot.
/// </summary>
/// <param name="SourceType">The source type (e.g., "character-personality").</param>
/// <param name="ServiceName">The service that provided this data.</param>
/// <param name="Data">The JSON data (decompressed).</param>
/// <param name="CollectedAt">When the data was collected.</param>
public sealed record ResourceSnapshotEntry(
    string SourceType,
    string ServiceName,
    string Data,
    DateTimeOffset CollectedAt);
