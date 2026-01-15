using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using BeyondImmersion.Bannou.AssetBundler.Processing;
using BeyondImmersion.Bannou.AssetBundler.State;
using BeyondImmersion.Bannou.AssetBundler.Upload;
using BeyondImmersion.Bannou.Bundle.Format;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetBundler.Pipeline;

/// <summary>
/// Orchestrates the complete bundling pipeline:
/// Source → Extract → Process → Bundle → Upload
/// </summary>
public sealed class BundlerPipeline
{
    private readonly ILogger<BundlerPipeline>? _logger;

    /// <summary>
    /// Creates a new bundler pipeline.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    public BundlerPipeline(ILogger<BundlerPipeline>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executes the complete bundling pipeline for a single source.
    /// </summary>
    /// <param name="source">Asset source to process.</param>
    /// <param name="processor">Asset processor (null = use RawAssetProcessor).</param>
    /// <param name="state">State manager for incremental builds.</param>
    /// <param name="uploader">Uploader for asset service (null = local only).</param>
    /// <param name="options">Pipeline options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Bundle result with path and upload status.</returns>
    public async Task<BundleResult> ExecuteAsync(
        IAssetSource source,
        IAssetProcessor? processor,
        BundlerStateManager state,
        IAssetUploader? uploader,
        BundlerOptions options,
        CancellationToken ct = default)
    {
        // Check if processing needed
        if (!options.ForceRebuild && !state.NeedsProcessing(source))
        {
            _logger?.LogInformation("Skipping {SourceId} - unchanged since last build", source.SourceId);
            return BundleResult.Skipped(source.SourceId);
        }

        var safeSourceId = source.SourceId.Replace('/', '_').Replace('\\', '_');
        var workingDir = new DirectoryInfo(Path.Combine(options.WorkingDirectory, safeSourceId));
        workingDir.Create();

        try
        {
            // Extract
            _logger?.LogInformation("Extracting {SourceId}...", source.SourceId);
            var typeInferencer = processor?.TypeInferencer;
            var extracted = await source.ExtractAsync(workingDir, typeInferencer, ct);

            if (extracted.Assets.Count == 0)
            {
                _logger?.LogWarning("No assets extracted from {SourceId}", source.SourceId);
                return BundleResult.Empty(source.SourceId);
            }

            _logger?.LogInformation(
                "Extracted {AssetCount} assets from {SourceId} ({TotalSize:N0} bytes)",
                extracted.Assets.Count,
                source.SourceId,
                extracted.TotalSizeBytes);

            // Process
            processor ??= RawAssetProcessor.Instance;
            _logger?.LogInformation(
                "Processing {AssetCount} assets with {ProcessorId}...",
                extracted.Assets.Count,
                processor.ProcessorId);

            var processed = await processor.ProcessAsync(
                extracted.Assets,
                workingDir,
                options.ProcessorOptions,
                ct);

            // Bundle
            Directory.CreateDirectory(options.OutputDirectory);
            var bundlePath = Path.Combine(options.OutputDirectory, $"{safeSourceId}.bannou");

            _logger?.LogInformation("Creating bundle at {BundlePath}...", bundlePath);
            await WriteBundleAsync(source, processed, bundlePath, options, ct);

            // Record state
            var record = new SourceProcessingRecord
            {
                SourceId = source.SourceId,
                ContentHash = source.ContentHash,
                Version = source.Version,
                ProcessedAt = DateTimeOffset.UtcNow,
                BundlePath = bundlePath,
                AssetCount = processed.Count
            };

            // Upload if uploader provided
            if (uploader != null)
            {
                _logger?.LogInformation("Uploading bundle to Bannou...");
                var uploadResult = await uploader.UploadAsync(bundlePath, source.SourceId, ct: ct);
                record.UploadedAt = DateTimeOffset.UtcNow;
                record.UploadedBundleId = uploadResult.BundleId;
            }

            state.RecordProcessed(record);

            _logger?.LogInformation(
                "Successfully bundled {SourceId}: {AssetCount} assets",
                source.SourceId,
                processed.Count);

            return new BundleResult
            {
                SourceId = source.SourceId,
                Status = BundleResultStatus.Success,
                BundlePath = bundlePath,
                AssetCount = processed.Count,
                UploadedBundleId = record.UploadedBundleId
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to bundle {SourceId}", source.SourceId);
            return BundleResult.Failed(source.SourceId, ex.Message);
        }
        finally
        {
            // Cleanup working directory if configured
            if (options.CleanupWorkingDirectory && workingDir.Exists)
            {
                try
                {
                    workingDir.Delete(recursive: true);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to cleanup working directory: {Path}", workingDir.FullName);
                }
            }
        }
    }

    /// <summary>
    /// Executes the pipeline for multiple sources with parallel processing.
    /// </summary>
    /// <param name="sources">Asset sources to process.</param>
    /// <param name="processor">Asset processor (null = use RawAssetProcessor).</param>
    /// <param name="state">State manager for incremental builds.</param>
    /// <param name="uploader">Uploader for asset service (null = local only).</param>
    /// <param name="options">Pipeline options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Results for all sources.</returns>
    public async Task<IReadOnlyList<BundleResult>> ExecuteBatchAsync(
        IAsyncEnumerable<IAssetSource> sources,
        IAssetProcessor? processor,
        BundlerStateManager state,
        IAssetUploader? uploader,
        BundlerOptions options,
        CancellationToken ct = default)
    {
        var results = new List<BundleResult>();
        var semaphore = new SemaphoreSlim(options.MaxParallelSources);
        var tasks = new List<Task<BundleResult>>();

        await foreach (var source in sources.WithCancellation(ct))
        {
            await semaphore.WaitAsync(ct);

            var capturedSource = source;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    return await ExecuteAsync(capturedSource, processor, state, uploader, options, ct);
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        results.AddRange(await Task.WhenAll(tasks));
        return results;
    }

    private async Task WriteBundleAsync(
        IAssetSource source,
        IReadOnlyDictionary<string, IProcessedAsset> assets,
        string bundlePath,
        BundlerOptions options,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(bundlePath) ?? ".");

        await using var fileStream = File.Create(bundlePath);
        using var writer = new BannouBundleWriter(fileStream);

        foreach (var (assetId, asset) in assets)
        {
            ct.ThrowIfCancellationRequested();

            // Convert metadata to Dictionary<string, object>? for the writer
            IReadOnlyDictionary<string, object>? metadata = asset.Metadata.Count > 0
                ? asset.Metadata
                : null;

            writer.AddAsset(
                assetId,
                asset.Filename,
                asset.ContentType,
                asset.Data.Span,
                metadata: metadata);

            // Add dependencies as separate assets
            foreach (var (depId, depData) in asset.Dependencies)
            {
                writer.AddAsset(
                    depId,
                    depId,
                    "application/octet-stream",
                    depData.Span);
            }
        }

        await writer.FinalizeAsync(
            source.SourceId,
            source.Name,
            source.Version,
            options.CreatedBy ?? "asset-bundler-sdk",
            description: null,
            source.Tags,
            ct);
    }
}
