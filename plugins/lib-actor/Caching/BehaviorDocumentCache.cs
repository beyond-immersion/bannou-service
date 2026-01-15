// =============================================================================
// Behavior Document Cache
// Caches parsed ABML documents loaded from lib-asset.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Parser;
using BeyondImmersion.BannouService.Asset;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Actor.Caching;

/// <summary>
/// Caches parsed ABML documents loaded from lib-asset.
/// Thread-safe via ConcurrentDictionary per IMPLEMENTATION TENETS.
/// Uses IServiceScopeFactory to resolve scoped IAssetClient on demand.
/// </summary>
public sealed class BehaviorDocumentCache : IBehaviorDocumentCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BehaviorDocumentCache> _logger;
    private readonly ConcurrentDictionary<string, AbmlDocument> _cache = new();

    /// <summary>
    /// Creates a new behavior document cache.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for resolving scoped dependencies.</param>
    /// <param name="httpClientFactory">HTTP client factory for downloading assets.</param>
    /// <param name="logger">Logger instance.</param>
    public BehaviorDocumentCache(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<BehaviorDocumentCache> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AbmlDocument> GetOrLoadAsync(string behaviorRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorRef);

        // Check cache first
        if (_cache.TryGetValue(behaviorRef, out var cached))
        {
            _logger.LogDebug("Behavior cache hit: {BehaviorRef}", behaviorRef);
            return cached;
        }

        _logger.LogDebug("Behavior cache miss, loading: {BehaviorRef}", behaviorRef);

        // Load from asset service
        var document = await LoadBehaviorDocumentAsync(behaviorRef, ct);

        // Cache the result
        _cache[behaviorRef] = document;
        _logger.LogInformation("Cached behavior document: {BehaviorRef}", behaviorRef);

        return document;
    }

    /// <inheritdoc/>
    public void Invalidate(string behaviorRef)
    {
        if (_cache.TryRemove(behaviorRef, out _))
        {
            _logger.LogDebug("Invalidated cached behavior: {BehaviorRef}", behaviorRef);
        }
    }

    /// <inheritdoc/>
    public void InvalidateByBehaviorId(string behaviorId)
    {
        if (string.IsNullOrWhiteSpace(behaviorId))
        {
            return;
        }

        var keysToRemove = _cache.Keys
            .Where(k => k.Contains(behaviorId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }

        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug(
                "Invalidated {Count} cached behaviors for behaviorId {BehaviorId}",
                keysToRemove.Count,
                behaviorId);
        }
    }

    /// <summary>
    /// Loads and parses a behavior document from lib-asset.
    /// </summary>
    private async Task<AbmlDocument> LoadBehaviorDocumentAsync(string behaviorRef, CancellationToken ct)
    {
        // Extract asset ID from behaviorRef
        // Formats: "behaviors/npc/standard.abml" or "asset://behaviors/npc/standard.abml"
        var assetId = ExtractAssetId(behaviorRef);

        // Fetch asset metadata with download URL from lib-asset
        // Use scope factory to resolve scoped IAssetClient (singleton cannot hold scoped dependency)
        AssetWithDownloadUrl assetInfo;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var assetClient = scope.ServiceProvider.GetRequiredService<IAssetClient>();
            assetInfo = await assetClient.GetAssetAsync(
                new GetAssetRequest { AssetId = assetId },
                ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to fetch behavior asset '{behaviorRef}': {ex.Message}", ex);
        }

        if (assetInfo.DownloadUrl == null)
        {
            throw new InvalidOperationException(
                $"Asset '{behaviorRef}' has no download URL");
        }

        // Download YAML content
        string yaml;
        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            yaml = await httpClient.GetStringAsync(assetInfo.DownloadUrl.ToString(), ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to download behavior YAML from '{assetInfo.DownloadUrl}': {ex.Message}", ex);
        }

        // Parse YAML into AbmlDocument
        var parser = new DocumentParser();
        var result = parser.Parse(yaml);

        if (!result.IsSuccess || result.Value == null)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Message));
            throw new InvalidOperationException(
                $"Failed to parse behavior '{behaviorRef}': {errors}");
        }

        _logger.LogDebug(
            "Loaded behavior document '{BehaviorRef}' with {FlowCount} flows",
            behaviorRef,
            result.Value.Flows.Count);

        return result.Value;
    }

    /// <summary>
    /// Extracts the asset ID from a behavior reference.
    /// </summary>
    private static string ExtractAssetId(string behaviorRef)
    {
        // Handle "asset://..." prefix
        if (behaviorRef.StartsWith("asset://", StringComparison.OrdinalIgnoreCase))
        {
            return behaviorRef[8..];
        }

        // Otherwise use as-is (path format)
        return behaviorRef;
    }
}
