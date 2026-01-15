using System.Security.Cryptography;
using System.Text;
using BeyondImmersion.Bannou.AssetBundler.Extraction;
using BeyondImmersion.Bannou.AssetBundler.Processing;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Tests.Processing;

/// <summary>
/// Tests for RawAssetProcessor (pass-through processing).
/// </summary>
public class RawAssetProcessorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _workingDir;

    public RawAssetProcessorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"raw-processor-tests-{Guid.NewGuid():N}");
        _workingDir = Path.Combine(_tempDir, "working");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_workingDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ExtractedAsset CreateExtractedAsset(string filename, byte[] content, AssetType assetType = AssetType.Other, string? contentType = null)
    {
        var filePath = Path.Combine(_tempDir, filename);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(filePath, content);

        return new ExtractedAsset
        {
            AssetId = $"asset-{Path.GetFileNameWithoutExtension(filename)}",
            Filename = filename,
            FilePath = filePath,
            RelativePath = filename,
            AssetType = assetType,
            SizeBytes = content.Length,
            ContentType = contentType // null allows inference
        };
    }

    [Fact]
    public void ProcessorId_ReturnsRaw()
    {
        // Arrange
        var processor = RawAssetProcessor.Instance;

        // Assert
        Assert.Equal("raw", processor.ProcessorId);
    }

    [Fact]
    public void OutputContentTypes_ReturnsEmpty()
    {
        // Arrange
        var processor = RawAssetProcessor.Instance;

        // Assert - Raw processor preserves original types
        Assert.Empty(processor.OutputContentTypes);
    }

    [Fact]
    public async Task ProcessAsync_SingleAsset_PreservesData()
    {
        // Arrange
        var processor = RawAssetProcessor.Instance;
        var content = Encoding.UTF8.GetBytes("Hello, World!");
        var asset = CreateExtractedAsset("hello.json", content);

        // Act
        var result = await processor.ProcessAsync(
            new[] { asset },
            new DirectoryInfo(_workingDir));

        // Assert
        Assert.Single(result);
        Assert.True(result.ContainsKey(asset.AssetId));

        var processed = result[asset.AssetId];
        Assert.Equal(asset.AssetId, processed.AssetId);
        Assert.Equal("hello.json", processed.Filename);
        Assert.Equal(content, processed.Data.ToArray());
    }

    [Fact]
    public async Task ProcessAsync_ComputesCorrectHash()
    {
        // Arrange
        var processor = RawAssetProcessor.Instance;
        var content = Encoding.UTF8.GetBytes("Hash test content");
        var asset = CreateExtractedAsset("hash.json", content);
        var expectedHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        // Act
        var result = await processor.ProcessAsync(
            new[] { asset },
            new DirectoryInfo(_workingDir));

        // Assert
        Assert.Equal(expectedHash, result[asset.AssetId].ContentHash);
    }

    [Fact]
    public async Task ProcessAsync_InfersContentType()
    {
        // Arrange
        var processor = RawAssetProcessor.Instance;

        var pngAsset = CreateExtractedAsset("image.png", new byte[] { 1, 2, 3 });
        var jsonAsset = CreateExtractedAsset("data.json", Encoding.UTF8.GetBytes("{}"));
        var wavAsset = CreateExtractedAsset("sound.wav", new byte[] { 4, 5, 6 });

        // Act
        var result = await processor.ProcessAsync(
            new[] { pngAsset, jsonAsset, wavAsset },
            new DirectoryInfo(_workingDir));

        // Assert
        Assert.Equal("image/png", result[pngAsset.AssetId].ContentType);
        Assert.Equal("application/json", result[jsonAsset.AssetId].ContentType);
        Assert.Equal("audio/wav", result[wavAsset.AssetId].ContentType);
    }

    [Fact]
    public async Task ProcessAsync_MultipleAssets_ProcessesAll()
    {
        // Arrange
        var processor = RawAssetProcessor.Instance;
        var assets = new List<ExtractedAsset>();

        for (int i = 0; i < 10; i++)
        {
            assets.Add(CreateExtractedAsset(
                $"file{i}.bin",
                Encoding.UTF8.GetBytes($"Content {i}")));
        }

        // Act
        var result = await processor.ProcessAsync(
            assets,
            new DirectoryInfo(_workingDir));

        // Assert
        Assert.Equal(10, result.Count);
        foreach (var asset in assets)
        {
            Assert.True(result.ContainsKey(asset.AssetId));
        }
    }

    [Fact]
    public async Task ProcessAsync_EmptyList_ReturnsEmptyDictionary()
    {
        // Arrange
        var processor = RawAssetProcessor.Instance;

        // Act
        var result = await processor.ProcessAsync(
            Array.Empty<ExtractedAsset>(),
            new DirectoryInfo(_workingDir));

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ProcessAsync_HasNoDependencies()
    {
        // Arrange
        var processor = RawAssetProcessor.Instance;
        var asset = CreateExtractedAsset("simple.bin", new byte[] { 1 });

        // Act
        var result = await processor.ProcessAsync(
            new[] { asset },
            new DirectoryInfo(_workingDir));

        // Assert
        Assert.Empty(result[asset.AssetId].Dependencies);
    }

    [Fact]
    public async Task ProcessAsync_HasEmptyMetadata()
    {
        // Arrange
        var processor = RawAssetProcessor.Instance;
        var asset = CreateExtractedAsset("simple.bin", new byte[] { 1 });

        // Act
        var result = await processor.ProcessAsync(
            new[] { asset },
            new DirectoryInfo(_workingDir));

        // Assert
        Assert.Empty(result[asset.AssetId].Metadata);
    }

    [Fact]
    public async Task ProcessAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        // Arrange
        var processor = RawAssetProcessor.Instance;
        var asset = CreateExtractedAsset("cancel.bin", new byte[] { 1, 2, 3 });

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => processor.ProcessAsync(
                new[] { asset },
                new DirectoryInfo(_workingDir),
                ct: cts.Token));
    }

    [Fact]
    public async Task ProcessAsync_LargeFile_ProcessesCorrectly()
    {
        // Arrange
        var processor = RawAssetProcessor.Instance;
        var largeContent = new byte[5_000_000]; // 5MB
        new Random(42).NextBytes(largeContent);
        var asset = CreateExtractedAsset("large.bin", largeContent);

        // Act
        var result = await processor.ProcessAsync(
            new[] { asset },
            new DirectoryInfo(_workingDir));

        // Assert
        Assert.Equal(largeContent, result[asset.AssetId].Data.ToArray());
    }

    [Fact]
    public void Instance_ReturnsSingleton()
    {
        // Act
        var instance1 = RawAssetProcessor.Instance;
        var instance2 = RawAssetProcessor.Instance;

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Theory]
    [InlineData(".glb", "model/gltf-binary")]
    [InlineData(".gltf", "model/gltf+json")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".ogg", "audio/ogg")]
    [InlineData(".mp3", "audio/mpeg")]
    [InlineData(".yaml", "application/x-yaml")]
    [InlineData(".yml", "application/x-yaml")]
    [InlineData(".fbx", "application/x-fbx")]
    [InlineData(".unknown", "application/octet-stream")]
    public async Task ProcessAsync_InfersCorrectMimeTypes(string extension, string expectedMime)
    {
        // Arrange
        var processor = RawAssetProcessor.Instance;
        var asset = CreateExtractedAsset($"file{extension}", new byte[] { 1, 2, 3 });

        // Act
        var result = await processor.ProcessAsync(
            new[] { asset },
            new DirectoryInfo(_workingDir));

        // Assert
        Assert.Equal(expectedMime, result[asset.AssetId].ContentType);
    }
}
