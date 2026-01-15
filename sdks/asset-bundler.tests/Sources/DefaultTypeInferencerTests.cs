using BeyondImmersion.Bannou.AssetBundler.Extraction;
using BeyondImmersion.Bannou.AssetBundler.Sources;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Tests.Sources;

/// <summary>
/// Tests for DefaultTypeInferencer.
/// </summary>
public class DefaultTypeInferencerTests
{
    private readonly DefaultTypeInferencer _inferencer = DefaultTypeInferencer.Instance;

    #region InferAssetType Tests

    [Theory]
    [InlineData("model.fbx", AssetType.Model)]
    [InlineData("character.FBX", AssetType.Model)]
    [InlineData("scene.obj", AssetType.Model)]
    [InlineData("environment.dae", AssetType.Model)]
    [InlineData("item.glb", AssetType.Model)]
    [InlineData("world.gltf", AssetType.Model)]
    public void InferAssetType_ModelFiles_ReturnsModel(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("texture.png", AssetType.Texture)]
    [InlineData("photo.jpg", AssetType.Texture)]
    [InlineData("image.jpeg", AssetType.Texture)]
    [InlineData("diffuse.tga", AssetType.Texture)]
    [InlineData("compressed.dds", AssetType.Texture)]
    [InlineData("modern.webp", AssetType.Texture)]
    public void InferAssetType_TextureFiles_ReturnsTexture(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("sound.wav", AssetType.Audio)]
    [InlineData("music.ogg", AssetType.Audio)]
    [InlineData("voice.mp3", AssetType.Audio)]
    public void InferAssetType_AudioFiles_ReturnsAudio(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("walk.anim", AssetType.Animation)]
    public void InferAssetType_AnimationFiles_ReturnsAnimation(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("behavior.yaml", AssetType.Behavior)]
    [InlineData("behavior.yml", AssetType.Behavior)]
    [InlineData("npc_behavior.json", AssetType.Behavior)]
    public void InferAssetType_BehaviorFiles_ReturnsBehavior(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("unknown.xyz", AssetType.Other)]
    [InlineData("readme.txt", AssetType.Other)]
    [InlineData("noextension", AssetType.Other)]
    public void InferAssetType_UnknownFiles_ReturnsOther(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Fact]
    public void InferAssetType_WithMimeType_UsesFilename()
    {
        // MIME type is secondary to filename-based inference
        var result = _inferencer.InferAssetType("model.fbx", "application/octet-stream");
        Assert.Equal(AssetType.Model, result);
    }

    #endregion

    #region InferTextureType Tests

    [Theory]
    [InlineData("diffuse.png", TextureType.Color)]
    [InlineData("albedo.jpg", TextureType.Color)]
    [InlineData("color_map.tga", TextureType.Color)]
    [InlineData("regular_texture.png", TextureType.Color)]
    public void InferTextureType_ColorTextures_ReturnsColor(string filename, TextureType expected)
    {
        Assert.Equal(expected, _inferencer.InferTextureType(filename));
    }

    [Theory]
    [InlineData("wall_normal.png", TextureType.NormalMap)]
    [InlineData("character_nml.tga", TextureType.NormalMap)]
    [InlineData("surface_n.png", TextureType.NormalMap)]
    public void InferTextureType_NormalMaps_ReturnsNormalMap(string filename, TextureType expected)
    {
        Assert.Equal(expected, _inferencer.InferTextureType(filename));
    }

    [Theory]
    [InlineData("glow_emissive.png", TextureType.Emissive)]
    [InlineData("light_emit.tga", TextureType.Emissive)]
    [InlineData("neon_e.png", TextureType.Emissive)]
    public void InferTextureType_Emissive_ReturnsEmissive(string filename, TextureType expected)
    {
        Assert.Equal(expected, _inferencer.InferTextureType(filename));
    }

    [Theory]
    [InlineData("surface_mask.png", TextureType.Mask)]
    [InlineData("metal_metallic.tga", TextureType.Mask)]
    [InlineData("rough_roughness.png", TextureType.Mask)]
    [InlineData("occlusion_ao.png", TextureType.Mask)]
    public void InferTextureType_Masks_ReturnsMask(string filename, TextureType expected)
    {
        Assert.Equal(expected, _inferencer.InferTextureType(filename));
    }

    [Theory]
    [InlineData("terrain_height.png", TextureType.HeightMap)]
    [InlineData("surface_displacement.tga", TextureType.HeightMap)]
    [InlineData("bump_h.png", TextureType.HeightMap)]
    public void InferTextureType_HeightMaps_ReturnsHeightMap(string filename, TextureType expected)
    {
        Assert.Equal(expected, _inferencer.InferTextureType(filename));
    }

    [Theory]
    [InlineData("spr_button.png", TextureType.UI)]
    [InlineData("ui_panel.png", TextureType.UI)]
    [InlineData("hud_health.png", TextureType.UI)]
    [InlineData("menu_icon.png", TextureType.UI)]
    [InlineData("play_button.png", TextureType.UI)]
    public void InferTextureType_UITextures_ReturnsUI(string filename, TextureType expected)
    {
        Assert.Equal(expected, _inferencer.InferTextureType(filename));
    }

    [Fact]
    public void InferTextureType_CaseInsensitive()
    {
        Assert.Equal(TextureType.NormalMap, _inferencer.InferTextureType("WALL_NORMAL.PNG"));
        Assert.Equal(TextureType.Emissive, _inferencer.InferTextureType("GLOW_EMISSIVE.TGA"));
    }

    #endregion

    #region ShouldExtract Tests

    [Theory]
    [InlineData("models/character.fbx", true)]
    [InlineData("textures/skin.png", true)]
    [InlineData("audio/footstep.wav", true)]
    [InlineData("data/config.json", true)]
    public void ShouldExtract_ValidPaths_ReturnsTrue(string path, bool expected)
    {
        Assert.Equal(expected, _inferencer.ShouldExtract(path));
    }

    [Theory]
    [InlineData("Assets/Unity/Materials/wood.mat", false)]
    [InlineData("unity/Prefabs/character.prefab", false)]
    [InlineData("models/character.fbx.meta", false)]
    public void ShouldExtract_UnityFiles_ReturnsFalse(string path, bool expected)
    {
        Assert.Equal(expected, _inferencer.ShouldExtract(path));
    }

    [Theory]
    [InlineData("unreal/Meshes/Character.uasset", false)]
    [InlineData("Content/Unreal/Maps/Level.umap", false)]
    public void ShouldExtract_UnrealFiles_ReturnsFalse(string path, bool expected)
    {
        Assert.Equal(expected, _inferencer.ShouldExtract(path));
    }

    [Theory]
    [InlineData("maya/scenes/character.ma", false)]
    [InlineData("Maya/Character.mb", false)]
    public void ShouldExtract_MayaFiles_ReturnsFalse(string path, bool expected)
    {
        Assert.Equal(expected, _inferencer.ShouldExtract(path));
    }

    [Fact]
    public void ShouldExtract_BackslashPaths_Works()
    {
        // Windows-style paths
        Assert.False(_inferencer.ShouldExtract("Assets\\Unity\\material.mat"));
        Assert.True(_inferencer.ShouldExtract("models\\character.fbx"));
    }

    [Fact]
    public void ShouldExtract_WithCategory_StillWorks()
    {
        // Category parameter is optional and doesn't affect basic filtering
        Assert.True(_inferencer.ShouldExtract("model.fbx", "models"));
        Assert.False(_inferencer.ShouldExtract("unity/prefab.prefab", "prefabs"));
    }

    #endregion

    #region Singleton Tests

    [Fact]
    public void Instance_ReturnsSingleton()
    {
        var instance1 = DefaultTypeInferencer.Instance;
        var instance2 = DefaultTypeInferencer.Instance;
        Assert.Same(instance1, instance2);
    }

    #endregion
}
