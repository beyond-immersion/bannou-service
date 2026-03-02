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
    /// Returns an internal <see cref="RestartOutcome"/> for the service to map to API response and status code.
    /// </summary>
    Task<RestartOutcome> RestartServiceAsync(ServiceRestartRequest request);
}
