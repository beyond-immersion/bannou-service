namespace BeyondImmersion.Bannou.SpriteTheory.Metadata;

/// <summary>
/// Input definition describing a specific character configuration to capture.
/// Combines a base model with equipment, material overrides, and scale to define
/// a unique visual variant (e.g., "warrior_plate_sword").
/// </summary>
/// <remarks>
/// Serialized as part of <see cref="SpriteSheet"/> metadata and sprite project files.
/// The variant fully describes what was captured, enabling reproducible batch captures.
/// </remarks>
/// <param name="Name">Variant identifier (e.g., "warrior_plate_sword").</param>
/// <param name="ModelPath">Path to the base character model (FBX/glTF).</param>
/// <param name="Equipment">Attached equipment pieces defining slot-to-mesh-to-bone mappings.</param>
/// <param name="MaterialOverrides">Material or palette swaps applied during capture. Null if no overrides.</param>
/// <param name="Scale">Model scale factor. Defaults to 1.0 (no scaling).</param>
public record CharacterVariant(
    string Name,
    string ModelPath,
    IReadOnlyList<EquipmentSlot> Equipment,
    Dictionary<string, string>? MaterialOverrides = null,
    float Scale = 1.0f);
