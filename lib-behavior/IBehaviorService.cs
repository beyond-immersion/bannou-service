using BeyondImmersion.BannouService;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Behavior
{
    /// <summary>
    /// Service interface for Behavior API - generated from controller
    /// </summary>
    public interface IBehaviorService
    {
        /// <summary>
        /// CompileAbmlBehavior operation  
        /// </summary>
        Task<(StatusCodes, CompileBehaviorResponse?)> CompileAbmlBehaviorAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// CompileBehaviorStack operation  
        /// </summary>
        Task<(StatusCodes, CompileBehaviorResponse?)> CompileBehaviorStackAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// ValidateAbml operation  
        /// </summary>
        Task<(StatusCodes, ValidateAbmlResponse?)> ValidateAbmlAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetCachedBehavior operation  
        /// </summary>
        Task<(StatusCodes, CachedBehaviorResponse?)> GetCachedBehaviorAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// ResolveContextVariables operation  
        /// </summary>
        Task<(StatusCodes, ResolveContextResponse?)> ResolveContextVariablesAsync(/* TODO: Add parameters from schema */);

    }
}
