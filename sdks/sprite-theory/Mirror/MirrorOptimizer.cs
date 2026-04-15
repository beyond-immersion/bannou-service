using BeyondImmersion.Bannou.SpriteTheory.Camera;
using BeyondImmersion.Bannou.SpriteTheory.Metadata;

namespace BeyondImmersion.Bannou.SpriteTheory.Mirror;

/// <summary>
/// Analyzes camera rig angles to compute mirror relationships and generates mirror
/// <see cref="SpriteFrame"/> entries from captured frames. Mirror frames share their
/// source frame's atlas rectangle — no duplicate pixels exist in the atlas.
/// </summary>
public static class MirrorOptimizer
{
    /// <summary>
    /// Extracts mirror relationships from a camera rig by scanning all angles
    /// where <see cref="CaptureAngle.ProducesMirror"/> is true and
    /// <see cref="CaptureAngle.MirrorTargetName"/> is not null.
    /// </summary>
    /// <param name="rig">Camera rig containing capture angles with optional mirror metadata.</param>
    /// <returns>List of mirror relationships, one per angle that produces a mirror.</returns>
    public static IReadOnlyList<MirrorInfo> ComputeMirrors(CameraRig rig)
    {
        var mirrors = new List<MirrorInfo>();

        foreach (var angle in rig.Angles)
        {
            if (angle.ProducesMirror && angle.MirrorTargetName is not null)
            {
                mirrors.Add(new MirrorInfo(
                    SourceAngleName: angle.Name,
                    TargetAngleName: angle.MirrorTargetName,
                    FlipAxis: angle.MirrorAxis));
            }
        }

        return mirrors;
    }

    /// <summary>
    /// Generates mirror <see cref="SpriteFrame"/> entries from captured frames and mirror relationships.
    /// Mirror frames share the source frame's atlas rectangle with a flipped pivot point.
    /// </summary>
    /// <remarks>
    /// Mirror frame indices start at <paramref name="capturedFrames"/>.Count, maintaining
    /// the deterministic ordering convention where captured frames come first.
    /// </remarks>
    /// <param name="capturedFrames">All captured (non-mirror) sprite frames from the capture session.</param>
    /// <param name="mirrors">Mirror relationships computed by <see cref="ComputeMirrors"/>.</param>
    /// <returns>List of mirror sprite frames to append after captured frames.</returns>
    public static IReadOnlyList<SpriteFrame> GenerateMirrorFrames(
        IReadOnlyList<SpriteFrame> capturedFrames,
        IReadOnlyList<MirrorInfo> mirrors)
    {
        var mirrorFrames = new List<SpriteFrame>();
        var nextIndex = capturedFrames.Count;

        foreach (var mirror in mirrors)
        {
            var sourceFrames = capturedFrames
                .Where(f => f.AngleName == mirror.SourceAngleName)
                .OrderBy(f => f.Index);

            foreach (var source in sourceFrames)
            {
                mirrorFrames.Add(new SpriteFrame(
                    Index: nextIndex,
                    AtlasIndex: source.AtlasIndex,
                    AngleName: mirror.TargetAngleName,
                    AnimationName: source.AnimationName,
                    FrameInAnimation: source.FrameInAnimation,
                    Rect: source.Rect,
                    TrimmedRect: source.TrimmedRect,
                    Pivot: FlipPivot(source.Pivot, mirror.FlipAxis),
                    Duration: source.Duration,
                    IsMirror: true,
                    MirrorSourceIndex: source.Index));

                nextIndex++;
            }
        }

        return mirrorFrames;
    }

    /// <summary>
    /// Flips a pivot point along the specified axis.
    /// Horizontal: X becomes 1-X. Vertical: Y becomes 1-Y.
    /// </summary>
    private static Vector2 FlipPivot(Vector2 pivot, MirrorAxis axis)
    {
        return axis switch
        {
            MirrorAxis.Horizontal => new Vector2(1.0f - pivot.X, pivot.Y),
            MirrorAxis.Vertical => new Vector2(pivot.X, 1.0f - pivot.Y),
            _ => pivot
        };
    }
}
