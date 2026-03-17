namespace BeyondImmersion.Bannou.VoxelCore.Meshing;

/// <summary>
/// Configuration for meshing operations.
/// </summary>
/// <param name="VoxelScale">World units per voxel (default 0.25).</param>
/// <param name="AmbientOcclusion">Whether to compute per-vertex ambient occlusion (default true).</param>
/// <param name="CollisionMode">
/// When true, skip UVs, colors, and AO — output null for optional arrays.
/// Use for physics collision meshes where only geometry is needed.
/// </param>
public sealed record MeshingOptions(
    float VoxelScale = 0.25f,
    bool AmbientOcclusion = true,
    bool CollisionMode = false)
{
    /// <summary>Default meshing options: scale 0.25, AO on, collision off.</summary>
    public static readonly MeshingOptions Default = new();
}
