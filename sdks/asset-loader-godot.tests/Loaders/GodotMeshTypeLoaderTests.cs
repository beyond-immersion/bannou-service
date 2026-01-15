using BeyondImmersion.Bannou.AssetLoader.Godot.Loaders;
using Godot;
using Xunit;

namespace BeyondImmersion.Bannou.AssetLoader.Godot.Tests.Loaders;

/// <summary>
/// Tests for GodotMeshTypeLoader.
/// Note: Full loading tests require Godot runtime. These tests verify metadata and structure.
/// </summary>
public class GodotMeshTypeLoaderTests
{
    [Fact]
    public void SupportedContentTypes_ContainsGltfBinary()
    {
        var loader = new GodotMeshTypeLoader();

        Assert.Contains("model/gltf-binary", loader.SupportedContentTypes);
    }

    [Fact]
    public void SupportedContentTypes_ContainsGltfJson()
    {
        var loader = new GodotMeshTypeLoader();

        Assert.Contains("model/gltf+json", loader.SupportedContentTypes);
    }

    [Fact]
    public void SupportedContentTypes_ContainsGltfBuffer()
    {
        var loader = new GodotMeshTypeLoader();

        Assert.Contains("application/gltf-buffer", loader.SupportedContentTypes);
    }

    [Fact]
    public void AssetType_IsMesh()
    {
        var loader = new GodotMeshTypeLoader();

        Assert.Equal(typeof(Mesh), loader.AssetType);
    }

    [Fact]
    public void Constructor_AcceptsNullDebugLog()
    {
        var loader = new GodotMeshTypeLoader(null);

        Assert.NotNull(loader);
    }

    [Fact]
    public void Constructor_AcceptsDebugLogCallback()
    {
        var messages = new List<string>();
        var loader = new GodotMeshTypeLoader(msg => messages.Add(msg));

        Assert.NotNull(loader);
    }
}
