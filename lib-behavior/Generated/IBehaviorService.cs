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
        /// CompileBehaviorStack operation
        /// </summary>
        Task<(StatusCodes, CompileBehaviorResponse?)> CompileBehaviorStackAsync(BehaviorStackRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// ValidateAbml operation
        /// </summary>
        Task<(StatusCodes, ValidateAbmlResponse?)> ValidateAbmlAsync(string body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetCachedBehavior operation
        /// </summary>
        Task<(StatusCodes, CachedBehaviorResponse?)> GetCachedBehaviorAsync(string behavior_id, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// ResolveContextVariables operation
        /// </summary>
        Task<(StatusCodes, ResolveContextResponse?)> ResolveContextVariablesAsync(ResolveContextRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
