// =============================================================================
// Inventory Data Cache Implementation
// Caches character inventory data with TTL.
// Owned by lib-inventory per service hierarchy.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Inventory.Caching;

/// <summary>
/// Caches character inventory data for actor behavior execution.
/// Uses <see cref="VariableProviderCacheBucket{TKey, TData}"/> for thread-safe
/// TTL-based caching with stale-data fallback (IMPLEMENTATION TENETS compliant).
/// </summary>
public sealed class InventoryDataCache : IInventoryDataCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InventoryDataCache> _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly int _queryPageSize;
    private readonly VariableProviderCacheBucket<Guid, CachedInventoryData> _bucket;

    // Long-lived templateId→code mapping that persists across cache refreshes
    private readonly ConcurrentDictionary<Guid, string> _templateCodeCache = new();

    /// <summary>
    /// Creates a new inventory data cache.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for creating scoped service clients.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="config">Service configuration with cache TTL and query page size.</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing.</param>
    public InventoryDataCache(
        IServiceScopeFactory scopeFactory,
        ILogger<InventoryDataCache> logger,
        InventoryServiceConfiguration config,
        ITelemetryProvider telemetryProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
        _queryPageSize = config.QueryPageSize;
        _bucket = new VariableProviderCacheBucket<Guid, CachedInventoryData>(
            TimeSpan.FromSeconds(config.ProviderCacheTtlSeconds),
            logger,
            telemetryProvider,
            "bannou.inventory",
            "InventoryDataCache");
    }

    /// <inheritdoc/>
    public async Task<CachedInventoryData?> GetOrLoadAsync(Guid characterId, CancellationToken ct = default)
    {
        return await _bucket.GetOrLoadAsync(characterId, async innerCt =>
        {
            using var scope = _scopeFactory.CreateScope();
            var inventoryClient = scope.ServiceProvider.GetRequiredService<IInventoryClient>();
            var itemClient = scope.ServiceProvider.GetRequiredService<IItemClient>();

            // Step 1: Get all containers for this character
            var containersResponse = await inventoryClient.ListContainersAsync(
                new ListContainersRequest
                {
                    OwnerId = characterId,
                    OwnerType = ContainerOwnerType.Character,
                },
                innerCt);

            var containers = containersResponse?.Containers;
            if (containers == null || containers.Count == 0)
            {
                return CachedInventoryData.Empty;
            }

            // Step 2: Query all items across all containers
            var itemCounts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var totalItemCount = 0;
            var offset = 0;

            while (true)
            {
                var itemsResponse = await inventoryClient.QueryItemsAsync(
                    new QueryItemsRequest
                    {
                        OwnerId = characterId,
                        OwnerType = ContainerOwnerType.Character,
                        Offset = offset,
                        Limit = _queryPageSize,
                    },
                    innerCt);

                if (itemsResponse?.Items == null || itemsResponse.Items.Count == 0)
                    break;

                foreach (var item in itemsResponse.Items)
                {
                    totalItemCount++;

                    // Resolve templateId to code
                    var code = await ResolveTemplateCodeAsync(item.TemplateId, itemClient, innerCt);
                    if (code != null)
                    {
                        if (itemCounts.TryGetValue(code, out var existing))
                        {
                            itemCounts[code] = existing + item.Quantity;
                        }
                        else
                        {
                            itemCounts[code] = item.Quantity;
                        }
                    }
                }

                if (itemsResponse.Items.Count < _queryPageSize)
                    break;

                offset += _queryPageSize;
            }

            // Step 3: Compute aggregate container stats
            var totalWeight = 0.0;
            var usedSlots = 0;
            var hasSpace = false;

            foreach (var container in containers)
            {
                // Only sum top-level container weights to avoid double-counting nested
                if (container.ParentContainerId == null)
                {
                    totalWeight += container.TotalWeight;
                }

                if (container.UsedSlots.HasValue)
                {
                    usedSlots += container.UsedSlots.Value;
                }

                // Check if any container has available capacity
                if (!hasSpace)
                {
                    if (container.MaxSlots.HasValue && container.UsedSlots.HasValue &&
                        container.UsedSlots.Value < container.MaxSlots.Value)
                    {
                        hasSpace = true;
                    }
                    else if (container.ConstraintModel == ContainerConstraintModel.Unlimited)
                    {
                        hasSpace = true;
                    }
                }
            }

            _logger.LogDebug("Loaded inventory data for character {CharacterId}: {ContainerCount} containers, {ItemCount} items",
                characterId, containers.Count, totalItemCount);

            return new CachedInventoryData
            {
                ItemCountsByTemplateCode = itemCounts,
                TotalContainers = containers.Count,
                TotalItemCount = totalItemCount,
                TotalWeight = totalWeight,
                UsedSlots = usedSlots,
                HasSpace = hasSpace,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public void Invalidate(Guid characterId)
    {
        _bucket.Invalidate(characterId);
        _logger.LogDebug("Invalidated inventory data cache for character {CharacterId}", characterId);
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        _bucket.InvalidateAll();
        _logger.LogInformation("Cleared all inventory data cache entries");
    }

    /// <summary>
    /// Resolves a template ID to its code string, using the long-lived code cache.
    /// </summary>
    private async Task<string?> ResolveTemplateCodeAsync(Guid templateId, IItemClient itemClient, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryDataCache.ResolveTemplateCode");

        if (_templateCodeCache.TryGetValue(templateId, out var code))
        {
            return code;
        }

        try
        {
            var template = await itemClient.GetItemTemplateAsync(
                new GetItemTemplateRequest { TemplateId = templateId },
                ct);

            if (template != null)
            {
                _templateCodeCache[templateId] = template.Code;
                return template.Code;
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Item template {TemplateId} not found", templateId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve template code for {TemplateId}", templateId);
        }

        return null;
    }
}
