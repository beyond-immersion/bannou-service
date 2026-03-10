// =============================================================================
// Currency Data Cache Implementation
// Caches character currency balance data with TTL.
// Owned by lib-currency per service hierarchy.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Currency.Caching;

/// <summary>
/// Caches character currency balance data for actor behavior execution.
/// Uses ConcurrentDictionary for thread-safety (IMPLEMENTATION TENETS compliant).
/// Cache is keyed by (characterId, realmId) to support realm-scoped wallets.
/// </summary>
[BannouHelperService("currency-data", typeof(ICurrencyService), typeof(ICurrencyDataCache), lifetime: ServiceLifetime.Singleton)]
public sealed class CurrencyDataCache : ICurrencyDataCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CurrencyDataCache> _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly TimeSpan _cacheTtl;

    // characterId → (realmId → cached entry)
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, CachedEntry>> _cache = new();

    /// <summary>
    /// Creates a new currency data cache.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for creating scoped service clients.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="config">Service configuration with cache TTL.</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing.</param>
    public CurrencyDataCache(
        IServiceScopeFactory scopeFactory,
        ILogger<CurrencyDataCache> logger,
        CurrencyServiceConfiguration config,
        ITelemetryProvider telemetryProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
        _cacheTtl = TimeSpan.FromSeconds(config.ProviderCacheTtlSeconds);
    }

    /// <inheritdoc/>
    public async Task<CachedCurrencyData?> GetOrLoadAsync(Guid characterId, Guid realmId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyDataCache.GetOrLoadAsync");

        // Check cache first
        var realmCache = _cache.GetOrAdd(characterId, _ => new ConcurrentDictionary<Guid, CachedEntry>());
        if (realmCache.TryGetValue(realmId, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Currency cache hit for character {CharacterId} realm {RealmId}", characterId, realmId);
            return cached.Data;
        }

        // Load from service
        _logger.LogDebug("Currency cache miss for character {CharacterId} realm {RealmId}, loading from service", characterId, realmId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICurrencyClient>();

            var balances = new Dictionary<string, CurrencyBalance>(StringComparer.OrdinalIgnoreCase);
            var walletCount = 0;

            // Load realm-scoped wallet
            try
            {
                var realmWallet = await client.GetWalletAsync(
                    new GetWalletRequest
                    {
                        OwnerId = characterId,
                        OwnerType = EntityType.Character,
                        RealmId = realmId,
                    },
                    ct);

                if (realmWallet?.Balances != null)
                {
                    walletCount++;
                    foreach (var balance in realmWallet.Balances)
                    {
                        balances[balance.CurrencyCode] = new CurrencyBalance(
                            balance.Amount, balance.LockedAmount, balance.EffectiveAmount);
                    }
                }
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogDebug("No realm-scoped wallet for character {CharacterId} in realm {RealmId}", characterId, realmId);
            }

            // Load global wallet (no realmId)
            try
            {
                var globalWallet = await client.GetWalletAsync(
                    new GetWalletRequest
                    {
                        OwnerId = characterId,
                        OwnerType = EntityType.Character,
                    },
                    ct);

                if (globalWallet?.Balances != null)
                {
                    walletCount++;
                    foreach (var balance in globalWallet.Balances)
                    {
                        if (balances.TryGetValue(balance.CurrencyCode, out var existing))
                        {
                            // Merge: sum amounts from both wallets for same currency
                            balances[balance.CurrencyCode] = new CurrencyBalance(
                                existing.Amount + balance.Amount,
                                existing.LockedAmount + balance.LockedAmount,
                                existing.EffectiveAmount + balance.EffectiveAmount);
                        }
                        else
                        {
                            balances[balance.CurrencyCode] = new CurrencyBalance(
                                balance.Amount, balance.LockedAmount, balance.EffectiveAmount);
                        }
                    }
                }
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogDebug("No global wallet for character {CharacterId}", characterId);
            }

            var data = new CachedCurrencyData
            {
                Balances = balances,
                WalletCount = walletCount,
            };

            realmCache[realmId] = new CachedEntry(data, DateTimeOffset.UtcNow.Add(_cacheTtl));
            _logger.LogDebug("Cached currency data for character {CharacterId} realm {RealmId}: {WalletCount} wallets, {CurrencyCount} currencies",
                characterId, realmId, walletCount, balances.Count);

            return data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load currency data for character {CharacterId} realm {RealmId}", characterId, realmId);
            // Stale-if-error: return cached data if available
            return cached?.Data;
        }
    }

    /// <inheritdoc/>
    public void Invalidate(Guid characterId)
    {
        // Events don't carry realmId, so invalidate all realm entries for this character
        _cache.TryRemove(characterId, out _);
        _logger.LogDebug("Invalidated currency data cache for character {CharacterId} (all realms)", characterId);
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        _cache.Clear();
        _logger.LogInformation("Cleared all currency data cache entries");
    }

    /// <summary>
    /// Cached currency data entry with expiration time.
    /// </summary>
    private sealed record CachedEntry(CachedCurrencyData Data, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
