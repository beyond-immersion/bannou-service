using System.Diagnostics;
using BeyondImmersion.Bannou.Asset.ClientEvents;
using BeyondImmersion.Bannou.Bundle.Format;
using BeyondImmersion.BannouService.Asset.Models;
using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

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
    public async Task HandleMetabundleJobQueuedAsync(MetabundleJobQueuedEvent evt)
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
            job.ErrorCode = MetabundleErrorCode.Timeout.ToString();
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
            job.ErrorCode = MetabundleErrorCode.Internal_error.ToString();
            job.ErrorMessage = ex.Message;
            await jobStore.SaveAsync(jobKey, job, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // Emit completion event to requester session
        await EmitJobCompletionEventAsync(job, evt.RequesterSessionId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes a metabundle job - creates the actual metabundle file.
    /// </summary>
    private async Task<MetabundleJobResult> ProcessMetabundleJobAsync(MetabundleJob job, CancellationToken cancellationToken)
    {
        var request = job.Request;
        var bucket = _configuration.StorageBucket;
        var bundleStore = _stateStoreFactory.GetStore<BundleMetadata>(_configuration.StatestoreName);

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
        var assetStore = _stateStoreFactory.GetStore<AssetMetadata>(_configuration.StatestoreName);
        var standaloneAssets = new List<AssetMetadata>();
        foreach (var assetId in request.StandaloneAssetIds ?? Enumerable.Empty<string>())
        {
            var assetKey = $"{_configuration.AssetKeyPrefix}{assetId}";
            var asset = await assetStore.GetAsync(assetKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (asset != null && asset.Status == AssetStatus.Ready)
            {
                standaloneAssets.Add(asset);
            }
        }

        // Build list of assets to include
        var assetsToInclude = new List<(StoredBundleAssetEntry Entry, string SourceBundleId)>();
        foreach (var sourceBundle in sourceBundles)
        {
            if (sourceBundle.Assets != null)
            {
                foreach (var asset in sourceBundle.Assets)
                {
                    assetsToInclude.Add((asset, sourceBundle.BundleId));
                }
            }
        }

        // Create metabundle
        var metabundlePath = $"{_configuration.BundleCurrentPathPrefix}/{request.MetabundleId}.bundle";

        using var bundleStream = new MemoryStream();
        using var writer = new BannouBundleWriter(bundleStream);

        // Track provenance
        var provenanceByBundle = new Dictionary<string, List<string>>();
        var standaloneAssetIds = new List<string>();

        // Process assets from source bundles
        foreach (var (entry, sourceBundleId) in assetsToInclude)
        {
            var sourceBundle = sourceBundles.First(b => b.BundleId == sourceBundleId);

            using var sourceBundleStream = await _storageProvider.GetObjectAsync(
                sourceBundle.Bucket ?? bucket,
                sourceBundle.StorageKey).ConfigureAwait(false);

            using var reader = new BannouBundleReader(sourceBundleStream);
            var assetData = reader.ReadAsset(entry.AssetId);

            if (assetData == null)
            {
                _logger.LogWarning("ProcessMetabundleJob: Asset {AssetId} not found in source bundle {BundleId}",
                    entry.AssetId, sourceBundleId);
                continue;
            }

            writer.AddAsset(
                entry.AssetId,
                entry.Filename ?? entry.AssetId,
                entry.ContentType ?? "application/octet-stream",
                assetData);

            if (!provenanceByBundle.TryGetValue(sourceBundleId, out var assetList))
            {
                assetList = new List<string>();
                provenanceByBundle[sourceBundleId] = assetList;
            }
            assetList.Add(entry.AssetId);
        }

        // Process standalone assets
        foreach (var standalone in standaloneAssets)
        {
            using var assetStream = await _storageProvider.GetObjectAsync(
                standalone.Bucket ?? bucket,
                standalone.StorageKey).ConfigureAwait(false);

            using var memoryStream = new MemoryStream();
            await assetStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            var assetData = memoryStream.ToArray();

            writer.AddAsset(
                standalone.AssetId,
                standalone.Filename ?? standalone.AssetId,
                standalone.ContentType ?? "application/octet-stream",
                assetData);

            standaloneAssetIds.Add(standalone.AssetId);
        }

        // Finalize the bundle
        writer.Finalize(
            request.MetabundleId,
            request.MetabundleId,
            request.Version ?? "1.0.0",
            request.Owner,
            request.Description,
            request.Metadata != null ? MetadataHelper.ConvertToStringDictionary(request.Metadata) : null);

        // Upload metabundle
        bundleStream.Position = 0;
        await _storageProvider.PutObjectAsync(
            bucket,
            metabundlePath,
            bundleStream,
            bundleStream.Length,
            "application/x-bannou-bundle").ConfigureAwait(false);

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

        // Build asset entries
        var metabundleAssets = assetsToInclude.Select(a => a.Entry).ToList();
        var standaloneEntries = standaloneAssets.Select(s => new StoredBundleAssetEntry
        {
            AssetId = s.AssetId,
            Filename = s.Filename,
            ContentType = s.ContentType,
            Size = s.Size,
            ContentHash = s.ContentHash
        }).ToList();
        metabundleAssets.AddRange(standaloneEntries);

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
            SizeBytes = bundleStream.Length,
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
                SizeBytes = bundleStream.Length,
                Owner = request.Owner
            }).ConfigureAwait(false);

        return new MetabundleJobResult
        {
            AssetCount = metabundleAssets.Count,
            StandaloneAssetCount = standaloneAssetIds.Count,
            SizeBytes = bundleStream.Length,
            StorageKey = metabundlePath,
            SourceBundles = sourceBundleRefs.Select(sb => new SourceBundleReferenceInternal
            {
                BundleId = sb.BundleId,
                Version = sb.Version,
                AssetIds = sb.AssetIds,
                ContentHash = sb.ContentHash
            }).ToList()
        };
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
                downloadUrl = await _storageProvider.GeneratePresignedGetUrlAsync(
                    _configuration.StorageBucket,
                    job.Result.StorageKey,
                    TimeSpan.FromSeconds(_configuration.DownloadTokenTtlSeconds)).ConfigureAwait(false);
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
            job.MetabundleId,
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
