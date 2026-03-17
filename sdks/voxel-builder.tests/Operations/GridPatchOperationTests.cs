using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Serialization;
using Xunit;
using VoxelBuilderClass = BeyondImmersion.Bannou.VoxelBuilder.Core.VoxelBuilder;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Operations;

/// <summary>
/// Unit tests for the <see cref="GridPatchOperation"/> delta-based atomic operation.
/// </summary>
public class GridPatchOperationTests
{
    private static VoxelBounds SmallBounds => new(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31));
    private static VoxelBuilderOptions DefaultOptions => VoxelBuilderOptions.Default;

    [Fact]
    public void Execute_AppliesDeltaToGrid()
    {
        var before = new VoxelGrid(SmallBounds);
        var after = new VoxelGrid(SmallBounds);
        after.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(7, VoxelFlags.None));
        after.SetVoxel(new VoxelCoord(6, 5, 5), new Voxel(8, VoxelFlags.None));

        var patchOp = VoxelBuilderClass.DiffToOperation(before, after, "gen");

        var target = new VoxelGrid(SmallBounds);
        patchOp.Execute(target, DefaultOptions);

        Assert.Equal(7, target.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
        Assert.Equal(8, target.GetVoxel(new VoxelCoord(6, 5, 5)).PaletteIndex);
    }

    [Fact]
    public void Undo_RestoresBeforeState()
    {
        var before = new VoxelGrid(SmallBounds);
        before.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(3, VoxelFlags.None));

        var after = new VoxelGrid(SmallBounds);
        after.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(7, VoxelFlags.None));

        var patchOp = VoxelBuilderClass.DiffToOperation(before, after, "gen");

        // Apply to a grid that matches "before" state
        var target = new VoxelGrid(SmallBounds);
        target.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(3, VoxelFlags.None));

        patchOp.Execute(target, DefaultOptions);
        Assert.Equal(7, target.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);

        patchOp.Undo(target);
        Assert.Equal(3, target.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
    }

    [Fact]
    public void Undo_RemovesAddedChunks()
    {
        var before = new VoxelGrid(SmallBounds);
        var after = new VoxelGrid(SmallBounds);
        after.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));

        var patchOp = VoxelBuilderClass.DiffToOperation(before, after, "gen");

        var target = new VoxelGrid(SmallBounds);
        patchOp.Execute(target, DefaultOptions);
        Assert.Equal(1, target.VoxelCount);

        patchOp.Undo(target);
        Assert.Equal(0, target.VoxelCount);
    }

    [Fact]
    public void ReceiverSide_CapturesBeforeStateOnExecute()
    {
        var before = new VoxelGrid(SmallBounds);
        var after = new VoxelGrid(SmallBounds);
        after.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));

        // Simulate receiver: create GridPatchOperation without pre-computed before-chunks
        var delta = VoxelDelta.Compute(before, after);
        var receiverOp = new GridPatchOperation(delta, "gen");

        // Before-chunks should be empty initially
        Assert.Empty(receiverOp.BeforeChunks);

        // Execute captures before-state
        var target = new VoxelGrid(SmallBounds);
        receiverOp.Execute(target, DefaultOptions);

        // Now before-chunks should be populated
        Assert.NotEmpty(receiverOp.BeforeChunks);

        // Undo should work
        receiverOp.Undo(target);
        Assert.Equal(0, target.VoxelCount);
    }

    [Fact]
    public void AffectedRegion_ComputedFromChunkCoords()
    {
        var before = new VoxelGrid(SmallBounds);
        var after = new VoxelGrid(SmallBounds);
        after.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));

        var patchOp = VoxelBuilderClass.DiffToOperation(before, after, "gen");

        var target = new VoxelGrid(SmallBounds);
        patchOp.Execute(target, DefaultOptions);

        var region = patchOp.AffectedRegion;
        Assert.True(region.Contains(new VoxelCoord(0, 0, 0)));
    }

    [Fact]
    public void Properties_AreCorrect()
    {
        var delta = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; // Minimal empty delta
        var op = new GridPatchOperation(delta, "generator");

        Assert.Equal(VoxelOperationType.GridPatch, op.OperationType);
        Assert.Equal("generator", op.SourceId);
        Assert.Contains("grid patch", op.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Same(delta, op.Delta);
    }
}
