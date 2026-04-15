namespace BeyondImmersion.Bannou.SpriteTheory;

/// <summary>
/// Camera projection mode for sprite capture.
/// </summary>
public enum ProjectionType
{
    /// <summary>
    /// Orthographic (parallel) projection — no perspective foreshortening.
    /// Standard for sprite capture; preserves consistent sizing across depth.
    /// </summary>
    Orthographic,

    /// <summary>
    /// Perspective projection — objects closer to the camera appear larger.
    /// Rarely used for sprite capture; may cause inconsistent frame sizing.
    /// </summary>
    Perspective
}

/// <summary>
/// Animation playback loop behavior for sprite animations.
/// </summary>
public enum LoopMode
{
    /// <summary>Play once and stop on the last frame.</summary>
    None,

    /// <summary>Loop from the beginning after reaching the last frame.</summary>
    Loop,

    /// <summary>Alternate between forward and reverse playback.</summary>
    PingPong
}

/// <summary>
/// Flip axis for mirror frame generation.
/// </summary>
public enum MirrorAxis
{
    /// <summary>Flip horizontally (left-right). Standard for character facing direction mirrors.</summary>
    Horizontal,

    /// <summary>Flip vertically (top-bottom). Rare; used for specialized projection effects.</summary>
    Vertical
}
