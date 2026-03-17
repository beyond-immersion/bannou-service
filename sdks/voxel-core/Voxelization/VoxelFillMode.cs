namespace BeyondImmersion.Bannou.VoxelCore.Voxelization;

/// <summary>
/// Fill mode for mesh voxelization.
/// </summary>
public enum VoxelFillMode
{
    /// <summary>Only the surface shell of the mesh is voxelized.</summary>
    Surface,

    /// <summary>The surface shell and the solid interior are both voxelized using ray-parity fill.</summary>
    Solid
}
