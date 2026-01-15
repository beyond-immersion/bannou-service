using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using BeyondImmersion.Bannou.AssetBundler.Extraction;
using BeyondImmersion.Bannou.AssetBundler.Pipeline;
using BeyondImmersion.Bannou.AssetBundler.State;
using BeyondImmersion.Bannou.AssetBundler.Upload;
using BeyondImmersion.Bannou.Bundle.Format;
using Moq;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Tests.Pipeline;

/// <summary>
/// Tests for BundlerPipeline orchestration.
/// Tests the full Extract → Process → Bundle → Upload workflow.
/// </summary>
public class BundlerPipelineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BundlerPipeline _pipeline;

    public BundlerPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"bundler-pipeline-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _pipeline = new BundlerPipeline();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithValidSource_CreatesBundleFile()
    {
        // Arrange
        var source = CreateMockSource("test/source", "Test Source", new[]
        {
            CreateExtractedAsset("asset1", "model.fbx", 1024),
            CreateExtractedAsset("asset2", "texture.png", 2048)
        });

        var state = CreateStateManager();
        var options = CreateOptions();

        // Act
        var result = await _pipeline.ExecuteAsync(source.Object, null, state, null, options);

        // Assert
        Assert.Equal(BundleResultStatus.Success, result.Status);
        Assert.Equal("test/source", result.SourceId);
        Assert.Equal(2, result.AssetCount);
        Assert.NotNull(result.BundlePath);
        Assert.True(File.Exists(result.BundlePath));
    }

    [Fact]
    public async Task ExecuteAsync_WithUploader_UploadsBundle()
    {
        // Arrange
        var source = CreateMockSource("test/uploaded", "Test Upload", new[]
        {
            CreateExtractedAsset("asset1", "model.fbx", 1024)
        });

        var uploader = new Mock<IAssetUploader>();
        uploader.Setup(u => u.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<UploadProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadResult
            {
                BundleId = "test/uploaded",
                UploadId = Guid.NewGuid(),
                SizeBytes = 1024
            });

        var state = CreateStateManager();
        var options = CreateOptions();

        // Act
        var result = await _pipeline.ExecuteAsync(source.Object, null, state, uploader.Object, options);

        // Assert
        Assert.Equal(BundleResultStatus.Success, result.Status);
        Assert.Equal("test/uploaded", result.UploadedBundleId);
        uploader.Verify(u => u.UploadAsync(
            It.Is<string>(p => p.EndsWith(".bannou")),
            "test/uploaded",
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_EmptySource_ReturnsEmpty()
    {
        // Arrange
        var source = CreateMockSource("test/empty", "Empty Source", Array.Empty<ExtractedAsset>());
        var state = CreateStateManager();
        var options = CreateOptions();

        // Act
        var result = await _pipeline.ExecuteAsync(source.Object, null, state, null, options);

        // Assert
        Assert.Equal(BundleResultStatus.Empty, result.Status);
        Assert.Equal("test/empty", result.SourceId);
    }

    [Fact]
    public async Task ExecuteAsync_UnchangedSource_SkipsProcessing()
    {
        // Arrange - first run
        var source = CreateMockSource("test/skip", "Skip Test", new[]
        {
            CreateExtractedAsset("asset1", "model.fbx", 1024)
        });

        var state = CreateStateManager();
        var options = CreateOptions();

        // First execution
        await _pipeline.ExecuteAsync(source.Object, null, state, null, options);

        // Act - second run with same content hash
        var result = await _pipeline.ExecuteAsync(source.Object, null, state, null, options);

        // Assert
        Assert.Equal(BundleResultStatus.Skipped, result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ForceRebuild_ProcessesUnchangedSource()
    {
        // Arrange - first run
        var source = CreateMockSource("test/force", "Force Test", new[]
        {
            CreateExtractedAsset("asset1", "model.fbx", 1024)
        });

        var state = CreateStateManager();
        var options = CreateOptions(forceRebuild: true);

        // First execution
        await _pipeline.ExecuteAsync(source.Object, null, state, null, options);

        // Act - second run with force rebuild
        var result = await _pipeline.ExecuteAsync(source.Object, null, state, null, options);

        // Assert
        Assert.Equal(BundleResultStatus.Success, result.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ExtractionFails_ReturnsFailed()
    {
        // Arrange
        var source = new Mock<IAssetSource>();
        source.Setup(s => s.SourceId).Returns("test/fail");
        source.Setup(s => s.Name).Returns("Failing Source");
        source.Setup(s => s.Version).Returns("1.0");
        source.Setup(s => s.ContentHash).Returns("hash123");
        source.Setup(s => s.Tags).Returns(new Dictionary<string, string>());
        source.Setup(s => s.ExtractAsync(
                It.IsAny<DirectoryInfo>(),
                It.IsAny<IAssetTypeInferencer>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Extraction failed"));

        var state = CreateStateManager();
        var options = CreateOptions();

        // Act
        var result = await _pipeline.ExecuteAsync(source.Object, null, state, null, options);

        // Assert
        Assert.Equal(BundleResultStatus.Failed, result.Status);
        Assert.Contains("Extraction failed", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_UploadFails_ReturnsFailed()
    {
        // Arrange
        var source = CreateMockSource("test/upload-fail", "Upload Fail", new[]
        {
            CreateExtractedAsset("asset1", "model.fbx", 1024)
        });

        var uploader = new Mock<IAssetUploader>();
        uploader.Setup(u => u.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<UploadProgress>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Upload failed"));

        var state = CreateStateManager();
        var options = CreateOptions();

        // Act
        var result = await _pipeline.ExecuteAsync(source.Object, null, state, uploader.Object, options);

        // Assert
        Assert.Equal(BundleResultStatus.Failed, result.Status);
        Assert.Contains("Upload failed", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesValidBundle()
    {
        // Arrange
        var source = CreateMockSource("test/valid", "Valid Bundle", new[]
        {
            CreateExtractedAsset("asset1", "model.fbx", 1024, "application/x-fbx"),
            CreateExtractedAsset("asset2", "texture.png", 2048, "image/png")
        });

        var state = CreateStateManager();
        var options = CreateOptions();

        // Act
        var result = await _pipeline.ExecuteAsync(source.Object, null, state, null, options);

        // Assert - verify bundle can be read
        Assert.Equal(BundleResultStatus.Success, result.Status);
        Assert.NotNull(result.BundlePath);

        await using var stream = File.OpenRead(result.BundlePath);
        using var reader = new BannouBundleReader(stream);
        await reader.ReadHeaderAsync();

        Assert.NotNull(reader.Manifest);
        Assert.Equal("test/valid", reader.Manifest.BundleId);
        Assert.Equal(2, reader.Manifest.Assets.Count);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsStateAfterSuccess()
    {
        // Arrange
        var source = CreateMockSource("test/state", "State Test", new[]
        {
            CreateExtractedAsset("asset1", "model.fbx", 1024)
        });

        var state = CreateStateManager();
        var options = CreateOptions();

        // Act
        await _pipeline.ExecuteAsync(source.Object, null, state, null, options);

        // Assert - verify state was recorded
        Assert.False(state.NeedsProcessing(source.Object));
    }

    #endregion

    #region ExecuteBatchAsync Tests

    [Fact]
    public async Task ExecuteBatchAsync_MultipleSources_ProcessesAll()
    {
        // Arrange
        var sources = new[]
        {
            CreateMockSource("batch/source1", "Source 1", new[]
            {
                CreateExtractedAsset("asset1", "model1.fbx", 1024)
            }),
            CreateMockSource("batch/source2", "Source 2", new[]
            {
                CreateExtractedAsset("asset2", "model2.fbx", 2048)
            }),
            CreateMockSource("batch/source3", "Source 3", new[]
            {
                CreateExtractedAsset("asset3", "model3.fbx", 3072)
            })
        };

        var state = CreateStateManager();
        var options = CreateOptions();

        // Act
        var results = await _pipeline.ExecuteBatchAsync(
            ToAsyncEnumerable(sources.Select(s => s.Object)),
            null,
            state,
            null,
            options);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(BundleResultStatus.Success, r.Status));
    }

    [Fact]
    public async Task ExecuteBatchAsync_MixedResults_ReturnsAllResults()
    {
        // Arrange
        var successSource = CreateMockSource("batch/success", "Success", new[]
        {
            CreateExtractedAsset("asset1", "model.fbx", 1024)
        });

        var emptySource = CreateMockSource("batch/empty", "Empty", Array.Empty<ExtractedAsset>());

        var state = CreateStateManager();
        var options = CreateOptions();

        // Act
        var results = await _pipeline.ExecuteBatchAsync(
            ToAsyncEnumerable(new[] { successSource.Object, emptySource.Object }),
            null,
            state,
            null,
            options);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Status == BundleResultStatus.Success);
        Assert.Contains(results, r => r.Status == BundleResultStatus.Empty);
    }

    [Fact]
    public async Task ExecuteBatchAsync_WithUploader_UploadsAll()
    {
        // Arrange
        var sources = new[]
        {
            CreateMockSource("batch/upload1", "Upload 1", new[]
            {
                CreateExtractedAsset("asset1", "model1.fbx", 1024)
            }),
            CreateMockSource("batch/upload2", "Upload 2", new[]
            {
                CreateExtractedAsset("asset2", "model2.fbx", 2048)
            })
        };

        var uploader = new Mock<IAssetUploader>();
        uploader.Setup(u => u.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<UploadProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, string id, IProgress<UploadProgress>? p, CancellationToken ct) =>
                new UploadResult { BundleId = id, UploadId = Guid.NewGuid(), SizeBytes = 1024 });

        var state = CreateStateManager();
        var options = CreateOptions();

        // Act
        var results = await _pipeline.ExecuteBatchAsync(
            ToAsyncEnumerable(sources.Select(s => s.Object)),
            null,
            state,
            uploader.Object,
            options);

        // Assert
        Assert.Equal(2, results.Count);
        uploader.Verify(u => u.UploadAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<IProgress<UploadProgress>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    #endregion

    #region Helper Methods

    private Mock<IAssetSource> CreateMockSource(
        string sourceId,
        string name,
        ExtractedAsset[] assets)
    {
        var mock = new Mock<IAssetSource>();
        mock.Setup(s => s.SourceId).Returns(sourceId);
        mock.Setup(s => s.Name).Returns(name);
        mock.Setup(s => s.Version).Returns("1.0");
        mock.Setup(s => s.ContentHash).Returns($"hash-{sourceId}");
        mock.Setup(s => s.Tags).Returns(new Dictionary<string, string>());

        mock.Setup(s => s.ExtractAsync(
                It.IsAny<DirectoryInfo>(),
                It.IsAny<IAssetTypeInferencer>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DirectoryInfo workDir, IAssetTypeInferencer? _, CancellationToken _) =>
            {
                // Create actual files for the extracted assets
                var extractedAssets = new List<ExtractedAsset>();
                long totalSize = 0;

                foreach (var asset in assets)
                {
                    var filePath = Path.Combine(workDir.FullName, asset.Filename);
                    File.WriteAllBytes(filePath, new byte[asset.SizeBytes]);

                    extractedAssets.Add(new ExtractedAsset
                    {
                        AssetId = asset.AssetId,
                        Filename = asset.Filename,
                        FilePath = filePath,
                        RelativePath = asset.Filename,
                        ContentType = asset.ContentType,
                        AssetType = asset.AssetType,
                        SizeBytes = asset.SizeBytes
                    });
                    totalSize += asset.SizeBytes;
                }

                return new ExtractionResult
                {
                    SourceId = sourceId,
                    Assets = extractedAssets,
                    WorkingDirectory = workDir,
                    TotalSizeBytes = totalSize
                };
            });

        return mock;
    }

    private static ExtractedAsset CreateExtractedAsset(
        string assetId,
        string filename,
        long sizeBytes,
        string? contentType = null)
    {
        return new ExtractedAsset
        {
            AssetId = assetId,
            Filename = filename,
            FilePath = filename, // Will be set by mock
            RelativePath = filename,
            ContentType = contentType ?? "application/octet-stream",
            AssetType = AssetType.Other,
            SizeBytes = sizeBytes
        };
    }

    private BundlerStateManager CreateStateManager()
    {
        var stateDir = new DirectoryInfo(Path.Combine(_tempDir, "state"));
        stateDir.Create();
        return new BundlerStateManager(stateDir);
    }

    private BundlerOptions CreateOptions(bool forceRebuild = false)
    {
        return new BundlerOptions
        {
            WorkingDirectory = Path.Combine(_tempDir, "working"),
            OutputDirectory = Path.Combine(_tempDir, "output"),
            CleanupWorkingDirectory = true,
            ForceRebuild = forceRebuild,
            MaxParallelSources = 2
        };
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }

    #endregion
}
