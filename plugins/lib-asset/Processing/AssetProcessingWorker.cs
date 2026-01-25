// =============================================================================
// Asset Processing Worker
// Background worker that processes asset jobs when running in processor node mode.
// Self-registers, emits heartbeats, and self-terminates after idle timeout.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Asset.Models;
using BeyondImmersion.BannouService.Asset.Pool;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Asset.Processing;

/// <summary>
/// Background worker that processes asset jobs when running in processor node mode.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deployment:</b> Runs when ProcessorNodeId is set (processor node mode).
/// </para>
/// <para>
/// <b>Lifecycle:</b>
/// <list type="bullet">
/// <item>On startup: Registers with pool manager (writes state to Redis)</item>
/// <item>Periodically: Emits heartbeat updating load and idle count</item>
/// <item>On idle timeout: Self-terminates (cleans up state, exits process)</item>
/// <item>On SIGTERM: Graceful shutdown (drains jobs, cleans up state)</item>
/// </list>
/// </para>
/// </remarks>
public sealed class AssetProcessingWorker : BackgroundService
{
    private readonly AssetProcessorRegistry _processorRegistry;
    private readonly IStateStore<AssetMetadata> _stateStore;
    private readonly IAssetStorageProvider _storageProvider;
    private readonly IOrchestratorClient _orchestratorClient;
    private readonly IAssetProcessorPoolManager _poolManager;
    private readonly IMessageBus _messageBus;
    private readonly AssetServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<AssetProcessingWorker> _logger;

    // Job tracking for load reporting
    private int _currentJobCount;
    private readonly object _jobCountLock = new();

    /// <summary>
    /// Creates a new AssetProcessingWorker.
    /// </summary>
    public AssetProcessingWorker(
        AssetProcessorRegistry processorRegistry,
        IStateStoreFactory stateStoreFactory,
        IAssetStorageProvider storageProvider,
        IOrchestratorClient orchestratorClient,
        IAssetProcessorPoolManager poolManager,
        IMessageBus messageBus,
        AssetServiceConfiguration configuration,
        AppConfiguration appConfiguration,
        IHostApplicationLifetime applicationLifetime,
        ILogger<AssetProcessingWorker> logger)
    {
        _processorRegistry = processorRegistry;
        _stateStore = stateStoreFactory.GetStore<AssetMetadata>(StateStoreDefinitions.Asset);
        _storageProvider = storageProvider;
        _orchestratorClient = orchestratorClient;
        _poolManager = poolManager;
        _messageBus = messageBus;
        _configuration = configuration;
        _appConfiguration = appConfiguration;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    /// <summary>
    /// Gets whether this worker is running as a processor node (has ProcessorNodeId configured).
    /// </summary>
    private bool IsProcessorNodeMode => !string.IsNullOrEmpty(_configuration.ProcessorNodeId);

    /// <summary>
    /// Gets the pool type this processor handles.
    /// Uses WorkerPool override if configured, otherwise falls back to ProcessingPoolType.
    /// </summary>
    private string PoolType => !string.IsNullOrEmpty(_configuration.WorkerPool)
        ? _configuration.WorkerPool
        : _configuration.ProcessingPoolType;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check processing mode from configuration - exit early if "api" mode (HTTP only)
        if (_configuration.ProcessingMode == ProcessingMode.Api)
        {
            _logger.LogInformation(
                "Asset processing worker disabled (ProcessingMode={Mode})", _configuration.ProcessingMode);
            return;
        }

        if (!IsProcessorNodeMode)
        {
            // Not running as processor node - just keep alive for local processing
            _logger.LogInformation(
                "Asset processing worker started in local mode (ProcessingMode={Mode}, no ProcessorNodeId configured)",
                _configuration.ProcessingMode);

            await KeepAliveAsync(stoppingToken);
            return;
        }

        // IMPLEMENTATION TENETS: Fail fast if required config is missing
        // IsProcessorNodeMode is true, so ProcessorNodeId must be set - validate explicitly
        var nodeId = _configuration.ProcessorNodeId
            ?? throw new InvalidOperationException(
                "ASSET_PROCESSOR_NODE_ID is required when running in processor node mode");

        var appId = _appConfiguration.EffectiveAppId;

        _logger.LogInformation(
            "Starting asset processing worker as processor node: NodeId={NodeId}, AppId={AppId}, PoolType={PoolType}",
            nodeId, appId, PoolType);

        // Register with pool manager
        var registered = await RegisterWithPoolAsync(nodeId, appId, stoppingToken);
        if (!registered)
        {
            _logger.LogError("Failed to register processor node {NodeId}, shutting down", nodeId);
            return;
        }

        // Start heartbeat loop
        var heartbeatTask = RunHeartbeatLoopAsync(nodeId, stoppingToken);

        _logger.LogInformation("Asset processor node {NodeId} started, waiting for jobs", nodeId);

        // Wait for either cancellation or idle timeout triggered shutdown
        try
        {
            await heartbeatTask;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }

        // Graceful shutdown
        await ShutdownAsync(nodeId);
    }

    /// <summary>
    /// Keeps the worker alive when not running as a processor node.
    /// </summary>
    private async Task KeepAliveAsync(CancellationToken stoppingToken)
    {
        var checkInterval = TimeSpan.FromSeconds(_configuration.ProcessingQueueCheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Asset processing worker stopped");
    }

    #region Pool Registration

    /// <summary>
    /// Registers this processor node with the pool manager.
    /// </summary>
    private async Task<bool> RegisterWithPoolAsync(
        string nodeId,
        string appId,
        CancellationToken cancellationToken)
    {
        try
        {
            var capacity = _configuration.ProcessorMaxConcurrentJobs;

            await _poolManager.RegisterNodeAsync(
                nodeId,
                appId,
                PoolType,
                capacity,
                cancellationToken);

            _logger.LogInformation(
                "Registered processor node {NodeId} with pool {PoolType} (capacity: {Capacity})",
                nodeId, PoolType, capacity);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register processor node {NodeId}", nodeId);
            return false;
        }
    }

    #endregion

    #region Heartbeat Loop

    /// <summary>
    /// Runs the heartbeat loop, updating state and checking for idle timeout.
    /// </summary>
    private async Task RunHeartbeatLoopAsync(string nodeId, CancellationToken stoppingToken)
    {
        var heartbeatInterval = TimeSpan.FromSeconds(_configuration.ProcessorHeartbeatIntervalSeconds);
        var idleTimeoutSeconds = _configuration.ProcessorIdleTimeoutSeconds;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var currentLoad = GetCurrentJobCount();

                // Update heartbeat in pool manager
                var state = await _poolManager.UpdateHeartbeatAsync(
                    nodeId,
                    PoolType,
                    currentLoad,
                    stoppingToken);

                if (state == null)
                {
                    // Node was removed externally, re-register
                    _logger.LogWarning(
                        "Processor node {NodeId} state not found, re-registering",
                        nodeId);

                    await RegisterWithPoolAsync(nodeId, _appConfiguration.EffectiveAppId, stoppingToken);
                }
                else
                {
                    // Check for idle timeout
                    if (idleTimeoutSeconds > 0 && ShouldShutdownDueToIdleTimeout(state, heartbeatInterval, idleTimeoutSeconds))
                    {
                        _logger.LogInformation(
                            "Processor node {NodeId} idle timeout reached ({IdleCount} heartbeats with zero load), initiating shutdown",
                            nodeId, state.IdleHeartbeatCount);

                        // Trigger application shutdown
                        _applicationLifetime.StopApplication();
                        return;
                    }

                    _logger.LogDebug(
                        "Heartbeat: NodeId={NodeId}, Load={Load}/{Capacity}, IdleCount={IdleCount}",
                        nodeId, currentLoad, state.Capacity, state.IdleHeartbeatCount);
                }

                await Task.Delay(heartbeatInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in heartbeat loop for node {NodeId}", nodeId);

                // Wait before retrying
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_configuration.ProcessingBatchIntervalSeconds), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Determines if the node should shut down due to idle timeout.
    /// </summary>
    private static bool ShouldShutdownDueToIdleTimeout(
        ProcessorNodeState state,
        TimeSpan heartbeatInterval,
        int idleTimeoutSeconds)
    {
        // Calculate total idle time based on heartbeat count
        var totalIdleSeconds = state.IdleHeartbeatCount * heartbeatInterval.TotalSeconds;
        return totalIdleSeconds >= idleTimeoutSeconds;
    }

    #endregion

    #region Graceful Shutdown

    /// <summary>
    /// Performs graceful shutdown: marks as draining, waits for jobs, cleans up state.
    /// </summary>
    private async Task ShutdownAsync(string nodeId)
    {
        _logger.LogInformation("Starting graceful shutdown for processor node {NodeId}", nodeId);

        try
        {
            // Mark as draining
            await _poolManager.SetDrainingAsync(nodeId, PoolType);

            // Wait for current jobs to complete (with timeout)
            var drainTimeout = TimeSpan.FromMinutes(_configuration.ShutdownDrainTimeoutMinutes);
            var drainStart = DateTimeOffset.UtcNow;

            while (GetCurrentJobCount() > 0)
            {
                if (DateTimeOffset.UtcNow - drainStart > drainTimeout)
                {
                    _logger.LogWarning(
                        "Drain timeout reached with {JobCount} jobs still running, forcing shutdown",
                        GetCurrentJobCount());
                    break;
                }

                _logger.LogInformation(
                    "Waiting for {JobCount} jobs to complete before shutdown",
                    GetCurrentJobCount());

                await Task.Delay(TimeSpan.FromSeconds(_configuration.ShutdownDrainIntervalSeconds));
            }

            // Remove node state
            await _poolManager.RemoveNodeAsync(nodeId, PoolType);

            _logger.LogInformation("Graceful shutdown complete for processor node {NodeId}", nodeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during graceful shutdown for node {NodeId}", nodeId);
        }
    }

    #endregion

    #region Job Load Tracking

    /// <summary>
    /// Gets the current number of jobs being processed.
    /// </summary>
    private int GetCurrentJobCount()
    {
        lock (_jobCountLock)
        {
            return _currentJobCount;
        }
    }

    /// <summary>
    /// Increments the job counter when starting a job.
    /// </summary>
    private void IncrementJobCount()
    {
        lock (_jobCountLock)
        {
            _currentJobCount++;
        }
    }

    /// <summary>
    /// Decrements the job counter when completing a job.
    /// </summary>
    private void DecrementJobCount()
    {
        lock (_jobCountLock)
        {
            _currentJobCount = Math.Max(0, _currentJobCount - 1);
        }
    }

    #endregion

    #region Job Processing

    /// <summary>
    /// Handles a processing job received via pub/sub.
    /// Called by the event controller.
    /// </summary>
    public async Task<bool> HandleProcessingJobAsync(
        AssetProcessingJobEvent job,
        CancellationToken cancellationToken = default)
    {
        var processor = _processorRegistry.GetProcessorByPoolType(job.PoolType);
        if (processor == null)
        {
            _logger.LogError(
                "No processor found for pool type {PoolType}",
                job.PoolType);
            return false;
        }

        _logger.LogInformation(
            "Processing job {JobId} for asset {AssetId} with processor {PoolType}",
            job.JobId,
            job.AssetId,
            job.PoolType);

        // Track job for load reporting
        IncrementJobCount();

        try
        {
            // Create processing context
            var context = new AssetProcessingContext
            {
                AssetId = job.AssetId.ToString(),
                StorageKey = job.StorageKey,
                ContentType = job.ContentType,
                SizeBytes = job.SizeBytes,
                Filename = job.Filename,
                Owner = job.Owner,
                RealmId = job.RealmId?.ToString(),
                Tags = job.Tags,
                ProcessingOptions = job.ProcessingOptions
            };

            // Process the asset
            var result = await processor.ProcessAsync(context, cancellationToken);

            // Update asset metadata with processing results
            await UpdateAssetMetadataAsync(job.AssetId.ToString(), result, cancellationToken);

            // Release the processor lease
            if (job.LeaseId != Guid.Empty)
            {
                await ReleaseProcessorLeaseAsync(job.LeaseId, result.Success, cancellationToken);
            }

            // Emit completion or failure event
            await EmitProcessingResultEventAsync(job, result, cancellationToken);

            _logger.LogInformation(
                "Completed job {JobId} for asset {AssetId}: Success={Success}, Duration={Duration}ms",
                job.JobId,
                job.AssetId,
                result.Success,
                result.ProcessingTimeMs);

            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process job {JobId} for asset {AssetId}",
                job.JobId,
                job.AssetId);

            // Release the processor lease on failure
            if (job.LeaseId != Guid.Empty)
            {
                await ReleaseProcessorLeaseAsync(job.LeaseId, false, cancellationToken);
            }

            // Emit failure event
            await EmitProcessingFailedEventAsync(job, ex.Message, cancellationToken);

            return false;
        }
        finally
        {
            // Always decrement job count when done
            DecrementJobCount();
        }
    }

    private async Task UpdateAssetMetadataAsync(
        string assetId,
        AssetProcessingResult result,
        CancellationToken cancellationToken)
    {
        var stateKey = $"{_configuration.AssetKeyPrefix}{assetId}";

        try
        {
            var existingMetadata = await _stateStore.GetAsync(stateKey, cancellationToken);

            if (existingMetadata == null)
            {
                _logger.LogWarning("Asset metadata not found for {AssetId}", assetId);
                return;
            }

            // Update the metadata with processing results
            existingMetadata.ProcessingStatus = result.Success
                ? ProcessingStatus.Complete
                : ProcessingStatus.Failed;
            existingMetadata.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStore.SaveAsync(stateKey, existingMetadata, null, cancellationToken);

            _logger.LogDebug(
                "Updated metadata for asset {AssetId}: Status={Status}",
                assetId,
                existingMetadata.ProcessingStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to update metadata for asset {AssetId}",
                assetId);
        }
    }

    private async Task ReleaseProcessorLeaseAsync(
        Guid leaseId,
        bool success,
        CancellationToken cancellationToken)
    {
        try
        {
            await _orchestratorClient.ReleaseProcessorAsync(
                new ReleaseProcessorRequest
                {
                    LeaseId = leaseId,
                    Success = success
                },
                cancellationToken);

            _logger.LogDebug(
                "Released processor lease {LeaseId}, success={Success}",
                leaseId,
                success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to release processor lease {LeaseId}",
                leaseId);
        }
    }

    private async Task EmitProcessingResultEventAsync(
        AssetProcessingJobEvent job,
        AssetProcessingResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            // Publish service-level event via message bus (not client event)
            var processingEvent = new AssetProcessingCompletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AssetId = job.AssetId.ToString(),
                ProcessingType = MapProcessingType(job.ContentType),
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
                Outputs = result.ProcessedStorageKey != null
                    ? new List<ProcessingOutput>
                    {
                        new ProcessingOutput
                        {
                            OutputType = "processed",
                            Key = result.ProcessedStorageKey,
                            Size = result.ProcessedSizeBytes ?? 0
                        }
                    }
                    : null
            };

            await _messageBus.TryPublishAsync("asset.processing.completed", processingEvent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to emit processing result event for asset {AssetId}",
                job.AssetId);
        }
    }

    private static ProcessingTypeEnum MapProcessingType(string contentType)
    {
        // Map content types to processing types defined in schema
        return contentType switch
        {
            var ct when ct.StartsWith("image/") => ProcessingTypeEnum.Mipmaps,
            var ct when ct.StartsWith("audio/") => ProcessingTypeEnum.Transcode,
            var ct when ct.StartsWith("model/") || ct.Contains("gltf") => ProcessingTypeEnum.Lod_generation,
            _ => ProcessingTypeEnum.Validation
        };
    }

    private async Task EmitProcessingFailedEventAsync(
        AssetProcessingJobEvent job,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            // Publish service-level event via message bus (not client event)
            var failureEvent = new AssetProcessingCompletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AssetId = job.AssetId.ToString(),
                ProcessingType = MapProcessingType(job.ContentType),
                Success = false,
                ErrorMessage = errorMessage
            };

            await _messageBus.TryPublishAsync("asset.processing.completed", failureEvent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to emit failure event for asset {AssetId}",
                job.AssetId);
        }
    }

    #endregion
}
