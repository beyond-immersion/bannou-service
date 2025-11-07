using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Orchestrator;

/// <summary>
/// Service interface for Orchestrator API
/// </summary>
public interface IOrchestratorService : IDaprService
{
        /// <summary>
        /// GetInfrastructureHealth operation
        /// </summary>
        Task<(StatusCodes, InfrastructureHealthResponse?)> GetInfrastructureHealthAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetServicesHealth operation
        /// </summary>
        Task<(StatusCodes, ServiceHealthReport?)> GetServicesHealthAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// RunTests operation
        /// </summary>
        Task<(StatusCodes, TestExecutionResult?)> RunTestsAsync(TestExecutionRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// RestartService operation
        /// </summary>
        Task<(StatusCodes, ServiceRestartResult?)> RestartServiceAsync(ServiceRestartRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// ShouldRestartService operation
        /// </summary>
        Task<(StatusCodes, RestartRecommendation?)> ShouldRestartServiceAsync(ShouldRestartServiceRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
