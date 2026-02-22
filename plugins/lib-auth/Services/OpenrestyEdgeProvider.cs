using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// OpenResty/NGINX edge revocation provider that writes revocation data to Redis
/// for direct querying by OpenResty Lua scripts.
/// </summary>
/// <remarks>
/// <para>
/// OpenResty uses resty.redis to query Redis directly - NOT pub/sub.
/// The EdgeRevocationService already writes revocation entries to the auth:edge
/// state store prefix. OpenResty Lua scripts read these keys directly:
/// </para>
/// <list type="bullet">
/// <item><description>auth:edge:token:{jti} - Token revocation entry</description></item>
/// <item><description>auth:edge:account:{accountId} - Account revocation entry</description></item>
/// </list>
/// <para>
/// Since both Bannou and OpenResty share the same Redis instance (bannou-redis),
/// this provider simply confirms the revocation data is accessible. The actual
/// revocation entries are written by EdgeRevocationService using lib-state.
/// </para>
/// <para>
/// For deployments where OpenResty uses a SEPARATE Redis instance, this provider
/// would need to be extended to write to that external Redis.
/// </para>
/// </remarks>
public class OpenrestyEdgeProvider : IEdgeRevocationProvider
{
    private readonly AuthServiceConfiguration _configuration;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<OpenrestyEdgeProvider> _logger;

    /// <summary>
    /// Initializes a new instance of OpenrestyEdgeProvider.
    /// </summary>
    /// <param name="configuration">Auth service configuration.</param>
    /// <param name="stateStoreFactory">State store factory for Redis access.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger instance.</param>
    public OpenrestyEdgeProvider(
        AuthServiceConfiguration configuration,
        IStateStoreFactory stateStoreFactory,
        ITelemetryProvider telemetryProvider,
        ILogger<OpenrestyEdgeProvider> logger)
    {
        _configuration = configuration;
        _stateStoreFactory = stateStoreFactory;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string ProviderId => "openresty";

    /// <inheritdoc/>
    public bool IsEnabled => _configuration.OpenrestyEdgeEnabled;

    /// <inheritdoc/>
    public async Task<bool> PushTokenRevocationAsync(string jti, Guid accountId, TimeSpan ttl, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "OpenrestyEdgeProvider.PushTokenRevocation");
        if (!IsEnabled)
        {
            return true;
        }

        // Verify the revocation entry exists in Redis (written by EdgeRevocationService)
        // This confirms OpenResty Lua can read it from the shared Redis instance
        try
        {
            var store = _stateStoreFactory.GetStore<TokenRevocationEntry>(StateStoreDefinitions.EdgeRevocation);
            var entry = await store.GetAsync($"token:{jti}", ct);

            if (entry != null)
            {
                _logger.LogDebug("OpenResty: Verified token revocation exists in Redis for JTI {Jti}", jti);
                return true;
            }

            // Entry doesn't exist - EdgeRevocationService may not have written it yet
            // or there was an error. Log warning but don't fail (defense-in-depth)
            _logger.LogWarning("OpenResty: Token revocation entry not found in Redis for JTI {Jti}", jti);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenResty: Failed to verify token revocation for JTI {Jti}", jti);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> PushAccountRevocationAsync(Guid accountId, DateTimeOffset issuedBefore, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "OpenrestyEdgeProvider.PushAccountRevocation");
        if (!IsEnabled)
        {
            return true;
        }

        // Verify the revocation entry exists in Redis (written by EdgeRevocationService)
        try
        {
            var store = _stateStoreFactory.GetStore<AccountRevocationEntry>(StateStoreDefinitions.EdgeRevocation);
            var entry = await store.GetAsync($"account:{accountId}", ct);

            if (entry != null)
            {
                _logger.LogDebug("OpenResty: Verified account revocation exists in Redis for AccountId {AccountId}", accountId);
                return true;
            }

            _logger.LogWarning("OpenResty: Account revocation entry not found in Redis for AccountId {AccountId}", accountId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenResty: Failed to verify account revocation for AccountId {AccountId}", accountId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<int> PushBatchAsync(IReadOnlyList<FailedEdgePushEntry> entries, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "OpenrestyEdgeProvider.PushBatch");
        if (!IsEnabled || entries.Count == 0)
        {
            return entries.Count;
        }

        // For batch operations, just verify each entry exists
        // Since EdgeRevocationService already wrote them, this is mostly a confirmation
        var successCount = 0;

        foreach (var entry in entries)
        {
            try
            {
                bool success;
                if (entry.Type == "token" && entry.Jti != null && entry.TtlSeconds.HasValue)
                {
                    success = await PushTokenRevocationAsync(entry.Jti, entry.AccountId, TimeSpan.FromSeconds(entry.TtlSeconds.Value), ct);
                }
                else if (entry.Type == "account" && entry.IssuedBeforeUnix.HasValue)
                {
                    success = await PushAccountRevocationAsync(entry.AccountId, DateTimeOffset.FromUnixTimeSeconds(entry.IssuedBeforeUnix.Value), ct);
                }
                else
                {
                    // Unknown entry type, count as success to avoid infinite retries
                    success = true;
                }

                if (success)
                {
                    successCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenResty batch verification failed for entry {Type}:{Id}",
                    entry.Type, entry.Type == "token" ? entry.Jti : entry.AccountId.ToString());
            }
        }

        return successCount;
    }
}
