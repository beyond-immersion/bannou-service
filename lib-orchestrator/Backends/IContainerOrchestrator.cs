using BeyondImmersion.BannouService.Orchestrator;

namespace LibOrchestrator.Backends;

/// <summary>
/// Abstraction for container orchestration backends.
/// Implementations handle Docker Compose, Swarm, Kubernetes, and Portainer.
/// </summary>
public interface IContainerOrchestrator : IDisposable
{
    /// <summary>
    /// Gets the type of this backend.
    /// </summary>
    BackendType BackendType { get; }

    /// <summary>
    /// Checks if this backend is available and can be used.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if backend is available, along with diagnostic message</returns>
    Task<(bool Available, string Message)> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of a container/pod by its Dapr app name.
    /// </summary>
    /// <param name="appName">The Dapr app-id to look up</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Container status information</returns>
    Task<ContainerStatus> GetContainerStatusAsync(string appName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts a container/pod/service by its Dapr app name.
    /// </summary>
    /// <param name="appName">The Dapr app-id to restart</param>
    /// <param name="request">Restart request with priority and grace period</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the restart operation</returns>
    Task<ContainerRestartResponse> RestartContainerAsync(
        string appName,
        ContainerRestartRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all containers/pods managed by this backend.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of container statuses</returns>
    Task<IReadOnlyList<ContainerStatus>> ListContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets logs from a container/pod by its Dapr app name.
    /// </summary>
    /// <param name="appName">The Dapr app-id to get logs from</param>
    /// <param name="tail">Number of lines to retrieve (default 100)</param>
    /// <param name="since">Only return logs since this time</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Log output as string</returns>
    Task<string> GetContainerLogsAsync(
        string appName,
        int tail = 100,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deploys a service using the specified configuration.
    /// </summary>
    /// <param name="serviceName">The logical service name (e.g., "auth", "accounts")</param>
    /// <param name="appId">The Dapr app-id for this service instance</param>
    /// <param name="environment">Environment variables for the service</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the deployment operation</returns>
    Task<DeployServiceResult> DeployServiceAsync(
        string serviceName,
        string appId,
        Dictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tears down (stops and removes) a service by its Dapr app name.
    /// </summary>
    /// <param name="appName">The Dapr app-id to tear down</param>
    /// <param name="removeVolumes">Whether to remove associated volumes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the teardown operation</returns>
    Task<TeardownServiceResult> TeardownServiceAsync(
        string appName,
        bool removeVolumes = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scales a service to the specified number of instances.
    /// </summary>
    /// <param name="appName">The Dapr app-id to scale</param>
    /// <param name="replicas">Desired number of instances</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the scale operation</returns>
    Task<ScaleServiceResult> ScaleServiceAsync(
        string appName,
        int replicas,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a service deployment operation.
/// </summary>
public class DeployServiceResult
{
    /// <summary>Whether the deployment was successful.</summary>
    public bool Success { get; set; }

    /// <summary>The Dapr app-id of the deployed service.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Container ID of the deployed service (if applicable).</summary>
    public string? ContainerId { get; set; }

    /// <summary>Human-readable message describing the result.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Result of a service teardown operation.
/// </summary>
public class TeardownServiceResult
{
    /// <summary>Whether the teardown was successful.</summary>
    public bool Success { get; set; }

    /// <summary>The Dapr app-id of the torn down service.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Container IDs that were stopped.</summary>
    public List<string> StoppedContainers { get; set; } = new();

    /// <summary>Volumes that were removed (if requested).</summary>
    public List<string> RemovedVolumes { get; set; } = new();

    /// <summary>Human-readable message describing the result.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Result of a service scale operation.
/// </summary>
public class ScaleServiceResult
{
    /// <summary>Whether the scale operation was successful.</summary>
    public bool Success { get; set; }

    /// <summary>The Dapr app-id of the scaled service.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Previous number of instances.</summary>
    public int PreviousReplicas { get; set; }

    /// <summary>Current number of instances after scaling.</summary>
    public int CurrentReplicas { get; set; }

    /// <summary>Human-readable message describing the result.</summary>
    public string Message { get; set; } = string.Empty;
}
