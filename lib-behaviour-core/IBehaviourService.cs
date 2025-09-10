using BeyondImmersion.BannouService.Services;
using System.Net;

namespace BeyondImmersion.BannouService.Behaviour;

/// <summary>
/// Service component responsible for ABML (Arcadia Behavior Markup Language) behavior management.
/// Handles YAML-based behavior compilation, stackable behavior sets, and GOAP integration.
/// </summary>
public interface IBehaviourService : IDaprService
{
    /// <summary>
    /// Compiles an ABML YAML behavior definition into executable behavior trees.
    /// Handles context variable resolution and cultural adaptations.
    /// </summary>
    /// <param name="abmlContent">Raw ABML YAML content to compile</param>
    /// <param name="characterContext">Character context for variable resolution</param>
    /// <returns>Compiled behavior response with executable behavior tree</returns>
    Task<ServiceResponse<CompileBehaviorResponse>> CompileAbmlBehavior(string abmlContent, CharacterContext? characterContext = null);

    /// <summary>
    /// Compiles multiple ABML behavior sets with priority-based merging.
    /// Handles cultural adaptations, profession specializations, and context resolution.
    /// </summary>
    /// <param name="behaviorSets">Array of behavior sets to compile together</param>
    /// <param name="characterContext">Character context for variable resolution</param>
    /// <returns>Compiled behavior response with merged behavior tree</returns>
    Task<ServiceResponse<CompileBehaviorStack>> CompileBehaviorStack(BehaviorSetDefinition[] behaviorSets, CharacterContext? characterContext = null);

    /// <summary>
    /// Validates ABML YAML against schema and checks for semantic correctness.
    /// Includes context variable validation and service dependency checking.
    /// </summary>
    /// <param name="abmlContent">Raw ABML YAML content to validate</param>
    /// <param name="strictMode">Enable strict validation mode with enhanced checking</param>
    /// <returns>Validation response with errors and warnings</returns>
    Task<ServiceResponse<ValidateAbmlResponse>> ValidateAbml(string abmlContent, bool strictMode = false);

    /// <summary>
    /// Retrieves a previously compiled behavior from the cache.
    /// Used for performance optimization in high-frequency behavior execution.
    /// </summary>
    /// <param name="behaviorId">Unique identifier for the cached behavior</param>
    /// <returns>Cached behavior response or null if not found</returns>
    Task<ServiceResponse<CachedBehaviorResponse?>> GetCachedBehavior(string behaviorId);

    /// <summary>
    /// Removes a behavior from the cache, forcing recompilation on next access.
    /// Used when behavior definitions are updated.
    /// </summary>
    /// <param name="behaviorId">Unique identifier for the cached behavior</param>
    /// <returns>Service response indicating success or failure</returns>
    Task<ServiceResponse> InvalidateCachedBehavior(string behaviorId);

    /// <summary>
    /// Resolves context variables in ABML definitions against character and world state.
    /// Used for dynamic behavior adaptation based on current game state.
    /// </summary>
    /// <param name="contextExpression">Context variable expression to resolve</param>
    /// <param name="characterContext">Character context for variable resolution</param>
    /// <returns>Resolved context response with the evaluated value</returns>
    Task<ServiceResponse<ResolveContextResponse>> ResolveContextVariables(string contextExpression, CharacterContext characterContext);

    /// <summary>
    /// Legacy method - adds a new behaviour tree to the system.
    /// Maintained for backward compatibility with existing JSON behavior system.
    /// </summary>
    [Obsolete("Use CompileAbmlBehavior for new YAML-based behaviors")]
    Task<ServiceResponse> AddBehaviourTree();
}
