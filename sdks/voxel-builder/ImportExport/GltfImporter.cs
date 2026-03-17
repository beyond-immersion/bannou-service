using BeyondImmersion.Bannou.VoxelCore.Meshing;
using SharpGLTF.Schema2;

namespace BeyondImmersion.Bannou.VoxelBuilder.ImportExport;

/// <summary>
/// Imports .glb/.gltf files into <see cref="MeshData"/> via SharpGLTF (MIT).
/// Used for terrain overlay voxelization input — parse glTF, extract the first mesh
/// primitive as MeshData for consumption by <see cref="VoxelCore.Voxelization.MeshVoxelizer"/>.
/// </summary>
public sealed class GltfImporter
{
    /// <summary>
    /// Import a glTF/GLB file from bytes into MeshData.
    /// Only the first mesh primitive is imported.
    /// </summary>
    /// <param name="data">File content bytes (.glb or .gltf with embedded buffers).</param>
    /// <returns>Engine-agnostic mesh data.</returns>
    public MeshData Import(byte[] data)
    {
        var model = ModelRoot.ParseGLB(data);
        return ExtractFirstMesh(model);
    }

    /// <summary>
    /// Import a glTF/GLB file from a stream into MeshData.
    /// </summary>
    /// <param name="stream">File content stream.</param>
    /// <returns>Engine-agnostic mesh data.</returns>
    public MeshData Import(Stream stream)
    {
        var model = ModelRoot.ReadGLB(stream);
        return ExtractFirstMesh(model);
    }

    private static MeshData ExtractFirstMesh(ModelRoot model)
    {
        var mesh = model.LogicalMeshes.FirstOrDefault()
            ?? throw new FormatException("glTF file contains no meshes");
        var primitive = mesh.Primitives.FirstOrDefault()
            ?? throw new FormatException("glTF mesh contains no primitives");

        // Extract vertex positions
        var positionAccessor = primitive.GetVertexAccessor("POSITION")
            ?? throw new FormatException("Mesh primitive has no POSITION attribute");
        var positions = positionAccessor.AsVector3Array();

        // Extract normals (optional)
        var normalAccessor = primitive.GetVertexAccessor("NORMAL");
        var normals = normalAccessor?.AsVector3Array();

        // Extract indices
        var indexAccessor = primitive.GetIndexAccessor()
            ?? throw new FormatException("Mesh primitive has no indices");
        var indices = indexAccessor.AsIndicesArray();

        // Extract vertex colors (optional)
        var colorAccessor = primitive.GetVertexAccessor("COLOR_0");
        var colors = colorAccessor?.AsColorArray();

        var vertexCount = positions.Count;
        var verticesArr = new float[vertexCount * 3];
        var normalsArr = new float[vertexCount * 3];
        byte[]? colorsArr = colors != null ? new byte[vertexCount * 4] : null;

        for (var i = 0; i < vertexCount; i++)
        {
            var pos = positions[i];
            verticesArr[i * 3] = pos.X;
            verticesArr[i * 3 + 1] = pos.Y;
            verticesArr[i * 3 + 2] = pos.Z;

            if (normals != null)
            {
                var n = normals[i];
                normalsArr[i * 3] = n.X;
                normalsArr[i * 3 + 1] = n.Y;
                normalsArr[i * 3 + 2] = n.Z;
            }
            else
            {
                normalsArr[i * 3 + 1] = 1f; // Default up normal
            }

            if (colorsArr != null && colors != null)
            {
                var c = colors[i];
                colorsArr[i * 4] = (byte)(c.X * 255);
                colorsArr[i * 4 + 1] = (byte)(c.Y * 255);
                colorsArr[i * 4 + 2] = (byte)(c.Z * 255);
                colorsArr[i * 4 + 3] = (byte)(c.W * 255);
            }
        }

        var indicesArr = new int[indices.Count];
        for (var i = 0; i < indices.Count; i++)
            indicesArr[i] = (int)indices[i];

        return new MeshData(
            verticesArr,
            normalsArr,
            null, // UVs not needed for voxelization
            indicesArr,
            colorsArr,
            null, // No AO
            vertexCount,
            indicesArr.Length / 3);
    }
}
