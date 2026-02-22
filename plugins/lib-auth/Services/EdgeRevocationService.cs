using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Orchestrates token revocation across edge providers.
/// Maintains local Redis revocation list and coordinates pushes to all enabled providers.
/// </summary>
/// <remarks>
/// <para>
/// This service implements the edge revocation system per IMPLEMENTATION TENETS:
/// - Uses lib-state for all Redis access (no direct connections)
/// - Uses IMessageBus for error event publishing
/// - Implements timeout handling with CancellationTokenSource
/// </para>
/// </remarks>
public class EdgeRevocationService : IEdgeRevocationService
{
    /// <summary>
    /// Buffer seconds added to JWT expiration for account revocation TTL.
    /// Ensures revocation entry outlives all access tokens issued before the revocation.
    /// </summary>
    private const int RevocationTtlBufferSeconds = 300;

    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IEnumerable<IEdgeRevocationProvider> _providers;
    private readonly IMessageBus _messageBus;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<EdgeRevocationService> _logger;
    private readonly AuthServiceConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of EdgeRevocationService.
    /// </summary>
    /// <param name="stateStoreFactory">Factory for accessing state stores.</param>
    /// <param name="providers">Collection of edge revocation providers.</param>
    /// <param name="messageBus">Message bus for error event publishing.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="configuration">Auth service configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public EdgeRevocationService(
        IStateStoreFactory stateStoreFactory,
        IEnumerable<IEdgeRevocationProvider> providers,
        IMessageBus messageBus,
        ITelemetryProvider telemetryProvider,
        AuthServiceConfiguration configuration,
        ILogger<EdgeRevocationService> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _providers = providers;
        _messageBus = messageBus;
        _telemetryProvider = telemetryProvider;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsEnabled => _configuration.EdgeRevocationEnabled;

    /// <inheritdoc/>
    public async Task RevokeTokenAsync(string jti, Guid accountId, TimeSpan ttl, string reason, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "EdgeRevocationService.RevokeToken");
        if (!IsEnabled)
        {
            return;
        }

        _logger.LogInformation("Revoking token at edge: JTI={Jti}, AccountId={AccountId}, TTL={Ttl}, Reason={Reason}",
            jti, accountId, ttl, reason);

        // Store locally first (lib-state per FOUNDATION TENETS)
        var entry = new TokenRevocationEntry
        {
            Jti = jti,
            AccountId = accountId,
            RevokedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl),
            Reason = reason
        };

        var tokenStore = _stateStoreFactory.GetStore<TokenRevocationEntry>(StateStoreDefinitions.EdgeRevocation);
        var ttlSeconds = (int)ttl.TotalSeconds;
        await tokenStore.SaveAsync($"token:{jti}", entry, new StateOptions { Ttl = ttlSeconds }, ct);

        // Add to index for listing
        var indexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.EdgeRevocation);
        await AddToIndexAsync(indexStore, "token-index", jti, ct);

        // Retry any failed pushes from previous attempts
        await RetryFailedPushesAsync(ct);

        // Push to all enabled providers with timeout
        await PushToProvidersAsync("token", jti, accountId, ttlSeconds, null, ct);
    }

    /// <inheritdoc/>
    public async Task RevokeAccountAsync(Guid accountId, DateTimeOffset issuedBefore, string reason, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "EdgeRevocationService.RevokeAccount");
        if (!IsEnabled)
        {
            return;
        }

        _logger.LogInformation("Revoking account tokens at edge: AccountId={AccountId}, IssuedBefore={IssuedBefore}, Reason={Reason}",
            accountId, issuedBefore, reason);

        // Store locally (lib-state per FOUNDATION TENETS)
        var entry = new AccountRevocationEntry
        {
            AccountId = accountId,
            IssuedBefore = issuedBefore,
            RevokedAt = DateTimeOffset.UtcNow,
            Reason = reason
        };

        var accountStore = _stateStoreFactory.GetStore<AccountRevocationEntry>(StateStoreDefinitions.EdgeRevocation);
        // Account revocations expire after all access tokens issued before issuedBefore have naturally expired.
        // TTL = JWT lifetime + buffer. After this, no valid access token can predate the revocation.
        var accountRevocationTtlSeconds = (_configuration.JwtExpirationMinutes * 60) + RevocationTtlBufferSeconds;
        await accountStore.SaveAsync($"account:{accountId}", entry, new StateOptions { Ttl = accountRevocationTtlSeconds }, ct);

        // Add to index for listing
        var indexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.EdgeRevocation);
        await AddToIndexAsync(indexStore, "account-index", accountId.ToString(), ct);

        // Retry any failed pushes from previous attempts
        await RetryFailedPushesAsync(ct);

        // Push to all enabled providers
        await PushToProvidersAsync("account", null, accountId, null, issuedBefore.ToUnixTimeSeconds(), ct);
    }

    /// <inheritdoc/>
    public async Task<(List<RevokedTokenEntry> tokens, List<RevokedAccountEntry> accounts, int failedCount, int? totalTokenCount)> GetRevocationListAsync(
        bool includeTokens, bool includeAccounts, int limit, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "EdgeRevocationService.GetRevocationList");
        var tokens = new List<RevokedTokenEntry>();
        var accounts = new List<RevokedAccountEntry>();
        int? totalTokenCount = null;

        var indexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.EdgeRevocation);

        if (includeTokens)
        {
            var tokenIndex = await indexStore.GetAsync("token-index", ct) ?? new List<string>();
            totalTokenCount = tokenIndex.Count;

            var tokenStore = _stateStoreFactory.GetStore<TokenRevocationEntry>(StateStoreDefinitions.EdgeRevocation);
            var count = 0;
            foreach (var jti in tokenIndex.Take(limit))
            {
                var entry = await tokenStore.GetAsync($"token:{jti}", ct);
                if (entry != null)
                {
                    tokens.Add(new RevokedTokenEntry
                    {
                        Jti = entry.Jti ?? throw new InvalidOperationException($"TokenRevocationEntry missing Jti for key token:{jti}"),
                        AccountId = entry.AccountId,
                        RevokedAt = entry.RevokedAt,
                        ExpiresAt = entry.ExpiresAt,
                        Reason = entry.Reason ?? throw new InvalidOperationException($"TokenRevocationEntry missing Reason for key token:{jti}")
                    });
                    count++;
                    if (count >= limit)
                    {
                        break;
                    }
                }
            }
        }

        if (includeAccounts)
        {
            var accountIndex = await indexStore.GetAsync("account-index", ct) ?? new List<string>();
            var accountStore = _stateStoreFactory.GetStore<AccountRevocationEntry>(StateStoreDefinitions.EdgeRevocation);
            var count = 0;
            foreach (var accountIdStr in accountIndex.Take(limit))
            {
                if (Guid.TryParse(accountIdStr, out var accountId))
                {
                    var entry = await accountStore.GetAsync($"account:{accountId}", ct);
                    if (entry != null)
                    {
                        accounts.Add(new RevokedAccountEntry
                        {
                            AccountId = entry.AccountId,
                            IssuedBefore = entry.IssuedBefore,
                            RevokedAt = entry.RevokedAt,
                            Reason = entry.Reason ?? throw new InvalidOperationException($"AccountRevocationEntry missing Reason for account:{accountId}")
                        });
                        count++;
                        if (count >= limit)
                        {
                            break;
                        }
                    }
                }
            }
        }

        // Get failed push count
        var failedPushIndex = await indexStore.GetAsync("failed-push-index", ct) ?? new List<string>();
        var failedCount = failedPushIndex.Count;

        return (tokens, accounts, failedCount, totalTokenCount);
    }

    /// <summary>
    /// Pushes revocation to all enabled providers with timeout handling.
    /// </summary>
    private async Task PushToProvidersAsync(string type, string? jti, Guid accountId, int? ttlSeconds, long? issuedBeforeUnix, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "EdgeRevocationService.PushToProviders");
        var enabledProviders = _providers.Where(p => p.IsEnabled).ToList();
        if (enabledProviders.Count == 0)
        {
            _logger.LogDebug("No edge providers enabled, skipping push");
            return;
        }

        foreach (var provider in enabledProviders)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_configuration.EdgeRevocationTimeoutSeconds));

            try
            {
                bool success;
                if (type == "token" && jti != null && ttlSeconds.HasValue)
                {
                    success = await provider.PushTokenRevocationAsync(jti, accountId, TimeSpan.FromSeconds(ttlSeconds.Value), timeoutCts.Token);
                }
                else if (type == "account" && issuedBeforeUnix.HasValue)
                {
                    success = await provider.PushAccountRevocationAsync(accountId, DateTimeOffset.FromUnixTimeSeconds(issuedBeforeUnix.Value), timeoutCts.Token);
                }
                else
                {
                    _logger.LogWarning("Invalid revocation type or missing parameters: Type={Type}, JTI={Jti}, TTL={Ttl}, IssuedBefore={IssuedBefore}",
                        type, jti, ttlSeconds, issuedBeforeUnix);
                    continue;
                }

                if (!success)
                {
                    await AddToFailedPushesAsync(type, jti, accountId, ttlSeconds, issuedBeforeUnix, provider.ProviderId, ct);
                    _logger.LogWarning("Edge push to {ProviderId} returned failure for {Type} revocation", provider.ProviderId, type);
                }
                else
                {
                    _logger.LogDebug("Successfully pushed {Type} revocation to {ProviderId}", type, provider.ProviderId);
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                await AddToFailedPushesAsync(type, jti, accountId, ttlSeconds, issuedBeforeUnix, provider.ProviderId, ct);
                _logger.LogWarning("Edge push to {ProviderId} timed out for {Type} revocation", provider.ProviderId, type);
            }
            catch (Exception ex)
            {
                // IMPLEMENTATION TENETS: Unexpected failures get error events
                await AddToFailedPushesAsync(type, jti, accountId, ttlSeconds, issuedBeforeUnix, provider.ProviderId, ct);
                _logger.LogError(ex, "Edge push to {ProviderId} failed unexpectedly for {Type} revocation", provider.ProviderId, type);
                await _messageBus.TryPublishErrorAsync("auth", "edge-revocation-push-failed", ex.GetType().Name, ex.Message, cancellationToken: ct);
            }
        }
    }

    /// <summary>
    /// Adds a failed push entry to the retry set.
    /// </summary>
    private async Task AddToFailedPushesAsync(string type, string? jti, Guid accountId, int? ttlSeconds, long? issuedBeforeUnix, string providerId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "EdgeRevocationService.AddToFailedPushes");
        var entry = new FailedEdgePushEntry
        {
            Type = type,
            Jti = jti,
            AccountId = accountId,
            TtlSeconds = ttlSeconds,
            IssuedBeforeUnix = issuedBeforeUnix,
            ProviderId = providerId,
            RetryCount = 0,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Create unique key for this failed push
        var key = type == "token" ? $"failed:{providerId}:token:{jti}" : $"failed:{providerId}:account:{accountId}";

        var failedStore = _stateStoreFactory.GetStore<FailedEdgePushEntry>(StateStoreDefinitions.EdgeRevocation);
        await failedStore.SaveAsync(key, entry, null, ct);

        // Add to failed push index
        var indexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.EdgeRevocation);
        await AddToIndexAsync(indexStore, "failed-push-index", key, ct);
    }

    /// <summary>
    /// Retries all failed pushes.
    /// </summary>
    private async Task RetryFailedPushesAsync(CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "EdgeRevocationService.RetryFailedPushes");
        var indexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.EdgeRevocation);
        var failedIndex = await indexStore.GetAsync("failed-push-index", ct);

        if (failedIndex == null || failedIndex.Count == 0)
        {
            return;
        }

        _logger.LogDebug("Retrying {Count} failed edge pushes", failedIndex.Count);

        var failedStore = _stateStoreFactory.GetStore<FailedEdgePushEntry>(StateStoreDefinitions.EdgeRevocation);
        var successfulKeys = new List<string>();
        var giveUpKeys = new List<string>();

        // Group by provider for batch operations
        var entriesByProvider = new Dictionary<string, List<(string key, FailedEdgePushEntry entry)>>();

        foreach (var key in failedIndex)
        {
            var entry = await failedStore.GetAsync(key, ct);
            if (entry == null)
            {
                // Entry expired or was removed, clean up index
                successfulKeys.Add(key);
                continue;
            }

            if (entry.RetryCount >= _configuration.EdgeRevocationMaxRetryAttempts)
            {
                _logger.LogError("Edge push to {ProviderId} exceeded max retry attempts for {Type} revocation, giving up",
                    entry.ProviderId, entry.Type);
                giveUpKeys.Add(key);
                continue;
            }

            if (string.IsNullOrEmpty(entry.ProviderId))
            {
                _logger.LogWarning("FailedEdgePushEntry missing ProviderId for key {Key}, skipping", key);
                giveUpKeys.Add(key);
                continue;
            }

            if (!entriesByProvider.TryGetValue(entry.ProviderId, out var list))
            {
                list = new List<(string, FailedEdgePushEntry)>();
                entriesByProvider[entry.ProviderId] = list;
            }
            list.Add((key, entry));
        }

        // Retry each provider's entries
        foreach (var provider in _providers.Where(p => p.IsEnabled))
        {
            if (!entriesByProvider.TryGetValue(provider.ProviderId, out var entries))
            {
                continue;
            }

            var successCount = await provider.PushBatchAsync(entries.Select(e => e.entry).ToList(), ct);

            // Mark successful entries
            for (var i = 0; i < Math.Min(successCount, entries.Count); i++)
            {
                successfulKeys.Add(entries[i].key);
            }

            // Increment retry count for remaining failures
            for (var i = successCount; i < entries.Count; i++)
            {
                var (key, entry) = entries[i];
                entry.RetryCount++;
                await failedStore.SaveAsync(key, entry, null, ct);
            }
        }

        // Clean up successful and given-up entries
        foreach (var key in successfulKeys.Concat(giveUpKeys))
        {
            await failedStore.DeleteAsync(key, ct);
            await RemoveFromIndexAsync(indexStore, "failed-push-index", key, ct);
        }

        if (successfulKeys.Count > 0)
        {
            _logger.LogInformation("Successfully retried {Count} failed edge pushes", successfulKeys.Count);
        }
        if (giveUpKeys.Count > 0)
        {
            _logger.LogWarning("Gave up on {Count} failed edge pushes after max retries", giveUpKeys.Count);
        }
    }

    /// <summary>
    /// Adds an item to an index list.
    /// </summary>
    private async Task AddToIndexAsync(IStateStore<List<string>> indexStore, string indexKey, string item, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "EdgeRevocationService.AddToIndex");
        var index = await indexStore.GetAsync(indexKey, ct) ?? new List<string>();
        if (!index.Contains(item))
        {
            index.Add(item);
            await indexStore.SaveAsync(indexKey, index, null, ct);
        }
    }

    /// <summary>
    /// Removes an item from an index list.
    /// </summary>
    private async Task RemoveFromIndexAsync(IStateStore<List<string>> indexStore, string indexKey, string item, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "EdgeRevocationService.RemoveFromIndex");
        var index = await indexStore.GetAsync(indexKey, ct);
        if (index != null && index.Remove(item))
        {
            if (index.Count > 0)
            {
                await indexStore.SaveAsync(indexKey, index, null, ct);
            }
            else
            {
                await indexStore.DeleteAsync(indexKey, ct);
            }
        }
    }
}
