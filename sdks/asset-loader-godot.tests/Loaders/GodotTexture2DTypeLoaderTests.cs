using BeyondImmersion.Bannou.AssetLoader.Godot.Loaders;
using Godot;
using Xunit;

namespace BeyondImmersion.Bannou.AssetLoader.Godot.Tests.Loaders;

/// <summary>
/// Tests for GodotTexture2DTypeLoader.
/// Note: Full loading tests require Godot runtime. These tests verify metadata and structure.
/// </summary>
public class GodotTexture2DTypeLoaderTests
{
    [Fact]
    public void SupportedContentTypes_ContainsPng()
    {
        var loader = new GodotTexture2DTypeLoader();

        Assert.Contains("image/png", loader.SupportedContentTypes);
    }

    [Fact]
    public void SupportedContentTypes_ContainsJpeg()
    {
        var loader = new GodotTexture2DTypeLoader();

        Assert.Contains("image/jpeg", loader.SupportedContentTypes);
        Assert.Contains("image/jpg", loader.SupportedContentTypes);
    }

    [Fact]
    public void AssetType_IsTexture2D()
    {
        var loader = new GodotTexture2DTypeLoader();

        Assert.Equal(typeof(Texture2D), loader.AssetType);
    }

    [Fact]
    public void Constructor_AcceptsNullDebugLog()
    {
        var loader = new GodotTexture2DTypeLoader(null);

        Assert.NotNull(loader);
    }

    [Fact]
    public void Constructor_AcceptsDebugLogCallback()
    {
        var messages = new List<string>();
        var loader = new GodotTexture2DTypeLoader(msg => messages.Add(msg));

        Assert.NotNull(loader);
    }
}
