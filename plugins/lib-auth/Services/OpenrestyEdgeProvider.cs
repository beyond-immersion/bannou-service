using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// OpenResty/NGINX edge revocation provider via Redis pub/sub.
/// Publishes revocation events to a Redis channel that OpenResty Lua scripts subscribe to.
/// </summary>
/// <remarks>
/// <para>
/// OpenResty (NGINX with LuaJIT) can subscribe to Redis pub/sub channels and maintain
/// a local cache of revoked tokens. When a request arrives, the Lua script checks the
/// local cache before forwarding to the upstream.
/// </para>
/// <para>
/// This provider uses IMessageBus (lib-messaging) for pub/sub per FOUNDATION TENETS.
/// The OpenResty Lua script should subscribe to the configured channel and handle
/// both "token" and "account" revocation message types.
/// </para>
/// </remarks>
public class OpenrestyEdgeProvider : IEdgeRevocationProvider
{
    private readonly AuthServiceConfiguration _configuration;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<OpenrestyEdgeProvider> _logger;

    /// <summary>
    /// Initializes a new instance of OpenrestyEdgeProvider.
    /// </summary>
    /// <param name="configuration">Auth service configuration.</param>
    /// <param name="messageBus">Message bus for Redis pub/sub.</param>
    /// <param name="logger">Logger instance.</param>
    public OpenrestyEdgeProvider(
        AuthServiceConfiguration configuration,
        IMessageBus messageBus,
        ILogger<OpenrestyEdgeProvider> logger)
    {
        _configuration = configuration;
        _messageBus = messageBus;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string ProviderId => "openresty";

    /// <inheritdoc/>
    public bool IsEnabled => _configuration.OpenrestyEdgeEnabled;

    /// <inheritdoc/>
    public async Task<bool> PushTokenRevocationAsync(string jti, Guid accountId, TimeSpan ttl, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return true;
        }

        try
        {
            // Publish to Redis channel that OpenResty Lua subscribes to
            // IMPLEMENTATION TENETS: BannouJson handles serialization automatically via IMessageBus
            await _messageBus.TryPublishAsync(_configuration.OpenrestyRedisChannel, new EdgeRevocationMessage
            {
                Type = "token",
                Jti = jti,
                AccountId = accountId,
                TtlSeconds = (int)ttl.TotalSeconds,
                RevokedAt = DateTimeOffset.UtcNow
            }, ct);

            _logger.LogDebug("Published token revocation to OpenResty channel for JTI {Jti}", jti);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenResty pub/sub failed for token {Jti}", jti);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> PushAccountRevocationAsync(Guid accountId, DateTimeOffset issuedBefore, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return true;
        }

        try
        {
            await _messageBus.TryPublishAsync(_configuration.OpenrestyRedisChannel, new EdgeRevocationMessage
            {
                Type = "account",
                AccountId = accountId,
                IssuedBefore = issuedBefore,
                RevokedAt = DateTimeOffset.UtcNow
            }, ct);

            _logger.LogDebug("Published account revocation to OpenResty channel for AccountId {AccountId}", accountId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenResty pub/sub failed for account {AccountId}", accountId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<int> PushBatchAsync(IReadOnlyList<FailedEdgePushEntry> entries, CancellationToken ct = default)
    {
        if (!IsEnabled || entries.Count == 0)
        {
            return entries.Count;
        }

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
                    continue;
                }

                if (success)
                {
                    successCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenResty batch push failed for entry {Type}:{Id}",
                    entry.Type, entry.Type == "token" ? entry.Jti : entry.AccountId.ToString());
            }
        }

        return successCount;
    }
}
