using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;

namespace BeyondImmersion.Bannou.VoxelBuilder.Operations;

/// <summary>
/// Places a single voxel at a specific coordinate with the given palette index.
/// Captures the previous voxel state for undo.
/// </summary>
public sealed class PlaceOperation : IVoxelOperation
{
    private Voxel _previousVoxel;
    private bool _executed;

    /// <summary>The coordinate to place the voxel at.</summary>
    public VoxelCoord Coord { get; }

    /// <summary>The palette index to assign to the placed voxel.</summary>
    public byte PaletteIndex { get; }

    /// <inheritdoc />
    public string SourceId { get; set; }

    /// <inheritdoc />
    public string Description => $"Place voxel ({PaletteIndex}) at {Coord}";

    /// <inheritdoc />
    public VoxelOperationType OperationType => VoxelOperationType.Place;

    /// <inheritdoc />
    public VoxelBounds AffectedRegion => new(Coord, Coord);

    /// <summary>
    /// Creates a new place operation.
    /// </summary>
    /// <param name="coord">The coordinate to place the voxel at.</param>
    /// <param name="paletteIndex">The palette index to assign.</param>
    /// <param name="sourceId">Who created this operation.</param>
    public PlaceOperation(VoxelCoord coord, byte paletteIndex, string sourceId = "local")
    {
        Coord = coord;
        PaletteIndex = paletteIndex;
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
        grid.SetVoxel(Coord, new Voxel(PaletteIndex, VoxelFlags.None));
    }

    /// <inheritdoc />
    public void Undo(VoxelGrid grid)
    {
        if (!_executed)
            return;

        grid.SetVoxel(Coord, _previousVoxel);
    }
}
