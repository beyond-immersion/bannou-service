namespace BeyondImmersion.Bannou.VoxelBuilder.Core;

/// <summary>
/// Brush shape type for paint/erase operations.
/// </summary>
public enum BrushType
{
    /// <summary>Voxels within Euclidean distance &lt;= Radius from center.</summary>
    Sphere,

    /// <summary>Voxels within axis-aligned box of half-extent = Radius.</summary>
    Cube,

    /// <summary>Voxels within XZ distance &lt;= Radius, any Y within bounds.</summary>
    Cylinder
}
