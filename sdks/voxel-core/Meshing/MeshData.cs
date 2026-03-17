namespace BeyondImmersion.Bannou.VoxelCore.Meshing;

/// <summary>
/// Engine-agnostic mesh output. Each engine bridge converts this to its native format.
/// Right-handed coordinate system, Y-up, counter-clockwise winding order.
/// Colors are stored as bytes (4 per vertex) matching Stride's Color struct.
/// </summary>
public sealed class MeshData
{
    /// <summary>Vertex positions as XYZ triplets (right-handed, Y-up).</summary>
    public float[] Vertices { get; }

    /// <summary>Vertex normals as XYZ triplets.</summary>
    public float[] Normals { get; }

    /// <summary>
    /// UV coordinates as pairs (palette atlas coordinates, texel-centered).
    /// Null when <see cref="MeshingOptions.CollisionMode"/> is true.
    /// </summary>
    public float[]? UVs { get; }

    /// <summary>Triangle indices (CCW winding = front face).</summary>
    public int[] Indices { get; }

    /// <summary>
    /// Per-vertex RGBA colors, 4 bytes each (from palette).
    /// Null when <see cref="MeshingOptions.CollisionMode"/> is true.
    /// </summary>
    public byte[]? Colors { get; }

    /// <summary>
    /// Per-vertex ambient occlusion values 0.0-1.0.
    /// Null if AO is disabled, <see cref="MeshingOptions.CollisionMode"/> is true,
    /// or the mesher does not support AO (e.g., MarchingCubesMesher).
    /// </summary>
    public float[]? AmbientOcclusion { get; }

    /// <summary>Number of vertices.</summary>
    public int VertexCount { get; }

    /// <summary>Number of triangles.</summary>
    public int TriangleCount { get; }

    /// <summary>
    /// Creates a new MeshData with the given geometry arrays.
    /// </summary>
    public MeshData(
        float[] vertices,
        float[] normals,
        float[]? uvs,
        int[] indices,
        byte[]? colors,
        float[]? ambientOcclusion,
        int vertexCount,
        int triangleCount)
    {
        Vertices = vertices;
        Normals = normals;
        UVs = uvs;
        Indices = indices;
        Colors = colors;
        AmbientOcclusion = ambientOcclusion;
        VertexCount = vertexCount;
        TriangleCount = triangleCount;
    }

    /// <summary>An empty mesh with no geometry.</summary>
    public static readonly MeshData Empty = new(
        Array.Empty<float>(),
        Array.Empty<float>(),
        Array.Empty<float>(),
        Array.Empty<int>(),
        Array.Empty<byte>(),
        Array.Empty<float>(),
        0, 0);
}
