using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Operations;

/// <summary>
/// Paints or erases voxels within a shaped brush (sphere, cube, or cylinder) centered
/// on a coordinate. Shape testing uses Euclidean distance for spheres, axis-aligned bounds
/// for cubes, and XZ-plane Euclidean distance for cylinders.
/// </summary>
public sealed class BrushOperation : IVoxelOperation
{
    private readonly Dictionary<VoxelCoord, Voxel> _previousVoxels = new();

    /// <summary>The center coordinate of the brush stroke.</summary>
    public VoxelCoord Center { get; }

    /// <summary>The brush shape and radius.</summary>
    public BrushShape Brush { get; }

    /// <summary>The palette index to paint with. Ignored when <see cref="Erase"/> is true.</summary>
    public byte PaletteIndex { get; }

    /// <summary>When true, erases voxels instead of painting them.</summary>
    public bool Erase { get; }

    /// <inheritdoc />
    public string SourceId { get; set; }

    /// <inheritdoc />
    public string Description => Erase
        ? $"Brush erase ({Brush.Type}, r={Brush.Radius}) at {Center}"
        : $"Brush paint ({Brush.Type}, r={Brush.Radius}, idx={PaletteIndex}) at {Center}";

    /// <inheritdoc />
    public VoxelOperationType OperationType => VoxelOperationType.Brush;

    /// <inheritdoc />
    public VoxelBounds AffectedRegion => new(
        new VoxelCoord(Center.X - Brush.Radius, Center.Y - Brush.Radius, Center.Z - Brush.Radius),
        new VoxelCoord(Center.X + Brush.Radius, Center.Y + Brush.Radius, Center.Z + Brush.Radius));

    /// <summary>
    /// Creates a new brush operation.
    /// </summary>
    /// <param name="center">The center coordinate of the brush stroke.</param>
    /// <param name="brush">The brush shape and radius.</param>
    /// <param name="paletteIndex">The palette index to paint with.</param>
    /// <param name="erase">When true, erases voxels instead of painting.</param>
    /// <param name="sourceId">Who created this operation.</param>
    public BrushOperation(VoxelCoord center, BrushShape brush, byte paletteIndex, bool erase = false, string sourceId = "local")
    {
        Center = center;
        Brush = brush;
        PaletteIndex = paletteIndex;
        Erase = erase;
        SourceId = sourceId;
    }

    /// <inheritdoc />
    public void Execute(VoxelGrid grid, VoxelBuilderOptions options)
    {
        var r = Brush.Radius;
        var minX = Center.X - r;
        var minY = Center.Y - r;
        var minZ = Center.Z - r;
        var maxX = Center.X + r;
        var maxY = Center.Y + r;
        var maxZ = Center.Z + r;

        var targetVoxel = Erase ? Voxel.Empty : new Voxel(PaletteIndex, VoxelFlags.None);

        for (var y = minY; y <= maxY; y++)
        {
            for (var z = minZ; z <= maxZ; z++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var coord = new VoxelCoord(x, y, z);
                    if (!grid.Contains(coord))
                        continue;

                    if (!IsInsideBrush(coord))
                        continue;

                    var existing = grid.GetVoxel(coord);
                    if (options.EnforceFrozen && existing.Flags.HasFlag(VoxelFlags.Frozen))
                        continue;

                    _previousVoxels[coord] = existing;
                    grid.SetVoxel(coord, targetVoxel);
                }
            }
        }
    }

    /// <inheritdoc />
    public void Undo(VoxelGrid grid)
    {
        foreach (var (coord, voxel) in _previousVoxels)
        {
            grid.SetVoxel(coord, voxel);
        }
    }

    private bool IsInsideBrush(VoxelCoord coord)
    {
        return Brush.Type switch
        {
            BrushType.Sphere => Center.Distance(coord) <= Brush.Radius,
            BrushType.Cube => true, // All coords in the bounding box are inside a cube brush
            BrushType.Cylinder => IsInsideCylinder(coord),
            _ => false
        };
    }

    private bool IsInsideCylinder(VoxelCoord coord)
    {
        var dx = (double)(coord.X - Center.X);
        var dz = (double)(coord.Z - Center.Z);
        return Math.Sqrt(dx * dx + dz * dz) <= Brush.Radius;
    }
}
