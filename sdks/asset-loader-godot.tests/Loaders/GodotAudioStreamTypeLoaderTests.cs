using BeyondImmersion.Bannou.AssetLoader.Godot.Loaders;
using Godot;
using Xunit;

namespace BeyondImmersion.Bannou.AssetLoader.Godot.Tests.Loaders;

/// <summary>
/// Tests for GodotAudioStreamTypeLoader.
/// Note: Full loading tests require Godot runtime. These tests verify metadata and structure.
/// </summary>
public class GodotAudioStreamTypeLoaderTests
{
    [Fact]
    public void SupportedContentTypes_ContainsWav()
    {
        var loader = new GodotAudioStreamTypeLoader();

        Assert.Contains("audio/wav", loader.SupportedContentTypes);
        Assert.Contains("audio/wave", loader.SupportedContentTypes);
        Assert.Contains("audio/x-wav", loader.SupportedContentTypes);
    }

    [Fact]
    public void SupportedContentTypes_ContainsOgg()
    {
        var loader = new GodotAudioStreamTypeLoader();

        Assert.Contains("audio/ogg", loader.SupportedContentTypes);
        Assert.Contains("audio/vorbis", loader.SupportedContentTypes);
    }

    [Fact]
    public void SupportedContentTypes_ContainsMp3()
    {
        var loader = new GodotAudioStreamTypeLoader();

        Assert.Contains("audio/mpeg", loader.SupportedContentTypes);
        Assert.Contains("audio/mp3", loader.SupportedContentTypes);
    }

    [Fact]
    public void AssetType_IsAudioStream()
    {
        var loader = new GodotAudioStreamTypeLoader();

        Assert.Equal(typeof(AudioStream), loader.AssetType);
    }

    [Fact]
    public void Constructor_AcceptsNullDebugLog()
    {
        var loader = new GodotAudioStreamTypeLoader(null);

        Assert.NotNull(loader);
    }

    [Fact]
    public void Constructor_AcceptsDebugLogCallback()
    {
        var messages = new List<string>();
        var loader = new GodotAudioStreamTypeLoader(msg => messages.Add(msg));

        Assert.NotNull(loader);
    }
}
