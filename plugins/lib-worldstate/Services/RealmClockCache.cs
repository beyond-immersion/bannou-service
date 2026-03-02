using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// In-memory cache for realm clock state backed by Redis.
/// Uses a ConcurrentDictionary with timestamp-based TTL expiry.
/// </summary>
internal sealed class RealmClockCache : IRealmClockCache
{
    private readonly IStateStore<RealmClockModel> _clockStore;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<RealmClockCache> _logger;
    private readonly WorldstateServiceConfiguration _configuration;
    private readonly ConcurrentDictionary<Guid, (RealmClockModel Clock, DateTimeOffset LoadedAt)> _cache = new();

    /// <summary>
    /// Creates a new instance of the RealmClockCache.
    /// </summary>
    /// <param name="stateStoreFactory">Factory for creating state stores.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Worldstate service configuration.</param>
    public RealmClockCache(
        IStateStoreFactory stateStoreFactory,
        ITelemetryProvider telemetryProvider,
        ILogger<RealmClockCache> logger,
        WorldstateServiceConfiguration configuration)
    {
        _clockStore = stateStoreFactory.GetStore<RealmClockModel>(StateStoreDefinitions.WorldstateRealmClock);
        _telemetryProvider = telemetryProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc/>
    public async Task<RealmClockModel?> GetOrLoadAsync(Guid realmId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.worldstate", "RealmClockCache.GetOrLoadAsync");

        if (_cache.TryGetValue(realmId, out var cached))
        {
            var age = DateTimeOffset.UtcNow - cached.LoadedAt;
            if (age.TotalSeconds < _configuration.ClockCacheTtlSeconds)
            {
                return cached.Clock;
            }

            _logger.LogDebug("Clock cache expired for realm {RealmId}, reloading from store", realmId);
        }

        var clockKey = $"realm:{realmId}";
        var clock = await _clockStore.GetAsync(clockKey, ct);
        if (clock == null)
        {
            // Remove stale entry if it exists
            _cache.TryRemove(realmId, out _);
            return null;
        }

        _cache[realmId] = (clock, DateTimeOffset.UtcNow);
        return clock;
    }

    /// <inheritdoc/>
    public void Invalidate(Guid realmId)
    {
        _cache.TryRemove(realmId, out _);
        _logger.LogDebug("Invalidated clock cache for realm {RealmId}", realmId);
    }

    /// <inheritdoc/>
    public void Update(Guid realmId, RealmClockModel model)
    {
        _cache[realmId] = (model, DateTimeOffset.UtcNow);
    }
}
