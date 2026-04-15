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
/// <param name="PivotOverride">
/// Optional per-variant pivot override in normalized frame coordinates (0,0 = top-left;
/// 0.5, 0.85 = center-bottom humanoid default). When null, consumers should auto-compute
/// from bounds via <see cref="BeyondImmersion.Bannou.SpriteTheory.Camera.PivotComputer.ComputeFromBounds"/>.
/// Set this for subjects whose feet projection is unreliable (flying enemies, tall bosses,
/// quadrupeds with unusual proportions).
/// </param>
public record CharacterVariant(
    string Name,
    string ModelPath,
    IReadOnlyList<EquipmentSlot> Equipment,
    IReadOnlyDictionary<string, string>? MaterialOverrides = null,
    float Scale = 1.0f,
    Vector2? PivotOverride = null);
