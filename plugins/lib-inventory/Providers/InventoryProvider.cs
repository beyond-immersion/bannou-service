// =============================================================================
// Inventory Variable Provider
// Provides inventory data for ABML expressions via ${inventory.*} paths.
// Owned by lib-inventory per service hierarchy (L2).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Inventory.Caching;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Inventory.Providers;

/// <summary>
/// Provides inventory data for ABML expressions.
/// Supports paths like ${inventory.has_item.IRON_SWORD}, ${inventory.count.IRON_ORE},
/// ${inventory.has_space}, ${inventory.total_containers}, ${inventory.total_items},
/// ${inventory.total_weight}, ${inventory.used_slots}.
/// </summary>
public sealed class InventoryProvider : IVariableProvider
{
    /// <summary>
    /// Empty provider for non-character actors.
    /// </summary>
    public static InventoryProvider Empty { get; } = new(CachedInventoryData.Empty);

    private readonly CachedInventoryData _data;

    /// <inheritdoc/>
    public string Name => VariableProviderDefinitions.Inventory;

    /// <summary>
    /// Creates a new inventory provider with the given cached data.
    /// </summary>
    /// <param name="data">The cached inventory data.</param>
    public InventoryProvider(CachedInventoryData data)
    {
        _data = data;
    }

    /// <inheritdoc/>
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        var firstSegment = path[0];

        // Handle ${inventory.has_item.{templateCode}} - boolean: owns at least one instance
        if (firstSegment.Equals("has_item", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Length < 2) return null;
            var code = path[1];
            return _data.ItemCountsByTemplateCode.TryGetValue(code, out var count) && count > 0;
        }

        // Handle ${inventory.count.{templateCode}} - total quantity across all containers
        if (firstSegment.Equals("count", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Length < 2) return null;
            var code = path[1];
            return _data.ItemCountsByTemplateCode.TryGetValue(code, out var count) ? count : 0.0;
        }

        // Handle ${inventory.has_space} - whether at least one container has space
        if (firstSegment.Equals("has_space", StringComparison.OrdinalIgnoreCase))
        {
            return _data.HasSpace;
        }

        // Handle ${inventory.total_containers} - number of containers
        if (firstSegment.Equals("total_containers", StringComparison.OrdinalIgnoreCase))
        {
            return _data.TotalContainers;
        }

        // Handle ${inventory.total_items} - total item count
        if (firstSegment.Equals("total_items", StringComparison.OrdinalIgnoreCase))
        {
            return _data.TotalItemCount;
        }

        // Handle ${inventory.total_weight} - aggregate weight
        if (firstSegment.Equals("total_weight", StringComparison.OrdinalIgnoreCase))
        {
            return _data.TotalWeight;
        }

        // Handle ${inventory.used_slots} - total used slots
        if (firstSegment.Equals("used_slots", StringComparison.OrdinalIgnoreCase))
        {
            return _data.UsedSlots;
        }

        return null;
    }

    /// <inheritdoc/>
    public object? GetRootValue()
    {
        return new Dictionary<string, object?>
        {
            ["has_space"] = _data.HasSpace,
            ["total_containers"] = _data.TotalContainers,
            ["total_items"] = _data.TotalItemCount,
            ["total_weight"] = _data.TotalWeight,
            ["used_slots"] = _data.UsedSlots,
        };
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        var firstSegment = path[0];
        return firstSegment.Equals("has_item", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("count", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("has_space", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("total_containers", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("total_items", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("total_weight", StringComparison.OrdinalIgnoreCase) ||
                firstSegment.Equals("used_slots", StringComparison.OrdinalIgnoreCase);
    }
}
