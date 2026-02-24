using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;

namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// CloudFlare Workers KV edge revocation provider.
/// Pushes token and account revocations to CloudFlare KV for edge-layer blocking.
/// </summary>
/// <remarks>
/// <para>
/// CloudFlare Workers can read from KV to check if a token is revoked before
/// forwarding requests to the origin. This provides defense-in-depth by blocking
/// revoked tokens at the CDN layer.
/// </para>
/// <para>
/// KV keys follow the pattern:
/// <list type="bullet">
/// <item><description>token:{jti} - Token revocation with TTL matching JWT expiry</description></item>
/// <item><description>account:{accountId} - Account revocation (no expiry)</description></item>
/// </list>
/// </para>
/// </remarks>
public class CloudflareEdgeProvider : IEdgeRevocationProvider
{
    private readonly AuthServiceConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<CloudflareEdgeProvider> _logger;

    private const string CloudflareApiBaseUrl = "https://api.cloudflare.com/client/v4";

    /// <summary>
    /// Initializes a new instance of CloudflareEdgeProvider.
    /// </summary>
    /// <param name="configuration">Auth service configuration.</param>
    /// <param name="httpClientFactory">HTTP client factory for making API requests.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger instance.</param>
    public CloudflareEdgeProvider(
        AuthServiceConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ITelemetryProvider telemetryProvider,
        ILogger<CloudflareEdgeProvider> logger)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string ProviderId => "cloudflare";

    /// <inheritdoc/>
    public bool IsEnabled => _configuration.CloudflareEdgeEnabled
        && !string.IsNullOrEmpty(_configuration.CloudflareApiToken)
        && !string.IsNullOrEmpty(_configuration.CloudflareAccountId)
        && !string.IsNullOrEmpty(_configuration.CloudflareKvNamespaceId);

    /// <inheritdoc/>
    public async Task<bool> PushTokenRevocationAsync(string jti, Guid accountId, TimeSpan ttl, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "CloudflareEdgeProvider.PushTokenRevocation");
        if (!IsEnabled)
        {
            return true;
        }

        try
        {
            // IMPLEMENTATION TENETS: Use BannouJson for serialization
            var value = BannouJson.Serialize(new
            {
                accountId = accountId.ToString(),
                revokedAt = DateTimeOffset.UtcNow.ToString("o"),
                type = "token"
            });

            return await WriteKvAsync($"token:{jti}", value, (int)ttl.TotalSeconds, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CloudFlare KV write failed for token {Jti}", jti);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> PushAccountRevocationAsync(Guid accountId, DateTimeOffset issuedBefore, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "CloudflareEdgeProvider.PushAccountRevocation");
        if (!IsEnabled)
        {
            return true;
        }

        try
        {
            var value = BannouJson.Serialize(new
            {
                accountId = accountId.ToString(),
                issuedBefore = issuedBefore.ToString("o"),
                revokedAt = DateTimeOffset.UtcNow.ToString("o"),
                type = "account"
            });

            // Account revocations don't expire
            return await WriteKvAsync($"account:{accountId}", value, null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CloudFlare KV write failed for account {AccountId}", accountId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<int> PushBatchAsync(IReadOnlyList<FailedEdgePushEntry> entries, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "CloudflareEdgeProvider.PushBatch");
        if (!IsEnabled || entries.Count == 0)
        {
            return entries.Count;
        }

        var successCount = 0;

        // CloudFlare KV has a bulk write endpoint, but for simplicity we'll do individual writes
        // This can be optimized later if needed
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
                _logger.LogWarning(ex, "CloudFlare batch push failed for entry {Type}:{Id}",
                    entry.Type, entry.Type == "token" ? entry.Jti : entry.AccountId.ToString());
            }
        }

        return successCount;
    }

    /// <summary>
    /// Writes a key-value pair to CloudFlare KV.
    /// </summary>
    /// <param name="key">The key to write.</param>
    /// <param name="value">The value to write (JSON string).</param>
    /// <param name="expirationTtl">Optional TTL in seconds.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if write succeeded.</returns>
    private async Task<bool> WriteKvAsync(string key, string value, int? expirationTtl, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.auth", "CloudflareEdgeProvider.WriteKv");
        var accountId = _configuration.CloudflareAccountId;
        var namespaceId = _configuration.CloudflareKvNamespaceId;
        var apiToken = _configuration.CloudflareApiToken;

        // CloudFlare KV API: PUT /accounts/{account_id}/storage/kv/namespaces/{namespace_id}/values/{key_name}
        var url = $"{CloudflareApiBaseUrl}/accounts/{accountId}/storage/kv/namespaces/{namespaceId}/values/{Uri.EscapeDataString(key)}";

        if (expirationTtl.HasValue)
        {
            url += $"?expiration_ttl={expirationTtl.Value}";
        }

        using var client = _httpClientFactory.CreateClient("cloudflare-kv");
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        request.Content = new StringContent(value, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("CloudFlare KV write failed: Status={Status}, Response={Response}",
                response.StatusCode, responseBody);
            return false;
        }

        _logger.LogDebug("CloudFlare KV write succeeded for key {Key}", key);
        return true;
    }
}
