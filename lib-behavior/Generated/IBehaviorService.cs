using BeyondImmersion.BannouService;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Service interface for Behavior API - generated from controller
/// </summary>
public interface IBehaviorService
{
    /// <summary>
    /// CompileAbmlBehavior operation
    /// </summary>
    Task<(StatusCodes, CompileBehaviorResponse?)> CompileAbmlBehaviorAsync(string body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Compile stackable behavior sets
    /// </summary>
    /// <remarks>
    /// Compiles multiple ABML behavior sets with priority-based merging.
    /// <br/>Handles cultural adaptations, profession specializations, and context resolution.
    /// </remarks>
    /// <returns>Behavior stack compiled successfully</returns>
    public abstract Task<Microsoft.AspNetCore.Mvc.ActionResult<CompileBehaviorResponse>> CompileBehaviorStack(BehaviorStackRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Validate ABML definition
    /// </summary>
    /// <remarks>
    /// Validates ABML YAML against schema and checks for semantic correctness.
    /// <br/>Includes context variable validation and service dependency checking.
    /// </remarks>
    /// <returns>Validation completed</returns>
    public abstract Task<Microsoft.AspNetCore.Mvc.ActionResult<ValidateAbmlResponse>> ValidateAbml(string body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetCachedBehavior operation
    /// </summary>
    Task<(StatusCodes, CachedBehaviorResponse?)> GetCachedBehaviorAsync(string behavior_id, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ResolveContextVariables operation
    /// </summary>
    Task<(StatusCodes, ResolveContextResponse?)> ResolveContextVariablesAsync(ResolveContextRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
