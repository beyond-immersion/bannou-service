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

    #region Clone Tests

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var original = new DefaultTypeInferencer();
        var clone = original.Clone();

        Assert.NotSame(original, clone);
    }

    [Fact]
    public void Clone_PreservesExcludedExtensions()
    {
        var original = new DefaultTypeInferencer();
        original.ExcludeExtension(".custom");

        var clone = original.Clone();

        // Both should exclude .custom
        Assert.False(original.ShouldExtract("file.custom"));
        Assert.False(clone.ShouldExtract("file.custom"));
    }

    [Fact]
    public void Clone_ModificationsAreIndependent()
    {
        var original = new DefaultTypeInferencer();
        var clone = original.Clone();

        // Modify clone only
        clone.ExcludeExtension(".cloneonly");

        // Original should not be affected
        Assert.True(original.ShouldExtract("file.cloneonly"));
        Assert.False(clone.ShouldExtract("file.cloneonly"));
    }

    #endregion

    #region ExcludeExtension Tests

    [Fact]
    public void ExcludeExtension_AddsNewExclusion()
    {
        var inferencer = new DefaultTypeInferencer();

        // By default, .xyz should be allowed
        Assert.True(inferencer.ShouldExtract("file.xyz"));

        // Exclude it
        inferencer.ExcludeExtension(".xyz");

        Assert.False(inferencer.ShouldExtract("file.xyz"));
    }

    [Fact]
    public void ExcludeExtension_CaseInsensitive()
    {
        var inferencer = new DefaultTypeInferencer();
        inferencer.ExcludeExtension(".XYZ");

        Assert.False(inferencer.ShouldExtract("file.xyz"));
        Assert.False(inferencer.ShouldExtract("file.XYZ"));
        Assert.False(inferencer.ShouldExtract("FILE.Xyz"));
    }

    [Fact]
    public void ExcludeExtension_ReturnsThisForChaining()
    {
        var inferencer = new DefaultTypeInferencer();

        var result = inferencer
            .ExcludeExtension(".a")
            .ExcludeExtension(".b")
            .ExcludeExtension(".c");

        Assert.Same(inferencer, result);
        Assert.False(inferencer.ShouldExtract("file.a"));
        Assert.False(inferencer.ShouldExtract("file.b"));
        Assert.False(inferencer.ShouldExtract("file.c"));
    }

    #endregion

    #region ExcludeDirectory Tests

    [Fact]
    public void ExcludeDirectory_AddsNewExclusion()
    {
        var inferencer = new DefaultTypeInferencer();

        // By default, /custom/ should be allowed
        Assert.True(inferencer.ShouldExtract("custom/file.fbx"));

        // Exclude it
        inferencer.ExcludeDirectory("custom");

        Assert.False(inferencer.ShouldExtract("custom/file.fbx"));
    }

    [Fact]
    public void ExcludeDirectory_MatchesAnywhereInPath()
    {
        var inferencer = new DefaultTypeInferencer();
        inferencer.ExcludeDirectory("excluded");

        Assert.False(inferencer.ShouldExtract("excluded/file.fbx"));
        Assert.False(inferencer.ShouldExtract("root/excluded/file.fbx"));
        Assert.False(inferencer.ShouldExtract("a/b/excluded/c/file.fbx"));
    }

    [Fact]
    public void ExcludeDirectory_CaseInsensitive()
    {
        var inferencer = new DefaultTypeInferencer();
        inferencer.ExcludeDirectory("EXCLUDED");

        Assert.False(inferencer.ShouldExtract("excluded/file.fbx"));
        Assert.False(inferencer.ShouldExtract("EXCLUDED/file.fbx"));
        Assert.False(inferencer.ShouldExtract("Excluded/file.fbx"));
    }

    [Fact]
    public void ExcludeDirectory_ReturnsThisForChaining()
    {
        var inferencer = new DefaultTypeInferencer();

        var result = inferencer
            .ExcludeDirectory("dir1")
            .ExcludeDirectory("dir2");

        Assert.Same(inferencer, result);
        Assert.False(inferencer.ShouldExtract("dir1/file.fbx"));
        Assert.False(inferencer.ShouldExtract("dir2/file.fbx"));
    }

    #endregion

    #region RegisterCategoryFilter Tests

    [Fact]
    public void RegisterCategoryFilter_AppliesFilterForCategory()
    {
        var inferencer = new DefaultTypeInferencer();

        // Register filter that only allows .png files for "textures" category
        inferencer.RegisterCategoryFilter("textures", path => path.EndsWith(".png"));

        // Without category, both are allowed
        Assert.True(inferencer.ShouldExtract("file.png"));
        Assert.True(inferencer.ShouldExtract("file.fbx"));

        // With category, filter applies
        Assert.True(inferencer.ShouldExtract("file.png", "textures"));
        Assert.False(inferencer.ShouldExtract("file.fbx", "textures"));
    }

    [Fact]
    public void RegisterCategoryFilter_CaseInsensitiveCategoryName()
    {
        var inferencer = new DefaultTypeInferencer();
        inferencer.RegisterCategoryFilter("MODELS", path => path.EndsWith(".fbx"));

        Assert.True(inferencer.ShouldExtract("file.fbx", "models"));
        Assert.True(inferencer.ShouldExtract("file.fbx", "MODELS"));
        Assert.True(inferencer.ShouldExtract("file.fbx", "Models"));
    }

    [Fact]
    public void RegisterCategoryFilter_StillAppliesDefaultExclusions()
    {
        var inferencer = new DefaultTypeInferencer();

        // Register permissive filter
        inferencer.RegisterCategoryFilter("all", _ => true);

        // Default exclusions still apply first (Unity .meta files)
        Assert.False(inferencer.ShouldExtract("file.meta", "all"));
    }

    [Fact]
    public void RegisterCategoryFilter_MultipleCategories()
    {
        var inferencer = new DefaultTypeInferencer();

        // Predicates match directory segments (with or without leading slash)
        inferencer.RegisterCategoryFilter("models", path => path.Contains("fbx/") || path.StartsWith("fbx/"));
        inferencer.RegisterCategoryFilter("textures", path => path.Contains("textures/") || path.StartsWith("textures/"));

        Assert.True(inferencer.ShouldExtract("fbx/model.fbx", "models"));
        Assert.False(inferencer.ShouldExtract("textures/diffuse.png", "models"));

        Assert.True(inferencer.ShouldExtract("textures/diffuse.png", "textures"));
        Assert.False(inferencer.ShouldExtract("fbx/model.fbx", "textures"));
    }

    [Fact]
    public void RegisterCategoryFilter_ReturnsThisForChaining()
    {
        var inferencer = new DefaultTypeInferencer();

        var result = inferencer
            .RegisterCategoryFilter("a", _ => true)
            .RegisterCategoryFilter("b", _ => false);

        Assert.Same(inferencer, result);
    }

    #endregion

    #region InferTextureType Directory Pattern Tests

    [Theory]
    [InlineData("ui/button.png", TextureType.UI)]
    [InlineData("assets/ui/panel.png", TextureType.UI)]
    [InlineData("hud/health_bar.png", TextureType.UI)]
    [InlineData("sprites/icon.png", TextureType.UI)]
    [InlineData("interface/menu.png", TextureType.UI)]
    public void InferTextureType_DirectoryPatterns_ReturnsUI(string path, TextureType expected)
    {
        Assert.Equal(expected, _inferencer.InferTextureType(path));
    }

    [Theory]
    [InlineData("textures/wall.png", TextureType.Color)]
    [InlineData("models/character/skin.png", TextureType.Color)]
    public void InferTextureType_NonUIDirectories_ReturnsColor(string path, TextureType expected)
    {
        Assert.Equal(expected, _inferencer.InferTextureType(path));
    }

    #endregion

    #region Combined Customization Tests

    [Fact]
    public void CombinedCustomization_WorksTogether()
    {
        var inferencer = new DefaultTypeInferencer()
            .ExcludeExtension(".custom")
            .ExcludeDirectory("excluded")
            .RegisterCategoryFilter("polygon", path =>
                path.Contains("fbx/") || path.Contains("textures/"));

        // Extension exclusion
        Assert.False(inferencer.ShouldExtract("file.custom"));

        // Directory exclusion
        Assert.False(inferencer.ShouldExtract("excluded/file.fbx"));

        // Category filter (without category)
        Assert.True(inferencer.ShouldExtract("random/file.fbx"));

        // Category filter (with category)
        Assert.True(inferencer.ShouldExtract("fbx/file.fbx", "polygon"));
        Assert.False(inferencer.ShouldExtract("random/file.fbx", "polygon"));
    }

    #endregion
}
