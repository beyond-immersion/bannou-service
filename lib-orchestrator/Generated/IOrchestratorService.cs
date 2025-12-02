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
        /// RestartService operation
        /// </summary>
        Task<(StatusCodes, ServiceRestartResult?)> RestartServiceAsync(ServiceRestartRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// ShouldRestartService operation
        /// </summary>
        Task<(StatusCodes, RestartRecommendation?)> ShouldRestartServiceAsync(ShouldRestartServiceRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetBackends operation
        /// </summary>
        Task<(StatusCodes, BackendsResponse?)> GetBackendsAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetPresets operation
        /// </summary>
        Task<(StatusCodes, PresetsResponse?)> GetPresetsAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Deploy operation
        /// </summary>
        Task<(StatusCodes, DeployResponse?)> DeployAsync(DeployRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetStatus operation
        /// </summary>
        Task<(StatusCodes, EnvironmentStatus?)> GetStatusAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Teardown operation
        /// </summary>
        Task<(StatusCodes, TeardownResponse?)> TeardownAsync(TeardownRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Clean operation
        /// </summary>
        Task<(StatusCodes, CleanResponse?)> CleanAsync(CleanRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetLogs operation
        /// </summary>
        Task<(StatusCodes, LogsResponse?)> GetLogsAsync(string? service, string? container, string? since, string? until, int? tail = 100, bool? follow = false, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdateTopology operation
        /// </summary>
        Task<(StatusCodes, TopologyUpdateResponse?)> UpdateTopologyAsync(TopologyUpdateRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// RequestContainerRestart operation
        /// </summary>
        Task<(StatusCodes, ContainerRestartResponse?)> RequestContainerRestartAsync(string appName, ContainerRestartRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetContainerStatus operation
        /// </summary>
        Task<(StatusCodes, ContainerStatus?)> GetContainerStatusAsync(string appName, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// RollbackConfiguration operation
        /// </summary>
        Task<(StatusCodes, ConfigRollbackResponse?)> RollbackConfigurationAsync(ConfigRollbackRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetConfigVersion operation
        /// </summary>
        Task<(StatusCodes, ConfigVersionResponse?)> GetConfigVersionAsync(CancellationToken cancellationToken = default(CancellationToken));

}
