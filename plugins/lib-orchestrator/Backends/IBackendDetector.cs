using BeyondImmersion.BannouService.Orchestrator;

namespace LibOrchestrator.Backends;

/// <summary>
/// Interface for detecting available container orchestration backends.
/// Enables unit testing through mocking.
/// </summary>
public interface IBackendDetector
{
    /// <summary>
    /// Detects all available backends and returns information about each.
    /// </summary>
    Task<BackendsResponse> DetectBackendsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an orchestrator instance for the specified backend type.
    /// </summary>
    IContainerOrchestrator CreateOrchestrator(BackendType backendType);

    /// <summary>
    /// Creates an orchestrator for the best available backend.
    /// </summary>
    Task<IContainerOrchestrator> CreateBestOrchestratorAsync(CancellationToken cancellationToken = default);
}
