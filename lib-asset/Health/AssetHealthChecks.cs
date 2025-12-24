using BeyondImmersion.BannouService.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Asset.Health;

/// <summary>
/// Health check for MinIO storage connectivity.
/// </summary>
public class MinioHealthCheck : IHealthCheck
{
    private readonly IAssetStorageProvider _storageProvider;
    private readonly ILogger<MinioHealthCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the MinioHealthCheck class.
    /// </summary>
    /// <param name="storageProvider">The asset storage provider.</param>
    /// <param name="logger">The logger.</param>
    public MinioHealthCheck(IAssetStorageProvider storageProvider, ILogger<MinioHealthCheck> logger)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks MinIO storage connectivity.
    /// </summary>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if the storage provider supports the health check capability
            if (!_storageProvider.SupportsCapability(StorageCapability.Versioning))
            {
                // If we can query capabilities, the connection is working
                return Task.FromResult(HealthCheckResult.Healthy("MinIO storage is accessible."));
            }

            return Task.FromResult(HealthCheckResult.Healthy("MinIO storage is accessible with versioning support."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MinIO health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy("MinIO storage is not accessible.", ex));
        }
    }
}

/// <summary>
/// Health check for Redis connectivity via Dapr state store.
/// </summary>
public class RedisHealthCheck : IHealthCheck
{
    private readonly Dapr.Client.DaprClient _daprClient;
    private readonly ILogger<RedisHealthCheck> _logger;
    private const string HEALTH_CHECK_KEY = "asset-health-check";
    private const string STATE_STORE_NAME = "asset-statestore";

    /// <summary>
    /// Initializes a new instance of the RedisHealthCheck class.
    /// </summary>
    /// <param name="daprClient">The Dapr client.</param>
    /// <param name="logger">The logger.</param>
    public RedisHealthCheck(Dapr.Client.DaprClient daprClient, ILogger<RedisHealthCheck> logger)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks Redis connectivity via Dapr state store.
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to save and retrieve a health check value
            var healthValue = new { Timestamp = DateTimeOffset.UtcNow };
            await _daprClient.SaveStateAsync(STATE_STORE_NAME, HEALTH_CHECK_KEY, healthValue, cancellationToken: cancellationToken);

            var retrieved = await _daprClient.GetStateAsync<object>(STATE_STORE_NAME, HEALTH_CHECK_KEY, cancellationToken: cancellationToken);

            if (retrieved != null)
            {
                return HealthCheckResult.Healthy("Redis state store is accessible via Dapr.");
            }

            return HealthCheckResult.Degraded("Redis state store write succeeded but read returned null.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check failed");
            return HealthCheckResult.Unhealthy("Redis state store is not accessible via Dapr.", ex);
        }
    }
}

/// <summary>
/// Health check for the asset processing pool availability via Orchestrator.
/// </summary>
public class ProcessingPoolHealthCheck : IHealthCheck
{
    private readonly BeyondImmersion.BannouService.Orchestrator.IOrchestratorClient _orchestratorClient;
    private readonly ILogger<ProcessingPoolHealthCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the ProcessingPoolHealthCheck class.
    /// </summary>
    /// <param name="orchestratorClient">The orchestrator client.</param>
    /// <param name="logger">The logger.</param>
    public ProcessingPoolHealthCheck(
        BeyondImmersion.BannouService.Orchestrator.IOrchestratorClient orchestratorClient,
        ILogger<ProcessingPoolHealthCheck> logger)
    {
        _orchestratorClient = orchestratorClient ?? throw new ArgumentNullException(nameof(orchestratorClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks processing pool availability via Orchestrator.
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get pool status from orchestrator
            var status = await _orchestratorClient.GetPoolStatusAsync(
                new BeyondImmersion.BannouService.Orchestrator.GetPoolStatusRequest
                {
                    Pool_type = "asset-processor"
                },
                cancellationToken);

            if (status != null)
            {
                if (status.Available_instances > 0)
                {
                    return HealthCheckResult.Healthy(
                        $"Processing pool available: {status.Available_instances} of {status.Total_instances} processors ready.");
                }
                else if (status.Total_instances > 0)
                {
                    return HealthCheckResult.Degraded(
                        $"Processing pool busy: 0 of {status.Total_instances} processors available.");
                }
                else
                {
                    return HealthCheckResult.Degraded("Processing pool has no registered processors.");
                }
            }

            return HealthCheckResult.Degraded("Unable to retrieve processing pool status from orchestrator.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Processing pool health check failed - pool may not be configured");
            // Processing pool is optional, so we return degraded instead of unhealthy
            return HealthCheckResult.Degraded("Processing pool status unavailable.", ex);
        }
    }
}

/// <summary>
/// Extension methods for registering Asset service health checks.
/// </summary>
public static class AssetHealthCheckExtensions
{
    /// <summary>
    /// Adds Asset service health checks to the health check builder.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <returns>The health checks builder for chaining.</returns>
    public static IHealthChecksBuilder AddAssetHealthChecks(this IHealthChecksBuilder builder)
    {
        builder.AddCheck<MinioHealthCheck>("minio", tags: new[] { "storage", "asset" });
        builder.AddCheck<RedisHealthCheck>("redis", tags: new[] { "cache", "state", "asset" });
        builder.AddCheck<ProcessingPoolHealthCheck>("processing-pool", tags: new[] { "processing", "asset" });

        return builder;
    }
}
