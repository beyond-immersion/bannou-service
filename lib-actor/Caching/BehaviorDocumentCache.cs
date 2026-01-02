// =============================================================================
// Behavior Document Cache
// Caches parsed ABML documents loaded from lib-asset.
// =============================================================================

using System.Collections.Concurrent;
using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Parser;
using BeyondImmersion.BannouService.Asset;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Actor.Caching;

/// <summary>
/// Caches parsed ABML documents loaded from lib-asset.
/// Thread-safe via ConcurrentDictionary (T9 compliant).
/// </summary>
public sealed class BehaviorDocumentCache : IBehaviorDocumentCache
{
    private readonly IAssetClient _assetClient;
    private readonly ILogger<BehaviorDocumentCache> _logger;
    private readonly ConcurrentDictionary<string, AbmlDocument> _cache = new();
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Creates a new behavior document cache.
    /// </summary>
    /// <param name="assetClient">Asset client for fetching behavior YAML.</param>
    /// <param name="logger">Logger instance.</param>
    public BehaviorDocumentCache(
        IAssetClient assetClient,
        ILogger<BehaviorDocumentCache> logger)
    {
        _assetClient = assetClient ?? throw new ArgumentNullException(nameof(assetClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = new HttpClient();
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
        AssetWithDownloadUrl assetInfo;
        try
        {
            assetInfo = await _assetClient.GetAssetAsync(
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
            yaml = await _httpClient.GetStringAsync(assetInfo.DownloadUrl.ToString(), ct);
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
