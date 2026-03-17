using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Meshing;
using Xunit;

namespace BeyondImmersion.Bannou.VoxelCore.Tests.Meshing;

/// <summary>
/// Unit tests for the <see cref="MarchingCubesMesher"/> — smooth surface extraction.
/// </summary>
public class MarchingCubesMesherTests
{
    private static readonly VoxelChunk?[] NoNeighbors = new VoxelChunk?[6];
    private readonly MarchingCubesMesher _mesher = new();

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
    public void SingleVoxel_ProducesTriangles()
    {
        var palette = CreateSimplePalette();
        var chunk = new VoxelChunk();
        chunk.SetVoxel(8, 8, 8, new Voxel(1, VoxelFlags.None));

        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);

        // Marching cubes produces triangles at isosurface boundaries
        Assert.True(mesh.TriangleCount > 0);
        Assert.True(mesh.VertexCount > 0);
    }

    [Fact]
    public void AmbientOcclusion_AlwaysNull()
    {
        var palette = CreateSimplePalette();
        var chunk = new VoxelChunk();
        chunk.SetVoxel(8, 8, 8, new Voxel(1, VoxelFlags.None));

        // AO is not applicable to marching cubes
        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette,
            new MeshingOptions(AmbientOcclusion: true));
        Assert.Null(mesh.AmbientOcclusion);
    }

    [Fact]
    public void SolidBlock_ProducesOuterSurfaceOnly()
    {
        var palette = CreateSimplePalette();
        var chunk = new VoxelChunk();

        // Fill a 4x4x4 block — marching cubes should only produce surface triangles
        for (var x = 4; x < 8; x++)
        for (var y = 4; y < 8; y++)
        for (var z = 4; z < 8; z++)
            chunk.SetVoxel(x, y, z, new Voxel(1, VoxelFlags.None));

        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);

        Assert.True(mesh.TriangleCount > 0);
        Assert.NotNull(mesh.Normals);
        Assert.Equal(mesh.VertexCount * 3, mesh.Normals.Length);
    }

    [Fact]
    public void Normals_AreUnitLength()
    {
        var palette = CreateSimplePalette();
        var chunk = new VoxelChunk();
        chunk.SetVoxel(8, 8, 8, new Voxel(1, VoxelFlags.None));

        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);

        for (var i = 0; i < mesh.VertexCount; i++)
        {
            var nx = mesh.Normals[i * 3];
            var ny = mesh.Normals[i * 3 + 1];
            var nz = mesh.Normals[i * 3 + 2];
            var length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            Assert.InRange(length, 0.9f, 1.1f);
        }
    }

    [Fact]
    public void CollisionMode_SkipsOptionalArrays()
    {
        var palette = CreateSimplePalette();
        var chunk = new VoxelChunk();
        chunk.SetVoxel(8, 8, 8, new Voxel(1, VoxelFlags.None));

        var options = new MeshingOptions(CollisionMode: true);
        var mesh = _mesher.Mesh(chunk, NoNeighbors, palette, options);

        Assert.Null(mesh.UVs);
        Assert.Null(mesh.Colors);
        Assert.True(mesh.VertexCount > 0);
    }

    [Fact]
    public void Deterministic_SameInputProducesSameOutput()
    {
        var palette = CreateSimplePalette();
        var chunk = new VoxelChunk();
        for (var x = 4; x < 8; x++)
        for (var y = 4; y < 8; y++)
        for (var z = 4; z < 8; z++)
            chunk.SetVoxel(x, y, z, new Voxel(1, VoxelFlags.None));

        var mesh1 = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);
        var mesh2 = _mesher.Mesh(chunk, NoNeighbors, palette, MeshingOptions.Default);

        Assert.Equal(mesh1.Vertices, mesh2.Vertices);
        Assert.Equal(mesh1.Indices, mesh2.Indices);
    }
}
