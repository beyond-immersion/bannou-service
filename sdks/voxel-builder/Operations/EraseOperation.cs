using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Operations;

/// <summary>
/// Removes a single voxel at a specific coordinate by setting it to <see cref="Voxel.Empty"/>.
/// Captures the previous voxel state for undo.
/// </summary>
public sealed class EraseOperation : IVoxelOperation
{
    private Voxel _previousVoxel;
    private bool _executed;

    /// <summary>The coordinate to erase.</summary>
    public VoxelCoord Coord { get; }

    /// <inheritdoc />
    public string SourceId { get; set; }

    /// <inheritdoc />
    public string Description => $"Erase voxel at {Coord}";

    /// <inheritdoc />
    public VoxelOperationType OperationType => VoxelOperationType.Erase;

    /// <inheritdoc />
    public VoxelBounds AffectedRegion => new(Coord, Coord);

    /// <summary>
    /// Creates a new erase operation.
    /// </summary>
    /// <param name="coord">The coordinate to erase.</param>
    /// <param name="sourceId">Who created this operation.</param>
    public EraseOperation(VoxelCoord coord, string sourceId = "local")
    {
        Coord = coord;
        SourceId = sourceId;
    }

    /// <inheritdoc />
    public void Execute(VoxelGrid grid, VoxelBuilderOptions options)
    {
        var existing = grid.GetVoxel(Coord);
        if (options.EnforceFrozen && existing.Flags.HasFlag(VoxelFlags.Frozen))
            return;

        _previousVoxel = existing;
        _executed = true;
        grid.SetVoxel(Coord, Voxel.Empty);
    }

    /// <inheritdoc />
    public void Undo(VoxelGrid grid)
    {
        if (!_executed)
            return;

        grid.SetVoxel(Coord, _previousVoxel);
    }
}
