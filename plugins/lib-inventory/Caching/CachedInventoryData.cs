// =============================================================================
// Cached Inventory Data Model
// Stores aggregated inventory data for variable provider consumption.
// =============================================================================

namespace BeyondImmersion.BannouService.Inventory.Caching;

/// <summary>
/// Aggregated inventory data for a character, cached for ABML variable provider access.
/// Item counts are keyed by template code (case-insensitive).
/// </summary>
public sealed record CachedInventoryData
{
    /// <summary>
    /// Empty instance for non-character actors or when no inventory data exists.
    /// </summary>
    public static CachedInventoryData Empty { get; } = new();

    /// <summary>
    /// Total item quantities keyed by template code (case-insensitive).
    /// Aggregated across all containers.
    /// </summary>
    public IReadOnlyDictionary<string, double> ItemCountsByTemplateCode { get; init; }
        = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Number of containers owned by this character.
    /// </summary>
    public int TotalContainers { get; init; }

    /// <summary>
    /// Total number of distinct item stacks across all containers.
    /// </summary>
    public int TotalItemCount { get; init; }

    /// <summary>
    /// Total weight of all containers (top-level only to avoid double-counting nested).
    /// </summary>
    public double TotalWeight { get; init; }

    /// <summary>
    /// Total used slots across all slot-based containers.
    /// </summary>
    public int UsedSlots { get; init; }

    /// <summary>
    /// Whether at least one container has available capacity.
    /// </summary>
    public bool HasSpace { get; init; }
}
