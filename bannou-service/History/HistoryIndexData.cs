namespace BeyondImmersion.BannouService.History;

/// <summary>
/// Generic index data model for dual-index storage patterns.
/// Stores a list of record IDs associated with an entity.
/// Used by both primary (entity -> records) and secondary (related entity -> records) indices.
/// </summary>
public class HistoryIndexData
{
    /// <summary>
    /// The entity ID this index is for.
    /// For primary index: the owning entity (e.g., character ID).
    /// For secondary index: the related entity (e.g., event ID).
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// List of record IDs associated with this entity.
    /// </summary>
    public List<string> RecordIds { get; set; } = new();
}
