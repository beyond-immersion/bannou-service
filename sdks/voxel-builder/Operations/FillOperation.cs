using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Operations;

/// <summary>
/// BFS flood fill operation. Starting from an origin coordinate, expands to all 6-connected
/// neighbors that share the same palette index as the origin. Replaces them with the fill
/// palette index. Bounded by an optional limit region.
/// </summary>
public sealed class FillOperation : IVoxelOperation
{
    private static readonly VoxelCoord[] Neighbors =
    [
        new( 1,  0,  0),
        new(-1,  0,  0),
        new( 0,  1,  0),
        new( 0, -1,  0),
        new( 0,  0,  1),
        new( 0,  0, -1)
    ];

    private readonly Dictionary<VoxelCoord, Voxel> _previousVoxels = new();
    private VoxelBounds _affectedRegion;

    /// <summary>The starting coordinate for the flood fill.</summary>
    public VoxelCoord Origin { get; }

    /// <summary>The palette index to fill with.</summary>
    public byte FillPaletteIndex { get; }

    /// <summary>Bounding box that constrains the fill region.</summary>
    public VoxelBounds Limit { get; }

    /// <inheritdoc />
    public string SourceId { get; set; }

    /// <inheritdoc />
    public string Description => $"Flood fill (idx={FillPaletteIndex}) from {Origin}";

    /// <inheritdoc />
    public VoxelOperationType OperationType => VoxelOperationType.Fill;

    /// <inheritdoc />
    public VoxelBounds AffectedRegion => _affectedRegion;

    /// <summary>
    /// Creates a new flood fill operation.
    /// </summary>
    /// <param name="origin">The starting coordinate for the flood fill.</param>
    /// <param name="fillPaletteIndex">The palette index to fill with.</param>
    /// <param name="limit">Bounding box that constrains the fill region.</param>
    /// <param name="sourceId">Who created this operation.</param>
    public FillOperation(VoxelCoord origin, byte fillPaletteIndex, VoxelBounds limit, string sourceId = "local")
    {
        Origin = origin;
        FillPaletteIndex = fillPaletteIndex;
        Limit = limit;
        _affectedRegion = new VoxelBounds(origin, origin);
        SourceId = sourceId;
    }

    /// <inheritdoc />
    public void Execute(VoxelGrid grid, VoxelBuilderOptions options)
    {
        var originVoxel = grid.GetVoxel(Origin);
        var targetIndex = originVoxel.PaletteIndex;

        // No-op if filling with the same palette index
        if (targetIndex == FillPaletteIndex)
            return;

        var visited = new HashSet<VoxelCoord>();
        var queue = new Queue<VoxelCoord>();

        queue.Enqueue(Origin);
        visited.Add(Origin);

        var minCoord = Origin;
        var maxCoord = Origin;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var existing = grid.GetVoxel(current);

            if (existing.PaletteIndex != targetIndex)
                continue;

            if (options.EnforceFrozen && existing.Flags.HasFlag(VoxelFlags.Frozen))
                continue;

            _previousVoxels[current] = existing;
            // Preserve existing flags (per implementation map) — only change the palette index
            grid.SetVoxel(current, new Voxel(FillPaletteIndex, existing.Flags));

            // Expand affected region
            minCoord = new VoxelCoord(
                Math.Min(minCoord.X, current.X),
                Math.Min(minCoord.Y, current.Y),
                Math.Min(minCoord.Z, current.Z));
            maxCoord = new VoxelCoord(
                Math.Max(maxCoord.X, current.X),
                Math.Max(maxCoord.Y, current.Y),
                Math.Max(maxCoord.Z, current.Z));

            foreach (var offset in Neighbors)
            {
                var neighbor = current + offset;
                if (visited.Contains(neighbor))
                    continue;

                if (!Limit.Contains(neighbor))
                    continue;

                if (!grid.Contains(neighbor))
                    continue;

                var neighborVoxel = grid.GetVoxel(neighbor);
                if (neighborVoxel.PaletteIndex != targetIndex)
                    continue;

                visited.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }

        _affectedRegion = new VoxelBounds(minCoord, maxCoord);
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
