using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Meshing;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Meshing;

/// <summary>
/// Unit tests for the <see cref="CulledMesher"/> — per-face culling with AO.
/// </summary>
public class CulledMesherTests
{
    private static readonly VoxelChunk?[] NoNeighbors = new VoxelChunk?[6];
    private readonly CulledMesher _mesher = new();

    private static (VoxelChunk chunk, Palette palette) CreateSingleVoxelChunk(byte paletteIndex = 1)
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));
        if (paletteIndex > 1)
            palette.Set(paletteIndex, new PaletteEntry(new Color(255, 0, 0), MaterialType.Metal, 0.3f));

        var chunk = new VoxelChunk();
        chunk.SetVoxel(8, 8, 8, new Voxel(paletteIndex, VoxelFlags.None));
        return (chunk, palette);
    }

    [Fact]
    public void EmptyChunk_ProducesEmptyMesh()
    {
        var chunk = new VoxelChunk();
        var palette = new Palette();
        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);

        Assert.Equal(0, mesh.VertexCount);
        Assert.Equal(0, mesh.TriangleCount);
    }

    [Fact]
    public void SingleVoxel_Produces6Faces()
    {
        var (chunk, palette) = CreateSingleVoxelChunk();
        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);

        // A single isolated voxel has 6 exposed faces
        // Each face = 4 vertices, 2 triangles
        Assert.Equal(24, mesh.VertexCount);   // 6 faces * 4 vertices
        Assert.Equal(12, mesh.TriangleCount); // 6 faces * 2 triangles
    }

    [Fact]
    public void SingleVoxel_HasVerticesNormalsIndices()
    {
        var (chunk, palette) = CreateSingleVoxelChunk();
        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);

        Assert.Equal(24 * 3, mesh.Vertices.Length);  // 24 verts * 3 components
        Assert.Equal(24 * 3, mesh.Normals.Length);
        Assert.Equal(36, mesh.Indices.Length);        // 12 triangles * 3 indices
    }

    [Fact]
    public void SingleVoxel_HasUVsAndColors()
    {
        var (chunk, palette) = CreateSingleVoxelChunk();
        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);

        Assert.NotNull(mesh.UVs);
        Assert.Equal(24 * 2, mesh.UVs.Length);    // 24 verts * 2 UV components
        Assert.NotNull(mesh.Colors);
        Assert.Equal(24 * 4, mesh.Colors.Length);  // 24 verts * 4 RGBA bytes
    }

    [Fact]
    public void SingleVoxel_HasAmbientOcclusion()
    {
        var (chunk, palette) = CreateSingleVoxelChunk();
        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);

        Assert.NotNull(mesh.AmbientOcclusion);
        Assert.Equal(24, mesh.AmbientOcclusion.Length); // 1 per vertex
    }

    [Fact]
    public void AdjacentVoxels_CullSharedFace()
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(Color.White, MaterialType.Diffuse, 0.5f));

        var chunk = new VoxelChunk();
        chunk.SetVoxel(8, 8, 8, new Voxel(1, VoxelFlags.None));
        chunk.SetVoxel(9, 8, 8, new Voxel(1, VoxelFlags.None)); // Adjacent in +X

        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);

        // Two adjacent voxels share one face that gets culled
        // Each voxel would have 6 faces alone, but they share 1 → 10 faces total
        Assert.Equal(10, mesh.TriangleCount / 2); // 10 quads = 20 triangles
    }

    [Fact]
    public void CollisionMode_SkipsUVsColorsAO()
    {
        var (chunk, palette) = CreateSingleVoxelChunk();
        var options = new MeshingOptions(CollisionMode: true);
        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, options);

        Assert.Null(mesh.UVs);
        Assert.Null(mesh.Colors);
        Assert.Null(mesh.AmbientOcclusion);
        Assert.NotNull(mesh.Vertices);
        Assert.NotNull(mesh.Normals);
        Assert.True(mesh.VertexCount > 0);
    }

    [Fact]
    public void AmbientOcclusion_Disabled_SkipsAO()
    {
        var (chunk, palette) = CreateSingleVoxelChunk();
        var options = new MeshingOptions(AmbientOcclusion: false);
        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, options);

        Assert.Null(mesh.AmbientOcclusion);
        Assert.NotNull(mesh.UVs);
        Assert.NotNull(mesh.Colors);
    }

    [Fact]
    public void VoxelScale_AffectsVertexPositions()
    {
        var (chunk, palette) = CreateSingleVoxelChunk();

        var mesh1 = _mesher.Mesh(chunk, NoNeighbors, palette, new MeshingOptions(VoxelScale: 1.0f));
        var mesh2 = _mesher.Mesh(chunk, NoNeighbors, palette, new MeshingOptions(VoxelScale: 0.5f));

        // With 2x scale, max vertex positions should be approximately 2x
        var max1 = mesh1.Vertices.Max();
        var max2 = mesh2.Vertices.Max();
        Assert.Equal(max1 / 2f, max2, 0.001f);
    }

    [Fact]
    public void Colors_MatchPalette()
    {
        var palette = new Palette();
        palette.Set(1, new PaletteEntry(new Color(200, 100, 50, 255), MaterialType.Diffuse, 0.5f));

        var chunk = new VoxelChunk();
        chunk.SetVoxel(8, 8, 8, new Voxel(1, VoxelFlags.None));
        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);

        // All vertex colors should match palette entry 1
        Assert.NotNull(mesh.Colors);
        for (var i = 0; i < mesh.VertexCount; i++)
        {
            Assert.Equal(200, mesh.Colors[i * 4]);
            Assert.Equal(100, mesh.Colors[i * 4 + 1]);
            Assert.Equal(50, mesh.Colors[i * 4 + 2]);
            Assert.Equal(255, mesh.Colors[i * 4 + 3]);
        }
    }

    [Fact]
    public void Deterministic_SameInputProducesSameOutput()
    {
        var (chunk, palette) = CreateSingleVoxelChunk();

        var mesh1 = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);
        var mesh2 = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);

        Assert.Equal(mesh1.Vertices, mesh2.Vertices);
        Assert.Equal(mesh1.Indices, mesh2.Indices);
    }
}
