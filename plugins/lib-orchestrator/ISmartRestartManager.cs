using BeyondImmersion.BannouService.Orchestrator;

namespace LibOrchestrator;

/// <summary>
/// Interface for managing intelligent service restart logic with Docker container lifecycle management.
/// Enables unit testing through mocking.
/// </summary>
public interface ISmartRestartManager : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Initialize Docker client for container management.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Restart a service container with optional environment updates.
    /// Implements smart restart logic based on health metrics.
    /// </summary>
    Task<ServiceRestartResult> RestartServiceAsync(ServiceRestartRequest request);
}
