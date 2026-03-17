using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Operations;

/// <summary>
/// Replaces all voxels of one palette index with another across the entire grid.
/// Preserves voxel flags during replacement.
/// </summary>
public sealed class ReplaceOperation : IVoxelOperation
{
    private readonly Dictionary<VoxelCoord, Voxel> _previousVoxels = new();

    /// <summary>The palette index to search for and replace.</summary>
    public byte FromIndex { get; }

    /// <summary>The palette index to replace with.</summary>
    public byte ToIndex { get; }

    /// <inheritdoc />
    public string SourceId { get; set; }

    /// <inheritdoc />
    public string Description => $"Replace palette index {FromIndex} with {ToIndex}";

    /// <inheritdoc />
    public VoxelOperationType OperationType => VoxelOperationType.Replace;

    /// <inheritdoc />
    public VoxelBounds AffectedRegion { get; private set; }

    /// <summary>
    /// Creates a new replace operation.
    /// </summary>
    /// <param name="fromIndex">The palette index to search for.</param>
    /// <param name="toIndex">The palette index to replace with.</param>
    /// <param name="sourceId">Who created this operation.</param>
    public ReplaceOperation(byte fromIndex, byte toIndex, string sourceId = "local")
    {
        FromIndex = fromIndex;
        ToIndex = toIndex;
        SourceId = sourceId;
    }

    /// <inheritdoc />
    public void Execute(VoxelGrid grid, VoxelBuilderOptions options)
    {
        var minCoord = new VoxelCoord(int.MaxValue, int.MaxValue, int.MaxValue);
        var maxCoord = new VoxelCoord(int.MinValue, int.MinValue, int.MinValue);
        var hasChanges = false;

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
                        if (voxel.PaletteIndex != FromIndex)
                            continue;

                        var coord = new VoxelCoord(chunkOrigin.X + x, chunkOrigin.Y + y, chunkOrigin.Z + z);

                        if (options.EnforceFrozen && voxel.Flags.HasFlag(VoxelFlags.Frozen))
                            continue;

                        _previousVoxels[coord] = voxel;
                        grid.SetVoxel(coord, new Voxel(ToIndex, voxel.Flags));

                        minCoord = new VoxelCoord(
                            Math.Min(minCoord.X, coord.X),
                            Math.Min(minCoord.Y, coord.Y),
                            Math.Min(minCoord.Z, coord.Z));
                        maxCoord = new VoxelCoord(
                            Math.Max(maxCoord.X, coord.X),
                            Math.Max(maxCoord.Y, coord.Y),
                            Math.Max(maxCoord.Z, coord.Z));
                        hasChanges = true;
                    }
                }
            }
        }

        AffectedRegion = hasChanges
            ? new VoxelBounds(minCoord, maxCoord)
            : new VoxelBounds(VoxelCoord.Zero, VoxelCoord.Zero);
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
