using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Asset.Processing;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Storage;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using StorageModels = BeyondImmersion.BannouService.Storage;

namespace BeyondImmersion.BannouService.Asset.Tests.Processing;

/// <summary>
/// Unit tests for AudioProcessor with mocked FFmpegService.
/// </summary>
public class AudioProcessorTests
{
    private readonly Mock<IAssetStorageProvider> _mockStorageProvider;
    private readonly Mock<IFFmpegService> _mockFfmpegService;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<ILogger<AudioProcessor>> _mockLogger;
    private readonly AssetServiceConfiguration _configuration;

    public AudioProcessorTests()
    {
        _mockStorageProvider = new Mock<IAssetStorageProvider>();
        _mockFfmpegService = new Mock<IFFmpegService>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockLogger = new Mock<ILogger<AudioProcessor>>();
        _configuration = new AssetServiceConfiguration
        {
            StorageBucket = "test-bucket",
            MaxUploadSizeMb = 500,
            AudioOutputFormat = AudioOutputFormat.Mp3,
            AudioBitrateKbps = 192,
            AudioPreserveLossless = true
        };
    }

    private AudioProcessor CreateProcessor() =>
        new AudioProcessor(
            _mockStorageProvider.Object,
            _mockFfmpegService.Object,
            _mockTelemetryProvider.Object,
            _mockLogger.Object,
            _configuration);

    private static AssetProcessingContext CreateContext(
        string assetId = "test-asset",
        string contentType = "audio/wav",
        long sizeBytes = 1024,
        string storageKey = "temp/test.wav",
        string filename = "test.wav",
        string owner = "test-owner",
        Dictionary<string, object>? processingOptions = null)
    {
        return new AssetProcessingContext
        {
            AssetId = assetId,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            StorageKey = storageKey,
            Filename = filename,
            Owner = owner,
            ProcessingOptions = processingOptions
        };
    }

    #region Constructor Tests

    /// <summary>
    /// Validates the AudioProcessor constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void AudioProcessor_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<AudioProcessor>();

    #endregion

    #region CanProcess Tests

    [Theory]
    [InlineData("audio/mpeg", true)]
    [InlineData("audio/mp3", true)]
    [InlineData("audio/wav", true)]
    [InlineData("audio/x-wav", true)]
    [InlineData("audio/ogg", true)]
    [InlineData("audio/opus", true)]
    [InlineData("audio/flac", true)]
    [InlineData("audio/aac", true)]
    [InlineData("audio/webm", true)]
    [InlineData("image/png", false)]
    [InlineData("video/mp4", false)]
    [InlineData("application/json", false)]
    public void CanProcess_ReturnsExpectedResult(string contentType, bool expected)
    {
        var processor = CreateProcessor();
        var result = processor.CanProcess(contentType);
        Assert.Equal(expected, result);
    }

    #endregion

    #region ValidateAsync Tests

    [Fact]
    public async Task ValidateAsync_WithUnsupportedContentType_ReturnsInvalid()
    {
        var processor = CreateProcessor();
        var context = CreateContext(contentType: "video/mp4");

        var result = await processor.ValidateAsync(context);

        Assert.False(result.IsValid);
        Assert.Equal(ProcessingErrorCode.UnsupportedContentType, result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_WithFileTooLarge_ReturnsInvalid()
    {
        var processor = CreateProcessor();
        var context = CreateContext(sizeBytes: 600L * 1024 * 1024); // 600MB, exceeds 500MB limit

        var result = await processor.ValidateAsync(context);

        Assert.False(result.IsValid);
        Assert.Equal(ProcessingErrorCode.FileTooLarge, result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_WithValidAudio_ReturnsValid()
    {
        var processor = CreateProcessor();
        var context = CreateContext(contentType: "audio/mp3", sizeBytes: 10 * 1024 * 1024);

        var result = await processor.ValidateAsync(context);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WithLosslessFormat_ReturnsWarning()
    {
        var processor = CreateProcessor();
        var context = CreateContext(contentType: "audio/wav", sizeBytes: 10 * 1024 * 1024);

        var result = await processor.ValidateAsync(context);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("Lossless original will be preserved"));
    }

    #endregion

    #region ProcessAsync Tests

    [Fact]
    public async Task ProcessAsync_WavToMp3_CallsFFmpegWithCorrectParams()
    {
        var processor = CreateProcessor();
        var assetId = "test-asset-123";
        var context = CreateContext(
            assetId: assetId,
            contentType: "audio/wav",
            storageKey: "temp/test.wav",
            filename: "test.wav");

        // Setup storage provider to return a test stream
        var testWavData = new MemoryStream(new byte[1024]);
        _mockStorageProvider
            .Setup(s => s.GetObjectAsync("test-bucket", "temp/test.wav", It.IsAny<string?>()))
            .ReturnsAsync(testWavData);

        // Setup FFmpeg service to return success
        var outputStream = new MemoryStream(new byte[512]);
        _mockFfmpegService
            .Setup(f => f.ConvertAudioAsync(
                It.IsAny<Stream>(),
                "wav",
                "mp3",
                192,
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(FFmpegResult.Succeeded(outputStream, 512, 100));

        // Setup storage provider put to succeed
        _mockStorageProvider
            .Setup(s => s.PutObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>?>()))
            .ReturnsAsync(new StorageModels.AssetReference("test-bucket", "processed/test.mp3", null, "etag", 512, DateTime.UtcNow));

        var result = await processor.ProcessAsync(context);

        Assert.True(result.Success);
        Assert.Equal($"processed/{assetId}/transcoded.mp3", result.ProcessedStorageKey);
        Assert.Equal("audio/mpeg", result.ProcessedContentType);

        // Verify FFmpeg was called with correct parameters
        _mockFfmpegService.Verify(f => f.ConvertAudioAsync(
            It.IsAny<Stream>(),
            "wav",
            "mp3",
            192,
            true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_FFmpegFails_ReturnsFailedResult()
    {
        var processor = CreateProcessor();
        var context = CreateContext(
            contentType: "audio/mp3",
            storageKey: "temp/test.mp3",
            filename: "test.mp3");

        // Setup storage provider to return a test stream
        var testData = new MemoryStream(new byte[1024]);
        _mockStorageProvider
            .Setup(s => s.GetObjectAsync("test-bucket", "temp/test.mp3", It.IsAny<string?>()))
            .ReturnsAsync(testData);

        // Setup FFmpeg service to return failure
        _mockFfmpegService
            .Setup(f => f.ConvertAudioAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(FFmpegResult.Failed("FFmpeg crashed", 50));

        var result = await processor.ProcessAsync(context);

        Assert.False(result.Success);
        Assert.Equal(ProcessingErrorCode.TranscodingFailed, result.ErrorCode);
    }

    [Fact]
    public async Task ProcessAsync_SourceNotFound_ReturnsFailedResult()
    {
        var processor = CreateProcessor();
        var context = CreateContext(
            contentType: "audio/wav",
            storageKey: "temp/missing.wav",
            filename: "missing.wav");

        // Setup storage provider to return null (file not found)
        _mockStorageProvider
            .Setup(s => s.GetObjectAsync("test-bucket", "temp/missing.wav", It.IsAny<string?>()))
            .ReturnsAsync((Stream?)null);

        var result = await processor.ProcessAsync(context);

        Assert.False(result.Success);
        Assert.Equal(ProcessingErrorCode.SourceNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task ProcessAsync_UsesConfigurationDefaults()
    {
        // Create processor with specific config
        var config = new AssetServiceConfiguration
        {
            StorageBucket = "test-bucket",
            MaxUploadSizeMb = 500,
            AudioOutputFormat = AudioOutputFormat.Opus, // Different default
            AudioBitrateKbps = 128,     // Different bitrate
            AudioPreserveLossless = false // Don't preserve
        };

        var processor = new AudioProcessor(
            _mockStorageProvider.Object,
            _mockFfmpegService.Object,
            _mockTelemetryProvider.Object,
            _mockLogger.Object,
            config);

        var context = CreateContext(
            contentType: "audio/wav",
            storageKey: "temp/test.wav",
            filename: "test.wav");

        var testData = new MemoryStream(new byte[1024]);
        _mockStorageProvider
            .Setup(s => s.GetObjectAsync("test-bucket", "temp/test.wav", It.IsAny<string?>()))
            .ReturnsAsync(testData);

        var outputStream = new MemoryStream(new byte[256]);
        _mockFfmpegService
            .Setup(f => f.ConvertAudioAsync(
                It.IsAny<Stream>(),
                "wav",
                "opus",  // Should use config default
                128,     // Should use config bitrate
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(FFmpegResult.Succeeded(outputStream, 256, 100));

        _mockStorageProvider
            .Setup(s => s.PutObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>?>()))
            .ReturnsAsync(new StorageModels.AssetReference("test-bucket", "processed/test.opus", null, "etag", 256, DateTime.UtcNow));

        var result = await processor.ProcessAsync(context);

        Assert.True(result.Success);

        // Verify config defaults were used
        _mockFfmpegService.Verify(f => f.ConvertAudioAsync(
            It.IsAny<Stream>(),
            "wav",
            "opus",
            128,
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAsync_WithCustomProcessingOptions_OverridesDefaults()
    {
        var processor = CreateProcessor();
        var context = CreateContext(
            contentType: "audio/mp3",
            storageKey: "temp/test.mp3",
            filename: "test.mp3",
            processingOptions: new Dictionary<string, object>
            {
                ["target_format"] = "aac",
                ["bitrate"] = 256,
                ["normalize"] = false
            });

        var testData = new MemoryStream(new byte[1024]);
        _mockStorageProvider
            .Setup(s => s.GetObjectAsync("test-bucket", "temp/test.mp3", It.IsAny<string?>()))
            .ReturnsAsync(testData);

        var outputStream = new MemoryStream(new byte[384]);
        _mockFfmpegService
            .Setup(f => f.ConvertAudioAsync(
                It.IsAny<Stream>(),
                "mp3",
                "aac",   // Custom format
                256,     // Custom bitrate
                false,   // Custom normalize
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(FFmpegResult.Succeeded(outputStream, 384, 100));

        _mockStorageProvider
            .Setup(s => s.PutObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<IDictionary<string, string>?>()))
            .ReturnsAsync(new StorageModels.AssetReference("test-bucket", "processed/test.aac", null, "etag", 384, DateTime.UtcNow));

        var result = await processor.ProcessAsync(context);

        Assert.True(result.Success);

        // Verify custom options were used
        _mockFfmpegService.Verify(f => f.ConvertAudioAsync(
            It.IsAny<Stream>(),
            "mp3",
            "aac",
            256,
            false,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region PoolType and SupportedContentTypes Tests

    [Fact]
    public void PoolType_ReturnsAudioProcessor()
    {
        var processor = CreateProcessor();
        Assert.Equal("audio-processor", processor.PoolType);
    }

    [Fact]
    public void SupportedContentTypes_ContainsExpectedTypes()
    {
        var processor = CreateProcessor();
        var types = processor.SupportedContentTypes;

        Assert.Contains("audio/mpeg", types);
        Assert.Contains("audio/wav", types);
        Assert.Contains("audio/flac", types);
        Assert.Contains("audio/opus", types);
    }

    #endregion
}
