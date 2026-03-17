using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Meshing;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using Buffer = Stride.Graphics.Buffer;

namespace BeyondImmersion.Bannou.VoxelBuilder.Stride;

/// <summary>
/// Per-chunk Stride entity with GPU vertex/index buffers and a ModelComponent.
/// Manages the GPU resource lifecycle for a single voxel chunk's rendered mesh.
/// </summary>
internal sealed class StrideChunkRenderer : IDisposable
{
    private readonly Entity _entity;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ChunkCoord _chunkCoord;
    private Buffer<VertexPositionNormalColor> _vertexBuffer;
    private Buffer<uint> _indexBuffer;
    private MeshDraw _meshDraw;
    private int _vertexCapacity;
    private int _indexCapacity;

    /// <summary>The Stride entity representing this chunk in the scene.</summary>
    internal Entity Entity => _entity;

    private StrideChunkRenderer(
        Entity entity,
        GraphicsDevice graphicsDevice,
        Buffer<VertexPositionNormalColor> vertexBuffer,
        Buffer<uint> indexBuffer,
        MeshDraw meshDraw,
        ChunkCoord chunkCoord,
        int vertexCapacity,
        int indexCapacity)
    {
        _entity = entity;
        _graphicsDevice = graphicsDevice;
        _vertexBuffer = vertexBuffer;
        _indexBuffer = indexBuffer;
        _meshDraw = meshDraw;
        _chunkCoord = chunkCoord;
        _vertexCapacity = vertexCapacity;
        _indexCapacity = indexCapacity;
    }

    /// <summary>
    /// Creates a new chunk renderer by interleaving MeshData into GPU buffers and assembling a Stride entity.
    /// </summary>
    /// <param name="coord">The chunk coordinate.</param>
    /// <param name="meshData">Engine-agnostic mesh data from the mesher.</param>
    /// <param name="graphicsDevice">Graphics device for buffer creation.</param>
    /// <param name="commandList">Command list for buffer upload.</param>
    /// <param name="material">Shared vertex-color material.</param>
    /// <param name="voxelScale">World units per voxel.</param>
    /// <returns>A new chunk renderer with GPU resources uploaded.</returns>
    internal static StrideChunkRenderer Create(
        ChunkCoord coord,
        MeshData meshData,
        GraphicsDevice graphicsDevice,
        CommandList commandList,
        Material material,
        float voxelScale)
    {
        var vertices = InterleaveVertices(meshData);
        var indices = ConvertIndices(meshData);

        // Create GPU buffers (count-only overload, then single upload via command list)
        var vertexBuffer = Buffer.New<VertexPositionNormalColor>(
            graphicsDevice, vertices.Length, BufferFlags.ShaderResource);
        vertexBuffer.SetData(commandList, vertices);

        var indexBuffer = Buffer.New<uint>(
            graphicsDevice, indices.Length, BufferFlags.ShaderResource);
        indexBuffer.SetData(commandList, indices);

        // Assemble MeshDraw
        var meshDraw = new MeshDraw
        {
            PrimitiveType = PrimitiveType.TriangleList,
            DrawCount = indices.Length,
            VertexBuffers = new[]
            {
                new VertexBufferBinding(vertexBuffer, VertexPositionNormalColor.Layout, vertices.Length)
            },
            IndexBuffer = new IndexBufferBinding(indexBuffer, true, indices.Length)
        };

        // Create Mesh → Model → Entity
        var mesh = new Mesh { Draw = meshDraw, MaterialIndex = 0 };
        var model = new Model();
        model.Meshes.Add(mesh);
        model.Materials.Add(material);

        var entity = new Entity($"Chunk_{coord.X}_{coord.Y}_{coord.Z}");
        entity.GetOrCreate<ModelComponent>().Model = model;
        entity.Transform.Position = coord.ToWorldPosition(voxelScale);

        return new StrideChunkRenderer(
            entity, graphicsDevice, vertexBuffer, indexBuffer, meshDraw,
            coord, vertices.Length, indices.Length);
    }

    /// <summary>
    /// Updates the chunk mesh with new data. Re-interleaves vertices and uploads to GPU.
    /// If the new data exceeds existing buffer capacity, old buffers are disposed and new ones created.
    /// </summary>
    /// <param name="meshData">New mesh data from the mesher.</param>
    /// <param name="commandList">Command list for buffer upload.</param>
    internal void Update(MeshData meshData, CommandList commandList)
    {
        var vertices = InterleaveVertices(meshData);
        var indices = ConvertIndices(meshData);

        if (vertices.Length <= _vertexCapacity && indices.Length <= _indexCapacity)
        {
            // Fits in existing buffers — SetData in-place
            _vertexBuffer.SetData(commandList, vertices);
            _indexBuffer.SetData(commandList, indices);
            _meshDraw.DrawCount = indices.Length;
        }
        else
        {
            // Buffer too small — dispose old, create new
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();

            _vertexBuffer = Buffer.New<VertexPositionNormalColor>(
                _graphicsDevice, vertices.Length, BufferFlags.ShaderResource);
            _vertexBuffer.SetData(commandList, vertices);

            _indexBuffer = Buffer.New<uint>(
                _graphicsDevice, indices.Length, BufferFlags.ShaderResource);
            _indexBuffer.SetData(commandList, indices);

            // Rebuild MeshDraw bindings
            _meshDraw.VertexBuffers = new[]
            {
                new VertexBufferBinding(_vertexBuffer, VertexPositionNormalColor.Layout, vertices.Length)
            };
            _meshDraw.IndexBuffer = new IndexBufferBinding(_indexBuffer, true, indices.Length);
            _meshDraw.DrawCount = indices.Length;
            _vertexCapacity = vertices.Length;
            _indexCapacity = indices.Length;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _entity.Scene?.Entities.Remove(_entity);
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
    }

    /// <summary>
    /// Interleaves MeshData arrays into VertexPositionNormalColor[], baking AO into vertex colors.
    /// </summary>
    internal static VertexPositionNormalColor[] InterleaveVertices(MeshData meshData)
    {
        var vertices = new VertexPositionNormalColor[meshData.VertexCount];

        for (var i = 0; i < meshData.VertexCount; i++)
        {
            var position = new global::Stride.Core.Mathematics.Vector3(
                meshData.Vertices[i * 3],
                meshData.Vertices[i * 3 + 1],
                meshData.Vertices[i * 3 + 2]);

            var normal = new global::Stride.Core.Mathematics.Vector3(
                meshData.Normals[i * 3],
                meshData.Normals[i * 3 + 1],
                meshData.Normals[i * 3 + 2]);

            // Color: zero-copy from MeshData.Colors (byte[]) to Stride Color (4 bytes RGBA)
            byte r = meshData.Colors != null ? meshData.Colors[i * 4] : (byte)255;
            byte g = meshData.Colors != null ? meshData.Colors[i * 4 + 1] : (byte)255;
            byte b = meshData.Colors != null ? meshData.Colors[i * 4 + 2] : (byte)255;
            byte a = meshData.Colors != null ? meshData.Colors[i * 4 + 3] : (byte)255;

            // Bake AO into vertex color
            if (meshData.AmbientOcclusion != null)
            {
                var ao = meshData.AmbientOcclusion[i];
                r = (byte)(r * ao);
                g = (byte)(g * ao);
                b = (byte)(b * ao);
            }

            vertices[i] = new VertexPositionNormalColor(
                position, normal, new global::Stride.Core.Mathematics.Color(r, g, b, a));
        }

        return vertices;
    }

    /// <summary>
    /// Converts MeshData.Indices (int[]) to uint[] for Stride's 32-bit index buffer.
    /// </summary>
    internal static uint[] ConvertIndices(MeshData meshData)
    {
        var indices = new uint[meshData.Indices.Length];
        for (var i = 0; i < meshData.Indices.Length; i++)
        {
            indices[i] = (uint)meshData.Indices[i];
        }
        return indices;
    }
}
