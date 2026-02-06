// =============================================================================
// Behavior Evaluator Base
// Abstract base class providing common evaluation and merge logic.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Intent;
using BeyondImmersion.Bannou.BehaviorCompiler.Runtime;

namespace BeyondImmersion.Bannou.Client.Behavior;

/// <summary>
/// Abstract base class for behavior evaluators providing common merge logic.
/// Game extends this with entity system specifics.
/// </summary>
/// <remarks>
/// <para>
/// This base class handles:
/// - Model cache management
/// - Intent merging for all 4 model types
/// - Output buffer allocation
/// </para>
/// <para>
/// Game-specific implementation provides:
/// - Which models are active per character
/// - How to get character-specific variant preferences
/// - How to populate the shared input state
/// </para>
/// <para>
/// Example game implementation:
/// <code>
/// public class GameBehaviorEvaluator : BehaviorEvaluatorBase
/// {
///     private readonly ICharacterManager _characters;
///
///     public GameBehaviorEvaluator(BehaviorModelCache cache) : base(cache)
///     {
///         _characters = GetCharacterManager();
///     }
///
///     protected override bool IsModelActive(Guid characterId, BehaviorModelType type)
///     {
///         var character = _characters.Get(characterId);
///         return character.ActiveBehaviors.Contains(type);
///     }
///
///     protected override string? GetPreferredVariant(Guid characterId, BehaviorModelType type)
///     {
///         var character = _characters.Get(characterId);
///         return type == BehaviorModelType.Combat
///             ? character.EquippedWeaponType
///             : null;
///     }
/// }
/// </code>
/// </para>
/// </remarks>
public abstract class BehaviorEvaluatorBase : IBehaviorEvaluator
{
    /// <summary>
    /// The model cache for interpreter lookup.
    /// </summary>
    protected readonly BehaviorModelCache ModelCache;

    /// <summary>
    /// The intent merger for combining outputs.
    /// </summary>
    protected readonly IntentMerger Merger = new();

    /// <summary>
    /// Pre-allocated output buffer to avoid per-frame allocation.
    /// Size is 15 slots: 5 channels * 2 slots + 5 extra slots for targets/positions.
    /// </summary>
    private readonly double[] _outputBuffer = new double[15];

    /// <summary>
    /// Creates a new behavior evaluator with the specified cache.
    /// </summary>
    /// <param name="cache">The model cache to use.</param>
    protected BehaviorEvaluatorBase(BehaviorModelCache cache)
    {
        ModelCache = cache;
    }

    /// <inheritdoc />
    public virtual MergedIntent EvaluateCharacter(Guid characterId, ReadOnlySpan<double> sharedInputState)
    {
        // Evaluate each model type
        var combat = EvaluateModelType(characterId, BehaviorModelType.Combat, sharedInputState);
        var movement = EvaluateModelType(characterId, BehaviorModelType.Movement, sharedInputState);
        var interaction = EvaluateModelType(characterId, BehaviorModelType.Interaction, sharedInputState);
        var idle = EvaluateModelType(characterId, BehaviorModelType.Idle, sharedInputState);

        // Merge all outputs
        return Merger.Merge(combat, movement, interaction, idle);
    }

    /// <inheritdoc />
    public void InvalidateModel(Guid characterId, BehaviorModelType type)
    {
        ModelCache.Invalidate(characterId, type);
    }

    /// <inheritdoc />
    public void InvalidateAllModels(Guid characterId)
    {
        ModelCache.InvalidateAll(characterId);
    }

    // =========================================================================
    // PROTECTED METHODS FOR GAME OVERRIDE
    // =========================================================================

    /// <summary>
    /// Evaluates a single model type for a character.
    /// </summary>
    /// <param name="characterId">Unique identifier for the character.</param>
    /// <param name="type">The behavior model type to evaluate.</param>
    /// <param name="inputState">Current game state as input values.</param>
    /// <returns>The behavior output, or null if the model is not active.</returns>
    protected virtual BehaviorOutput? EvaluateModelType(
        Guid characterId,
        BehaviorModelType type,
        ReadOnlySpan<double> inputState)
    {
        // Check if this model type is active for this character
        if (!IsModelActive(characterId, type))
            return null;

        // Get the preferred variant for this character/type
        var variant = GetPreferredVariant(characterId, type);

        // Get or create the interpreter
        var interpreter = ModelCache.GetInterpreter(characterId, type, variant);
        if (interpreter == null)
            return null;

        // Clear and evaluate
        Array.Clear(_outputBuffer);
        interpreter.Evaluate(inputState, _outputBuffer);

        // Extract structured output
        return BehaviorOutput.FromOutputBuffer(_outputBuffer, interpreter.StringTable);
    }

    /// <summary>
    /// Determines if a behavior model type is active for a character.
    /// </summary>
    /// <param name="characterId">Unique identifier for the character.</param>
    /// <param name="type">The behavior model type to check.</param>
    /// <returns>True if the model should be evaluated, false to skip.</returns>
    /// <remarks>
    /// <para>
    /// Override this to implement game-specific activation logic.
    /// For example:
    /// - Combat model only active when in combat
    /// - Interaction model only active near interactable objects
    /// - Idle model always active but with low urgency
    /// </para>
    /// <para>
    /// Default implementation returns true for all model types.
    /// </para>
    /// </remarks>
    protected virtual bool IsModelActive(Guid characterId, BehaviorModelType type)
    {
        // Default: all models are active
        return true;
    }

    /// <summary>
    /// Gets the preferred variant for a character/type combination.
    /// </summary>
    /// <param name="characterId">Unique identifier for the character.</param>
    /// <param name="type">The behavior model type.</param>
    /// <returns>Variant name, or null for default.</returns>
    /// <remarks>
    /// <para>
    /// Override this to implement game-specific variant selection.
    /// For example:
    /// - Combat variant based on equipped weapon type
    /// - Movement variant based on mount state
    /// - Interaction variant based on character class
    /// </para>
    /// <para>
    /// Default implementation returns null (uses default variant).
    /// </para>
    /// </remarks>
    protected virtual string? GetPreferredVariant(Guid characterId, BehaviorModelType type)
    {
        // Default: use default variant
        return null;
    }
}
