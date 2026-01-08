// =============================================================================
// Control Source
// Identifies what is controlling an entity's Intent Channels.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior.Control;

/// <summary>
/// Identifies the source of control for an entity's Intent Channels.
/// </summary>
/// <remarks>
/// <para>
/// Control priority (highest to lowest):
/// 1. Cinematic - Full entity puppeting during cutscenes
/// 2. Player - Direct player commands
/// 3. Opportunity - Offered (becomes cinematic if accepted)
/// 4. Behavior - Autonomous AI behavior stack
/// </para>
/// </remarks>
public enum ControlSource
{
    /// <summary>
    /// Normal autonomous AI behavior stack output.
    /// Lowest priority - can be overridden by all other sources.
    /// </summary>
    Behavior = 0,

    /// <summary>
    /// Offered opportunity that hasn't been accepted yet.
    /// Higher than behavior but lower than player.
    /// </summary>
    Opportunity = 1,

    /// <summary>
    /// Direct player input commands.
    /// Can be overridden by cinematic or character willingness.
    /// </summary>
    Player = 2,

    /// <summary>
    /// Cutscene/cinematic puppeting.
    /// Highest priority - full control of entity.
    /// </summary>
    Cinematic = 3,
}

/// <summary>
/// Options for taking control of an entity.
/// </summary>
/// <param name="Source">The control source requesting control.</param>
/// <param name="CinematicId">ID of the cinematic taking control (if Cinematic source).</param>
/// <param name="AllowBehaviorInput">Channels where behavior can still contribute (optional).</param>
/// <param name="Duration">Expected duration of control (null = indefinite).</param>
public sealed record ControlOptions(
    ControlSource Source,
    string? CinematicId = null,
    IReadOnlySet<string>? AllowBehaviorInput = null,
    TimeSpan? Duration = null)
{
    /// <summary>
    /// Creates options for behavior control.
    /// </summary>
    public static ControlOptions ForBehavior()
        => new(ControlSource.Behavior);

    /// <summary>
    /// Creates options for player control.
    /// </summary>
    public static ControlOptions ForPlayer()
        => new(ControlSource.Player);

    /// <summary>
    /// Creates options for cinematic control.
    /// </summary>
    /// <param name="cinematicId">The cutscene identifier.</param>
    /// <param name="allowBehaviorChannels">Channels where behavior can still contribute.</param>
    /// <param name="duration">Expected duration.</param>
    public static ControlOptions ForCinematic(
        string cinematicId,
        IReadOnlySet<string>? allowBehaviorChannels = null,
        TimeSpan? duration = null)
        => new(ControlSource.Cinematic, cinematicId, allowBehaviorChannels, duration);

    /// <summary>
    /// Creates options for offered opportunity.
    /// </summary>
    public static ControlOptions ForOpportunity()
        => new(ControlSource.Opportunity);
}

/// <summary>
/// Style of control handoff when returning control.
/// </summary>
public enum HandoffStyle
{
    /// <summary>
    /// Immediate transfer - no transition period.
    /// </summary>
    Instant = 0,

    /// <summary>
    /// Smooth blend from cinematic to behavior over time.
    /// </summary>
    Blend = 1,

    /// <summary>
    /// Explicit action triggers handoff (e.g., return_control action).
    /// </summary>
    Explicit = 2,
}
