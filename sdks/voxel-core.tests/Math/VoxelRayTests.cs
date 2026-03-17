using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Math;

/// <summary>
/// Unit tests for <see cref="VoxelRay"/> DDA raycasting.
/// </summary>
public class VoxelRayTests
{
    private static VoxelGrid CreateTestGrid()
    {
        var grid = new VoxelGrid(new VoxelBounds(
            new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31)));
        return grid;
    }

    [Fact]
    public void Cast_HitsSolidVoxel()
    {
        var grid = CreateTestGrid();
        grid.SetVoxel(new VoxelCoord(5, 0, 0), new Voxel(1, VoxelFlags.None));

        var ray = VoxelRay.Create(new VoxelCoord(0, 0, 0), 1f, 0f, 0f);
        var result = ray.Cast(grid, 100);

        Assert.NotNull(result);
        Assert.Equal(new VoxelCoord(5, 0, 0), result.Value.Hit);
    }

    [Fact]
    public void Cast_MissesEmptyGrid()
    {
        var grid = CreateTestGrid();
        var ray = VoxelRay.Create(new VoxelCoord(0, 0, 0), 1f, 0f, 0f);
        var result = ray.Cast(grid, 100);
        Assert.Null(result);
    }

    [Fact]
    public void Cast_ReturnsCorrectFaceNormal()
    {
        var grid = CreateTestGrid();
        grid.SetVoxel(new VoxelCoord(5, 0, 0), new Voxel(1, VoxelFlags.None));

        // Casting in +X direction, should hit the -X face
        var ray = VoxelRay.Create(new VoxelCoord(0, 0, 0), 1f, 0f, 0f);
        var result = ray.Cast(grid, 100);

        Assert.NotNull(result);
        Assert.Equal(new VoxelCoord(-1, 0, 0), result.Value.FaceNormal);
    }

    [Fact]
    public void Cast_DiagonalDirection_FindsVoxel()
    {
        var grid = CreateTestGrid();
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(1, VoxelFlags.None));

        var ray = VoxelRay.Create(new VoxelCoord(0, 0, 0), 1f, 1f, 1f);
        var result = ray.Cast(grid, 100);

        Assert.NotNull(result);
        Assert.Equal(new VoxelCoord(5, 5, 5), result.Value.Hit);
    }

    [Fact]
    public void Cast_NegativeDirection_FindsVoxel()
    {
        var grid = CreateTestGrid();
        grid.SetVoxel(new VoxelCoord(2, 0, 0), new Voxel(1, VoxelFlags.None));

        var ray = VoxelRay.Create(new VoxelCoord(10, 0, 0), -1f, 0f, 0f);
        var result = ray.Cast(grid, 100);

        Assert.NotNull(result);
        Assert.Equal(new VoxelCoord(2, 0, 0), result.Value.Hit);
    }

    [Fact]
    public void Cast_MaxStepsReached_ReturnsNull()
    {
        var grid = CreateTestGrid();
        grid.SetVoxel(new VoxelCoord(20, 0, 0), new Voxel(1, VoxelFlags.None));

        var ray = VoxelRay.Create(new VoxelCoord(0, 0, 0), 1f, 0f, 0f);
        var result = ray.Cast(grid, 5); // Only 5 steps, voxel is at distance 20

        Assert.Null(result);
    }

    [Fact]
    public void Step_AdvancesAlongRay()
    {
        var ray = VoxelRay.Create(new VoxelCoord(0, 0, 0), 1f, 0f, 0f);
        var next = ray.Step();
        Assert.Equal(new VoxelCoord(1, 0, 0), next);
    }
}
