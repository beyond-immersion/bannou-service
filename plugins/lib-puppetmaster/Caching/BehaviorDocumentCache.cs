using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.Bannou.BehaviorCompiler.Parser;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Puppetmaster.Caching;

/// <summary>
/// Thread-safe cache for ABML behavior documents loaded from the asset service.
/// </summary>
public sealed class BehaviorDocumentCache : IBehaviorDocumentCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BehaviorDocumentCache> _logger;
    private readonly PuppetmasterServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly DocumentParser _parser = new();
    private readonly TimeSpan _ttl;

    /// <summary>
    /// Creates a new behavior document cache.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for resolving scoped services.</param>
    /// <param name="httpClientFactory">HTTP client factory for downloading assets.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Puppetmaster service configuration.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public BehaviorDocumentCache(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<BehaviorDocumentCache> logger,
        PuppetmasterServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
        _ttl = TimeSpan.FromSeconds(_configuration.BehaviorCacheTtlSeconds);
    }

    /// <summary>
    /// Gets the number of cached behavior documents.
    /// </summary>
    public int CachedCount => _cache.Count;

    /// <summary>
    /// Gets or loads a behavior document by reference.
    /// </summary>
    /// <param name="behaviorRef">The behavior reference (asset ID).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded document, or null if loading fails.</returns>
    public async Task<AbmlDocument?> GetOrLoadAsync(string behaviorRef, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.puppetmaster", "BehaviorDocumentCache.GetOrLoadAsync");
        if (_cache.TryGetValue(behaviorRef, out var cached))
        {
            if (!cached.IsExpired)
            {
                _logger.LogDebug("Cache hit for behavior {BehaviorRef}", behaviorRef);
                return cached.Document;
            }

            // Expired entry - remove and reload
            _cache.TryRemove(behaviorRef, out _);
            _logger.LogDebug("Cache entry expired for behavior {BehaviorRef}, reloading", behaviorRef);
        }

        _logger.LogDebug("Cache miss for behavior {BehaviorRef}, loading from asset service", behaviorRef);

        try
        {
            var yaml = await DownloadBehaviorYamlAsync(behaviorRef, ct);
            if (string.IsNullOrWhiteSpace(yaml))
            {
                _logger.LogWarning("Empty YAML content for behavior {BehaviorRef}", behaviorRef);
                return null;
            }

            var parseResult = _parser.Parse(yaml);
            if (!parseResult.IsSuccess)
            {
                _logger.LogError(
                    "Failed to parse behavior {BehaviorRef}: {Errors}",
                    behaviorRef,
                    string.Join(", ", parseResult.Errors));
                return null;
            }

            var document = parseResult.Value;
            if (document == null)
            {
                _logger.LogError("Parser returned success but null document for behavior {BehaviorRef}", behaviorRef);
                return null;
            }

            // Cache the document (with size limit check)
            if (_cache.Count < _configuration.BehaviorCacheMaxSize)
            {
                var entry = new CacheEntry(document, DateTimeOffset.UtcNow.Add(_ttl));
                _cache.TryAdd(behaviorRef, entry);
                _logger.LogDebug("Cached behavior {BehaviorRef} with TTL {TtlSeconds}s", behaviorRef, _ttl.TotalSeconds);
            }
            else
            {
                _logger.LogWarning(
                    "Behavior cache at max size ({MaxSize}), not caching {BehaviorRef}",
                    _configuration.BehaviorCacheMaxSize,
                    behaviorRef);
            }

            return document;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Loading behavior {BehaviorRef} was cancelled", behaviorRef);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading behavior {BehaviorRef}", behaviorRef);
            return null;
        }
    }

    /// <summary>
    /// Invalidates a specific cached behavior.
    /// </summary>
    /// <param name="behaviorRef">The behavior reference to invalidate.</param>
    /// <returns>True if the behavior was cached and removed.</returns>
    public bool Invalidate(string behaviorRef)
    {
        var removed = _cache.TryRemove(behaviorRef, out _);
        if (removed)
        {
            _logger.LogInformation("Invalidated cached behavior {BehaviorRef}", behaviorRef);
        }
        return removed;
    }

    /// <summary>
    /// Invalidates all cached behaviors.
    /// </summary>
    /// <returns>Number of behaviors invalidated.</returns>
    public int InvalidateAll()
    {
        var count = _cache.Count;
        _cache.Clear();
        _logger.LogInformation("Invalidated all {Count} cached behaviors", count);
        return count;
    }

    private async Task<string?> DownloadBehaviorYamlAsync(string behaviorRef, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.puppetmaster", "BehaviorDocumentCache.DownloadBehaviorYamlAsync");
        if (!Guid.TryParse(behaviorRef, out _))
        {
            _logger.LogWarning("Invalid behavior reference format (not a GUID): {BehaviorRef}", behaviorRef);
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var assetClient = scope.ServiceProvider.GetRequiredService<IAssetClient>();

        // Get asset metadata with download URL
        AssetWithDownloadUrl? asset;
        try
        {
            asset = await assetClient.GetAssetAsync(
                new GetAssetRequest { AssetId = behaviorRef },
                ct);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("Asset not found for behavior {BehaviorRef}", behaviorRef);
            return null;
        }

        if (asset?.DownloadUrl == null)
        {
            _logger.LogWarning(
                "No download URL for behavior {BehaviorRef}",
                behaviorRef);
            return null;
        }

        // Download the YAML content
        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(_configuration.AssetDownloadTimeoutSeconds);

        var yaml = await httpClient.GetStringAsync(asset.DownloadUrl, ct);
        return yaml;
    }

    /// <summary>
    /// Cache entry with expiration tracking.
    /// </summary>
    private sealed record CacheEntry(AbmlDocument Document, DateTimeOffset ExpiresAt)
    {
        /// <summary>
        /// Returns true if this entry has expired.
        /// </summary>
        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
    }
}
