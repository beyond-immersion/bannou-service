using BeyondImmersion.Bannou.Asset.ClientEvents;
using BeyondImmersion.BannouService.Asset.Models;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Asset.Processing;

/// <summary>
/// Background worker that processes asset jobs when running in worker mode.
/// </summary>
public sealed class AssetProcessingWorker : BackgroundService
{
    private readonly AssetProcessorRegistry _processorRegistry;
    private readonly IStateStore<AssetMetadata> _stateStore;
    private readonly IAssetStorageProvider _storageProvider;
    private readonly IOrchestratorClient _orchestratorClient;
    private readonly IClientEventPublisher? _clientEventPublisher;
    private readonly AssetServiceConfiguration _configuration;
    private readonly ILogger<AssetProcessingWorker> _logger;

    private const string ASSET_PREFIX = "asset:";

    /// <summary>
    /// Creates a new AssetProcessingWorker.
    /// </summary>
    public AssetProcessingWorker(
        AssetProcessorRegistry processorRegistry,
        IStateStoreFactory stateStoreFactory,
        IAssetStorageProvider storageProvider,
        IOrchestratorClient orchestratorClient,
        AssetServiceConfiguration configuration,
        ILogger<AssetProcessingWorker> logger,
        IClientEventPublisher? clientEventPublisher = null)
    {
        _processorRegistry = processorRegistry ?? throw new ArgumentNullException(nameof(processorRegistry));
        ArgumentNullException.ThrowIfNull(stateStoreFactory);
        _stateStore = stateStoreFactory.GetStore<AssetMetadata>("asset-statestore");
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _orchestratorClient = orchestratorClient ?? throw new ArgumentNullException(nameof(orchestratorClient));
        _clientEventPublisher = clientEventPublisher;
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Asset processing worker started. Worker pool: {WorkerPool}",
            _configuration.WorkerPool ?? "default");

        // The worker receives jobs via MassTransit pub/sub subscription
        // The actual job processing is done in HandleProcessingJob
        // This background service just keeps the worker alive

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Heartbeat logging
                _logger.LogDebug("Asset processing worker heartbeat");

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Asset processing worker stopped");
    }

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

        try
        {
            // Create processing context
            var context = new AssetProcessingContext
            {
                AssetId = job.AssetId,
                StorageKey = job.StorageKey,
                ContentType = job.ContentType,
                SizeBytes = job.SizeBytes,
                Filename = job.Filename,
                SessionId = job.SessionId,
                RealmId = job.RealmId,
                Tags = job.Tags,
                ProcessingOptions = job.ProcessingOptions
            };

            // Process the asset
            var result = await processor.ProcessAsync(context, cancellationToken);

            // Update asset metadata with processing results
            await UpdateAssetMetadataAsync(job.AssetId, result, cancellationToken);

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
    }

    private async Task UpdateAssetMetadataAsync(
        string assetId,
        AssetProcessingResult result,
        CancellationToken cancellationToken)
    {
        var stateKey = $"{ASSET_PREFIX}{assetId}";

        try
        {
            var existingMetadata = await _stateStore.GetAsync(stateKey, cancellationToken);

            if (existingMetadata == null)
            {
                _logger.LogWarning("Asset metadata not found for {AssetId}", assetId);
                return;
            }

            // Update the metadata with processing results
            existingMetadata.Processing_status = result.Success
                ? ProcessingStatus.Complete
                : ProcessingStatus.Failed;
            existingMetadata.Updated_at = DateTimeOffset.UtcNow;

            await _stateStore.SaveAsync(stateKey, existingMetadata, null, cancellationToken);

            _logger.LogDebug(
                "Updated metadata for asset {AssetId}: Status={Status}",
                assetId,
                existingMetadata.Processing_status);
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
                    Lease_id = leaseId,
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
        if (_clientEventPublisher == null)
        {
            _logger.LogWarning(
                "Cannot emit processing result event for asset {AssetId}: IClientEventPublisher not available",
                job.AssetId);
            return;
        }

        try
        {
            if (result.Success)
            {
                var completionEvent = new AssetProcessingCompleteEvent
                {
                    Event_id = Guid.NewGuid(),
                    Event_name = AssetProcessingCompleteEventEvent_name.Asset_processing_complete,
                    Asset_id = job.AssetId,
                    Success = true,
                    Outputs = result.ProcessedStorageKey != null
                        ? new List<ProcessingOutput>
                        {
                            new ProcessingOutput
                            {
                                Output_type = "processed",
                                Asset_id = job.AssetId,
                                Size = result.ProcessedSizeBytes ?? 0
                            }
                        }
                        : null
                };

                await _clientEventPublisher.PublishToSessionAsync(job.SessionId, completionEvent, cancellationToken);
            }
            else
            {
                var failureEvent = new AssetProcessingFailedEvent
                {
                    Event_id = Guid.NewGuid(),
                    Event_name = AssetProcessingFailedEventEvent_name.Asset_processing_failed,
                    Asset_id = job.AssetId,
                    Error_message = result.ErrorMessage ?? "Unknown error",
                    Error_code = MapErrorCode(result.ErrorCode),
                    Retry_available = true
                };

                await _clientEventPublisher.PublishToSessionAsync(job.SessionId, failureEvent, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to emit processing result event for asset {AssetId}",
                job.AssetId);
        }
    }

    private static ProcessingErrorCode? MapErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            "PROCESSING_FAILED" => ProcessingErrorCode.PROCESSING_FAILED,
            "INVALID_FORMAT" => ProcessingErrorCode.INVALID_FORMAT,
            "RESOURCE_EXHAUSTED" => ProcessingErrorCode.RESOURCE_EXHAUSTED,
            "TIMEOUT" => ProcessingErrorCode.TIMEOUT,
            "PROCESSOR_UNAVAILABLE" => ProcessingErrorCode.PROCESSOR_UNAVAILABLE,
            _ => ProcessingErrorCode.PROCESSING_FAILED
        };
    }

    private async Task EmitProcessingFailedEventAsync(
        AssetProcessingJobEvent job,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        if (_clientEventPublisher == null)
        {
            _logger.LogWarning(
                "Cannot emit failure event for asset {AssetId}: IClientEventPublisher not available",
                job.AssetId);
            return;
        }

        try
        {
            var failureEvent = new AssetProcessingFailedEvent
            {
                Event_id = Guid.NewGuid(),
                Event_name = AssetProcessingFailedEventEvent_name.Asset_processing_failed,
                Asset_id = job.AssetId,
                Error_message = errorMessage,
                Error_code = ProcessingErrorCode.PROCESSING_FAILED,
                Retry_available = true
            };

            await _clientEventPublisher.PublishToSessionAsync(job.SessionId, failureEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to emit failure event for asset {AssetId}",
                job.AssetId);
        }
    }
}
