using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// In-memory cache for calendar template models backed by MySQL.
/// Uses a ConcurrentDictionary with timestamp-based TTL expiry.
/// </summary>
internal sealed class CalendarTemplateCache : ICalendarTemplateCache
{
    private readonly IStateStore<CalendarTemplateModel> _calendarStore;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<CalendarTemplateCache> _logger;
    private readonly WorldstateServiceConfiguration _configuration;
    private readonly ConcurrentDictionary<string, (CalendarTemplateModel Calendar, DateTimeOffset LoadedAt)> _cache = new();

    /// <summary>
    /// Creates a new instance of the CalendarTemplateCache.
    /// </summary>
    /// <param name="stateStoreFactory">Factory for creating state stores.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Worldstate service configuration.</param>
    public CalendarTemplateCache(
        IStateStoreFactory stateStoreFactory,
        ITelemetryProvider telemetryProvider,
        ILogger<CalendarTemplateCache> logger,
        WorldstateServiceConfiguration configuration)
    {
        _calendarStore = stateStoreFactory.GetStore<CalendarTemplateModel>(StateStoreDefinitions.WorldstateCalendar);
        _telemetryProvider = telemetryProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc/>
    public async Task<CalendarTemplateModel?> GetOrLoadAsync(Guid gameServiceId, string templateCode, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.worldstate", "CalendarTemplateCache.GetOrLoadAsync");

        var cacheKey = $"{gameServiceId}:{templateCode}";

        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            var age = DateTimeOffset.UtcNow - cached.LoadedAt;
            if (age.TotalMinutes < _configuration.CalendarCacheTtlMinutes)
            {
                return cached.Calendar;
            }

            _logger.LogDebug("Calendar cache expired for {CacheKey}, reloading from store", cacheKey);
        }

        var storeKey = $"calendar:{gameServiceId}:{templateCode}";
        var calendar = await _calendarStore.GetAsync(storeKey, ct);
        if (calendar == null)
        {
            // Remove stale entry if it exists
            _cache.TryRemove(cacheKey, out _);
            return null;
        }

        _cache[cacheKey] = (calendar, DateTimeOffset.UtcNow);
        return calendar;
    }

    /// <inheritdoc/>
    public void Invalidate(Guid gameServiceId, string templateCode)
    {
        var cacheKey = $"{gameServiceId}:{templateCode}";
        _cache.TryRemove(cacheKey, out _);
        _logger.LogDebug("Invalidated calendar cache for {CacheKey}", cacheKey);
    }
}
