using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Operations;

/// <summary>
/// Fills or erases all voxels within an axis-aligned bounding box.
/// </summary>
public sealed class BoxOperation : IVoxelOperation
{
    private readonly Dictionary<VoxelCoord, Voxel> _previousVoxels = new();

    /// <summary>The bounding box to fill or erase.</summary>
    public VoxelBounds Bounds { get; }

    /// <summary>The palette index to fill with. Ignored when <see cref="Erase"/> is true.</summary>
    public byte PaletteIndex { get; }

    /// <summary>When true, erases voxels instead of filling them.</summary>
    public bool Erase { get; }

    /// <inheritdoc />
    public string SourceId { get; set; }

    /// <inheritdoc />
    public string Description => Erase
        ? $"Box erase {Bounds}"
        : $"Box fill (idx={PaletteIndex}) {Bounds}";

    /// <inheritdoc />
    public VoxelOperationType OperationType => VoxelOperationType.Box;

    /// <inheritdoc />
    public VoxelBounds AffectedRegion => Bounds;

    /// <summary>
    /// Creates a new box operation.
    /// </summary>
    /// <param name="bounds">The bounding box to fill or erase.</param>
    /// <param name="paletteIndex">The palette index to fill with.</param>
    /// <param name="erase">When true, erases voxels instead of filling.</param>
    /// <param name="sourceId">Who created this operation.</param>
    public BoxOperation(VoxelBounds bounds, byte paletteIndex, bool erase = false, string sourceId = "local")
    {
        Bounds = bounds;
        PaletteIndex = paletteIndex;
        Erase = erase;
        SourceId = sourceId;
    }

    /// <inheritdoc />
    public void Execute(VoxelGrid grid, VoxelBuilderOptions options)
    {
        var targetVoxel = Erase ? Voxel.Empty : new Voxel(PaletteIndex, VoxelFlags.None);

        for (var y = Bounds.Min.Y; y <= Bounds.Max.Y; y++)
        {
            for (var z = Bounds.Min.Z; z <= Bounds.Max.Z; z++)
            {
                for (var x = Bounds.Min.X; x <= Bounds.Max.X; x++)
                {
                    var coord = new VoxelCoord(x, y, z);
                    if (!grid.Contains(coord))
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
}
