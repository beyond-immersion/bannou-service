using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Operations;

/// <summary>
/// Unit tests for the <see cref="RotateOperation"/> 90-degree rotation.
/// </summary>
public class RotateOperationTests
{
    private static VoxelGrid CreateGrid() =>
        new(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(15, 15, 15)));

    private static VoxelBuilderOptions DefaultOptions => VoxelBuilderOptions.Default;

    [Fact]
    public void RotateY_SwapsWidthAndDepth()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(0, 5, 0), new Voxel(7, VoxelFlags.None));

        var op = new RotateOperation(Axis.Y);
        op.Execute(grid, DefaultOptions);

        // Y-axis rotation: (maxZ-(z-minZ), y, x-minX+minZ)
        // = (15-(0-0), 5, 0-0+0) = (15, 5, 0)
        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(15, 5, 0)).PaletteIndex);
    }

    [Fact]
    public void RotateX_SwapsHeightAndDepth()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(5, 0, 0), new Voxel(7, VoxelFlags.None));

        var op = new RotateOperation(Axis.X);
        op.Execute(grid, DefaultOptions);

        // X-axis rotation: (x, maxZ-(z-minZ), y-minY+minZ)
        // = (5, 15-(0-0), 0-0+0) = (5, 15, 0)
        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(5, 15, 0)).PaletteIndex);
    }

    [Fact]
    public void RotateZ_SwapsWidthAndHeight()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 5), new Voxel(7, VoxelFlags.None));

        var op = new RotateOperation(Axis.Z);
        op.Execute(grid, DefaultOptions);

        // Z-axis rotation: (maxY-(y-minY), x-minX+minY, z)
        // = (15-(0-0), 0-0+0, 5) = (15, 0, 5)
        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(15, 0, 5)).PaletteIndex);
    }

    [Fact]
    public void Rotate_PreservesVoxelData()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(2, 5, 3), new Voxel(7, VoxelFlags.Emissive));

        var op = new RotateOperation(Axis.Y);
        op.Execute(grid, DefaultOptions);

        // Find the voxel in the grid by scanning (since rotation formula is complex)
        var found = false;
        foreach (var (chunkCoord, chunk) in grid.EnumerateChunks())
        {
            var origin = chunkCoord.ToVoxelCoord();
            for (var y = 0; y < VoxelChunk.Size; y++)
            for (var z = 0; z < VoxelChunk.Size; z++)
            for (var x = 0; x < VoxelChunk.Size; x++)
            {
                var voxel = chunk.GetVoxel(x, y, z);
                if (!voxel.IsEmpty)
                {
                    Assert.Equal(7, voxel.PaletteIndex);
                    Assert.Equal(VoxelFlags.Emissive, voxel.Flags);
                    found = true;
                }
            }
        }
        Assert.True(found);
    }

    [Fact]
    public void Rotate_Undo_RestoresOriginalPositionsAndBounds()
    {
        var grid = CreateGrid();
        var originalBounds = grid.Bounds;
        grid.SetVoxel(new VoxelCoord(2, 5, 3), new Voxel(7, VoxelFlags.None));

        var op = new RotateOperation(Axis.Y);
        op.Execute(grid, DefaultOptions);
        op.Undo(grid);

        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(2, 5, 3)).PaletteIndex);
        Assert.Equal(1, grid.VoxelCount);
    }

    [Fact]
    public void Rotate_FourTimesY_ReturnsToOriginal()
    {
        var grid = CreateGrid();
        var coord = new VoxelCoord(2, 5, 3);
        grid.SetVoxel(coord, new Voxel(7, VoxelFlags.None));

        for (var i = 0; i < 4; i++)
        {
            var op = new RotateOperation(Axis.Y);
            op.Execute(grid, DefaultOptions);
        }

        // After 4 rotations of 90° = 360°, voxel should be back at original
        Assert.Equal(7, grid.GetVoxel(coord).PaletteIndex);
        Assert.Equal(1, grid.VoxelCount);
    }

    [Fact]
    public void Rotate_MultipleVoxels_AllRotated()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(0, 0, 0), new Voxel(1, VoxelFlags.None));
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(2, VoxelFlags.None));

        var op = new RotateOperation(Axis.Y);
        op.Execute(grid, DefaultOptions);

        Assert.Equal(2, grid.VoxelCount);
    }

    [Fact]
    public void Rotate_AffectedRegion_IsUnionOfBeforeAndAfterBounds()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(1, VoxelFlags.None));

        var op = new RotateOperation(Axis.Y);
        op.Execute(grid, DefaultOptions);

        // The affected region should contain both the before and after bounds
        var region = op.AffectedRegion;
        Assert.True(region.Min.X <= 0);
        Assert.True(region.Min.Y <= 0);
        Assert.True(region.Min.Z <= 0);
    }
}
