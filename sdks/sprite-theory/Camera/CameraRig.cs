namespace BeyondImmersion.Bannou.SpriteTheory.Camera;

/// <summary>
/// Defines how a character is captured from one orientation. Contains the projection mode,
/// list of capture angles, frame dimensions, and rendering options.
/// </summary>
/// <remarks>
/// <para>
/// Every angle in <see cref="Angles"/> is rendered by the bridge. Mirror targets
/// (like "NW" derived from "NE") are NOT in this list — they are computed by
/// MirrorOptimizer from <see cref="CaptureAngle.ProducesMirror"/> metadata.
/// </para>
/// <para>
/// CameraRig is immutable (C# record). Changes produce new instances, supporting
/// undo/redo in the composer via before/after rig snapshots.
/// </para>
/// </remarks>
/// <param name="Name">Rig identifier (e.g., "SideView-Brawler", "TopDown-8Dir").</param>
/// <param name="Projection">Camera projection mode. Defaults to Orthographic.</param>
/// <param name="Angles">Angles to render — every angle IS captured.</param>
/// <param name="FrameSize">Per-frame pixel dimensions (Width, Height).</param>
/// <param name="Padding">Pixels between frames in atlas. Defaults to 2.</param>
/// <param name="BackgroundColor">Render clear color. Defaults to transparent.</param>
/// <param name="IncludeNormalMap">Whether to also generate a depth-to-normal atlas.</param>
/// <param name="TrimTransparent">Whether to trim transparent borders per frame.</param>
public record CameraRig(
    string Name,
    ProjectionType Projection,
    IReadOnlyList<CaptureAngle> Angles,
    (int Width, int Height) FrameSize,
    int Padding = 2,
    Color BackgroundColor = default,
    bool IncludeNormalMap = false,
    bool TrimTransparent = false);
