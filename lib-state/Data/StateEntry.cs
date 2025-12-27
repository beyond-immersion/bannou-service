#nullable enable

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeyondImmersion.BannouService.State.Data;

/// <summary>
/// EF Core entity for state storage in MySQL.
/// Each row stores a key-value pair with metadata for a specific store.
/// </summary>
[Table("state_entries")]
public class StateEntry
{
    /// <summary>
    /// Name of the state store this entry belongs to.
    /// Part of composite primary key.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string StoreName { get; set; } = default!;

    /// <summary>
    /// The key for this state entry.
    /// Part of composite primary key.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Key { get; set; } = default!;

    /// <summary>
    /// The JSON-serialized value.
    /// Uses LONGTEXT for large values.
    /// </summary>
    [Required]
    [Column(TypeName = "LONGTEXT")]
    public string ValueJson { get; set; } = default!;

    /// <summary>
    /// Entity tag for optimistic concurrency.
    /// Updated on each save operation.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string ETag { get; set; } = default!;

    /// <summary>
    /// When this entry was first created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this entry was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Version number for concurrency tracking.
    /// Incremented on each save.
    /// </summary>
    public int Version { get; set; }
}
