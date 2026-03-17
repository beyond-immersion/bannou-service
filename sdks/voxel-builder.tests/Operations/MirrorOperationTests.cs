using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Operations;

/// <summary>
/// Unit tests for the <see cref="MirrorOperation"/>.
/// </summary>
public class MirrorOperationTests
{
    private static VoxelGrid CreateGrid() =>
        new(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(15, 15, 15)));

    private static VoxelBuilderOptions DefaultOptions => VoxelBuilderOptions.Default;

    [Fact]
    public void MirrorX_ReflectsAcrossXAxis()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(2, 5, 5), new Voxel(7, VoxelFlags.None));

        var op = new MirrorOperation(Axis.X);
        op.Execute(grid, DefaultOptions);

        // Original position should be empty; mirrored position should have the voxel
        // Formula: mirrored.X = min.X + max.X - coord.X = 0 + 15 - 2 = 13
        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(13, 5, 5)).PaletteIndex);
    }

    [Fact]
    public void MirrorY_ReflectsAcrossYAxis()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(5, 3, 5), new Voxel(7, VoxelFlags.None));

        var op = new MirrorOperation(Axis.Y);
        op.Execute(grid, DefaultOptions);

        // mirrored.Y = 0 + 15 - 3 = 12
        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(5, 12, 5)).PaletteIndex);
    }

    [Fact]
    public void MirrorZ_ReflectsAcrossZAxis()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(5, 5, 1), new Voxel(7, VoxelFlags.None));

        var op = new MirrorOperation(Axis.Z);
        op.Execute(grid, DefaultOptions);

        // mirrored.Z = 0 + 15 - 1 = 14
        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(5, 5, 14)).PaletteIndex);
    }

    [Fact]
    public void Mirror_PreservesVoxelData()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(2, 5, 5), new Voxel(7, VoxelFlags.Emissive));

        var op = new MirrorOperation(Axis.X);
        op.Execute(grid, DefaultOptions);

        var mirrored = grid.GetVoxel(new VoxelCoord(13, 5, 5));
        Assert.Equal(7, mirrored.PaletteIndex);
        Assert.Equal(VoxelFlags.Emissive, mirrored.Flags);
    }

    [Fact]
    public void Mirror_Undo_RestoresOriginalPositions()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(2, 5, 5), new Voxel(7, VoxelFlags.None));

        var op = new MirrorOperation(Axis.X);
        op.Execute(grid, DefaultOptions);
        op.Undo(grid);

        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(2, 5, 5)).PaletteIndex);
        Assert.True(grid.GetVoxel(new VoxelCoord(13, 5, 5)).IsEmpty);
    }

    [Fact]
    public void Mirror_CenterVoxel_StaysInPlace()
    {
        var grid = CreateGrid(); // bounds 0-15, center sum = 15
        // For odd width, center-ish voxel at (7, 5, 5) mirrors to (8, 5, 5)
        // For even width (16 voxels 0-15), there's no exact center
        grid.SetVoxel(new VoxelCoord(7, 5, 5), new Voxel(3, VoxelFlags.None));

        var op = new MirrorOperation(Axis.X);
        op.Execute(grid, DefaultOptions);

        // mirrored.X = 0 + 15 - 7 = 8
        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(8, 5, 5)).PaletteIndex);
    }

    [Fact]
    public void Mirror_MultipleVoxels_AllReflected()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(3, 7, 10), new Voxel(2, VoxelFlags.None));

        var op = new MirrorOperation(Axis.X);
        op.Execute(grid, DefaultOptions);

        Assert.Equal(1, grid.GetVoxel(new VoxelCoord(15, 0, 0)).PaletteIndex);
        Assert.Equal(2, grid.GetVoxel(new VoxelCoord(12, 7, 10)).PaletteIndex);
    }

    [Fact]
    public void Mirror_AffectedRegion_IsGridBounds()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(1, VoxelFlags.None));

        var op = new MirrorOperation(Axis.X);
        op.Execute(grid, DefaultOptions);

        Assert.Equal(grid.Bounds, op.AffectedRegion);
    }
}
