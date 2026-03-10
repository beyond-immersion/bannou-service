// =============================================================================
// Backstory Cache Implementation
// Caches character backstory data with TTL.
// Owned by lib-character-history per service hierarchy.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BeyondImmersion.BannouService.Attributes;

namespace BeyondImmersion.BannouService.CharacterHistory.Caching;

/// <summary>
/// Caches character backstory data for actor behavior execution.
/// Composes <see cref="VariableProviderCacheBucket{TKey, TData}"/> for thread-safe
/// TTL-based caching with stale-data fallback (IMPLEMENTATION TENETS compliant).
/// </summary>
[BannouHelperService("backstory", typeof(ICharacterHistoryService), typeof(IBackstoryCache), lifetime: ServiceLifetime.Singleton)]
public sealed class BackstoryCache : IBackstoryCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VariableProviderCacheBucket<Guid, BackstoryResponse> _backstoryBucket;

    /// <summary>
    /// Creates a new backstory cache.
    /// </summary>
    public BackstoryCache(
        IServiceScopeFactory scopeFactory,
        ILogger<BackstoryCache> logger,
        CharacterHistoryServiceConfiguration config,
        ITelemetryProvider telemetryProvider)
    {
        _scopeFactory = scopeFactory;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _backstoryBucket = new VariableProviderCacheBucket<Guid, BackstoryResponse>(
            TimeSpan.FromMinutes(config.BackstoryCacheTtlMinutes),
            logger, telemetryProvider, "bannou.character-history", "BackstoryCache");
    }

    /// <inheritdoc/>
    public async Task<BackstoryResponse?> GetOrLoadAsync(Guid characterId, CancellationToken ct = default)
    {
        return await _backstoryBucket.GetOrLoadAsync(characterId, async loadCt =>
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterHistoryClient>();
            return await client.GetBackstoryAsync(
                new GetBackstoryRequest { CharacterId = characterId },
                loadCt);
        }, ct);
    }

    /// <inheritdoc/>
    public void Invalidate(Guid characterId)
    {
        _backstoryBucket.Invalidate(characterId);
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        _backstoryBucket.InvalidateAll();
    }
}
