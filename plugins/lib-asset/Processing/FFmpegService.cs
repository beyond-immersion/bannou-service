using FFMpegCore;
using FFMpegCore.Pipes;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Asset.Processing;

/// <summary>
/// FFmpeg-based audio transcoding service using FFMpegCore.
/// Uses process isolation (LGPL compliant - no code linking).
/// </summary>
public sealed class FFmpegService : IFFmpegService
{
    private readonly AssetServiceConfiguration _configuration;
    private readonly ILogger<FFmpegService> _logger;
    private readonly string _workingDirectory;

    /// <summary>
    /// Creates a new FFmpegService instance.
    /// </summary>
    public FFmpegService(
        AssetServiceConfiguration configuration,
        ILogger<FFmpegService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        // Ensure working directory exists
        _workingDirectory = string.IsNullOrEmpty(_configuration.FfmpegWorkingDirectory)
            ? Path.Combine(Path.GetTempPath(), "bannou-ffmpeg")
            : _configuration.FfmpegWorkingDirectory;

        Directory.CreateDirectory(_workingDirectory);

        // Configure FFMpegCore if custom path is specified
        if (!string.IsNullOrEmpty(_configuration.FfmpegPath))
        {
            var binaryFolder = Path.GetDirectoryName(_configuration.FfmpegPath);
            if (string.IsNullOrEmpty(binaryFolder))
            {
                throw new InvalidOperationException(
                    $"Invalid FfmpegPath configuration: '{_configuration.FfmpegPath}' - cannot extract directory. " +
                    "FfmpegPath must be a full path to the ffmpeg binary, not a root path or bare filename.");
            }

            GlobalFFOptions.Configure(options =>
            {
                options.BinaryFolder = binaryFolder;
            });
        }
    }

    /// <inheritdoc />
    public async Task<FFmpegResult> ConvertAudioAsync(
        Stream inputStream,
        string inputFormat,
        string outputFormat,
        int bitrateKbps,
        bool normalize,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug(
                "Starting audio conversion: {InputFormat} -> {OutputFormat}, bitrate={BitrateKbps}kbps, normalize={Normalize}",
                inputFormat, outputFormat, bitrateKbps, normalize);

            // Create a temp file for input since FFMpegCore pipe input can be unreliable for some formats
            var inputTempPath = Path.Combine(_workingDirectory, $"{Guid.NewGuid():N}.{inputFormat}");
            var outputTempPath = Path.Combine(_workingDirectory, $"{Guid.NewGuid():N}.{outputFormat}");

            try
            {
                // Write input stream to temp file
                await using (var inputFile = File.Create(inputTempPath))
                {
                    await inputStream.CopyToAsync(inputFile, cancellationToken);
                }

                // Build FFmpeg arguments
                var audioCodec = GetAudioCodecForFormat(outputFormat);

                var arguments = FFMpegArguments
                    .FromFileInput(inputTempPath)
                    .OutputToFile(outputTempPath, true, options =>
                    {
                        options
                            .WithAudioCodec(audioCodec)
                            .WithAudioBitrate(bitrateKbps)
                            .ForceFormat(outputFormat);

                        // Apply normalization filter if requested
                        if (normalize)
                        {
                            options.WithCustomArgument("-af loudnorm=I=-16:TP=-1.5:LRA=11");
                        }
                    });

                // Execute FFmpeg
                var success = await arguments.ProcessAsynchronously(true, new FFOptions
                {
                    WorkingDirectory = _workingDirectory,
                    TemporaryFilesFolder = _workingDirectory
                });

                stopwatch.Stop();

                if (!success || !File.Exists(outputTempPath))
                {
                    _logger.LogError("FFmpeg conversion failed for {InputFormat} -> {OutputFormat}", inputFormat, outputFormat);
                    return FFmpegResult.Failed("FFmpeg conversion failed", stopwatch.ElapsedMilliseconds);
                }

                // Read output into memory stream (caller will upload to storage)
                var outputStream = new MemoryStream();
                await using (var outputFile = File.OpenRead(outputTempPath))
                {
                    await outputFile.CopyToAsync(outputStream, cancellationToken);
                }
                outputStream.Position = 0;

                var outputSize = outputStream.Length;

                _logger.LogInformation(
                    "Audio conversion completed: {InputFormat} -> {OutputFormat}, size={OutputSize} bytes, duration={Duration}ms",
                    inputFormat, outputFormat, outputSize, stopwatch.ElapsedMilliseconds);

                return FFmpegResult.Succeeded(outputStream, outputSize, stopwatch.ElapsedMilliseconds);
            }
            finally
            {
                // Cleanup temp files
                TryDeleteFile(inputTempPath);
                TryDeleteFile(outputTempPath);
            }
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogWarning("Audio conversion cancelled");
            return FFmpegResult.Failed("Operation cancelled", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Audio conversion failed with exception");
            return FFmpegResult.Failed(ex.Message, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <inheritdoc />
    public async Task<AudioMetadata?> ProbeAudioAsync(
        Stream inputStream,
        string inputFormat,
        CancellationToken cancellationToken = default)
    {
        var inputTempPath = Path.Combine(_workingDirectory, $"{Guid.NewGuid():N}.{inputFormat}");

        try
        {
            // Write input stream to temp file for probing
            await using (var inputFile = File.Create(inputTempPath))
            {
                await inputStream.CopyToAsync(inputFile, cancellationToken);
            }

            // Reset stream position for caller
            if (inputStream.CanSeek)
            {
                inputStream.Position = 0;
            }

            var analysis = await FFProbe.AnalyseAsync(inputTempPath, cancellationToken: cancellationToken);

            if (analysis?.PrimaryAudioStream == null)
            {
                _logger.LogWarning("No audio stream found in input");
                return null;
            }

            var audio = analysis.PrimaryAudioStream;

            return new AudioMetadata(
                DurationSeconds: analysis.Duration.TotalSeconds,
                SampleRate: audio.SampleRateHz,
                Channels: audio.Channels,
                BitRate: audio.BitRate,
                Codec: audio.CodecName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to probe audio file");
            return null;
        }
        finally
        {
            TryDeleteFile(inputTempPath);
        }
    }

    /// <summary>
    /// Gets the appropriate audio codec for the given output format.
    /// Uses LGPL-compliant codecs only.
    /// </summary>
    private static string GetAudioCodecForFormat(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "mp3" => "libmp3lame",    // LGPL
            "opus" => "libopus",       // BSD
            "aac" => "aac",            // LGPL (FFmpeg native)
            "flac" => "flac",          // BSD
            "ogg" => "libvorbis",      // BSD
            "wav" => "pcm_s16le",      // No encoding
            "webm" => "libopus",       // BSD
            _ => "libmp3lame"          // Default to MP3
        };
    }

    /// <summary>
    /// Attempts to delete a file, logging any errors but not throwing.
    /// </summary>
    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temporary file: {Path}", path);
        }
    }
}
