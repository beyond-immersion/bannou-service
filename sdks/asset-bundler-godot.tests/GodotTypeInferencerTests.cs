using BeyondImmersion.Bannou.AssetBundler.Extraction;
using BeyondImmersion.Bannou.AssetBundler.Godot;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Godot.Tests;

/// <summary>
/// Tests for GodotTypeInferencer.
/// </summary>
public class GodotTypeInferencerTests
{
    private readonly GodotTypeInferencer _inferencer = new();

    #region InferAssetType Tests

    [Theory]
    [InlineData("character.glb", AssetType.Model)]
    [InlineData("environment.gltf", AssetType.Model)]
    [InlineData("prop.GLB", AssetType.Model)]
    public void InferAssetType_RuntimeModelFiles_ReturnsModel(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("character.fbx", AssetType.Model)]
    [InlineData("environment.FBX", AssetType.Model)]
    [InlineData("prop.obj", AssetType.Model)]
    [InlineData("scene.dae", AssetType.Model)]
    [InlineData("item.blend", AssetType.Model)]
    public void InferAssetType_ConvertibleModelFiles_ReturnsModel(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("diffuse.png", AssetType.Texture)]
    [InlineData("albedo.jpg", AssetType.Texture)]
    [InlineData("normal.jpeg", AssetType.Texture)]
    [InlineData("modern.webp", AssetType.Texture)]
    public void InferAssetType_RuntimeTextureFiles_ReturnsTexture(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("mask.tga", AssetType.Texture)]
    [InlineData("compressed.dds", AssetType.Texture)]
    [InlineData("old.bmp", AssetType.Texture)]
    [InlineData("photo.tiff", AssetType.Texture)]
    public void InferAssetType_ConvertibleTextureFiles_ReturnsTexture(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("footstep.wav", AssetType.Audio)]
    [InlineData("music.ogg", AssetType.Audio)]
    [InlineData("voice.mp3", AssetType.Audio)]
    public void InferAssetType_RuntimeAudioFiles_ReturnsAudio(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("soundtrack.flac", AssetType.Audio)]
    [InlineData("audio.opus", AssetType.Audio)]
    public void InferAssetType_ConvertibleAudioFiles_ReturnsAudio(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("npc_behavior.yaml", AssetType.Behavior)]
    [InlineData("ai_behavior.yml", AssetType.Behavior)]
    [InlineData("enemy_behavior.json", AssetType.Behavior)]
    public void InferAssetType_BehaviorFiles_ReturnsBehavior(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Theory]
    [InlineData("readme.txt", AssetType.Other)]
    [InlineData("unknown.xyz", AssetType.Other)]
    [InlineData("noextension", AssetType.Other)]
    [InlineData("scene.tscn", AssetType.Other)]
    [InlineData("resource.tres", AssetType.Other)]
    public void InferAssetType_OtherFiles_ReturnsOther(string filename, AssetType expected)
    {
        Assert.Equal(expected, _inferencer.InferAssetType(filename));
    }

    [Fact]
    public void InferAssetType_CaseInsensitive()
    {
        Assert.Equal(AssetType.Model, _inferencer.InferAssetType("MODEL.GLB"));
        Assert.Equal(AssetType.Texture, _inferencer.InferAssetType("TEXTURE.PNG"));
        Assert.Equal(AssetType.Audio, _inferencer.InferAssetType("SOUND.WAV"));
    }

    #endregion

    #region InferTextureType Tests

    [Theory]
    [InlineData("wall_normal.png", TextureType.NormalMap)]
    [InlineData("surface_nml.tga", TextureType.NormalMap)]
    [InlineData("brick_n.png", TextureType.NormalMap)]
    [InlineData("floor_norm.jpg", TextureType.NormalMap)]
    public void InferTextureType_NormalMaps_ReturnsNormalMap(string filename, TextureType expected)
    {
        Assert.Equal(expected, _inferencer.InferTextureType(filename));
    }

    [Theory]
    [InlineData("glow_emissive.png", TextureType.Emissive)]
    [InlineData("light_emit.tga", TextureType.Emissive)]
    [InlineData("neon_e.png", TextureType.Emissive)]
    [InlineData("screen_glow.jpg", TextureType.Emissive)]
    public void InferTextureType_Emissive_ReturnsEmissive(string filename, TextureType expected)
    {
        Assert.Equal(expected, _inferencer.InferTextureType(filename));
    }

    [Theory]
    [InlineData("metal_mask.png", TextureType.Mask)]
    [InlineData("surface_metallic.tga", TextureType.Mask)]
    [InlineData("material_roughness.png", TextureType.Mask)]
    [InlineData("occlusion_ao.png", TextureType.Mask)]
    [InlineData("combined_orm.png", TextureType.Mask)]
    [InlineData("packed_rma.png", TextureType.Mask)]
    public void InferTextureType_Masks_ReturnsMask(string filename, TextureType expected)
    {
        Assert.Equal(expected, _inferencer.InferTextureType(filename));
    }

    [Theory]
    [InlineData("terrain_height.png", TextureType.HeightMap)]
    [InlineData("surface_displacement.tga", TextureType.HeightMap)]
    [InlineData("bump_h.png", TextureType.HeightMap)]
    [InlineData("floor_bump.jpg", TextureType.HeightMap)]
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
    [InlineData("panel_gui.png", TextureType.UI)]
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
    [InlineData("models/character.glb", true)]
    [InlineData("textures/diffuse.png", true)]
    [InlineData("audio/footstep.wav", true)]
    [InlineData("models/prop.fbx", true)]
    public void ShouldExtract_ValidPaths_ReturnsTrue(string path, bool expected)
    {
        Assert.Equal(expected, _inferencer.ShouldExtract(path));
    }

    [Theory]
    [InlineData("stride/Models/character.sdpkg", false)]
    [InlineData("Assets/Stride/model.sdmodel", false)]
    [InlineData("compiled/texture.sdtex", false)]
    [InlineData("animations/walk.sdanim", false)]
    [InlineData("materials/wood.sdmat", false)]
    public void ShouldExtract_StrideFiles_ReturnsFalse(string path, bool expected)
    {
        Assert.Equal(expected, _inferencer.ShouldExtract(path));
    }

    [Theory]
    [InlineData("unity/Materials/wood.mat", false)]
    [InlineData("Assets/Unity/Prefabs/character.prefab", false)]
    [InlineData("models/character.fbx.meta", false)]
    [InlineData("scenes/main.unity", false)]
    [InlineData("animations/walk.anim", false)]
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
    [InlineData("3dsmax/scene.max", false)]
    public void ShouldExtract_DCCFiles_ReturnsFalse(string path, bool expected)
    {
        Assert.Equal(expected, _inferencer.ShouldExtract(path));
    }

    [Theory]
    [InlineData("docs/readme.txt", false)]
    [InlineData("README.md", false)]
    [InlineData("reference.pdf", false)]
    [InlineData("docs/guide.html", false)]
    public void ShouldExtract_DocumentationFiles_ReturnsFalse(string path, bool expected)
    {
        Assert.Equal(expected, _inferencer.ShouldExtract(path));
    }

    [Theory]
    [InlineData("sources/texture.psd", false)]
    [InlineData("substance/material.spp", false)]
    [InlineData("substance/material.sbs", false)]
    [InlineData("substance/material.sbsar", false)]
    public void ShouldExtract_SourceFiles_ReturnsFalse(string path, bool expected)
    {
        Assert.Equal(expected, _inferencer.ShouldExtract(path));
    }

    [Theory]
    [InlineData("textures/diffuse.png.import", false)]
    [InlineData("models/character.glb.import", false)]
    public void ShouldExtract_GodotImportFiles_ReturnsFalse(string path, bool expected)
    {
        Assert.Equal(expected, _inferencer.ShouldExtract(path));
    }

    [Fact]
    public void ShouldExtract_WindowsPaths_Works()
    {
        Assert.False(_inferencer.ShouldExtract("Assets\\Unity\\material.mat"));
        Assert.False(_inferencer.ShouldExtract("Stride\\model.sdmodel"));
        Assert.True(_inferencer.ShouldExtract("models\\character.glb"));
    }

    #endregion

    #region Static Helper Tests

    [Theory]
    [InlineData(".glb", true)]
    [InlineData(".gltf", true)]
    [InlineData(".png", true)]
    [InlineData(".jpg", true)]
    [InlineData(".webp", true)]
    [InlineData(".wav", true)]
    [InlineData(".ogg", true)]
    [InlineData(".mp3", true)]
    public void IsRuntimeLoadable_SupportedExtensions_ReturnsTrue(string extension, bool expected)
    {
        Assert.Equal(expected, GodotTypeInferencer.IsRuntimeLoadable(extension));
    }

    [Theory]
    [InlineData(".fbx", false)]
    [InlineData(".obj", false)]
    [InlineData(".tga", false)]
    [InlineData(".dds", false)]
    [InlineData(".flac", false)]
    public void IsRuntimeLoadable_ConvertibleExtensions_ReturnsFalse(string extension, bool expected)
    {
        Assert.Equal(expected, GodotTypeInferencer.IsRuntimeLoadable(extension));
    }

    [Theory]
    [InlineData(".fbx", true)]
    [InlineData(".obj", true)]
    [InlineData(".dae", true)]
    [InlineData(".blend", true)]
    [InlineData(".tga", true)]
    [InlineData(".dds", true)]
    [InlineData(".bmp", true)]
    [InlineData(".flac", true)]
    [InlineData(".opus", true)]
    public void RequiresConversion_ConvertibleExtensions_ReturnsTrue(string extension, bool expected)
    {
        Assert.Equal(expected, GodotTypeInferencer.RequiresConversion(extension));
    }

    [Theory]
    [InlineData(".glb", false)]
    [InlineData(".png", false)]
    [InlineData(".wav", false)]
    [InlineData(".unknown", false)]
    public void RequiresConversion_NativeExtensions_ReturnsFalse(string extension, bool expected)
    {
        Assert.Equal(expected, GodotTypeInferencer.RequiresConversion(extension));
    }

    #endregion
}
