namespace BeyondImmersion.Bannou.VoxelBuilder.Core;

/// <summary>
/// Configuration for the VoxelBuilder.
/// </summary>
/// <param name="MaxUndoDepth">Maximum undo history per source (default 100).</param>
/// <param name="AutoMeshOnEdit">Whether to notify the bridge after each operation (default true).</param>
/// <param name="EnforceFrozen">Whether to skip frozen voxels in operations (default true).</param>
public sealed record VoxelBuilderOptions(
    int MaxUndoDepth = 100,
    bool AutoMeshOnEdit = true,
    bool EnforceFrozen = true)
{
    /// <summary>Default options.</summary>
    public static readonly VoxelBuilderOptions Default = new();
}
