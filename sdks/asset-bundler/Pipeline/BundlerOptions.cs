using BeyondImmersion.Bannou.AssetBundler.Abstractions;

namespace BeyondImmersion.Bannou.AssetBundler.Pipeline;

/// <summary>
/// Pipeline configuration options.
/// </summary>
public sealed class BundlerOptions
{
    /// <summary>
    /// Working directory for extraction and processing.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Output directory for generated bundles.
    /// </summary>
    public required string OutputDirectory { get; init; }

    /// <summary>
    /// Force rebuild even if source unchanged.
    /// </summary>
    public bool ForceRebuild { get; init; }

    /// <summary>
    /// Delete working directory after bundling.
    /// </summary>
    public bool CleanupWorkingDirectory { get; init; } = true;

    /// <summary>
    /// Maximum sources to process in parallel.
    /// </summary>
    public int MaxParallelSources { get; init; } = 4;

    /// <summary>
    /// Creator identifier for bundle metadata.
    /// </summary>
    public string? CreatedBy { get; init; }

    /// <summary>
    /// Options passed to the asset processor.
    /// </summary>
    public ProcessorOptions? ProcessorOptions { get; init; }
}

/// <summary>
/// Result of bundling a single source.
/// </summary>
public sealed class BundleResult
{
    /// <summary>
    /// Source identifier.
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// Result status.
    /// </summary>
    public required BundleResultStatus Status { get; init; }

    /// <summary>
    /// Path to the generated bundle (if successful).
    /// </summary>
    public string? BundlePath { get; init; }

    /// <summary>
    /// Number of assets in the bundle.
    /// </summary>
    public int AssetCount { get; init; }

    /// <summary>
    /// Bundle ID assigned by server after upload (if applicable).
    /// </summary>
    public string? UploadedBundleId { get; init; }

    /// <summary>
    /// Error message (if failed).
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a skipped result.
    /// </summary>
    public static BundleResult Skipped(string sourceId) => new()
    {
        SourceId = sourceId,
        Status = BundleResultStatus.Skipped
    };

    /// <summary>
    /// Creates an empty result (no assets extracted).
    /// </summary>
    public static BundleResult Empty(string sourceId) => new()
    {
        SourceId = sourceId,
        Status = BundleResultStatus.Empty
    };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static BundleResult Failed(string sourceId, string errorMessage) => new()
    {
        SourceId = sourceId,
        Status = BundleResultStatus.Failed,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Status of a bundle operation.
/// </summary>
public enum BundleResultStatus
{
    /// <summary>
    /// Bundle created successfully.
    /// </summary>
    Success,

    /// <summary>
    /// Source unchanged, skipped processing.
    /// </summary>
    Skipped,

    /// <summary>
    /// No assets extracted from source.
    /// </summary>
    Empty,

    /// <summary>
    /// Processing failed with error.
    /// </summary>
    Failed
}
