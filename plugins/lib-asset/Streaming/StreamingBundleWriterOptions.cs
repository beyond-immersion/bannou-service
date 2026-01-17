namespace BeyondImmersion.BannouService.Asset.Streaming;

/// <summary>
/// Configuration options for streaming bundle writer.
/// Controls memory usage and performance during streaming metabundle assembly.
/// </summary>
public sealed class StreamingBundleWriterOptions
{
    /// <summary>
    /// Maximum memory in bytes for streaming operations.
    /// Limits total buffer allocation during streaming metabundle assembly.
    /// Default: 100MB.
    /// </summary>
    public long MaxMemoryBytes { get; init; } = 100 * 1024 * 1024;

    /// <summary>
    /// Size of each part in bytes for streaming multipart uploads.
    /// S3/MinIO requires minimum 5MB per part except for the last part.
    /// Default: 50MB.
    /// </summary>
    public long PartSizeBytes { get; init; } = 50 * 1024 * 1024;

    /// <summary>
    /// Maximum number of source bundles to stream concurrently.
    /// Higher values use more memory but improve throughput.
    /// Default: 2.
    /// </summary>
    public int MaxConcurrentSourceStreams { get; init; } = 2;

    /// <summary>
    /// Size of compression buffer in bytes for LZ4 streaming compression.
    /// Larger buffers improve compression ratio but use more memory.
    /// Default: 16MB.
    /// </summary>
    public int CompressionBufferSize { get; init; } = 16 * 1024 * 1024;

    /// <summary>
    /// Number of assets to process before updating job progress.
    /// Lower values give more frequent updates but add overhead.
    /// Default: 10.
    /// </summary>
    public int ProgressUpdateIntervalAssets { get; init; } = 10;

    /// <summary>
    /// Creates options from the asset service configuration.
    /// </summary>
    /// <param name="config">The asset service configuration.</param>
    /// <returns>Configured streaming options.</returns>
    public static StreamingBundleWriterOptions FromConfiguration(AssetServiceConfiguration config)
    {
        return new StreamingBundleWriterOptions
        {
            MaxMemoryBytes = config.StreamingMaxMemoryMb * 1024L * 1024L,
            PartSizeBytes = config.StreamingPartSizeMb * 1024L * 1024L,
            MaxConcurrentSourceStreams = config.StreamingMaxConcurrentSourceStreams,
            CompressionBufferSize = config.StreamingCompressionBufferKb * 1024,
            ProgressUpdateIntervalAssets = config.StreamingProgressUpdateIntervalAssets
        };
    }
}
