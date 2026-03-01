namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// In-memory cache for calendar template models with configurable TTL.
/// Calendar structures change rarely; caching avoids MySQL queries on every clock tick
/// and variable provider resolution.
/// </summary>
/// <remarks>
/// <para>
/// Uses a ConcurrentDictionary with timestamp-based TTL expiry controlled by
/// <c>WorldstateServiceConfiguration.CalendarCacheTtlMinutes</c>.
/// </para>
/// <para>
/// <b>Thread safety</b>: All methods are safe for concurrent access.
/// </para>
/// <para>
/// <b>Cache coherence</b>: Cross-node invalidation is handled by the
/// <c>worldstate.calendar-template.updated</c> event subscription in
/// <c>WorldstateServiceEvents.HandleCalendarTemplateUpdatedAsync</c>.
/// When any node updates a calendar template, all nodes receive the event
/// and call <see cref="Invalidate"/> to clear their local cache.
/// </para>
/// </remarks>
internal interface ICalendarTemplateCache
{
    /// <summary>
    /// Gets the cached calendar template, loading from MySQL if not cached
    /// or if the cached entry has expired past the configured TTL.
    /// </summary>
    /// <param name="gameServiceId">The game service that owns the calendar template.</param>
    /// <param name="templateCode">The calendar template code.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The calendar template model, or null if the template does not exist.</returns>
    Task<CalendarTemplateModel?> GetOrLoadAsync(Guid gameServiceId, string templateCode, CancellationToken ct);

    /// <summary>
    /// Removes a cached calendar template entry, forcing the next read to go to MySQL.
    /// Called when a calendar template is updated or deleted, and on cross-node
    /// event-driven cache invalidation.
    /// </summary>
    /// <param name="gameServiceId">The game service that owns the calendar template.</param>
    /// <param name="templateCode">The calendar template code.</param>
    void Invalidate(Guid gameServiceId, string templateCode);
}
