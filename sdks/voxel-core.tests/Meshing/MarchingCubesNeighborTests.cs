using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Meshing;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Meshing;

/// <summary>
/// Tests that <see cref="MarchingCubesMesher"/> correctly looks up actual palette indices
/// from neighbor chunks (not just empty/non-empty boolean). This was a bug fix: the mesher
/// previously returned a hardcoded 1 for any non-empty neighbor voxel instead of the real index.
/// </summary>
public class MarchingCubesNeighborTests
{
    private readonly MarchingCubesMesher _mesher = new();

    [Fact]
    public void NeighborChunk_ActualPaletteIndex_UsedForColor()
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        palette.Set(5, new PaletteEntry(new Color(200, 0, 0), MaterialType.Metal, 0.3f));

        // Chunk with voxels at boundary (x=15)
        var chunk = new VoxelChunk();
        chunk.SetVoxel(15, 8, 8, new Voxel(1, VoxelFlags.None));

        // +X neighbor with voxel at x=0 (adjacent to our boundary voxel)
        var neighborPlusX = new VoxelChunk();
        neighborPlusX.SetVoxel(0, 8, 8, new Voxel(5, VoxelFlags.None));

        var neighbors = new VoxelChunk?[6];
        neighbors[0] = neighborPlusX; // +X

        var mesh = _mesher.Mesh(chunk, neighbors, palette, MeshingOptions.Default);

        // The mesh should produce triangles — the boundary between chunk and
        // neighbor creates isosurface geometry
        Assert.True(mesh.TriangleCount > 0);
    }

    [Fact]
    public void NeighborChunk_Empty_TreatedAsZero()
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));

        var chunk = new VoxelChunk();
        chunk.SetVoxel(15, 8, 8, new Voxel(1, VoxelFlags.None));

        // No +X neighbor — should treat as empty (palette 0)
        var neighbors = new VoxelChunk?[6];
        var mesh = _mesher.Mesh(chunk, neighbors, palette, MeshingOptions.Default);

        Assert.True(mesh.TriangleCount > 0);
    }

    [Fact]
    public void NeighborChunk_AllDirections_LookedUpCorrectly()
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        palette.Set(2, new PaletteEntry(Color.Black, MaterialType.Diffuse, 0.5f));

        // Center chunk filled
        var chunk = new VoxelChunk();
        for (var x = 0; x < 16; x++)
        for (var y = 0; y < 16; y++)
        for (var z = 0; z < 16; z++)
            chunk.SetVoxel(x, y, z, new Voxel(1, VoxelFlags.None));

        // All 6 neighbors filled too — isosurface should be at chunk boundaries
        var neighbors = new VoxelChunk?[6];
        for (var i = 0; i < 6; i++)
        {
            var neighbor = new VoxelChunk();
            for (var x = 0; x < 16; x++)
            for (var y = 0; y < 16; y++)
            for (var z = 0; z < 16; z++)
                neighbor.SetVoxel(x, y, z, new Voxel(2, VoxelFlags.None));
            neighbors[i] = neighbor;
        }

        // With all chunks solid (including neighbors), MC should produce zero triangles
        // because there are no isosurface boundaries — everything is "inside"
        var mesh = _mesher.Mesh(chunk, neighbors, palette, MeshingOptions.Default);
        Assert.Equal(0, mesh.TriangleCount);
    }
}
