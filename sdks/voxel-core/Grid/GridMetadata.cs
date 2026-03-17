using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.VoxelCore.Grid;

/// <summary>
/// Grid-level properties stored alongside the voxel data: name, author, creation date, tags, and voxel scale.
/// Serialized as JSON within the .bvox format metadata section.
/// </summary>
public sealed class GridMetadata
{
    /// <summary>Name of this voxel grid.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Author of this voxel grid.</summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>Creation date of this grid.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Descriptive tags for categorization and search.</summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>
    /// World units per voxel. Arcadia default: 0.25 (one voxel = 25cm cube, ~4x Minecraft resolution).
    /// Affects meshing output coordinates and Scene/Mapping integration.
    /// </summary>
    [JsonPropertyName("voxelScale")]
    public float VoxelScale { get; set; } = 0.25f;
}
