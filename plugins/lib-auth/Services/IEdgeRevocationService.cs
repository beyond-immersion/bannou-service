namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Orchestrates token revocation across edge providers (CloudFlare, OpenResty) for CDN/firewall-layer blocking.
/// Maintains local Redis revocation list and coordinates pushes to all enabled providers.
/// </summary>
/// <remarks>
/// <para>
/// Edge revocation is a defense-in-depth mechanism that pushes revoked tokens to CDN/firewall layers,
/// allowing requests to be blocked before they reach the Bannou service. This reduces load and improves
/// security by catching revoked tokens at the network edge.
/// </para>
/// <para>
/// The service supports two revocation types:
/// <list type="bullet">
/// <item><description>Token-level (JTI): Revokes a specific JWT by its unique identifier</description></item>
/// <item><description>Account-level: Revokes all tokens for an account issued before a timestamp</description></item>
/// </list>
/// </para>
/// </remarks>
public interface IEdgeRevocationService
{
    /// <summary>
    /// Gets whether edge revocation is enabled via configuration.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Revokes a specific token by JTI, pushing to all enabled edge providers.
    /// </summary>
    /// <param name="jti">JWT unique identifier (jti claim) to revoke.</param>
    /// <param name="accountId">Account that owns the token.</param>
    /// <param name="ttl">How long the revocation should persist (matches JWT expiry).</param>
    /// <param name="reason">Revocation reason for audit logging.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the revocation has been processed.</returns>
    Task RevokeTokenAsync(string jti, Guid accountId, TimeSpan ttl, string reason, CancellationToken ct = default);

    /// <summary>
    /// Revokes all tokens for an account issued before a timestamp.
    /// </summary>
    /// <param name="accountId">Account whose tokens should be revoked.</param>
    /// <param name="issuedBefore">Tokens issued before this timestamp are invalid.</param>
    /// <param name="reason">Revocation reason for audit logging.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the revocation has been processed.</returns>
    Task RevokeAccountAsync(Guid accountId, DateTimeOffset issuedBefore, string reason, CancellationToken ct = default);

    /// <summary>
    /// Gets the current revocation list for the admin API endpoint.
    /// </summary>
    /// <param name="includeTokens">Whether to include token-level revocations.</param>
    /// <param name="includeAccounts">Whether to include account-level revocations.</param>
    /// <param name="limit">Maximum entries to return per category.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tuple of token revocations, account revocations, failed push count, and total token count.</returns>
    Task<(List<RevokedTokenEntry> tokens, List<RevokedAccountEntry> accounts, int failedCount, int? totalTokenCount)> GetRevocationListAsync(
        bool includeTokens, bool includeAccounts, int limit, CancellationToken ct = default);
}
