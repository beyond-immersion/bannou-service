using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Behaviour;

/// <summary>
/// Service interface for ABML behavior operations.
/// Maps directly to the schema-generated controller methods.
/// </summary>
public interface IBehaviorService
{
    /// <summary>
    /// Compiles a YAML-based ABML behavior definition into executable behavior trees.
    /// </summary>
    Task<ActionResult<CompileBehaviorResponse>> CompileAbmlBehavior(string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compiles multiple ABML behavior sets with priority-based merging.
    /// </summary>
    Task<ActionResult<CompileBehaviorResponse>> CompileBehaviorStack(BehaviorStackRequest body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates ABML YAML against schema and checks for semantic correctness.
    /// </summary>
    Task<ActionResult<ValidateAbmlResponse>> ValidateAbml(string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a previously compiled behavior from the cache.
    /// </summary>
    Task<ActionResult<CachedBehaviorResponse>> GetCachedBehavior(string behavior_id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a behavior from the cache, forcing recompilation on next access.
    /// </summary>
    Task<IActionResult> InvalidateCachedBehavior(string behavior_id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves context variables in ABML definitions against character and world state.
    /// </summary>
    Task<ActionResult<ResolveContextResponse>> ResolveContextVariables(ResolveContextRequest body, CancellationToken cancellationToken = default);
}