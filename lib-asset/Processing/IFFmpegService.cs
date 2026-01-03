namespace BeyondImmersion.BannouService.Asset.Processing;

/// <summary>
/// Service for audio/video transcoding operations using FFmpeg.
/// </summary>
public interface IFFmpegService
{
    /// <summary>
    /// Converts audio from one format to another.
    /// </summary>
    /// <param name="inputStream">The input audio stream.</param>
    /// <param name="inputFormat">The input format (e.g., "wav", "flac").</param>
    /// <param name="outputFormat">The target output format (e.g., "mp3", "opus", "aac").</param>
    /// <param name="bitrateKbps">The target bitrate in kbps.</param>
    /// <param name="normalize">Whether to apply audio normalization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The transcoding result containing the output stream.</returns>
    Task<FFmpegResult> ConvertAudioAsync(
        Stream inputStream,
        string inputFormat,
        string outputFormat,
        int bitrateKbps,
        bool normalize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Probes an audio stream to get its metadata.
    /// </summary>
    /// <param name="inputStream">The input audio stream.</param>
    /// <param name="inputFormat">The input format hint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Audio metadata if successful, null otherwise.</returns>
    Task<AudioMetadata?> ProbeAudioAsync(
        Stream inputStream,
        string inputFormat,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an FFmpeg transcoding operation.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="OutputStream">The output stream containing transcoded data (caller must dispose).</param>
/// <param name="OutputSizeBytes">Size of the output in bytes.</param>
/// <param name="ErrorMessage">Error message if the operation failed.</param>
/// <param name="ProcessingTimeMs">Time taken to process in milliseconds.</param>
public record FFmpegResult(
    bool Success,
    Stream? OutputStream,
    long OutputSizeBytes,
    string? ErrorMessage,
    long ProcessingTimeMs)
{
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static FFmpegResult Succeeded(Stream outputStream, long outputSizeBytes, long processingTimeMs) =>
        new(true, outputStream, outputSizeBytes, null, processingTimeMs);

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static FFmpegResult Failed(string errorMessage, long processingTimeMs) =>
        new(false, null, 0, errorMessage, processingTimeMs);
}

/// <summary>
/// Metadata about an audio file.
/// </summary>
/// <param name="DurationSeconds">Duration in seconds.</param>
/// <param name="SampleRate">Sample rate in Hz.</param>
/// <param name="Channels">Number of audio channels.</param>
/// <param name="BitRate">Bit rate in bits per second.</param>
/// <param name="Codec">Audio codec name.</param>
public record AudioMetadata(
    double DurationSeconds,
    int SampleRate,
    int Channels,
    long BitRate,
    string Codec);
