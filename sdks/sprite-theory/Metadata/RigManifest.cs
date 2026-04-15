namespace BeyondImmersion.Bannou.SpriteTheory.Metadata;

/// <summary>
/// Per-rig breakdown within a <see cref="CaptureManifest"/>. Reports the expected number of
/// captured frames, mirror frames, rendered angles, and mirror angles for a single camera rig.
/// </summary>
/// <param name="RigName">Camera rig name (e.g., "TopDown-8Dir", "SideView-Brawler").</param>
/// <param name="CapturedFrames">Total frames requiring render passes for this rig.</param>
/// <param name="MirrorFrames">Total mirror frames generated from captured frames for this rig.</param>
/// <param name="AngleCount">Number of rendered angles in this rig.</param>
/// <param name="MirrorCount">Number of angles that produce mirrors in this rig.</param>
public record RigManifest(
    string RigName,
    int CapturedFrames,
    int MirrorFrames,
    int AngleCount,
    int MirrorCount);
