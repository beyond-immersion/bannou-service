// =============================================================================
// Behavior Evaluator Interface
// Game integration interface for behavior model evaluation.
// =============================================================================

using BeyondImmersion.BannouService.Behavior.Intent;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Interface for evaluating behavior models per character.
/// Game implements this to integrate with its entity/component system.
/// </summary>
/// <remarks>
/// <para>
/// The SDK provides IntentMerger and BehaviorEvaluatorBase, but the game
/// is responsible for:
/// - Managing which models are active per character
/// - Providing the shared input state (game state)
/// - Applying the MergedIntent to animations/physics
/// - Calling EvaluateCharacter each frame
/// </para>
/// <para>
/// Example game integration:
/// <code>
/// public class GameBehaviorEvaluator : BehaviorEvaluatorBase
/// {
///     protected override BehaviorOutput? EvaluateModelType(
///         Guid characterId,
///         BehaviorModelType type,
///         ReadOnlySpan&lt;double&gt; inputState)
///     {
///         var character = GetCharacter(characterId);
///         if (!character.HasActiveModel(type))
///             return null;
///
///         var interpreter = ModelCache.GetInterpreter(characterId, type);
///         var output = new double[15];
///         interpreter.Evaluate(inputState, output);
///         return BehaviorOutput.FromOutputBuffer(output, interpreter.StringTable);
///     }
/// }
/// </code>
/// </para>
/// </remarks>
public interface IBehaviorEvaluator
{
    /// <summary>
    /// Evaluates all active behavior models for a character and returns the merged intent.
    /// </summary>
    /// <param name="characterId">Unique identifier for the character.</param>
    /// <param name="sharedInputState">Current game state as input values.</param>
    /// <returns>Merged intent from all active behavior models.</returns>
    /// <remarks>
    /// <para>
    /// The sharedInputState contains values like:
    /// - Character health, stamina, position
    /// - Nearby enemy positions and states
    /// - Current equipped items
    /// - Environment conditions
    /// </para>
    /// <para>
    /// All 4 model types (combat, movement, interaction, idle) are evaluated
    /// if active, then merged using urgency-based resolution.
    /// </para>
    /// </remarks>
    MergedIntent EvaluateCharacter(Guid characterId, ReadOnlySpan<double> sharedInputState);

    /// <summary>
    /// Invalidates a cached model when it needs to be reloaded.
    /// </summary>
    /// <param name="characterId">Unique identifier for the character.</param>
    /// <param name="type">The behavior model type to invalidate.</param>
    /// <remarks>
    /// <para>
    /// Call this when:
    /// - Character equips new weapon (combat model may change)
    /// - Character's class changes (all models may change)
    /// - Model is hot-reloaded during development
    /// </para>
    /// </remarks>
    void InvalidateModel(Guid characterId, BehaviorModelType type);

    /// <summary>
    /// Invalidates all cached models for a character.
    /// </summary>
    /// <param name="characterId">Unique identifier for the character.</param>
    /// <remarks>
    /// Call this when a character is respawned or their configuration changes significantly.
    /// </remarks>
    void InvalidateAllModels(Guid characterId);
}
