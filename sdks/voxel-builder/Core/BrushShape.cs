namespace BeyondImmersion.Bannou.VoxelBuilder.Core;

/// <summary>
/// Defines a brush shape and size for paint/erase operations.
/// </summary>
/// <param name="Type">Brush shape: Sphere, Cube, or Cylinder.</param>
/// <param name="Radius">Brush radius in voxels.</param>
public sealed record BrushShape(BrushType Type, int Radius)
{
    /// <summary>Default brush: sphere with radius 1.</summary>
    public static readonly BrushShape Default = new(BrushType.Sphere, 1);
}
