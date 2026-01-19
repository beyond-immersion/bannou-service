using BeyondImmersion.Bannou.AssetLoader.Abstractions;
using BeyondImmersion.Bannou.Bundle.Format;
using Godot;

namespace BeyondImmersion.Bannou.AssetLoader.Godot.Loaders;

/// <summary>
/// IAssetTypeLoader for Godot Mesh assets.
/// Loads glTF/glb models using GLTFDocument.
/// </summary>
public sealed class GodotMeshTypeLoader : IAssetTypeLoader<Mesh>
{
    private readonly Action<string>? _debugLog;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedContentTypes { get; } = new[]
    {
        "model/gltf-binary",
        "model/gltf+json",
        "application/gltf-buffer"
    };

    /// <inheritdoc />
    public Type AssetType => typeof(Mesh);

    /// <summary>
    /// Creates a new Godot Mesh type loader.
    /// </summary>
    /// <param name="debugLog">Optional debug logging callback.</param>
    public GodotMeshTypeLoader(Action<string>? debugLog = null)
    {
        _debugLog = debugLog;
    }

    /// <inheritdoc />
    public async Task<Mesh> LoadAsync(
        ReadOnlyMemory<byte> data,
        BundleAssetEntry metadata,
        CancellationToken ct = default)
    {
        await Task.CompletedTask; // Synchronous Godot API - placeholder for future async implementation

        ct.ThrowIfCancellationRequested();

        using var gltfDoc = new GltfDocument();
        using var gltfState = new GltfState();

        // Load from buffer
        var error = gltfDoc.AppendFromBuffer(data.ToArray(), "", gltfState);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Failed to parse glTF '{metadata.AssetId}': {error}");

        // Generate scene to get the mesh
        var root = gltfDoc.GenerateScene(gltfState);
        if (root == null)
            throw new InvalidOperationException($"Failed to generate scene from glTF '{metadata.AssetId}'");

        // Find the first MeshInstance3D and extract its mesh
        var mesh = ExtractFirstMesh(root);

        // Clean up the generated scene (we only need the mesh)
        root.QueueFree();

        if (mesh == null)
            throw new InvalidOperationException($"No mesh found in glTF '{metadata.AssetId}'");

        _debugLog?.Invoke($"Loaded Mesh: {metadata.AssetId}");
        return mesh;
    }

    private static Mesh? ExtractFirstMesh(Node node)
    {
        if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
        {
            // Duplicate the mesh so it survives when we free the scene
            return (Mesh)meshInstance.Mesh.Duplicate();
        }

        foreach (var child in node.GetChildren())
        {
            var mesh = ExtractFirstMesh(child);
            if (mesh != null)
                return mesh;
        }

        return null;
    }

    /// <inheritdoc />
    public void Unload(Mesh asset)
    {
        if (GodotObject.IsInstanceValid(asset))
        {
            asset.Free();
        }
    }
}
