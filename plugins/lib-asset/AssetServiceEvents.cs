using BeyondImmersion.Bannou.Asset.ClientEvents;
using BeyondImmersion.Bannou.Bundle.Format;
using BeyondImmersion.BannouService.Asset.Models;
using BeyondImmersion.BannouService.Asset.Streaming;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Asset;

/// <summary>
/// Event subscription handlers for Asset service.
/// Handles metabundle job processing events.
/// </summary>
public partial class AssetService
{
    /// <summary>
    /// Register event consumers for the Asset service.
    /// Called from constructor after all dependencies are initialized.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Subscribe to metabundle job queue for async processing
        eventConsumer.RegisterHandler<IAssetService, MetabundleJobQueuedEvent>(
            "asset.metabundle.job.queued",
            async (svc, evt) => await ((AssetService)svc).HandleMetabundleJobQueuedAsync(evt));
    }

    /// <summary>
    /// Handles queued metabundle jobs for async processing.
    /// Processes the metabundle creation and emits completion event.
    /// </summary>
    /// <param name="evt">The job queued event.</param>
    internal async Task HandleMetabundleJobQueuedAsync(MetabundleJobQueuedEvent evt)
    {
        var stopwatch = Stopwatch.StartNew();
        var cancellationToken = CancellationToken.None;

        _logger.LogInformation(
            "Processing metabundle job: JobId={JobId}, MetabundleId={MetabundleId}, AssetCount={AssetCount}",
            evt.JobId, evt.MetabundleId, evt.AssetCount);

        // Load job from state store
        var jobStore = _stateStoreFactory.GetStore<MetabundleJob>(_configuration.StatestoreName);
        var jobKey = $"{_configuration.MetabundleJobKeyPrefix}{evt.JobId}";
        var job = await jobStore.GetAsync(jobKey, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (job == null)
        {
            _logger.LogError("Metabundle job not found: JobId={JobId}", evt.JobId);
            return;
        }

        // Check for timeout before starting
        var jobAgeSeconds = (DateTimeOffset.UtcNow - job.CreatedAt).TotalSeconds;
        if (jobAgeSeconds > _configuration.MetabundleJobTimeoutSeconds)
        {
            _logger.LogWarning("Metabundle job timed out before processing: JobId={JobId}, Age={AgeSeconds}s",
                evt.JobId, jobAgeSeconds);

            job.Status = InternalJobStatus.Failed;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorCode = MetabundleErrorCode.TIMEOUT.ToString();
            job.ErrorMessage = "Job timed out before processing could start";
            await jobStore.SaveAsync(jobKey, job, cancellationToken: cancellationToken).ConfigureAwait(false);

            await EmitJobCompletionEventAsync(job, evt.RequesterSessionId, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Update status to Processing
        job.Status = InternalJobStatus.Processing;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await jobStore.SaveAsync(jobKey, job, cancellationToken: cancellationToken).ConfigureAwait(false);

        try
        {
            // Process the metabundle
            var result = await ProcessMetabundleJobAsync(job, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            // Update job with result
            job.Status = InternalJobStatus.Ready;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
            job.Result = result;
            await jobStore.SaveAsync(jobKey, job, cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Metabundle job completed: JobId={JobId}, MetabundleId={MetabundleId}, AssetCount={AssetCount}, ProcessingTimeMs={ProcessingTimeMs}",
                evt.JobId, evt.MetabundleId, result.AssetCount, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex, "Metabundle job failed: JobId={JobId}, MetabundleId={MetabundleId}",
                evt.JobId, evt.MetabundleId);

            // Update job with failure
            job.Status = InternalJobStatus.Failed;
            job.UpdatedAt = DateTimeOffset.UtcNow;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
            job.ErrorCode = MetabundleErrorCode.INTERNAL_ERROR.ToString();
            job.ErrorMessage = ex.Message;
            await jobStore.SaveAsync(jobKey, job, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // Emit completion event to requester session
        await EmitJobCompletionEventAsync(job, evt.RequesterSessionId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes a metabundle job using streaming to limit memory usage.
    /// Uses server-side multipart upload for efficient large bundle assembly.
    /// </summary>
    private async Task<MetabundleJobResult> ProcessMetabundleJobAsync(MetabundleJob job, CancellationToken cancellationToken)
    {
        if (job.Request == null)
        {
            throw new InvalidOperationException($"Job {job.JobId} has null Request - data corruption detected");
        }

        var request = job.Request;
        var bucket = _configuration.StorageBucket;
        var bundleStore = _stateStoreFactory.GetStore<BundleMetadata>(_configuration.StatestoreName);
        var jobStore = _stateStoreFactory.GetStore<MetabundleJob>(_configuration.StatestoreName);
        var jobKey = $"{_configuration.MetabundleJobKeyPrefix}{job.JobId}";

        // Load streaming options from configuration
        var streamingOptions = StreamingBundleWriterOptions.FromConfiguration(_configuration);

        // Load source bundles and validate
        var sourceBundles = new List<BundleMetadata>();
        foreach (var sourceBundleId in request.SourceBundleIds ?? Enumerable.Empty<string>())
        {
            var bundleKey = $"{_configuration.BundleKeyPrefix}{sourceBundleId}";
            var sourceBundle = await bundleStore.GetAsync(bundleKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (sourceBundle != null && sourceBundle.LifecycleStatus == BundleLifecycleStatus.Active)
            {
                sourceBundles.Add(sourceBundle);
            }
        }

        // Load standalone assets if specified
        var assetStore = _stateStoreFactory.GetStore<InternalAssetRecord>(_configuration.StatestoreName);
        var standaloneAssets = new List<InternalAssetRecord>();
        foreach (var assetId in request.StandaloneAssetIds ?? Enumerable.Empty<string>())
        {
            var assetKey = $"{_configuration.AssetKeyPrefix}{assetId}";
            var asset = await assetStore.GetAsync(assetKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (asset != null && asset.ProcessingStatus == ProcessingStatus.Complete)
            {
                standaloneAssets.Add(asset);
            }
        }

        // Build list of assets to include with source bundle grouping
        var assetsBySourceBundle = new Dictionary<string, List<(StoredBundleAssetEntry Entry, BundleMetadata SourceBundle)>>();
        foreach (var sourceBundle in sourceBundles)
        {
            if (sourceBundle.Assets != null)
            {
                var entries = new List<(StoredBundleAssetEntry, BundleMetadata)>();
                foreach (var asset in sourceBundle.Assets)
                {
                    entries.Add((asset, sourceBundle));
                }
                assetsBySourceBundle[sourceBundle.BundleId] = entries;
            }
        }

        // Calculate total assets for progress tracking
        var totalAssetCount = assetsBySourceBundle.Values.Sum(list => list.Count) + standaloneAssets.Count;

        // Create metabundle path
        var metabundlePath = $"{_configuration.BundleCurrentPathPrefix}/{request.MetabundleId}.bundle";

        // Initialize streaming multipart upload
        var uploadSession = await _storageProvider.InitiateServerMultipartUploadAsync(
            bucket,
            metabundlePath,
            "application/x-bannou-bundle",
            cancellationToken).ConfigureAwait(false);

        long bundleSize;
        var provenanceByBundle = new Dictionary<string, List<string>>();
        var standaloneAssetIds = new List<string>();
        var metabundleAssets = new List<StoredBundleAssetEntry>();
        var processedCount = 0;

        try
        {
            await using var writer = new StreamingBundleWriter(
                uploadSession,
                _storageProvider,
                streamingOptions,
                _logger);

            // Process assets from source bundles - stream one bundle at a time
            foreach (var (sourceBundleId, assets) in assetsBySourceBundle)
            {
                var sourceBundle = sourceBundles.First(b => b.BundleId == sourceBundleId);

                // Use streaming download to get bundle data
                // Note: BannouBundleReader requires seekable stream for random access,
                // so we buffer the bundle but process one bundle at a time to limit memory
                await using var sourceBundleStream = await _storageProvider.GetObjectStreamingAsync(
                    sourceBundle.Bucket ?? bucket,
                    sourceBundle.StorageKey,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                using var bufferedStream = new MemoryStream();
                await sourceBundleStream.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
                bufferedStream.Position = 0;

                using var reader = new BannouBundleReader(bufferedStream, leaveOpen: true);
                reader.ReadHeader();

                foreach (var (entry, _) in assets)
                {
                    var added = await writer.AddAssetFromBundleAsync(
                        reader,
                        entry.AssetId,
                        entry.Filename ?? entry.AssetId,
                        entry.ContentType ?? "application/octet-stream",
                        cancellationToken).ConfigureAwait(false);

                    if (!added)
                    {
                        _logger.LogWarning(
                            "ProcessMetabundleJob: Asset {AssetId} not found in source bundle {BundleId}",
                            entry.AssetId, sourceBundleId);
                        continue;
                    }

                    // Track provenance
                    if (!provenanceByBundle.TryGetValue(sourceBundleId, out var assetList))
                    {
                        assetList = new List<string>();
                        provenanceByBundle[sourceBundleId] = assetList;
                    }
                    assetList.Add(entry.AssetId);
                    metabundleAssets.Add(entry);

                    processedCount++;

                    // Update progress periodically
                    if (processedCount % streamingOptions.ProgressUpdateIntervalAssets == 0)
                    {
                        var progress = 10 + (int)(80.0 * processedCount / totalAssetCount);
                        await UpdateJobProgressAsync(jobStore, jobKey, job, progress, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }

            // Process standalone assets
            foreach (var standalone in standaloneAssets)
            {
                await using var assetStream = await _storageProvider.GetObjectStreamingAsync(
                    standalone.Bucket ?? bucket,
                    standalone.StorageKey,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                await writer.AddAssetFromStreamAsync(
                    standalone.AssetId,
                    standalone.Filename ?? standalone.AssetId,
                    standalone.ContentType ?? "application/octet-stream",
                    assetStream,
                    cancellationToken).ConfigureAwait(false);

                standaloneAssetIds.Add(standalone.AssetId);
                metabundleAssets.Add(new StoredBundleAssetEntry
                {
                    AssetId = standalone.AssetId,
                    Filename = standalone.Filename,
                    ContentType = standalone.ContentType,
                    Size = standalone.Size,
                    ContentHash = standalone.ContentHash
                });

                processedCount++;

                // Update progress periodically
                if (processedCount % streamingOptions.ProgressUpdateIntervalAssets == 0)
                {
                    var progress = 10 + (int)(80.0 * processedCount / totalAssetCount);
                    await UpdateJobProgressAsync(jobStore, jobKey, job, progress, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            // Update progress to finalizing phase
            await UpdateJobProgressAsync(jobStore, jobKey, job, 90, cancellationToken).ConfigureAwait(false);

            // Finalize the streaming bundle
            bundleSize = await writer.FinalizeAsync(
                request.MetabundleId,
                request.MetabundleId,
                request.Version ?? "1.0.0",
                request.Owner,
                request.Description,
                request.Metadata != null ? MetadataHelper.ConvertToStringDictionary(request.Metadata) : null,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // On failure, the StreamingBundleWriter.DisposeAsync will abort the upload
            throw;
        }

        // Build provenance references
        var sourceBundleRefs = sourceBundles
            .Where(sb => provenanceByBundle.ContainsKey(sb.BundleId))
            .Select(sb => new StoredSourceBundleReference
            {
                BundleId = sb.BundleId,
                Version = sb.Version,
                AssetIds = provenanceByBundle[sb.BundleId],
                ContentHash = sb.StorageKey
            })
            .ToList();

        var allAssetIds = metabundleAssets.Select(a => a.AssetId).ToList();

        // Store metabundle metadata
        var metabundleKey = $"{_configuration.BundleKeyPrefix}{request.MetabundleId}";
        var metabundleMetadata = new BundleMetadata
        {
            BundleId = request.MetabundleId,
            Version = request.Version ?? "1.0.0",
            BundleType = BundleType.Metabundle,
            Realm = request.Realm,
            AssetIds = allAssetIds,
            Assets = metabundleAssets,
            StorageKey = metabundlePath,
            Bucket = bucket,
            SizeBytes = bundleSize,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = Models.BundleStatus.Ready,
            Owner = request.Owner,
            SourceBundles = sourceBundleRefs,
            StandaloneAssetIds = standaloneAssetIds.Count > 0 ? standaloneAssetIds : null,
            Metadata = request.Metadata != null ? MetadataHelper.ConvertToDictionary(request.Metadata) : null
        };

        await bundleStore.SaveAsync(metabundleKey, metabundleMetadata, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Index bundle assets
        await IndexBundleAssetsAsync(metabundleMetadata, cancellationToken).ConfigureAwait(false);

        // Publish metabundle.created event
        await _messageBus.TryPublishAsync(
            "asset.metabundle.created",
            new MetabundleCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                MetabundleId = request.MetabundleId,
                Version = request.Version ?? "1.0.0",
                Realm = (BeyondImmersion.BannouService.Events.RealmEnum)request.Realm,
                SourceBundleCount = sourceBundles.Count,
                SourceBundleIds = sourceBundles.Select(sb => sb.BundleId).ToList(),
                AssetCount = metabundleAssets.Count,
                StandaloneAssetCount = standaloneAssetIds.Count,
                Bucket = bucket,
                Key = metabundlePath,
                SizeBytes = bundleSize,
                Owner = request.Owner
            }).ConfigureAwait(false);

        return new MetabundleJobResult
        {
            AssetCount = metabundleAssets.Count,
            StandaloneAssetCount = standaloneAssetIds.Count,
            SizeBytes = bundleSize,
            StorageKey = metabundlePath,
            SourceBundles = sourceBundleRefs.Select(sb => new SourceBundleReferenceInternal
            {
                BundleId = Guid.Parse(sb.BundleId),
                Version = sb.Version,
                AssetIds = sb.AssetIds.Select(Guid.Parse).ToList(),
                ContentHash = sb.ContentHash
            }).ToList()
        };
    }

    /// <summary>
    /// Updates job progress in state store.
    /// </summary>
    private static async Task UpdateJobProgressAsync(
        IStateStore<MetabundleJob> jobStore,
        string jobKey,
        MetabundleJob job,
        int progress,
        CancellationToken cancellationToken)
    {
        job.Progress = progress;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await jobStore.SaveAsync(jobKey, job, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Emits the job completion event to the requester's session (if available).
    /// </summary>
    private async Task EmitJobCompletionEventAsync(MetabundleJob job, string? sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            _logger.LogDebug("No session ID for job completion event: JobId={JobId}", job.JobId);
            return;
        }

        var success = job.Status == InternalJobStatus.Ready;
        Uri? downloadUrl = null;

        // Generate download URL for successful jobs
        if (success && job.Result?.StorageKey != null)
        {
            try
            {
                var downloadResult = await _storageProvider.GenerateDownloadUrlAsync(
                    _configuration.StorageBucket,
                    job.Result.StorageKey,
                    null,
                    TimeSpan.FromSeconds(_configuration.DownloadTokenTtlSeconds)).ConfigureAwait(false);
                downloadUrl = new Uri(downloadResult.DownloadUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate download URL for job {JobId}", job.JobId);
            }
        }

        // Map internal status to client event status
        var clientStatus = job.Status switch
        {
            InternalJobStatus.Queued => MetabundleJobStatus.Queued,
            InternalJobStatus.Processing => MetabundleJobStatus.Processing,
            InternalJobStatus.Ready => MetabundleJobStatus.Ready,
            InternalJobStatus.Failed => MetabundleJobStatus.Failed,
            InternalJobStatus.Cancelled => MetabundleJobStatus.Cancelled,
            _ => MetabundleJobStatus.Failed
        };

        // Map error code
        MetabundleErrorCode? errorCode = null;
        if (!string.IsNullOrEmpty(job.ErrorCode) && Enum.TryParse<MetabundleErrorCode>(job.ErrorCode, true, out var parsed))
        {
            errorCode = parsed;
        }

        await _eventEmitter.EmitMetabundleCreationCompleteAsync(
            sessionId,
            job.JobId,
            job.MetabundleId.ToString(),
            success,
            clientStatus,
            downloadUrl,
            job.Result?.SizeBytes,
            job.Result?.AssetCount,
            job.Result?.StandaloneAssetCount,
            job.ProcessingTimeMs,
            errorCode,
            job.ErrorMessage,
            cancellationToken).ConfigureAwait(false);
    }
}
