using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Operations;

/// <summary>
/// Rotates all non-empty voxels 90 degrees around a specified axis. Rotation formulas
/// (relative to grid bounds max): Y-axis <c>(maxZ-z, y, x)</c>, X-axis <c>(x, maxZ-z, y)</c>,
/// Z-axis <c>(maxY-y, x, z)</c>. The affected region is the union of before and after bounds.
/// </summary>
public sealed class RotateOperation : IVoxelOperation
{
    private readonly Dictionary<VoxelCoord, Voxel> _previousVoxels = new();
    private VoxelBounds _beforeBounds;

    /// <summary>The axis to rotate around.</summary>
    public Axis RotateAxis { get; }

    /// <inheritdoc />
    public string SourceId { get; set; }

    /// <inheritdoc />
    public string Description => $"Rotate 90 degrees around {RotateAxis} axis";

    /// <inheritdoc />
    public VoxelOperationType OperationType => VoxelOperationType.Rotate;

    /// <inheritdoc />
    public VoxelBounds AffectedRegion { get; private set; }

    /// <summary>
    /// Creates a new rotate operation.
    /// </summary>
    /// <param name="rotateAxis">The axis to rotate around.</param>
    /// <param name="sourceId">Who created this operation.</param>
    public RotateOperation(Axis rotateAxis, string sourceId = "local")
    {
        RotateAxis = rotateAxis;
        SourceId = sourceId;
    }

    /// <inheritdoc />
    public void Execute(VoxelGrid grid, VoxelBuilderOptions options)
    {
        _beforeBounds = grid.Bounds;

        // Snapshot all non-empty voxels
        foreach (var (chunkCoord, chunk) in grid.EnumerateChunks())
        {
            var chunkOrigin = chunkCoord.ToVoxelCoord();

            for (var y = 0; y < VoxelChunk.Size; y++)
            {
                for (var z = 0; z < VoxelChunk.Size; z++)
                {
                    for (var x = 0; x < VoxelChunk.Size; x++)
                    {
                        var voxel = chunk.GetVoxel(x, y, z);
                        if (voxel.IsEmpty)
                            continue;

                        var coord = new VoxelCoord(chunkOrigin.X + x, chunkOrigin.Y + y, chunkOrigin.Z + z);
                        _previousVoxels[coord] = voxel;
                    }
                }
            }
        }

        // Clear all snapshotted voxels
        foreach (var coord in _previousVoxels.Keys)
        {
            grid.SetVoxel(coord, Voxel.Empty);
        }

        // Compute new bounds BEFORE writing rotated voxels — rotation swaps axis extents
        var oldMin = _beforeBounds.Min;
        var oldMax = _beforeBounds.Max;

        var newBounds = RotateAxis switch
        {
            // Y-axis rotation: width(X) <-> depth(Z)
            Axis.Y => new VoxelBounds(
                new VoxelCoord(oldMin.Z, oldMin.Y, oldMin.X),
                new VoxelCoord(oldMax.Z, oldMax.Y, oldMax.X)),
            // X-axis rotation: height(Y) <-> depth(Z)
            Axis.X => new VoxelBounds(
                new VoxelCoord(oldMin.X, oldMin.Z, oldMin.Y),
                new VoxelCoord(oldMax.X, oldMax.Z, oldMax.Y)),
            // Z-axis rotation: width(X) <-> height(Y)
            Axis.Z => new VoxelBounds(
                new VoxelCoord(oldMin.Y, oldMin.X, oldMin.Z),
                new VoxelCoord(oldMax.Y, oldMax.X, oldMax.Z)),
            _ => _beforeBounds
        };

        // Update grid bounds via internal setter (requires InternalsVisibleTo)
        grid.Bounds = newBounds;

        // Compute rotated voxel positions
        var rotatedVoxels = new Dictionary<VoxelCoord, Voxel>();
        foreach (var (coord, voxel) in _previousVoxels)
        {
            var rotated = RotateAxis switch
            {
                // Y-axis: (maxZ-(z-minZ), y, x-minX+minZ) — width<->depth swap
                Axis.Y => new VoxelCoord(
                    oldMax.Z - (coord.Z - oldMin.Z),
                    coord.Y,
                    coord.X - oldMin.X + oldMin.Z),
                // X-axis: (x, maxZ-(z-minZ), y-minY+minZ) — height<->depth swap
                Axis.X => new VoxelCoord(
                    coord.X,
                    oldMax.Z - (coord.Z - oldMin.Z),
                    coord.Y - oldMin.Y + oldMin.Z),
                // Z-axis: (maxY-(y-minY), x-minX+minY, z) — width<->height swap
                Axis.Z => new VoxelCoord(
                    oldMax.Y - (coord.Y - oldMin.Y),
                    coord.X - oldMin.X + oldMin.Y,
                    coord.Z),
                _ => coord
            };

            rotatedVoxels[rotated] = voxel;
        }

        // Write rotated voxels — bounds are already updated so Contains checks pass
        foreach (var (coord, voxel) in rotatedVoxels)
        {
            if (!grid.Contains(coord))
                continue;

            grid.SetVoxel(coord, voxel);
        }

        AffectedRegion = new VoxelBounds(
            new VoxelCoord(
                Math.Min(_beforeBounds.Min.X, newBounds.Min.X),
                Math.Min(_beforeBounds.Min.Y, newBounds.Min.Y),
                Math.Min(_beforeBounds.Min.Z, newBounds.Min.Z)),
            new VoxelCoord(
                Math.Max(_beforeBounds.Max.X, newBounds.Max.X),
                Math.Max(_beforeBounds.Max.Y, newBounds.Max.Y),
                Math.Max(_beforeBounds.Max.Z, newBounds.Max.Z)));
    }

    /// <inheritdoc />
    public void Undo(VoxelGrid grid)
    {
        // Clear all current voxels within affected region
        var bounds = AffectedRegion;
        for (var y = bounds.Min.Y; y <= bounds.Max.Y; y++)
        {
            for (var z = bounds.Min.Z; z <= bounds.Max.Z; z++)
            {
                for (var x = bounds.Min.X; x <= bounds.Max.X; x++)
                {
                    var coord = new VoxelCoord(x, y, z);
                    if (!grid.GetVoxel(coord).IsEmpty)
                        grid.SetVoxel(coord, Voxel.Empty);
                }
            }
        }

        // Restore from snapshot
        foreach (var (coord, voxel) in _previousVoxels)
        {
            grid.SetVoxel(coord, voxel);
        }
    }
}
