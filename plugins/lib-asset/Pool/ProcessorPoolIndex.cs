// =============================================================================
// Processor Pool Index
// Tracks all node IDs in a pool to avoid expensive KEYS operations.
// =============================================================================

namespace BeyondImmersion.BannouService.Asset.Pool;

/// <summary>
/// Index of all processor node IDs in a pool.
/// Maintained alongside individual node state entries to avoid KEYS operations.
/// Per IMPLEMENTATION TENETS: "No expensive KEYS/SCAN operations".
/// </summary>
public class ProcessorPoolIndex
{
    /// <summary>
    /// Set of all known node IDs in this pool.
    /// </summary>
    public HashSet<string> NodeIds { get; set; } = new();

    /// <summary>
    /// When the index was last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; }
}
