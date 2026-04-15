namespace BeyondImmersion.Bannou.SpriteTheory.Metadata;

/// <summary>
/// An optional per-frame event marker within a sprite animation.
/// Used for hit frames, sound cues, visual effects, or other frame-synchronized game events.
/// </summary>
/// <param name="FrameIndex">Which frame this event occurs on (0-based within the animation).</param>
/// <param name="EventType">Event category identifier (e.g., "hit", "sound", "effect").</param>
/// <param name="EventData">Event-specific payload data (interpretation is game-defined).</param>
public record AnimationEvent(
    int FrameIndex,
    string EventType,
    string EventData);
