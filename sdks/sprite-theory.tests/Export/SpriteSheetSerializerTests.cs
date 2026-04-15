using System.Text.Json;
using BeyondImmersion.Bannou.SpriteTheory;
using BeyondImmersion.Bannou.SpriteTheory.Camera;
using BeyondImmersion.Bannou.SpriteTheory.Export;
using BeyondImmersion.Bannou.SpriteTheory.Metadata;
using Xunit;

namespace BeyondImmersion.Bannou.SpriteTheory.Tests.Export;

public class SpriteSheetSerializerTests
{
    private static SpriteSheet CreateTestSpriteSheet(Dictionary<string, string>? customProperties = null)
    {
        var variant = new CharacterVariant("test", "model.fbx", Array.Empty<EquipmentSlot>());
        var rig = CameraRigPresets.SideViewBrawler();
        var frames = new List<SpriteFrame>
        {
            new SpriteFrame(0, 0, "right", "idle", 0,
                new Rectangle(0, 0, 128, 128), null, new Vector2(0.5f, 0.85f),
                0.125f, false, null)
        };
        var animations = new List<SpriteAnimation>
        {
            new SpriteAnimation("idle", LoopMode.Loop, 1.0f,
                new Dictionary<string, int[]> { ["right"] = new[] { 0 } }, null)
        };
        var atlases = new List<AtlasInfo>
        {
            new AtlasInfo(0, "test_atlas.png", 128, 128)
        };

        return new SpriteSheet(
            "1.0", "Test", DateTimeOffset.UtcNow,
            variant, rig, atlases, animations, frames, customProperties);
    }

    [Fact]
    public void Serialize_Roundtrip_PreservesData()
    {
        var original = CreateTestSpriteSheet();

        var json = SpriteSheetSerializer.Serialize(original);
        var deserialized = SpriteSheetSerializer.Deserialize(json);

        Assert.Equal(original.Version, deserialized.Version);
        Assert.Equal(original.Generator, deserialized.Generator);
        Assert.Equal(original.Variant.Name, deserialized.Variant.Name);
        Assert.Equal(original.Variant.ModelPath, deserialized.Variant.ModelPath);
        Assert.Equal(original.Rig.Name, deserialized.Rig.Name);
        Assert.Equal(original.Atlases.Count, deserialized.Atlases.Count);
        Assert.Equal(original.Atlases[0].Filename, deserialized.Atlases[0].Filename);
        Assert.Equal(original.Animations.Count, deserialized.Animations.Count);
        Assert.Equal(original.Animations[0].Name, deserialized.Animations[0].Name);
        Assert.Equal(original.Frames.Count, deserialized.Frames.Count);
        Assert.Equal(original.Frames[0].AngleName, deserialized.Frames[0].AngleName);
        Assert.Equal(original.Frames[0].Duration, deserialized.Frames[0].Duration);
    }

    [Fact]
    public void Serialize_CamelCasePropertyNames()
    {
        var sheet = CreateTestSpriteSheet();

        var json = SpriteSheetSerializer.Serialize(sheet);

        // camelCase properties should be present
        Assert.Contains("\"version\"", json);
        Assert.Contains("\"generator\"", json);
        Assert.Contains("\"generatedAt\"", json);
        Assert.Contains("\"variant\"", json);

        // PascalCase should NOT be present as property names
        Assert.DoesNotContain("\"Version\"", json);
        Assert.DoesNotContain("\"Generator\"", json);
    }

    [Fact]
    public void Serialize_NullFieldsOmitted()
    {
        // Create sheet with null CustomProperties
        var sheet = CreateTestSpriteSheet(customProperties: null);

        var json = SpriteSheetSerializer.Serialize(sheet);

        // customProperties should not appear in JSON when null
        Assert.DoesNotContain("\"customProperties\"", json);
    }

    [Fact]
    public void Deserialize_ValidJson_ReturnsSpriteSheet()
    {
        // Serialize a known sheet to get the canonical JSON format (enum representation, etc.)
        var original = CreateTestSpriteSheet();
        var json = SpriteSheetSerializer.Serialize(original);

        var sheet = SpriteSheetSerializer.Deserialize(json);

        Assert.Equal("1.0", sheet.Version);
        Assert.Equal("Test", sheet.Generator);
        Assert.Equal("test", sheet.Variant.Name);
        Assert.Equal("model.fbx", sheet.Variant.ModelPath);
        Assert.Single(sheet.Atlases);
        Assert.Equal("test_atlas.png", sheet.Atlases[0].Filename);
        Assert.Equal(128, sheet.Atlases[0].Width);
    }

    [Fact]
    public void Deserialize_InvalidJson_ThrowsJsonException()
    {
        var invalidJson = "{ this is not valid json }";

        Assert.Throws<JsonException>(() => SpriteSheetSerializer.Deserialize(invalidJson));
    }
}
