using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Meshing;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Meshing;

/// <summary>
/// Unit tests for the <see cref="GreedyMesher"/> — coplanar face merging.
/// </summary>
public class GreedyMesherTests
{
    private static readonly VoxelChunk?[] NoNeighbors = new VoxelChunk?[6];
    private readonly GreedyMesher _mesher = new();
    private readonly CulledMesher _culledMesher = new();

    private static Palette CreateSimplePalette()
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        return palette;
    }

    [Fact]
    public void EmptyChunk_ProducesEmptyMesh()
    {
        var chunk = new VoxelChunk();
        var mesh = _mesher.Mesh(chunk, NoNeighbors, CreateSimplePalette(), MeshingOptions.Default);
        Assert.Equal(0, mesh.VertexCount);
    }

    [Fact]
    public void SingleVoxel_Produces6Faces()
    {
        var palette = CreateSimplePalette();
        var chunk = new VoxelChunk();
        chunk.SetVoxel(8, 8, 8, new Voxel(1, VoxelFlags.None));

        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);
        // Single voxel with no merging opportunity still produces 6 faces
        Assert.Equal(12, mesh.TriangleCount);
    }

    [Fact]
    public void FlatSurface_MergesFaces_FewerTrianglesThanCulled()
    {
        var palette = CreateSimplePalette();
        var chunk = new VoxelChunk();

        // Create a 4x4 flat surface at y=0 (same material)
        for (var x = 0; x < 4; x++)
        for (var z = 0; z < 4; z++)
            chunk.SetVoxel(x, 0, z, new Voxel(1, VoxelFlags.None));

        var options = new MeshingOptions(AmbientOcclusion: false);
        var greedyMesh = _mesher.Mesh(chunk, NoNeighbors, palette, options);
        var culledMesh = _culledMesher.Mesh(chunk, NoNeighbors, palette, options);

        // Greedy should produce fewer triangles through face merging
        Assert.True(greedyMesh.TriangleCount < culledMesh.TriangleCount,
            $"Greedy ({greedyMesh.TriangleCount}) should have fewer triangles than Culled ({culledMesh.TriangleCount})");
    }

    [Fact]
    public void SolidBlock_MergesEfficiently()
    {
        var palette = CreateSimplePalette();
        var chunk = new VoxelChunk();

        // 4x4x4 solid block — each face direction has a single 4x4 quad (6 total)
        for (var x = 4; x < 8; x++)
        for (var y = 4; y < 8; y++)
        for (var z = 4; z < 8; z++)
            chunk.SetVoxel(x, y, z, new Voxel(1, VoxelFlags.None));

        var options = new MeshingOptions(AmbientOcclusion: false);
        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, options);

        // A 4x4x4 solid block should produce exactly 6 quads (12 triangles) without AO
        // because each face direction is one 4x4 merged quad
        Assert.Equal(12, mesh.TriangleCount);
    }

    [Fact]
    public void DifferentMaterials_PreventMerging()
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        palette.Set(2, new PaletteEntry(Color.Black, MaterialType.Diffuse, 0.5f));

        var chunk = new VoxelChunk();
        // Checkerboard pattern on a row — prevents merging
        chunk.SetVoxel(0, 0, 0, new Voxel(1, VoxelFlags.None));
        chunk.SetVoxel(1, 0, 0, new Voxel(2, VoxelFlags.None));
        chunk.SetVoxel(2, 0, 0, new Voxel(1, VoxelFlags.None));
        chunk.SetVoxel(3, 0, 0, new Voxel(2, VoxelFlags.None));

        var options = new MeshingOptions(AmbientOcclusion: false);
        var greedyMesh = _mesher.Mesh(chunk, NoNeighbors, palette, options);
        var culledMesh = _culledMesher.Mesh(chunk, NoNeighbors, palette, options);

        // With alternating materials, greedy can't merge much — triangle count should be similar
        // (though still potentially fewer on side faces)
        Assert.True(greedyMesh.TriangleCount <= culledMesh.TriangleCount);
    }

    [Fact]
    public void CollisionMode_SkipsOptionalArrays()
    {
        var palette = CreateSimplePalette();
        var chunk = new VoxelChunk();
        chunk.SetVoxel(0, 0, 0, new Voxel(1, VoxelFlags.None));

        var options = new MeshingOptions(CollisionMode: true);
        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, options);

        Assert.Null(mesh.UVs);
        Assert.Null(mesh.Colors);
        Assert.Null(mesh.AmbientOcclusion);
        Assert.True(mesh.VertexCount > 0);
    }

    [Fact]
    public void Deterministic_SameInputProducesSameOutput()
    {
        var palette = CreateSimplePalette();
        var chunk = new VoxelChunk();
        for (var x = 0; x < 4; x++)
        for (var z = 0; z < 4; z++)
            chunk.SetVoxel(x, 0, z, new Voxel(1, VoxelFlags.None));

        var mesh1 = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);
        var mesh2 = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);

        Assert.Equal(mesh1.Vertices, mesh2.Vertices);
        Assert.Equal(mesh1.Indices, mesh2.Indices);
        Assert.Equal(mesh1.TriangleCount, mesh2.TriangleCount);
    }
}
