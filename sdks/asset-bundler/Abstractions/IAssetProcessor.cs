using BeyondImmersion.Bannou.AssetBundler.Extraction;

namespace BeyondImmersion.Bannou.AssetBundler.Abstractions;

/// <summary>
/// Processes extracted assets into engine-specific format.
/// Implementations: StrideBatchCompiler, GodotAssetProcessor, RawAssetProcessor (pass-through).
/// </summary>
public interface IAssetProcessor
{
    /// <summary>
    /// Processor identifier (e.g., "stride", "godot", "raw").
    /// </summary>
    string ProcessorId { get; }

    /// <summary>
    /// Content types this processor outputs (e.g., "application/x-stride-model").
    /// Empty for pass-through processors that preserve original types.
    /// </summary>
    IReadOnlyList<string> OutputContentTypes { get; }

    /// <summary>
    /// Gets the type inferencer appropriate for this processor's assets.
    /// May return null if the processor has no specific type inference needs.
    /// </summary>
    IAssetTypeInferencer? TypeInferencer { get; }

    /// <summary>
    /// Processes extracted assets into engine-ready format.
    /// </summary>
    /// <param name="assets">Assets to process.</param>
    /// <param name="workingDir">Working directory for intermediate files.</param>
    /// <param name="options">Processor-specific options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary of asset ID to processed result.</returns>
    Task<IReadOnlyDictionary<string, IProcessedAsset>> ProcessAsync(
        IReadOnlyList<ExtractedAsset> assets,
        DirectoryInfo workingDir,
        ProcessorOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>
/// Base options for asset processors.
/// </summary>
public class ProcessorOptions
{
    /// <summary>
    /// Whether to fail on first error or continue processing.
    /// </summary>
    public bool FailFast { get; init; }

    /// <summary>
    /// Maximum parallel processing operations.
    /// </summary>
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;
}
