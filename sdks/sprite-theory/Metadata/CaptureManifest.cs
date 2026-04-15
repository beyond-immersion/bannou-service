using BeyondImmersion.Bannou.SpriteTheory.Animation;
using BeyondImmersion.Bannou.SpriteTheory.Camera;

namespace BeyondImmersion.Bannou.SpriteTheory.Metadata;

/// <summary>
/// Pre-capture estimation of expected frame counts, mirror counts, and estimated capture time.
/// Computed from a character variant, one or more camera rigs, and a list of animations with configs.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="Compute"/> to generate a manifest before starting a capture session. This allows
/// progress tracking, resource estimation, and verification of expected output.
/// </para>
/// <para>
/// Estimated capture time assumes ~50ms per captured frame (render + readback). Mirror frames
/// are generated from captured data and do not require additional render passes.
/// </para>
/// </remarks>
/// <param name="Variant">The character variant that will be captured.</param>
/// <param name="Rigs">Per-rig breakdown of expected frame counts.</param>
/// <param name="TotalCapturedFrames">Total frames requiring render passes across all rigs.</param>
/// <param name="TotalMirrorFrames">Total mirror frames generated from captured frames across all rigs.</param>
/// <param name="TotalFrames">Sum of captured and mirror frames.</param>
/// <param name="EstimatedCaptureTimeMs">Estimated capture time in milliseconds (~50ms per captured frame).</param>
/// <param name="AnimationCount">Number of animations to capture.</param>
public record CaptureManifest(
    CharacterVariant Variant,
    IReadOnlyList<RigManifest> Rigs,
    int TotalCapturedFrames,
    int TotalMirrorFrames,
    int TotalFrames,
    int EstimatedCaptureTimeMs,
    int AnimationCount)
{
    /// <summary>
    /// Computes a capture manifest from a character variant, camera rigs, and animation configurations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For each rig, every angle in <see cref="CameraRig.Angles"/> is captured (rendered).
    /// Angles with <see cref="CaptureAngle.ProducesMirror"/> set to true additionally generate
    /// mirror frames without requiring extra render passes.
    /// </para>
    /// <para>
    /// Verification example (TopDown8Dir + SideViewBrawler, 20 animations x 8 frames):
    /// TopDown8Dir has 5 angles (3 produce mirrors), SideViewBrawler has 1 angle (1 produces mirror).
    /// Captured = (5 * 160) + (1 * 160) = 960, Mirror = (3 * 160) + (1 * 160) = 640, Total = 1600.
    /// </para>
    /// </remarks>
    /// <param name="variant">The character variant to capture.</param>
    /// <param name="rigs">Camera rigs defining capture angles and frame dimensions.</param>
    /// <param name="animations">Animation info and config pairs defining what to capture.</param>
    /// <returns>A manifest with per-rig breakdowns and totals.</returns>
    public static CaptureManifest Compute(
        CharacterVariant variant,
        IReadOnlyList<CameraRig> rigs,
        IReadOnlyList<(AnimationInfo Info, AnimationConfig Config)> animations)
    {
        var totalCaptured = 0;
        var totalMirror = 0;
        var rigManifests = new List<RigManifest>();

        foreach (var rig in rigs)
        {
            var angleCount = rig.Angles.Count;
            var mirrorCount = 0;
            foreach (var angle in rig.Angles)
            {
                if (angle.ProducesMirror)
                    mirrorCount++;
            }

            var rigCaptured = 0;
            var rigMirror = 0;

            foreach (var (_, config) in animations)
            {
                rigCaptured += angleCount * config.FrameCount;
                rigMirror += mirrorCount * config.FrameCount;
            }

            totalCaptured += rigCaptured;
            totalMirror += rigMirror;
            rigManifests.Add(new RigManifest(rig.Name, rigCaptured, rigMirror, angleCount, mirrorCount));
        }

        return new CaptureManifest(
            Variant: variant,
            Rigs: rigManifests,
            TotalCapturedFrames: totalCaptured,
            TotalMirrorFrames: totalMirror,
            TotalFrames: totalCaptured + totalMirror,
            EstimatedCaptureTimeMs: totalCaptured * 50,
            AnimationCount: animations.Count);
    }
}
