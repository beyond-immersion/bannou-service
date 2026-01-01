using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Service interface for Behavior API
/// </summary>
public partial interface IBehaviorService : IBannouService
{
    /// <summary>
    /// CompileAbmlBehavior operation
    /// </summary>
    Task<(StatusCodes, CompileBehaviorResponse?)> CompileAbmlBehaviorAsync(CompileBehaviorRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// CompileBehaviorStack operation
    /// </summary>
    Task<(StatusCodes, CompileBehaviorResponse?)> CompileBehaviorStackAsync(BehaviorStackRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ValidateAbml operation
    /// </summary>
    Task<(StatusCodes, ValidateAbmlResponse?)> ValidateAbmlAsync(ValidateAbmlRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetCachedBehavior operation
    /// </summary>
    Task<(StatusCodes, CachedBehaviorResponse?)> GetCachedBehaviorAsync(GetCachedBehaviorRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// InvalidateCachedBehavior operation
    /// </summary>
    Task<StatusCodes> InvalidateCachedBehaviorAsync(InvalidateCacheRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ResolveContextVariables operation
    /// </summary>
    Task<(StatusCodes, ResolveContextResponse?)> ResolveContextVariablesAsync(ResolveContextRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GenerateGoapPlan operation
    /// </summary>
    Task<(StatusCodes, GoapPlanResponse?)> GenerateGoapPlanAsync(GoapPlanRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ValidateGoapPlan operation
    /// </summary>
    Task<(StatusCodes, ValidateGoapPlanResponse?)> ValidateGoapPlanAsync(ValidateGoapPlanRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
