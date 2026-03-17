using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Meshing;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace BeyondImmersion.Bannou.VoxelBuilder.ImportExport;

/// <summary>
/// Exports a VoxelGrid to glTF/GLB format via meshing + SharpGLTF writer.
/// Used for asset pipeline export and terrain bake-back. Includes per-vertex colors
/// from the grid's palette.
/// </summary>
public sealed class MeshExporter : IVoxelExporter
{
    private readonly IMesher _mesher;

    /// <summary>
    /// Creates a mesh exporter with the specified mesher.
    /// </summary>
    /// <param name="mesher">The mesher to use for voxel-to-mesh conversion. Defaults to GreedyMesher if null.</param>
    public MeshExporter(IMesher? mesher = null)
    {
        _mesher = mesher ?? new GreedyMesher();
    }

    /// <summary>
    /// Export a VoxelGrid as GLB binary.
    /// </summary>
    /// <param name="grid">The grid to export.</param>
    /// <param name="meshingOptions">Meshing options. Defaults to <see cref="MeshingOptions.Default"/> if null.</param>
    /// <returns>GLB file bytes.</returns>
    public byte[] Export(VoxelGrid grid, MeshingOptions? meshingOptions = null)
    {
        var scene = BuildScene(grid, meshingOptions);
        var model = scene.ToGltf2();
        using var ms = new MemoryStream();
        model.WriteGLB(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Export a VoxelGrid as GLB binary (IVoxelExporter implementation, uses default options).
    /// </summary>
    /// <param name="grid">The grid to export.</param>
    /// <returns>GLB file bytes.</returns>
    byte[] IVoxelExporter.Export(VoxelGrid grid) => Export(grid);

    /// <summary>
    /// Export a VoxelGrid as glTF JSON string.
    /// </summary>
    /// <param name="grid">The grid to export.</param>
    /// <param name="meshingOptions">Meshing options. Defaults to <see cref="MeshingOptions.Default"/> if null.</param>
    /// <returns>glTF JSON content.</returns>
    public string ExportGltf(VoxelGrid grid, MeshingOptions? meshingOptions = null)
    {
        var scene = BuildScene(grid, meshingOptions);
        var model = scene.ToGltf2();
        return model.GetJsonPreview();
    }

    private SceneBuilder BuildScene(VoxelGrid grid, MeshingOptions? meshingOptions)
    {
        var options = meshingOptions ?? MeshingOptions.Default;

        var material = new MaterialBuilder("VoxelMaterial")
            .WithUnlitShader();

        // Use VertexPositionNormal + VertexColor1 to include per-vertex colors in the export.
        // Without VertexColor1, all exported meshes are monochrome.
        var meshBuilder = new MeshBuilder<VertexPositionNormal, VertexColor1, VertexEmpty>("VoxelMesh");
        var primitive = meshBuilder.UsePrimitive(material);

        var neighbors = new VoxelChunk?[6];

        foreach (var (chunkCoord, chunk) in grid.EnumerateChunks())
        {
            // Gather neighbor chunks for boundary face culling
            neighbors[0] = grid.GetChunk(new ChunkCoord(chunkCoord.X + 1, chunkCoord.Y, chunkCoord.Z));
            neighbors[1] = grid.GetChunk(new ChunkCoord(chunkCoord.X - 1, chunkCoord.Y, chunkCoord.Z));
            neighbors[2] = grid.GetChunk(new ChunkCoord(chunkCoord.X, chunkCoord.Y + 1, chunkCoord.Z));
            neighbors[3] = grid.GetChunk(new ChunkCoord(chunkCoord.X, chunkCoord.Y - 1, chunkCoord.Z));
            neighbors[4] = grid.GetChunk(new ChunkCoord(chunkCoord.X, chunkCoord.Y, chunkCoord.Z + 1));
            neighbors[5] = grid.GetChunk(new ChunkCoord(chunkCoord.X, chunkCoord.Y, chunkCoord.Z - 1));

            var meshData = _mesher.Mesh(chunk, neighbors, grid.Palette, options);
            if (meshData.VertexCount == 0) continue;

            var offsetX = chunkCoord.X * 16 * options.VoxelScale;
            var offsetY = chunkCoord.Y * 16 * options.VoxelScale;
            var offsetZ = chunkCoord.Z * 16 * options.VoxelScale;

            for (var t = 0; t < meshData.Indices.Length; t += 3)
            {
                var i0 = meshData.Indices[t];
                var i1 = meshData.Indices[t + 1];
                var i2 = meshData.Indices[t + 2];

                var v0 = CreateVertex(meshData, i0, offsetX, offsetY, offsetZ);
                var v1 = CreateVertex(meshData, i1, offsetX, offsetY, offsetZ);
                var v2 = CreateVertex(meshData, i2, offsetX, offsetY, offsetZ);

                primitive.AddTriangle(v0, v1, v2);
            }
        }

        var scene = new SceneBuilder();
        scene.AddRigidMesh(meshBuilder, System.Numerics.Matrix4x4.Identity);
        return scene;
    }

    private static VertexBuilder<VertexPositionNormal, VertexColor1, VertexEmpty> CreateVertex(
        MeshData meshData, int index, float offsetX, float offsetY, float offsetZ)
    {
        var position = new System.Numerics.Vector3(
            meshData.Vertices[index * 3] + offsetX,
            meshData.Vertices[index * 3 + 1] + offsetY,
            meshData.Vertices[index * 3 + 2] + offsetZ);

        var normal = new System.Numerics.Vector3(
            meshData.Normals[index * 3],
            meshData.Normals[index * 3 + 1],
            meshData.Normals[index * 3 + 2]);

        // Per-vertex color from palette (byte[] → float4 for glTF)
        var color = new System.Numerics.Vector4(1, 1, 1, 1); // Default white
        if (meshData.Colors != null)
        {
            var r = meshData.Colors[index * 4] / 255f;
            var g = meshData.Colors[index * 4 + 1] / 255f;
            var b = meshData.Colors[index * 4 + 2] / 255f;
            var a = meshData.Colors[index * 4 + 3] / 255f;

            // Bake AO into vertex color if present
            if (meshData.AmbientOcclusion != null)
            {
                var ao = meshData.AmbientOcclusion[index];
                r *= ao;
                g *= ao;
                b *= ao;
            }

            color = new System.Numerics.Vector4(r, g, b, a);
        }

        return new VertexBuilder<VertexPositionNormal, VertexColor1, VertexEmpty>(
            new VertexPositionNormal(position, normal),
            new VertexColor1(color));
    }
}
