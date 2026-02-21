namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Provider interface for pushing token revocations to edge infrastructure (CDN, firewall).
/// Implementations handle specific providers like CloudFlare KV or OpenResty/Redis.
/// </summary>
/// <remarks>
/// <para>
/// Providers are responsible for pushing revocation data to external edge systems.
/// Each provider handles its own connectivity, serialization, and error handling.
/// </para>
/// <para>
/// Provider implementations should:
/// <list type="bullet">
/// <item><description>Return false on failure rather than throwing exceptions</description></item>
/// <item><description>Log warnings for transient failures</description></item>
/// <item><description>Support both token-level and account-level revocations</description></item>
/// </list>
/// </para>
/// </remarks>
public interface IEdgeRevocationProvider
{
    /// <summary>
    /// Gets the unique identifier for this provider (e.g., "cloudflare", "openresty").
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Gets whether this provider is enabled via configuration.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Pushes a single token revocation to the edge provider.
    /// </summary>
    /// <param name="jti">JWT unique identifier to revoke.</param>
    /// <param name="accountId">Account that owns the token.</param>
    /// <param name="ttl">How long the revocation should persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if push succeeded, false if it failed.</returns>
    Task<bool> PushTokenRevocationAsync(string jti, Guid accountId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Pushes an account-level revocation to the edge provider.
    /// </summary>
    /// <param name="accountId">Account whose tokens should be revoked.</param>
    /// <param name="issuedBefore">Tokens issued before this timestamp are invalid.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if push succeeded, false if it failed.</returns>
    Task<bool> PushAccountRevocationAsync(Guid accountId, DateTimeOffset issuedBefore, CancellationToken ct = default);

    /// <summary>
    /// Pushes a batch of revocation entries (used for retrying failed pushes).
    /// </summary>
    /// <param name="entries">List of revocation entries to push.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of successfully pushed entries.</returns>
    Task<int> PushBatchAsync(IReadOnlyList<FailedEdgePushEntry> entries, CancellationToken ct = default);
}
