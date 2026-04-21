namespace BeyondImmersion.Bannou.SpriteTheory.Metadata;

/// <summary>
/// Input definition describing a specific character configuration to capture.
/// Combines a base model with equipment, material overrides, and scale to define
/// a unique visual variant (e.g., "warrior_plate_sword").
/// </summary>
/// <remarks>
/// <para>
/// Serialized as part of <see cref="SpriteSheet"/> metadata and sprite project files.
/// The variant fully describes what was captured, enabling reproducible batch captures.
/// </para>
/// <para>
/// <b>Pivot resolution order</b> (evaluated by consumers such as sprite-composer during capture):
/// <list type="number">
/// <item><description><see cref="PivotOverride"/> — used verbatim when non-null.</description></item>
/// <item><description><see cref="AnchorBoneName"/> — when non-null AND the bridge supports skeleton
///   introspection AND the bone exists, the bone's world position is projected onto the frame plane
///   via <see cref="Camera.PivotComputer.ProjectWorldPointToFrame"/>.</description></item>
/// <item><description><see cref="Camera.PivotComputer.ComputeFromBounds"/> — falls back to feet-on-ground
///   projection from the model bounds.</description></item>
/// <item><description><see cref="Camera.PivotComputer.DefaultHumanoidPivot"/> — terminal fallback when
///   the camera basis is degenerate.</description></item>
/// </list>
/// </para>
/// </remarks>
/// <param name="Name">Variant identifier (e.g., "warrior_plate_sword").</param>
/// <param name="Model">Reference to the base character model asset (FBX/glTF/engine-native).</param>
/// <param name="Equipment">Attached equipment pieces defining slot-to-mesh-to-bone mappings.</param>
/// <param name="MaterialOverrides">
/// Material or palette swaps applied during capture, keyed by material slot name. Null if no overrides.
/// </param>
/// <param name="Scale">Model scale factor. Defaults to 1.0 (no scaling).</param>
/// <param name="PivotOverride">
/// Optional per-variant pivot override in normalized frame coordinates (0,0 = top-left;
/// 0.5, 0.85 = center-bottom humanoid default). When set, takes precedence over every
/// auto-computation path — see the pivot resolution order in this type's remarks.
/// Use for subjects whose auto-computed pivot is unreliable even with a bone anchor.
/// </param>
/// <param name="AnchorBoneName">
/// Optional skeleton bone whose world position anchors the pivot. When set, the bridge's
/// <c>TryGetBonePosition</c> is queried and the returned point is projected onto the frame plane
/// via <see cref="Camera.PivotComputer.ProjectWorldPointToFrame"/>. Null to skip bone anchoring
/// and fall through to bounds-based auto-computation. Use for subjects whose bounding-box minimum
/// is not a sensible anchor (flowing cloth extending bounds below the feet, weapons extending
/// bounds outward, asymmetric rigs).
/// </param>
public record CharacterVariant(
    string Name,
    AssetReference Model,
    IReadOnlyList<EquipmentSlot> Equipment,
    IReadOnlyDictionary<string, AssetReference>? MaterialOverrides = null,
    float Scale = 1.0f,
    Vector2? PivotOverride = null,
    string? AnchorBoneName = null);
