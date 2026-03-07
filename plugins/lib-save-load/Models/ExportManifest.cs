namespace BeyondImmersion.BannouService.SaveLoad.Models;

/// <summary>
/// Manifest for save data export archives.
/// Stored as manifest.json in the root of export ZIP files.
/// </summary>
public sealed class ExportManifest
{
    /// <summary>
    /// Game ID the saves are from.
    /// </summary>
    public string GameId { get; set; } = string.Empty;

    /// <summary>
    /// Owner ID (player or realm) the saves belong to.
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Owner type (Player/Realm/Game).
    /// </summary>
    public EntityType OwnerType { get; set; }

    /// <summary>
    /// When the export was created.
    /// </summary>
    public DateTimeOffset ExportedAt { get; set; }

    /// <summary>
    /// Export format version for future compatibility.
    /// </summary>
    public int FormatVersion { get; set; } = 1;

    /// <summary>
    /// List of slots included in the export.
    /// </summary>
    public List<ExportSlotEntry> Slots { get; set; } = new();
}

/// <summary>
/// Entry for a single slot in an export manifest.
/// </summary>
public sealed class ExportSlotEntry
{
    /// <summary>
    /// Original slot ID.
    /// </summary>
    public Guid SlotId { get; set; }

    /// <summary>
    /// Slot name (used as folder name in archive).
    /// </summary>
    public string SlotName { get; set; } = string.Empty;

    /// <summary>
    /// Category (autosave, quicksave, manual, etc.).
    /// </summary>
    public SaveCategory? Category { get; set; }

    /// <summary>
    /// Display name for the slot.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Version number that was exported.
    /// </summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// Schema version of the save data.
    /// </summary>
    public string? SchemaVersion { get; set; }

    /// <summary>
    /// SHA-256 hash of the uncompressed data.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Size of the uncompressed data in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// When the version was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Custom metadata from the version.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}
