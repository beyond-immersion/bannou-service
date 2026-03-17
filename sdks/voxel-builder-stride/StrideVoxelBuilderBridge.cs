using BeyondImmersion.Bannou.VoxelBuilder.Core;
using BeyondImmersion.Bannou.VoxelCore.Grid;
using BeyondImmersion.Bannou.VoxelCore.Math;
using BeyondImmersion.Bannou.VoxelCore.Meshing;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using StrideVec3 = Stride.Core.Mathematics.Vector3;
using StrideMatrix = Stride.Core.Mathematics.Matrix;
using StrideViewport = Stride.Graphics.Viewport;

namespace BeyondImmersion.Bannou.VoxelBuilder.Stride;

/// <summary>
/// Stride engine implementation of <see cref="IVoxelBuilderBridge"/>.
/// Manages per-chunk Entity lifecycle, GPU buffer creation/upload, and mouse picking.
/// Constructor takes Scene, CameraComponent, GraphicsDevice, and a CommandList provider
/// following the same pattern as scene-composer-stride.
/// </summary>
public sealed class StrideVoxelBuilderBridge : IVoxelBuilderBridge
{
    private readonly Scene _scene;
    private readonly CameraComponent _camera;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly Func<CommandList> _commandListProvider;
    private readonly Dictionary<ChunkCoord, StrideChunkRenderer> _chunkRenderers = new();
    private readonly Entity _rootEntity;
    private readonly Material _sharedMaterial;

    private IMesher _mesher;
    private VoxelGrid? _grid;
    private float _voxelScale;

    /// <summary>
    /// Creates a new Stride voxel builder bridge.
    /// </summary>
    /// <param name="scene">The Stride scene to add chunk entities to.</param>
    /// <param name="camera">The camera component for mouse picking (Viewport.Unproject).</param>
    /// <param name="graphicsDevice">The graphics device for buffer and material creation.</param>
    /// <param name="commandListProvider">
    /// Provides the current-frame CommandList for buffer uploads.
    /// In a typical SyncScript, this is <c>() =&gt; Game.GraphicsContext.CommandList</c>.
    /// </param>
    /// <param name="mesher">Optional mesher. Defaults to <see cref="GreedyMesher"/>.</param>
    public StrideVoxelBuilderBridge(
        Scene scene,
        CameraComponent camera,
        GraphicsDevice graphicsDevice,
        Func<CommandList> commandListProvider,
        IMesher? mesher = null)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _commandListProvider = commandListProvider ?? throw new ArgumentNullException(nameof(commandListProvider));
        _mesher = mesher ?? new GreedyMesher();

        // Create shared vertex-color material
        _sharedMaterial = StrideVoxelMaterial.Create(graphicsDevice);

        // Create root entity as parent for all chunks
        _rootEntity = new Entity("VoxelRoot");
        _scene.Entities.Add(_rootEntity);
    }

    /// <inheritdoc />
    public void OnGridLoaded(VoxelGrid grid)
    {
        // Dispose all existing chunk renderers
        foreach (var renderer in _chunkRenderers.Values)
        {
            renderer.Dispose();
        }
        _chunkRenderers.Clear();

        _grid = grid;
        _voxelScale = grid.Metadata.VoxelScale;

        var commandList = _commandListProvider();
        var options = new MeshingOptions(VoxelScale: _voxelScale);
        var neighbors = new VoxelChunk?[6];

        foreach (var (coord, chunk) in grid.EnumerateChunks())
        {
            GatherNeighbors(grid, coord, neighbors);
            var meshData = _mesher.Mesh(chunk, neighbors, grid.Palette, options);
            if (meshData.VertexCount == 0) continue;

            var renderer = StrideChunkRenderer.Create(
                coord, meshData, _graphicsDevice, commandList, _sharedMaterial, _voxelScale);
            _rootEntity.AddChild(renderer.Entity);
            _chunkRenderers[coord] = renderer;
        }
    }

    /// <inheritdoc />
    public void OnChunksModified(IReadOnlySet<ChunkCoord> coords)
    {
        if (_grid == null) return;

        var commandList = _commandListProvider();
        var options = new MeshingOptions(VoxelScale: _voxelScale);
        var neighbors = new VoxelChunk?[6];

        // Collect all coords to update (dirty + their 6 neighbors for boundary face changes)
        var updateSet = new HashSet<ChunkCoord>(coords);
        foreach (var coord in coords)
        {
            updateSet.Add(new ChunkCoord(coord.X + 1, coord.Y, coord.Z));
            updateSet.Add(new ChunkCoord(coord.X - 1, coord.Y, coord.Z));
            updateSet.Add(new ChunkCoord(coord.X, coord.Y + 1, coord.Z));
            updateSet.Add(new ChunkCoord(coord.X, coord.Y - 1, coord.Z));
            updateSet.Add(new ChunkCoord(coord.X, coord.Y, coord.Z + 1));
            updateSet.Add(new ChunkCoord(coord.X, coord.Y, coord.Z - 1));
        }

        foreach (var coord in updateSet)
        {
            var chunk = _grid.GetChunk(coord);

            if (chunk == null || chunk.IsEmpty)
            {
                // Chunk became empty — remove renderer
                if (_chunkRenderers.TryGetValue(coord, out var existingRenderer))
                {
                    _rootEntity.RemoveChild(existingRenderer.Entity);
                    existingRenderer.Dispose();
                    _chunkRenderers.Remove(coord);
                }
            }
            else if (_chunkRenderers.TryGetValue(coord, out var renderer))
            {
                // Chunk exists and has renderer — update mesh
                GatherNeighbors(_grid, coord, neighbors);
                var meshData = _mesher.Mesh(chunk, neighbors, _grid.Palette, options);
                if (meshData.VertexCount == 0)
                {
                    _rootEntity.RemoveChild(renderer.Entity);
                    renderer.Dispose();
                    _chunkRenderers.Remove(coord);
                }
                else
                {
                    renderer.Update(meshData, commandList);
                }
            }
            else
            {
                // Chunk exists but no renderer — create new
                GatherNeighbors(_grid, coord, neighbors);
                var meshData = _mesher.Mesh(chunk, neighbors, _grid.Palette, options);
                if (meshData.VertexCount > 0)
                {
                    var newRenderer = StrideChunkRenderer.Create(
                        coord, meshData, _graphicsDevice, commandList, _sharedMaterial, _voxelScale);
                    _rootEntity.AddChild(newRenderer.Entity);
                    _chunkRenderers[coord] = newRenderer;
                }
            }
        }
    }

    /// <inheritdoc />
    public void OnPaletteChanged(Palette palette)
    {
        if (_grid == null) return;

        // Palette colors changed — must re-interleave ALL chunks because vertex colors are baked
        var commandList = _commandListProvider();
        var options = new MeshingOptions(VoxelScale: _voxelScale);
        var neighbors = new VoxelChunk?[6];

        foreach (var (coord, renderer) in _chunkRenderers)
        {
            var chunk = _grid.GetChunk(coord);
            if (chunk == null) continue;

            GatherNeighbors(_grid, coord, neighbors);
            var meshData = _mesher.Mesh(chunk, neighbors, _grid.Palette, options);
            if (meshData.VertexCount > 0)
            {
                renderer.Update(meshData, commandList);
            }
        }
    }

    /// <inheritdoc />
    public VoxelCoord? ScreenToVoxel(float screenX, float screenY)
    {
        if (_grid == null) return null;

        // Create viewport from back buffer dimensions
        var backBuffer = _graphicsDevice.Presenter.BackBuffer;
        var viewport = new StrideViewport(0, 0, backBuffer.Width, backBuffer.Height);

        // Ensure view/projection matrices are current
        _camera.Update();

        // Unproject screen position to near/far world points
        var nearPoint = viewport.Unproject(
            new StrideVec3(screenX, screenY, 0),
            _camera.ProjectionMatrix, _camera.ViewMatrix, StrideMatrix.Identity);
        var farPoint = viewport.Unproject(
            new StrideVec3(screenX, screenY, 1),
            _camera.ProjectionMatrix, _camera.ViewMatrix, StrideMatrix.Identity);

        var rayOrigin = nearPoint;
        var rayDirection = StrideVec3.Normalize(farPoint - nearPoint);

        // Transform ray to grid-local space
        StrideMatrix.Invert(ref _rootEntity.Transform.WorldMatrix, out var worldToLocal);
        var localOrigin = StrideVec3.TransformCoordinate(rayOrigin, worldToLocal);
        var localDirection = StrideVec3.TransformNormal(rayDirection, worldToLocal);

        // Convert to voxel-space (divide by voxelScale)
        var voxelOriginX = localOrigin.X / _voxelScale;
        var voxelOriginY = localOrigin.Y / _voxelScale;
        var voxelOriginZ = localOrigin.Z / _voxelScale;

        // DDA raycast through grid
        var ray = VoxelRay.Create(
            new VoxelCoord((int)voxelOriginX, (int)voxelOriginY, (int)voxelOriginZ),
            localDirection.X, localDirection.Y, localDirection.Z);
        var hit = ray.Cast(_grid, maxSteps: 200);

        if (hit == null) return null;
        return hit.Value.Hit;
    }

    /// <inheritdoc />
    public void SetMesher(IMesher mesher)
    {
        _mesher = mesher ?? throw new ArgumentNullException(nameof(mesher));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var renderer in _chunkRenderers.Values)
        {
            renderer.Dispose();
        }
        _chunkRenderers.Clear();

        _scene.Entities.Remove(_rootEntity);
    }

    /// <summary>
    /// Gathers the 6 axis-aligned neighbor chunks for boundary face culling and AO.
    /// Order: +X, -X, +Y, -Y, +Z, -Z (matches IMesher contract).
    /// </summary>
    private static void GatherNeighbors(VoxelGrid grid, ChunkCoord coord, VoxelChunk?[] neighbors)
    {
        neighbors[0] = grid.GetChunk(new ChunkCoord(coord.X + 1, coord.Y, coord.Z));
        neighbors[1] = grid.GetChunk(new ChunkCoord(coord.X - 1, coord.Y, coord.Z));
        neighbors[2] = grid.GetChunk(new ChunkCoord(coord.X, coord.Y + 1, coord.Z));
        neighbors[3] = grid.GetChunk(new ChunkCoord(coord.X, coord.Y - 1, coord.Z));
        neighbors[4] = grid.GetChunk(new ChunkCoord(coord.X, coord.Y, coord.Z + 1));
        neighbors[5] = grid.GetChunk(new ChunkCoord(coord.X, coord.Y, coord.Z - 1));
    }
}
