using BeyondImmersion.Bannou.AssetBundler.Godot;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Godot.Tests;

/// <summary>
/// Tests for GodotContentTypes constants and helper methods.
/// </summary>
public class GodotContentTypesTests
{
    [Fact]
    public void Constants_AreCorrectMimeTypes()
    {
        // Textures
        Assert.Equal("image/png", GodotContentTypes.TexturePng);
        Assert.Equal("image/jpeg", GodotContentTypes.TextureJpeg);
        Assert.Equal("image/webp", GodotContentTypes.TextureWebp);

        // Models
        Assert.Equal("model/gltf-binary", GodotContentTypes.ModelGltfBinary);
        Assert.Equal("model/gltf+json", GodotContentTypes.ModelGltfJson);

        // Audio
        Assert.Equal("audio/wav", GodotContentTypes.AudioWav);
        Assert.Equal("audio/ogg", GodotContentTypes.AudioOgg);
        Assert.Equal("audio/mpeg", GodotContentTypes.AudioMp3);

        // Data
        Assert.Equal("application/json", GodotContentTypes.DataJson);
        Assert.Equal("application/x-yaml", GodotContentTypes.DataYaml);
        Assert.Equal("application/octet-stream", GodotContentTypes.Binary);
    }

    #region FromExtension Tests

    [Theory]
    [InlineData(".png", "image/png")]
    [InlineData("png", "image/png")]
    [InlineData(".PNG", "image/png")]
    public void FromExtension_Png_ReturnsPngType(string extension, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData(".JPEG", "image/jpeg")]
    public void FromExtension_Jpeg_ReturnsJpegType(string extension, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".webp", "image/webp")]
    [InlineData("webp", "image/webp")]
    public void FromExtension_WebP_ReturnsWebPType(string extension, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".glb", "model/gltf-binary")]
    [InlineData("glb", "model/gltf-binary")]
    [InlineData(".GLB", "model/gltf-binary")]
    public void FromExtension_Glb_ReturnsGltfBinaryType(string extension, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".gltf", "model/gltf+json")]
    [InlineData("gltf", "model/gltf+json")]
    public void FromExtension_Gltf_ReturnsGltfJsonType(string extension, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".wav", "audio/wav")]
    [InlineData("wave", "audio/wav")]
    [InlineData(".WAV", "audio/wav")]
    public void FromExtension_Wav_ReturnsWavType(string extension, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".ogg", "audio/ogg")]
    [InlineData("ogg", "audio/ogg")]
    public void FromExtension_Ogg_ReturnsOggType(string extension, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".mp3", "audio/mpeg")]
    [InlineData("mp3", "audio/mpeg")]
    public void FromExtension_Mp3_ReturnsMpegType(string extension, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".json", "application/json")]
    [InlineData("json", "application/json")]
    public void FromExtension_Json_ReturnsJsonType(string extension, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".yaml", "application/x-yaml")]
    [InlineData(".yml", "application/x-yaml")]
    public void FromExtension_Yaml_ReturnsYamlType(string extension, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.FromExtension(extension));
    }

    [Theory]
    [InlineData(".unknown", "application/octet-stream")]
    [InlineData("xyz", "application/octet-stream")]
    [InlineData("", "application/octet-stream")]
    public void FromExtension_Unknown_ReturnsBinaryType(string extension, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.FromExtension(extension));
    }

    [Fact]
    public void FromExtension_CaseInsensitive()
    {
        Assert.Equal(GodotContentTypes.TexturePng, GodotContentTypes.FromExtension(".PNG"));
        Assert.Equal(GodotContentTypes.TexturePng, GodotContentTypes.FromExtension(".Png"));
        Assert.Equal(GodotContentTypes.ModelGltfBinary, GodotContentTypes.FromExtension(".GLB"));
    }

    #endregion

    #region ToExtension Tests

    [Theory]
    [InlineData("image/png", "png")]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/webp", "webp")]
    [InlineData("model/gltf-binary", "glb")]
    [InlineData("model/gltf+json", "gltf")]
    [InlineData("audio/wav", "wav")]
    [InlineData("audio/ogg", "ogg")]
    [InlineData("audio/mpeg", "mp3")]
    [InlineData("application/json", "json")]
    [InlineData("application/x-yaml", "yaml")]
    public void ToExtension_KnownTypes_ReturnsCorrectExtension(string contentType, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.ToExtension(contentType));
    }

    [Theory]
    [InlineData("application/octet-stream", "bin")]
    [InlineData("unknown/type", "bin")]
    public void ToExtension_Unknown_ReturnsBin(string contentType, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.ToExtension(contentType));
    }

    #endregion

    #region IsRuntimeLoadable Tests

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("image/webp", true)]
    [InlineData("model/gltf-binary", true)]
    [InlineData("model/gltf+json", true)]
    [InlineData("audio/wav", true)]
    [InlineData("audio/ogg", true)]
    [InlineData("audio/mpeg", true)]
    [InlineData("application/json", true)]
    [InlineData("application/x-yaml", true)]
    public void IsRuntimeLoadable_SupportedTypes_ReturnsTrue(string contentType, bool expected)
    {
        Assert.Equal(expected, GodotContentTypes.IsRuntimeLoadable(contentType));
    }

    [Theory]
    [InlineData("application/octet-stream", false)]
    [InlineData("application/x-fbx", false)]
    [InlineData("image/targa", false)]
    [InlineData("unknown/type", false)]
    public void IsRuntimeLoadable_UnsupportedTypes_ReturnsFalse(string contentType, bool expected)
    {
        Assert.Equal(expected, GodotContentTypes.IsRuntimeLoadable(contentType));
    }

    #endregion

    #region RequiresConversion Tests

    [Theory]
    [InlineData("application/x-fbx", true)]
    [InlineData("image/targa", true)]
    [InlineData("image/x-tga", true)]
    [InlineData("image/vnd-ms.dds", true)]
    [InlineData("image/bmp", true)]
    [InlineData("image/tiff", true)]
    [InlineData("audio/flac", true)]
    public void RequiresConversion_ConvertibleTypes_ReturnsTrue(string contentType, bool expected)
    {
        Assert.Equal(expected, GodotContentTypes.RequiresConversion(contentType));
    }

    [Theory]
    [InlineData("image/png", false)]
    [InlineData("model/gltf-binary", false)]
    [InlineData("audio/ogg", false)]
    [InlineData("application/octet-stream", false)]
    public void RequiresConversion_NativeTypes_ReturnsFalse(string contentType, bool expected)
    {
        Assert.Equal(expected, GodotContentTypes.RequiresConversion(contentType));
    }

    #endregion

    #region GetConversionTarget Tests

    [Theory]
    [InlineData("application/x-fbx", "model/gltf-binary")]
    [InlineData("image/targa", "image/png")]
    [InlineData("image/x-tga", "image/png")]
    [InlineData("image/vnd-ms.dds", "image/png")]
    [InlineData("image/bmp", "image/png")]
    [InlineData("image/tiff", "image/png")]
    [InlineData("audio/flac", "audio/ogg")]
    public void GetConversionTarget_ConvertibleTypes_ReturnsTarget(string source, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.GetConversionTarget(source));
    }

    [Theory]
    [InlineData("image/png", "image/png")]
    [InlineData("model/gltf-binary", "model/gltf-binary")]
    [InlineData("audio/ogg", "audio/ogg")]
    public void GetConversionTarget_NativeTypes_ReturnsSameType(string source, string expected)
    {
        Assert.Equal(expected, GodotContentTypes.GetConversionTarget(source));
    }

    #endregion
}
