// =============================================================================
// Prerequisite Provider Factory Interface
// Enables dependency inversion for Quest's prerequisite validation pattern.
// Higher-layer services (L4) implement this to register prerequisite checkers.
// =============================================================================

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Factory interface for creating prerequisite validators for quest acceptance.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables the dependency inversion pattern for Quest prerequisite validation:
/// </para>
/// <list type="bullet">
///   <item>Quest (L2) defines this interface and accepts factories via DI collection</item>
///   <item>Higher-layer services (L4) implement factories and register them</item>
///   <item>Quest queries all registered factories to validate dynamic prerequisites</item>
/// </list>
/// <para>
/// <b>Built-in Prerequisites (L2)</b>: Quest handles these directly via service clients:
/// <c>quest_completed</c>, <c>currency</c>, <c>item</c>, <c>character_level</c>
/// </para>
/// <para>
/// <b>Dynamic Prerequisites (L4)</b>: Handled by provider factories:
/// <c>skill</c>, <c>magic</c>, <c>achievement</c>, <c>reputation</c>, etc.
/// </para>
/// <para>
/// <b>Example Implementation</b>:
/// </para>
/// <code>
/// public class SkillPrerequisiteProviderFactory : IPrerequisiteProviderFactory
/// {
///     public string ProviderName => "skill";
///
///     public async Task&lt;PrerequisiteResult&gt; CheckAsync(
///         Guid characterId, string code,
///         IReadOnlyDictionary&lt;string, object?&gt; parameters, CancellationToken ct)
///     {
///         var currentLevel = await _skillStore.GetLevelAsync(characterId, code, ct);
///         var requiredLevel = (int)(parameters.GetValueOrDefault("level") ?? 1);
///
///         return currentLevel >= requiredLevel
///             ? PrerequisiteResult.Success()
///             : PrerequisiteResult.Failure($"Requires {code} level {requiredLevel}", currentLevel, requiredLevel);
///     }
/// }
/// </code>
/// </remarks>
public interface IPrerequisiteProviderFactory
{
    /// <summary>
    /// Gets the prerequisite type this factory handles (e.g., "skill", "reputation").
    /// </summary>
    /// <remarks>
    /// This is matched against the prerequisite type in quest definitions.
    /// Multiple factories can handle the same type; all will be queried.
    /// </remarks>
    string ProviderName { get; }

    /// <summary>
    /// Checks if a character meets a prerequisite of this type.
    /// </summary>
    /// <param name="characterId">The character to check.</param>
    /// <param name="prerequisiteCode">The specific prerequisite code (e.g., skill name, faction code).</param>
    /// <param name="parameters">Additional parameters from the prerequisite definition.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating whether the prerequisite is satisfied.</returns>
    Task<PrerequisiteResult> CheckAsync(
        Guid characterId,
        string prerequisiteCode,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct);
}

/// <summary>
/// Result of a prerequisite check.
/// </summary>
/// <param name="Satisfied">Whether the prerequisite is met.</param>
/// <param name="FailureReason">Human-readable explanation if not satisfied.</param>
/// <param name="CurrentValue">The character's current value (for display).</param>
/// <param name="RequiredValue">The required value (for display).</param>
public record PrerequisiteResult(
    bool Satisfied,
    string? FailureReason = null,
    object? CurrentValue = null,
    object? RequiredValue = null
)
{
    /// <summary>
    /// Creates a successful prerequisite result.
    /// </summary>
    public static PrerequisiteResult Success() => new(true);

    /// <summary>
    /// Creates a failed prerequisite result with details.
    /// </summary>
    /// <param name="reason">Human-readable failure reason.</param>
    /// <param name="current">Current value the character has.</param>
    /// <param name="required">Required value for the prerequisite.</param>
    public static PrerequisiteResult Failure(string reason, object? current = null, object? required = null)
        => new(false, reason, current, required);
}
