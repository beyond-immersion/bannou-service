using BeyondImmersion.Bannou.SpriteTheory.Camera;

namespace BeyondImmersion.Bannou.SpriteTheory.Metadata;

/// <summary>
/// Complete output metadata for a sprite capture session. Contains the variant that was captured,
/// the camera rig used, atlas image info, all animations with per-angle frame maps, and all frames
/// (both captured and mirror entries). Serializes to a canonical JSON format via SpriteSheetSerializer.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Frames"/> list contains captured frames first (indices 0 through N-1),
/// followed by mirror frames (indices N through N+M-1). This ordering is deterministic.
/// </para>
/// <para>
/// <see cref="CustomProperties"/> is optional game-specific metadata embedded in the output JSON.
/// It is not read, validated, or interpreted by any SDK component. Example uses: game build version,
/// character class tag, content pipeline batch ID.
/// </para>
/// </remarks>
/// <param name="Version">Schema version (e.g., "1.0").</param>
/// <param name="Generator">Generator identifier (e.g., "BeyondImmersion.Bannou.SpriteComposer").</param>
/// <param name="GeneratedAt">Timestamp when the capture was performed.</param>
/// <param name="Variant">The character variant that was captured (model, equipment, materials, scale).</param>
/// <param name="Rig">Camera rig used for capture (source angles only, no mirror targets).</param>
/// <param name="Atlases">Atlas image descriptors (one or more if frames overflowed a single atlas).</param>
/// <param name="Animations">All captured animations with per-angle frame index maps.</param>
/// <param name="Frames">All frames — captured frames first, then mirror frames appended.</param>
/// <param name="CustomProperties">Optional game-specific opaque metadata. Null if not provided.</param>
public record SpriteSheet(
    string Version,
    string Generator,
    DateTimeOffset GeneratedAt,
    CharacterVariant Variant,
    CameraRig Rig,
    IReadOnlyList<AtlasInfo> Atlases,
    IReadOnlyList<SpriteAnimation> Animations,
    IReadOnlyList<SpriteFrame> Frames,
    IReadOnlyDictionary<string, string>? CustomProperties);
