using BeyondImmersion.BannouService.Asset.Models;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Storage;
using Dapr.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Asset.Processing;

/// <summary>
/// Background worker that processes asset jobs when running in worker mode.
/// </summary>
public sealed class AssetProcessingWorker : BackgroundService
{
    private readonly AssetProcessorRegistry _processorRegistry;
    private readonly DaprClient _daprClient;
    private readonly IAssetStorageProvider _storageProvider;
    private readonly IOrchestratorClient _orchestratorClient;
    private readonly AssetServiceConfiguration _configuration;
    private readonly ILogger<AssetProcessingWorker> _logger;

    private const string STATE_STORE = "asset-statestore";
    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string ASSET_PREFIX = "asset:";

    /// <summary>
    /// Creates a new AssetProcessingWorker.
    /// </summary>
    public AssetProcessingWorker(
        AssetProcessorRegistry processorRegistry,
        DaprClient daprClient,
        IAssetStorageProvider storageProvider,
        IOrchestratorClient orchestratorClient,
        AssetServiceConfiguration configuration,
        ILogger<AssetProcessingWorker> logger)
    {
        _processorRegistry = processorRegistry ?? throw new ArgumentNullException(nameof(processorRegistry));
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _orchestratorClient = orchestratorClient ?? throw new ArgumentNullException(nameof(orchestratorClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Asset processing worker started. Worker pool: {WorkerPool}",
            _configuration.WorkerPool ?? "default");

        // The worker receives jobs via Dapr pub/sub subscription
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
    /// Called by the Dapr event controller.
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
            var existingMetadata = await _daprClient.GetStateAsync<AssetMetadata>(
                STATE_STORE,
                stateKey,
                cancellationToken: cancellationToken);

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

            await _daprClient.SaveStateAsync(
                STATE_STORE,
                stateKey,
                existingMetadata,
                cancellationToken: cancellationToken);

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
        try
        {
            if (result.Success)
            {
                var completionEvent = new AssetProcessingCompleteEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    Timestamp = DateTimeOffset.UtcNow.ToString("o"),
                    AssetId = job.AssetId,
                    SessionId = job.SessionId,
                    ProcessingTimeMs = result.ProcessingTimeMs,
                    ProcessedStorageKey = result.ProcessedStorageKey,
                    ProcessedSizeBytes = result.ProcessedSizeBytes ?? 0
                };

                // Publish to session-specific channel for WebSocket delivery
                await _daprClient.PublishEventAsync(
                    PUBSUB_NAME,
                    $"CONNECT_{job.SessionId}",
                    completionEvent,
                    cancellationToken);
            }
            else
            {
                var failureEvent = new AssetProcessingFailedEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    Timestamp = DateTimeOffset.UtcNow.ToString("o"),
                    AssetId = job.AssetId,
                    SessionId = job.SessionId,
                    ErrorMessage = result.ErrorMessage ?? "Unknown error",
                    ErrorCode = result.ErrorCode ?? "UNKNOWN_ERROR"
                };

                await _daprClient.PublishEventAsync(
                    PUBSUB_NAME,
                    $"CONNECT_{job.SessionId}",
                    failureEvent,
                    cancellationToken);
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

    private async Task EmitProcessingFailedEventAsync(
        AssetProcessingJobEvent job,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var failureEvent = new AssetProcessingFailedEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow.ToString("o"),
                AssetId = job.AssetId,
                SessionId = job.SessionId,
                ErrorMessage = errorMessage,
                ErrorCode = "PROCESSING_EXCEPTION"
            };

            await _daprClient.PublishEventAsync(
                PUBSUB_NAME,
                $"CONNECT_{job.SessionId}",
                failureEvent,
                cancellationToken);
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

/// <summary>
/// Event emitted when asset processing completes successfully.
/// </summary>
public sealed class AssetProcessingCompleteEvent
{
    /// <summary>
    /// Unique identifier for this event.
    /// </summary>
    public required string EventId { get; init; }

    /// <summary>
    /// Timestamp when the event was created.
    /// </summary>
    public required string Timestamp { get; init; }

    /// <summary>
    /// The asset ID that was processed.
    /// </summary>
    public required string AssetId { get; init; }

    /// <summary>
    /// The session ID that initiated the upload.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Time taken to process in milliseconds.
    /// </summary>
    public long ProcessingTimeMs { get; init; }

    /// <summary>
    /// The storage key of the processed asset.
    /// </summary>
    public string? ProcessedStorageKey { get; init; }

    /// <summary>
    /// Size of the processed asset in bytes.
    /// </summary>
    public long ProcessedSizeBytes { get; init; }
}

/// <summary>
/// Event emitted when asset processing fails.
/// </summary>
public sealed class AssetProcessingFailedEvent
{
    /// <summary>
    /// Unique identifier for this event.
    /// </summary>
    public required string EventId { get; init; }

    /// <summary>
    /// Timestamp when the event was created.
    /// </summary>
    public required string Timestamp { get; init; }

    /// <summary>
    /// The asset ID that failed processing.
    /// </summary>
    public required string AssetId { get; init; }

    /// <summary>
    /// The session ID that initiated the upload.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Error message describing the failure.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// Error code for programmatic handling.
    /// </summary>
    public required string ErrorCode { get; init; }
}
