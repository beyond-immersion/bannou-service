using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BeyondImmersion.BannouService.Asset.Metrics;

/// <summary>
/// Prometheus-compatible metrics for the Asset service.
/// Uses System.Diagnostics.Metrics which can be exported via OpenTelemetry.
/// </summary>
public sealed class AssetMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _uploadsTotal;
    private readonly Counter<long> _downloadsTotal;
    private readonly Counter<long> _bundleCreationsTotal;
    private readonly Counter<long> _processingCompletedTotal;
    private readonly Counter<long> _processingFailedTotal;
    private readonly Histogram<double> _processingDurationSeconds;
    private readonly Histogram<double> _uploadDurationSeconds;
    private readonly Histogram<double> _downloadDurationSeconds;

    /// <summary>
    /// Meter name for OpenTelemetry export configuration.
    /// </summary>
    public const string MeterName = "BeyondImmersion.Bannou.Asset";

    /// <summary>
    /// Creates a new AssetMetrics instance with instrumentation.
    /// </summary>
    public AssetMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _uploadsTotal = _meter.CreateCounter<long>(
            "asset_uploads_total",
            unit: "{uploads}",
            description: "Total number of asset uploads");

        _downloadsTotal = _meter.CreateCounter<long>(
            "asset_downloads_total",
            unit: "{downloads}",
            description: "Total number of asset downloads");

        _bundleCreationsTotal = _meter.CreateCounter<long>(
            "asset_bundle_creations_total",
            unit: "{bundles}",
            description: "Total number of bundles created");

        _processingCompletedTotal = _meter.CreateCounter<long>(
            "asset_processing_completed_total",
            unit: "{operations}",
            description: "Total number of completed processing operations");

        _processingFailedTotal = _meter.CreateCounter<long>(
            "asset_processing_failed_total",
            unit: "{operations}",
            description: "Total number of failed processing operations");

        _processingDurationSeconds = _meter.CreateHistogram<double>(
            "asset_processing_duration_seconds",
            unit: "s",
            description: "Duration of asset processing operations");

        _uploadDurationSeconds = _meter.CreateHistogram<double>(
            "asset_upload_duration_seconds",
            unit: "s",
            description: "Duration of asset upload operations");

        _downloadDurationSeconds = _meter.CreateHistogram<double>(
            "asset_download_duration_seconds",
            unit: "s",
            description: "Duration of asset download operations");
    }

    /// <summary>
    /// Records an upload completion.
    /// </summary>
    /// <param name="assetType">Type of asset uploaded.</param>
    /// <param name="success">Whether the upload succeeded.</param>
    /// <param name="sizeBytes">Size of the uploaded asset in bytes.</param>
    public void RecordUpload(string assetType, bool success, long sizeBytes)
    {
        var tags = new TagList
        {
            { "asset_type", assetType },
            { "success", success.ToString().ToLowerInvariant() }
        };
        _uploadsTotal.Add(1, tags);
    }

    /// <summary>
    /// Records an upload duration.
    /// </summary>
    /// <param name="assetType">Type of asset uploaded.</param>
    /// <param name="durationSeconds">Duration of the upload in seconds.</param>
    public void RecordUploadDuration(string assetType, double durationSeconds)
    {
        var tags = new TagList
        {
            { "asset_type", assetType }
        };
        _uploadDurationSeconds.Record(durationSeconds, tags);
    }

    /// <summary>
    /// Records a download completion.
    /// </summary>
    /// <param name="assetType">Type of asset downloaded.</param>
    public void RecordDownload(string assetType)
    {
        var tags = new TagList
        {
            { "asset_type", assetType }
        };
        _downloadsTotal.Add(1, tags);
    }

    /// <summary>
    /// Records a download duration.
    /// </summary>
    /// <param name="assetType">Type of asset downloaded.</param>
    /// <param name="durationSeconds">Duration of the download in seconds.</param>
    public void RecordDownloadDuration(string assetType, double durationSeconds)
    {
        var tags = new TagList
        {
            { "asset_type", assetType }
        };
        _downloadDurationSeconds.Record(durationSeconds, tags);
    }

    /// <summary>
    /// Records a bundle creation.
    /// </summary>
    /// <param name="assetCount">Number of assets in the bundle.</param>
    /// <param name="success">Whether creation succeeded.</param>
    public void RecordBundleCreation(int assetCount, bool success)
    {
        var tags = new TagList
        {
            { "success", success.ToString().ToLowerInvariant() }
        };
        _bundleCreationsTotal.Add(1, tags);
    }

    /// <summary>
    /// Records a processing completion.
    /// </summary>
    /// <param name="processingType">Type of processing performed.</param>
    /// <param name="success">Whether processing succeeded.</param>
    /// <param name="durationSeconds">Duration of processing in seconds.</param>
    public void RecordProcessingCompletion(string processingType, bool success, double durationSeconds)
    {
        var tags = new TagList
        {
            { "processing_type", processingType },
            { "success", success.ToString().ToLowerInvariant() }
        };

        if (success)
        {
            _processingCompletedTotal.Add(1, tags);
        }
        else
        {
            _processingFailedTotal.Add(1, tags);
        }

        _processingDurationSeconds.Record(durationSeconds, tags);
    }

    /// <summary>
    /// Disposes the meter and releases resources.
    /// </summary>
    public void Dispose()
    {
        _meter.Dispose();
    }
}
