using BeyondImmersion.Bannou.AssetBundler.Extraction;
using BeyondImmersion.Bannou.AssetBundler.Stride;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Stride.Tests;

/// <summary>
/// Tests for StrideTypeInferencer.
/// </summary>
public class StrideTypeInferencerTests
{
    private readonly StrideTypeInferencer _inferencer = new();

    #region InferAssetType Tests

    [Theory]
    [InlineData("character.fbx", AssetType.Model)]
    [InlineData("environment.FBX", AssetType.Model)]
    [InlineData("prop.obj", AssetType.Model)]
    [InlineData("scene.dae", AssetType.Model)]
    [InlineData("item.glb", AssetType.Model)]
    [InlineData("world.gltf", AssetType.Model)]
    public void InferAssetType_ModelFiles_ReturnsModel(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("diffuse.png", AssetType.Texture)]
    [InlineData("albedo.jpg", AssetType.Texture)]
    [InlineData("normal.jpeg", AssetType.Texture)]
    [InlineData("mask.tga", AssetType.Texture)]
    [InlineData("compressed.dds", AssetType.Texture)]
    [InlineData("modern.webp", AssetType.Texture)]
    public void InferAssetType_TextureFiles_ReturnsTexture(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("footstep.wav", AssetType.Audio)]
    [InlineData("music.ogg", AssetType.Audio)]
    [InlineData("voice.mp3", AssetType.Audio)]
    public void InferAssetType_AudioFiles_ReturnsAudio(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("walk.anim", AssetType.Animation)]
    [InlineData("character_anim.fbx", AssetType.Animation)]
    [InlineData("run_animation.fbx", AssetType.Animation)]
    public void InferAssetType_AnimationFiles_ReturnsAnimation(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("npc_behavior.yaml", AssetType.Behavior)]
    [InlineData("ai_behavior.yml", AssetType.Behavior)]
    [InlineData("behavior.json", AssetType.Behavior)]
    public void InferAssetType_BehaviorFiles_ReturnsBehavior(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("readme.txt", AssetType.Other)]
    [InlineData("unknown.xyz", AssetType.Other)]
    [InlineData("noextension", AssetType.Other)]
    public void InferAssetType_OtherFiles_ReturnsOther(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Fact]
    public void InferAssetType_CaseInsensitive()
    {
        Assert.Equal(AssetType.Model, _inferencer.InferAssetType("MODEL.FBX"));
        Assert.Equal(AssetType.Texture, _inferencer.InferAssetType("TEXTURE.PNG"));
    }

    #endregion

    #region InferTextureType Tests

    [Theory]
    [InlineData("wall_normal.png", TextureType.NormalMap)]
    [InlineData("surface_nml.tga", TextureType.NormalMap)]
    [InlineData("brick_n.png", TextureType.NormalMap)]
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
    [InlineData("metal_mask.png", TextureType.Mask)]
    [InlineData("surface_metallic.tga", TextureType.Mask)]
    [InlineData("material_roughness.png", TextureType.Mask)]
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
    [InlineData("spr_icon.png", TextureType.UI)]
    [InlineData("ui_button.png", TextureType.UI)]
    [InlineData("hud_health.png", TextureType.UI)]
    [InlineData("menu_icon.png", TextureType.UI)]
    [InlineData("settings_button.png", TextureType.UI)]
    public void InferTextureType_UI_ReturnsUI(string filename, TextureType expected)
    {
        Assert.Equal(expected, _inferencer.InferTextureType(filename));
    }

    [Theory]
    [InlineData("diffuse.png", TextureType.Color)]
    [InlineData("albedo.jpg", TextureType.Color)]
    [InlineData("regular.tga", TextureType.Color)]
    public void InferTextureType_Color_ReturnsColor(string filename, TextureType expected)
    {
        Assert.Equal(expected, _inferencer.InferTextureType(filename));
    }

    #endregion

    #region ShouldExtract Tests

    [Theory]
    [InlineData("models/character.fbx", true)]
    [InlineData("textures/diffuse.png", true)]
    [InlineData("audio/footstep.wav", true)]
    public void ShouldExtract_ValidPaths_ReturnsTrue(string path, bool expected)
    {
        Assert.Equal(expected, _inferencer.ShouldExtract(path));
    }

    [Theory]
    [InlineData("unity/Materials/wood.mat", false)]
    [InlineData("Assets/Unity/Prefabs/character.prefab", false)]
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
    [InlineData("Maya/model.mb", false)]
    public void ShouldExtract_MayaFiles_ReturnsFalse(string path, bool expected)
    {
        Assert.Equal(expected, _inferencer.ShouldExtract(path));
    }

    [Fact]
    public void ShouldExtract_WindowsPaths_Works()
    {
        Assert.False(_inferencer.ShouldExtract("Assets\\Unity\\material.mat"));
        Assert.True(_inferencer.ShouldExtract("models\\character.fbx"));
    }

    #endregion
}
