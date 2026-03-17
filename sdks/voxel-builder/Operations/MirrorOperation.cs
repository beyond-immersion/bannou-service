using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Operations;

/// <summary>
/// Mirrors all non-empty voxels across a specified axis. The reflection uses the
/// formula <c>mirroredCoord = Min + Max - originalCoord</c> for the mirrored axis,
/// preserving the other two axes.
/// </summary>
public sealed class MirrorOperation : IVoxelOperation
{
    private readonly Dictionary<VoxelCoord, Voxel> _previousVoxels = new();

    /// <summary>The axis to mirror across.</summary>
    public Axis MirrorAxis { get; }

    /// <inheritdoc />
    public string SourceId { get; set; }

    /// <inheritdoc />
    public string Description => $"Mirror across {MirrorAxis} axis";

    /// <inheritdoc />
    public VoxelOperationType OperationType => VoxelOperationType.Mirror;

    /// <inheritdoc />
    public VoxelBounds AffectedRegion { get; private set; }

    /// <summary>
    /// Creates a new mirror operation.
    /// </summary>
    /// <param name="mirrorAxis">The axis to mirror across.</param>
    /// <param name="sourceId">Who created this operation.</param>
    public MirrorOperation(Axis mirrorAxis, string sourceId = "local")
    {
        MirrorAxis = mirrorAxis;
        SourceId = sourceId;
    }

    /// <inheritdoc />
    public void Execute(VoxelGrid grid, VoxelBuilderOptions options)
    {
        AffectedRegion = grid.Bounds;

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

        // Write mirrored positions
        var min = grid.Bounds.Min;
        var max = grid.Bounds.Max;

        foreach (var (coord, voxel) in _previousVoxels)
        {
            var mirrored = MirrorAxis switch
            {
                Axis.X => new VoxelCoord(min.X + max.X - coord.X, coord.Y, coord.Z),
                Axis.Y => new VoxelCoord(coord.X, min.Y + max.Y - coord.Y, coord.Z),
                Axis.Z => new VoxelCoord(coord.X, coord.Y, min.Z + max.Z - coord.Z),
                _ => coord
            };

            if (!grid.Contains(mirrored))
                continue;

            grid.SetVoxel(mirrored, voxel);
        }
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
