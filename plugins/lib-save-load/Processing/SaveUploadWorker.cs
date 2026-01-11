using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.SaveLoad;
using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace BeyondImmersion.BannouService.SaveLoad.Processing;

/// <summary>
/// Background service that processes the async upload queue.
/// Uploads save data from Redis to MinIO via the Asset service.
/// </summary>
public class SaveUploadWorker : BackgroundService
{
    /// <summary>
    /// Key for the Redis set that tracks pending upload IDs.
    /// Used because Redis key-value stores don't support LINQ queries.
    /// </summary>
    public const string PendingUploadIdsSetKey = "pending-upload-ids";

    private readonly IServiceProvider _serviceProvider;
    private readonly SaveLoadServiceConfiguration _configuration;
    private readonly ILogger<SaveUploadWorker> _logger;

    /// <summary>
    /// Creates a new SaveUploadWorker instance.
    /// </summary>
    public SaveUploadWorker(
        IServiceProvider serviceProvider,
        SaveLoadServiceConfiguration configuration,
        ILogger<SaveUploadWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the background processing loop.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.AsyncUploadEnabled)
        {
            _logger.LogInformation("Async upload is disabled, SaveUploadWorker not starting");
            return;
        }

        // Wait for other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        _logger.LogInformation(
            "SaveUploadWorker starting with interval of {IntervalMs}ms, batch size {BatchSize}",
            _configuration.UploadBatchIntervalMs,
            _configuration.UploadBatchSize);

        var interval = TimeSpan.FromMilliseconds(_configuration.UploadBatchIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                await ProcessPendingUploadsAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during upload processing");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("SaveUploadWorker stopped");
    }

    private async Task ProcessPendingUploadsAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var stateStoreFactory = serviceProvider.GetRequiredService<IStateStoreFactory>();
        var messageBus = serviceProvider.GetRequiredService<IMessageBus>();
        var assetClient = serviceProvider.GetRequiredService<IAssetClient>();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var circuitBreaker = new StorageCircuitBreaker(
            stateStoreFactory,
            messageBus,
            _configuration,
            serviceProvider.GetRequiredService<ILogger<StorageCircuitBreaker>>());

        // Check circuit breaker
        if (!await circuitBreaker.IsAllowedAsync(cancellationToken))
        {
            _logger.LogDebug("Circuit breaker is open, skipping upload processing");
            return;
        }

        var pendingStore = stateStoreFactory.GetStore<PendingUploadEntry>(_configuration.PendingUploadStoreName);
        var versionStore = stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);

        // Get pending upload IDs from tracking set (Redis doesn't support LINQ queries)
        var pendingUploadIds = await pendingStore.GetSetAsync<string>(PendingUploadIdsSetKey, cancellationToken);

        if (pendingUploadIds.Count == 0)
        {
            return;
        }

        // Fetch all pending entries using bulk get
        var pendingKeys = pendingUploadIds.Select(PendingUploadEntry.GetStateKey).ToList();
        var entriesDict = await pendingStore.GetBulkAsync(pendingKeys, cancellationToken);

        // Filter by attempt count, order by priority, and take batch size
        var pendingEntries = entriesDict.Values
            .Where(e => e.AttemptCount < _configuration.UploadRetryAttempts)
            .OrderBy(e => e.Priority)
            .Take(_configuration.UploadBatchSize)
            .ToList();

        if (pendingEntries.Count == 0)
        {
            // Clean up expired/orphaned entries from the tracking set
            var orphanedIds = pendingUploadIds.Except(entriesDict.Values.Select(e => e.UploadId)).ToList();
            foreach (var orphanedId in orphanedIds)
            {
                await pendingStore.RemoveFromSetAsync(PendingUploadIdsSetKey, orphanedId, cancellationToken);
            }
            return;
        }

        _logger.LogDebug("Processing {Count} pending uploads", pendingEntries.Count);

        foreach (var entry in pendingEntries)
        {
            try
            {
                await ProcessUploadAsync(entry, pendingStore, versionStore, assetClient, messageBus, circuitBreaker, httpClientFactory, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing upload {UploadId}", entry.UploadId);
                await RecordUploadFailureAsync(entry, pendingStore, messageBus, ex.Message, circuitBreaker, cancellationToken);
            }
        }
    }

    private async Task ProcessUploadAsync(
        PendingUploadEntry entry,
        IStateStore<PendingUploadEntry> pendingStore,
        IStateStore<SaveVersionManifest> versionStore,
        IAssetClient assetClient,
        IMessageBus messageBus,
        StorageCircuitBreaker circuitBreaker,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Processing upload {UploadId} for slot {SlotId} version {Version}",
            entry.UploadId, entry.SlotId, entry.VersionNumber);

        // Update attempt count
        entry.AttemptCount++;
        entry.LastAttemptAt = DateTimeOffset.UtcNow;
        await pendingStore.SaveAsync(entry.GetStateKey(), entry, cancellationToken: cancellationToken);

        // Request upload URL from Asset service
        var uploadRequest = new UploadRequest
        {
            Owner = $"{entry.OwnerType}:{entry.OwnerId}",
            ContentType = "application/octet-stream",
            Filename = $"save_{entry.SlotId}_{entry.VersionNumber}.dat",
            Size = entry.CompressedSizeBytes,
            Metadata = new AssetMetadataInput
            {
                Tags = new List<string> { "save-data", entry.GameId, entry.SlotId, entry.VersionNumber.ToString() }
            }
        };

        var uploadResponse = await assetClient.RequestUploadAsync(uploadRequest, cancellationToken);

        if (uploadResponse?.UploadUrl == null)
        {
            throw new InvalidOperationException("Asset service did not return an upload URL");
        }

        // Upload data to presigned URL
        var data = Convert.FromBase64String(entry.Data);
        using var httpClient = httpClientFactory.CreateClient();
        using var content = new ByteArrayContent(data);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        var response = await httpClient.PutAsync(uploadResponse.UploadUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Complete upload
        var completeRequest = new CompleteUploadRequest
        {
            UploadId = uploadResponse.UploadId
        };

        var assetMetadata = await assetClient.CompleteUploadAsync(completeRequest, cancellationToken);

        // Update version manifest with asset ID
        var versionKey = SaveVersionManifest.GetStateKey(entry.SlotId, entry.VersionNumber);
        var manifest = await versionStore.GetAsync(versionKey, cancellationToken);

        if (manifest != null)
        {
            manifest.AssetId = assetMetadata.AssetId;
            manifest.UploadStatus = "COMPLETE";
            await versionStore.SaveAsync(versionKey, manifest, cancellationToken: cancellationToken);
        }

        // Delete pending entry and remove from tracking set
        await pendingStore.DeleteAsync(entry.GetStateKey(), cancellationToken);
        await pendingStore.RemoveFromSetAsync(PendingUploadIdsSetKey, entry.UploadId, cancellationToken);

        // Record success with circuit breaker
        await circuitBreaker.RecordSuccessAsync(cancellationToken);

        // Publish completion event
        var completedEvent = new SaveUploadCompletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SlotId = Guid.Parse(entry.SlotId),
            SlotName = entry.SlotId, // We don't have slot name in pending entry
            VersionNumber = entry.VersionNumber,
            AssetId = Guid.Parse(assetMetadata.AssetId)
        };

        await messageBus.TryPublishAsync(
            "save-load.upload.completed",
            completedEvent,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Upload completed for slot {SlotId} version {Version}, asset ID {AssetId}",
            entry.SlotId, entry.VersionNumber, assetMetadata.AssetId);
    }

    private async Task RecordUploadFailureAsync(
        PendingUploadEntry entry,
        IStateStore<PendingUploadEntry> pendingStore,
        IMessageBus messageBus,
        string errorMessage,
        StorageCircuitBreaker circuitBreaker,
        CancellationToken cancellationToken)
    {
        entry.LastError = errorMessage;
        entry.LastAttemptAt = DateTimeOffset.UtcNow;

        if (entry.AttemptCount >= _configuration.UploadRetryAttempts)
        {
            _logger.LogError(
                "Upload failed permanently for slot {SlotId} version {Version} after {Attempts} attempts: {Error}",
                entry.SlotId, entry.VersionNumber, entry.AttemptCount, errorMessage);

            // Publish failure event
            var failedEvent = new SaveUploadFailedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SlotId = Guid.Parse(entry.SlotId),
                SlotName = entry.SlotId,
                VersionNumber = entry.VersionNumber,
                OwnerId = Guid.Parse(entry.OwnerId),
                OwnerType = entry.OwnerType,
                ErrorMessage = errorMessage,
                RetryCount = entry.AttemptCount,
                WillRetry = false
            };

            await messageBus.TryPublishAsync(
                "save-load.upload.failed",
                failedEvent,
                cancellationToken: cancellationToken);

            // Delete the failed entry after max attempts and remove from tracking set
            await pendingStore.DeleteAsync(entry.GetStateKey(), cancellationToken);
            await pendingStore.RemoveFromSetAsync(PendingUploadIdsSetKey, entry.UploadId, cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "Upload attempt {Attempt} failed for slot {SlotId} version {Version}: {Error}",
                entry.AttemptCount, entry.SlotId, entry.VersionNumber, errorMessage);

            // Save updated entry for retry
            await pendingStore.SaveAsync(entry.GetStateKey(), entry, cancellationToken: cancellationToken);
        }

        // Record failure with circuit breaker
        await circuitBreaker.RecordFailureAsync(cancellationToken);
    }
}
