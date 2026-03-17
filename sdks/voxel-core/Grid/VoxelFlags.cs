namespace BeyondImmersion.Bannou.VoxelCore.Grid;

/// <summary>
/// Per-voxel property flags stored as a single byte. Bits 5-7 are reserved for game-specific flags.
/// </summary>
[Flags]
public enum VoxelFlags : byte
{
    /// <summary>No flags set.</summary>
    None = 0,

    /// <summary>Voxel exists but is not rendered (structural support).</summary>
    Hidden = 1,

    /// <summary>Visual damage state (cracks, weathering).</summary>
    Damaged = 2,

    /// <summary>Emits light using the palette color as light color.</summary>
    Emissive = 4,

    /// <summary>Semi-transparent voxel (glass, water).</summary>
    Transparent = 8,

    /// <summary>
    /// Boundary-locked voxel. The voxel-builder operation system rejects edits to frozen voxels.
    /// VoxelGrid.SetVoxel does NOT enforce this flag — enforcement lives in voxel-builder.
    /// This lets generators and the voxelizer write to frozen coordinates during initial setup
    /// while preventing player/NPC edits afterward.
    /// </summary>
    Frozen = 16
}
