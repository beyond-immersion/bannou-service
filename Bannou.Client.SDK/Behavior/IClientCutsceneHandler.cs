// =============================================================================
// Client-Side Cutscene Handler
// Game engine integration point for receiving and executing cutscenes.
// =============================================================================

namespace BeyondImmersion.Bannou.Client.SDK.Behavior;

/// <summary>
/// Client-side cutscene handler for game engine integration.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface in your game engine to receive cutscene commands
/// from the behavior system and translate them into game actions.
/// </para>
/// <para>
/// The typical flow is:
/// </para>
/// <list type="number">
/// <item><see cref="OnCutsceneStartAsync"/> - Take control of entities, disable player input</item>
/// <item><see cref="ExecuteActionAsync"/> - Execute choreographed actions (walk_to, attack, look_at)</item>
/// <item><see cref="OnSyncPointReachedAsync"/> - Report when local entity reaches sync point</item>
/// <item><see cref="OnSyncPointReleasedAsync"/> - Resume execution when server releases sync</item>
/// <item><see cref="OnCutsceneEndAsync"/> - Return control, re-enable player input</item>
/// </list>
/// </remarks>
public interface IClientCutsceneHandler
{
    /// <summary>
    /// Called when a cutscene begins.
    /// </summary>
    /// <remarks>
    /// The game engine should:
    /// <list type="bullet">
    /// <item>Disable player input for controlled entities</item>
    /// <item>Prepare visual elements (letterbox, UI hide)</item>
    /// <item>Store current entity states for potential skip-to-end</item>
    /// </list>
    /// </remarks>
    /// <param name="sessionId">The cutscene session ID.</param>
    /// <param name="cinematicId">The cinematic being executed.</param>
    /// <param name="controlledEntities">Entities under cutscene control.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnCutsceneStartAsync(
        string sessionId,
        string cinematicId,
        IReadOnlyList<Guid> controlledEntities,
        CancellationToken ct = default);

    /// <summary>
    /// Called when a cutscene ends.
    /// </summary>
    /// <remarks>
    /// The game engine should:
    /// <list type="bullet">
    /// <item>Re-enable player input for controlled entities</item>
    /// <item>Remove visual elements (letterbox, etc.)</item>
    /// <item>Sync final entity states if necessary</item>
    /// </list>
    /// </remarks>
    /// <param name="sessionId">The cutscene session ID.</param>
    /// <param name="wasAborted">Whether the cutscene was aborted (vs completed normally).</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnCutsceneEndAsync(
        string sessionId,
        bool wasAborted,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a cinematic action on an entity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Common actions include:
    /// </para>
    /// <list type="bullet">
    /// <item><c>walk_to</c> - Move entity to position/mark</item>
    /// <item><c>attack</c> - Execute attack animation</item>
    /// <item><c>look_at</c> - Face toward target</item>
    /// <item><c>play_animation</c> - Play specific animation clip</item>
    /// <item><c>emote</c> - Display emotion/expression</item>
    /// <item><c>speak</c> - Show dialogue/speech</item>
    /// </list>
    /// <para>
    /// The game engine is responsible for interpreting action names and
    /// parameters according to its own animation/movement systems.
    /// </para>
    /// </remarks>
    /// <param name="entityId">The target entity.</param>
    /// <param name="action">The action name.</param>
    /// <param name="parameters">Action-specific parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ExecuteActionAsync(
        Guid entityId,
        string action,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct = default);

    /// <summary>
    /// Called when a local entity reaches a sync point.
    /// </summary>
    /// <remarks>
    /// The game engine should pause execution for this entity until
    /// <see cref="OnSyncPointReleasedAsync"/> is called for this sync point.
    /// </remarks>
    /// <param name="syncPointId">The sync point ID.</param>
    /// <param name="entityId">The entity that reached it.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnSyncPointReachedAsync(
        string syncPointId,
        Guid entityId,
        CancellationToken ct = default);

    /// <summary>
    /// Called when server releases a sync point (all participants reached).
    /// </summary>
    /// <remarks>
    /// All entities waiting at this sync point should resume execution.
    /// </remarks>
    /// <param name="syncPointId">The sync point ID.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnSyncPointReleasedAsync(
        string syncPointId,
        CancellationToken ct = default);

    /// <summary>
    /// Called when a camera action is requested.
    /// </summary>
    /// <remarks>
    /// Camera actions are separate from entity actions and typically include:
    /// <list type="bullet">
    /// <item><c>move_to</c> - Move camera to position</item>
    /// <item><c>track</c> - Follow/track an entity</item>
    /// <item><c>shake</c> - Apply camera shake effect</item>
    /// <item><c>zoom</c> - Zoom in/out</item>
    /// <item><c>cut_to</c> - Instant camera cut</item>
    /// </list>
    /// </remarks>
    /// <param name="action">The camera action name.</param>
    /// <param name="parameters">Action-specific parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ExecuteCameraActionAsync(
        string action,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct = default);
}
