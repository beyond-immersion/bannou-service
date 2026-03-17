using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelBuilder.Operations;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelBuilder.Tests.Operations;

/// <summary>
/// Unit tests for the <see cref="BrushOperation"/> covering all three brush shapes.
/// </summary>
public class BrushOperationTests
{
    private static VoxelGrid CreateGrid() =>
        new(new VoxelBounds(new VoxelCoord(0, 0, 0), new VoxelCoord(31, 31, 31)));

    private static VoxelBuilderOptions DefaultOptions => VoxelBuilderOptions.Default;

    [Fact]
    public void CubeBrush_FillsEntireBox()
    {
        var grid = CreateGrid();
        var op = new BrushOperation(new VoxelCoord(5, 5, 5), new BrushShape(BrushType.Cube, 1), 3, erase: false);
        op.Execute(grid, DefaultOptions);

        // Cube r=1 from center (5,5,5): coords (4,4,4) to (6,6,6) = 3x3x3 = 27
        Assert.Equal(27, grid.VoxelCount);
        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(4, 4, 4)).PaletteIndex);
        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(6, 6, 6)).PaletteIndex);
    }

    [Fact]
    public void SphereBrush_OnlyFillsWithinRadius()
    {
        var grid = CreateGrid();
        var center = new VoxelCoord(10, 10, 10);
        var op = new BrushOperation(center, new BrushShape(BrushType.Sphere, 2), 5, erase: false);
        op.Execute(grid, DefaultOptions);

        // All filled voxels should be within Euclidean distance 2 of center
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
                    var coord = new VoxelCoord(origin.X + x, origin.Y + y, origin.Z + z);
                    Assert.True(center.Distance(coord) <= 2.0f + 0.01f,
                        $"Voxel at {coord} is outside sphere radius (dist={center.Distance(coord)})");
                }
            }
        }

        // Corner of bounding box should NOT be filled (distance = sqrt(12) ≈ 3.46 > 2)
        Assert.True(grid.GetVoxel(new VoxelCoord(8, 8, 8)).IsEmpty);
    }

    [Fact]
    public void CylinderBrush_FiltersOnXZPlane()
    {
        var grid = CreateGrid();
        var center = new VoxelCoord(10, 10, 10);
        var op = new BrushOperation(center, new BrushShape(BrushType.Cylinder, 2), 5, erase: false);
        op.Execute(grid, DefaultOptions);

        // Check that voxels along Y within radius still exist
        Assert.False(grid.GetVoxel(new VoxelCoord(10, 8, 10)).IsEmpty); // directly above center in Y

        // Check that XZ corners are excluded (distance in XZ plane = sqrt(8) ≈ 2.83 > 2)
        Assert.True(grid.GetVoxel(new VoxelCoord(12, 10, 12)).IsEmpty);
    }

    [Fact]
    public void BrushErase_ClearsVoxelsInShape()
    {
        var grid = CreateGrid();
        // Fill the whole area first
        for (var y = 3; y <= 7; y++)
        for (var z = 3; z <= 7; z++)
        for (var x = 3; x <= 7; x++)
            grid.SetVoxel(new VoxelCoord(x, y, z), new Voxel(5, VoxelFlags.None));

        var before = grid.VoxelCount;
        var op = new BrushOperation(new VoxelCoord(5, 5, 5), new BrushShape(BrushType.Cube, 1), 0, erase: true);
        op.Execute(grid, DefaultOptions);

        Assert.True(grid.VoxelCount < before);
        Assert.True(grid.GetVoxel(new VoxelCoord(5, 5, 5)).IsEmpty);
    }

    [Fact]
    public void Brush_Undo_RestoresAllPreviousVoxels()
    {
        var grid = CreateGrid();
        // Place some existing voxels
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(3, VoxelFlags.Emissive));

        var op = new BrushOperation(new VoxelCoord(5, 5, 5), new BrushShape(BrushType.Cube, 1), 7, erase: false);
        op.Execute(grid, DefaultOptions);
        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);

        op.Undo(grid);
        var restored = grid.GetVoxel(new VoxelCoord(5, 5, 5));
        Assert.Equal(3, restored.PaletteIndex);
        Assert.Equal(VoxelFlags.Emissive, restored.Flags);
    }

    [Fact]
    public void Brush_SkipsFrozenVoxels()
    {
        var grid = CreateGrid();
        grid.SetVoxel(new VoxelCoord(5, 5, 5), new Voxel(3, VoxelFlags.Frozen));
        grid.SetVoxel(new VoxelCoord(6, 5, 5), new Voxel(3, VoxelFlags.None));

        var op = new BrushOperation(new VoxelCoord(5, 5, 5), new BrushShape(BrushType.Cube, 1), 7, erase: false);
        op.Execute(grid, DefaultOptions);

        // Frozen voxel untouched
        Assert.Equal(3, grid.GetVoxel(new VoxelCoord(5, 5, 5)).PaletteIndex);
        Assert.Equal(VoxelFlags.Frozen, grid.GetVoxel(new VoxelCoord(5, 5, 5)).Flags);
        // Non-frozen voxel changed
        Assert.Equal(7, grid.GetVoxel(new VoxelCoord(6, 5, 5)).PaletteIndex);
    }

    [Fact]
    public void Brush_OutOfBoundsCoords_AreSkipped()
    {
        var grid = CreateGrid(); // bounds 0-31
        // Brush at corner — some coords will be out of bounds
        var op = new BrushOperation(new VoxelCoord(0, 0, 0), new BrushShape(BrushType.Cube, 2), 5, erase: false);
        op.Execute(grid, DefaultOptions);

        // Only the in-bounds portion should be filled
        Assert.False(grid.GetVoxel(new VoxelCoord(0, 0, 0)).IsEmpty);
        Assert.False(grid.GetVoxel(new VoxelCoord(2, 2, 2)).IsEmpty);
    }

    [Fact]
    public void Brush_AffectedRegion_MatchesBounds()
    {
        var center = new VoxelCoord(10, 10, 10);
        var op = new BrushOperation(center, new BrushShape(BrushType.Sphere, 3), 5, erase: false);

        var region = op.AffectedRegion;
        Assert.Equal(new VoxelCoord(7, 7, 7), region.Min);
        Assert.Equal(new VoxelCoord(13, 13, 13), region.Max);
    }

    [Fact]
    public void Brush_Properties_AreCorrect()
    {
        var op = new BrushOperation(
            new VoxelCoord(5, 5, 5),
            new BrushShape(BrushType.Sphere, 3),
            42,
            erase: false);

        Assert.Equal(VoxelOperationType.Brush, op.OperationType);
        Assert.Equal(new VoxelCoord(5, 5, 5), op.Center);
        Assert.Equal(BrushType.Sphere, op.Brush.Type);
        Assert.Equal(3, op.Brush.Radius);
        Assert.Equal(42, op.PaletteIndex);
        Assert.False(op.Erase);
        Assert.Contains("Sphere", op.Description);
    }
}
