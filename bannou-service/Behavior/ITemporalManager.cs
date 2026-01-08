// =============================================================================
// Temporal Manager Interface
// Game engine integration point for temporal desync (THE DREAM).
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Manages temporal desync for multiplayer cutscenes.
/// </summary>
/// <remarks>
/// <para>
/// This interface defines the contract for THE DREAM's "three-version solution"
/// where participants experience time dilation during QTE sequences while
/// spectators see real-time playback.
/// </para>
/// <para>
/// Key concepts:
/// </para>
/// <list type="bullet">
/// <item><b>Time Budget</b>: Participants "earn" slow-mo time by fast-forwarding through setup</item>
/// <item><b>Time Dilation</b>: Applied during QTE sections to extend reaction windows</item>
/// <item><b>Spectator View</b>: Unaffected by time dilation (sees real-time)</item>
/// </list>
/// <para>
/// Implementation note: This is a game engine concern. The ABML/behavior system
/// marks time-gain and QTE sections; the game engine implements the actual
/// time dilation. This interface provides the integration point.
/// </para>
/// </remarks>
public interface ITemporalManager
{
    /// <summary>
    /// Adds time budget to a participant.
    /// </summary>
    /// <remarks>
    /// Called when a participant fast-forwards through a time-gain section
    /// (e.g., walking to the arena entrance). The "saved" time is added to
    /// their slow-mo budget for later QTE sections.
    /// </remarks>
    /// <param name="participant">The participant entity ID.</param>
    /// <param name="amount">Amount of time budget to add.</param>
    void AddTimeBudget(Guid participant, TimeSpan amount);

    /// <summary>
    /// Spends time budget for a participant.
    /// </summary>
    /// <remarks>
    /// Called when a participant uses slow-mo during a QTE section.
    /// The amount spent is deducted from their budget.
    /// </remarks>
    /// <param name="participant">The participant entity ID.</param>
    /// <param name="amount">Amount of time budget to spend.</param>
    void SpendTimeBudget(Guid participant, TimeSpan amount);

    /// <summary>
    /// Gets the current time budget for a participant.
    /// </summary>
    /// <param name="participant">The participant entity ID.</param>
    /// <returns>The remaining time budget.</returns>
    TimeSpan GetTimeBudget(Guid participant);

    /// <summary>
    /// Gets the current time dilation for an entity.
    /// </summary>
    /// <remarks>
    /// <para>Time dilation values:</para>
    /// <list type="bullet">
    /// <item>1.0 = Normal time (no dilation)</item>
    /// <item>0.1 = 10x slow-mo (participant has budget, in QTE section)</item>
    /// <item>2.0 = 2x fast-forward (participant in time-gain section)</item>
    /// </list>
    /// <para>
    /// Spectators always return 1.0 (real-time playback).
    /// </para>
    /// </remarks>
    /// <param name="entity">The entity ID.</param>
    /// <returns>Time dilation multiplier (1.0 = normal).</returns>
    float GetTimeDilation(Guid entity);

    /// <summary>
    /// Checks if an entity is currently a participant in a temporal session.
    /// </summary>
    /// <param name="entity">The entity ID.</param>
    /// <returns>True if the entity is a participant (can experience dilation).</returns>
    bool IsParticipant(Guid entity);

    /// <summary>
    /// Registers an entity as a participant in temporal desync.
    /// </summary>
    /// <param name="entity">The entity ID.</param>
    /// <param name="sessionId">The cutscene session ID.</param>
    void RegisterParticipant(Guid entity, string sessionId);

    /// <summary>
    /// Unregisters an entity from temporal desync.
    /// </summary>
    /// <param name="entity">The entity ID.</param>
    void UnregisterParticipant(Guid entity);

    /// <summary>
    /// Enters a time-gain section for a participant.
    /// </summary>
    /// <remarks>
    /// During time-gain sections, participants fast-forward through content
    /// (e.g., walking sequences) to "earn" slow-mo budget for later QTEs.
    /// </remarks>
    /// <param name="participant">The participant entity ID.</param>
    /// <param name="maxGain">Maximum time that can be gained in this section.</param>
    void EnterTimeGainSection(Guid participant, TimeSpan maxGain);

    /// <summary>
    /// Exits a time-gain section for a participant.
    /// </summary>
    /// <param name="participant">The participant entity ID.</param>
    /// <returns>The actual time gained.</returns>
    TimeSpan ExitTimeGainSection(Guid participant);

    /// <summary>
    /// Enters a QTE section where time budget can be spent.
    /// </summary>
    /// <param name="participant">The participant entity ID.</param>
    /// <param name="realTimeWindow">The actual window duration without slow-mo.</param>
    /// <param name="maxSlowMo">Maximum slow-mo multiplier (e.g., 0.1 for 10x).</param>
    void EnterQteSection(Guid participant, TimeSpan realTimeWindow, float maxSlowMo);

    /// <summary>
    /// Exits a QTE section.
    /// </summary>
    /// <param name="participant">The participant entity ID.</param>
    void ExitQteSection(Guid participant);
}

/// <summary>
/// Null implementation of temporal manager for when temporal desync is disabled.
/// </summary>
/// <remarks>
/// Use this implementation when:
/// <list type="bullet">
/// <item>Single-player mode (no temporal desync needed)</item>
/// <item>Game engine doesn't support temporal desync</item>
/// <item>Testing without temporal features</item>
/// </list>
/// All time dilations return 1.0 (normal time).
/// </remarks>
public sealed class NullTemporalManager : ITemporalManager
{
    /// <summary>
    /// Shared instance for convenience.
    /// </summary>
    public static readonly NullTemporalManager Instance = new();

    /// <inheritdoc/>
    public void AddTimeBudget(Guid participant, TimeSpan amount) { }

    /// <inheritdoc/>
    public void SpendTimeBudget(Guid participant, TimeSpan amount) { }

    /// <inheritdoc/>
    public TimeSpan GetTimeBudget(Guid participant) => TimeSpan.Zero;

    /// <inheritdoc/>
    public float GetTimeDilation(Guid entity) => 1.0f;

    /// <inheritdoc/>
    public bool IsParticipant(Guid entity) => false;

    /// <inheritdoc/>
    public void RegisterParticipant(Guid entity, string sessionId) { }

    /// <inheritdoc/>
    public void UnregisterParticipant(Guid entity) { }

    /// <inheritdoc/>
    public void EnterTimeGainSection(Guid participant, TimeSpan maxGain) { }

    /// <inheritdoc/>
    public TimeSpan ExitTimeGainSection(Guid participant) => TimeSpan.Zero;

    /// <inheritdoc/>
    public void EnterQteSection(Guid participant, TimeSpan realTimeWindow, float maxSlowMo) { }

    /// <inheritdoc/>
    public void ExitQteSection(Guid participant) { }
}
