using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Service interface for Behavior API
/// </summary>
public interface IBehaviorService : IDaprService
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
    Task<(StatusCodes, object?)> InvalidateCachedBehaviorAsync(InvalidateCacheRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ResolveContextVariables operation
    /// </summary>
    Task<(StatusCodes, ResolveContextResponse?)> ResolveContextVariablesAsync(ResolveContextRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
