namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// In-memory cache for realm clock state with configurable TTL.
/// Prevents Redis round-trips on every ABML variable resolution and clock tick.
/// </summary>
/// <remarks>
/// <para>
/// Uses a ConcurrentDictionary with timestamp-based TTL expiry controlled by
/// <c>WorldstateServiceConfiguration.ClockCacheTtlSeconds</c>.
/// </para>
/// <para>
/// <b>Thread safety</b>: All methods are safe for concurrent access.
/// </para>
/// <para>
/// <b>Cache coherence</b>: The worker calls <see cref="Update"/> after each
/// successful clock advancement. Service endpoints call <see cref="Invalidate"/>
/// on explicit mutations (ratio changes, admin advances) to force a fresh read.
/// </para>
/// </remarks>
internal interface IRealmClockCache
{
    /// <summary>
    /// Gets the cached clock state for a realm, loading from Redis if not cached
    /// or if the cached entry has expired past the configured TTL.
    /// </summary>
    /// <param name="realmId">The realm to get the clock for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The clock model, or null if no clock is initialized for this realm.</returns>
    Task<RealmClockModel?> GetOrLoadAsync(Guid realmId, CancellationToken ct);

    /// <summary>
    /// Removes a cached clock entry, forcing the next read to go to Redis.
    /// Called when the clock is explicitly mutated outside normal tick advancement.
    /// </summary>
    /// <param name="realmId">The realm whose cache entry to invalidate.</param>
    void Invalidate(Guid realmId);

    /// <summary>
    /// Updates the cached clock entry with a fresh model and resets the TTL timer.
    /// Called by the worker after each successful clock advancement to keep the
    /// cache warm without requiring a Redis round-trip on the next read.
    /// </summary>
    /// <param name="realmId">The realm whose cache entry to update.</param>
    /// <param name="model">The updated clock model.</param>
    void Update(Guid realmId, RealmClockModel model);
}
