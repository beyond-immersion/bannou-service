namespace BeyondImmersion.Bannou.VoxelBuilder.Core;

/// <summary>
/// Type discriminator for operation serialization/deserialization.
/// </summary>
public enum VoxelOperationType : byte
{
    /// <summary>Place a single voxel.</summary>
    Place = 0,
    /// <summary>Erase a single voxel.</summary>
    Erase = 1,
    /// <summary>Flood fill connected region.</summary>
    Fill = 2,
    /// <summary>Paint/erase with a shaped brush.</summary>
    Brush = 3,
    /// <summary>Fill or erase an axis-aligned box region.</summary>
    Box = 4,
    /// <summary>Mirror grid content across an axis.</summary>
    Mirror = 5,
    /// <summary>90-degree rotation around an axis.</summary>
    Rotate = 6,
    /// <summary>Copy region and paste at offset.</summary>
    CopyPaste = 7,
    /// <summary>Replace all voxels of one palette index with another.</summary>
    Replace = 8,
    /// <summary>Group of operations as one atomic undo unit.</summary>
    Compound = 9,
    /// <summary>Apply a VoxelDelta as a single operation (generator output).</summary>
    GridPatch = 10
}
