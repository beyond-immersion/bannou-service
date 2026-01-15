using System.Text;
using BeyondImmersion.Bannou.Bundle.Format;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Tests.Bundle;

/// <summary>
/// Round-trip tests for BannouBundleWriter and BannouBundleReader.
/// </summary>
public class BundleWriterReaderTests : IDisposable
{
    private readonly string _tempDir;

    public BundleWriterReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"bundle-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void RoundTrip_SingleAsset_PreservesData()
    {
        // Arrange
        var bundlePath = Path.Combine(_tempDir, "test.bannou");
        var assetData = Encoding.UTF8.GetBytes("Hello, World!");
        var assetId = "test-asset-001";
        var filename = "hello.txt";
        var contentType = "text/plain";

        // Act - Write
        using (var writeStream = File.Create(bundlePath))
        using (var writer = new BannouBundleWriter(writeStream))
        {
            writer.AddAsset(assetId, filename, contentType, assetData);
            writer.Finalize("test-bundle", "Test Bundle", "1.0.0", "test-author");
        }

        // Act - Read
        using var readStream = File.OpenRead(bundlePath);
        using var reader = new BannouBundleReader(readStream);
        reader.ReadHeader();

        var manifest = reader.Manifest;
        var readData = reader.ReadAsset(assetId);

        // Assert
        Assert.Equal("test-bundle", manifest.BundleId);
        Assert.Equal("Test Bundle", manifest.Name);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("test-author", manifest.CreatedBy);
        Assert.Equal(1, manifest.AssetCount);

        Assert.NotNull(readData);
        Assert.Equal(assetData, readData);

        var entry = reader.GetAssetEntry(assetId);
        Assert.NotNull(entry);
        Assert.Equal(filename, entry.Filename);
        Assert.Equal(contentType, entry.ContentType);
    }

    [Fact]
    public void RoundTrip_MultipleAssets_PreservesAllData()
    {
        // Arrange
        var bundlePath = Path.Combine(_tempDir, "multi.bannou");
        var assets = new Dictionary<string, byte[]>
        {
            ["asset-1"] = Encoding.UTF8.GetBytes("First asset content"),
            ["asset-2"] = Encoding.UTF8.GetBytes("Second asset with more data"),
            ["asset-3"] = new byte[1024], // Binary data
        };

        // Fill binary data with pattern
        for (int i = 0; i < assets["asset-3"].Length; i++)
            assets["asset-3"][i] = (byte)(i % 256);

        // Act - Write
        using (var writeStream = File.Create(bundlePath))
        using (var writer = new BannouBundleWriter(writeStream))
        {
            foreach (var (id, data) in assets)
            {
                writer.AddAsset(id, $"{id}.bin", "application/octet-stream", data);
            }
            writer.Finalize("multi-bundle", "Multi Asset Bundle", "2.0.0", "author");
        }

        // Act - Read
        using var readStream = File.OpenRead(bundlePath);
        using var reader = new BannouBundleReader(readStream);
        reader.ReadHeader();

        // Assert
        Assert.Equal(3, reader.Manifest.AssetCount);

        foreach (var (id, expectedData) in assets)
        {
            var readData = reader.ReadAsset(id);
            Assert.NotNull(readData);
            Assert.Equal(expectedData, readData);
        }
    }

    [Fact]
    public void RoundTrip_LargeAsset_CompressesEffectively()
    {
        // Arrange
        var bundlePath = Path.Combine(_tempDir, "large.bannou");

        // Create compressible data (repeated pattern)
        var largeData = new byte[100_000];
        var pattern = Encoding.UTF8.GetBytes("This is a repeating pattern. ");
        for (int i = 0; i < largeData.Length; i++)
            largeData[i] = pattern[i % pattern.Length];

        // Act - Write
        using (var writeStream = File.Create(bundlePath))
        using (var writer = new BannouBundleWriter(writeStream))
        {
            writer.AddAsset("large-asset", "large.bin", "application/octet-stream", largeData);
            writer.Finalize("large-bundle", "Large Bundle", "1.0.0", "author");
        }

        // Assert - File should be smaller than original data due to compression
        var fileInfo = new FileInfo(bundlePath);
        Assert.True(fileInfo.Length < largeData.Length,
            $"Compressed bundle ({fileInfo.Length} bytes) should be smaller than original data ({largeData.Length} bytes)");

        // Act - Read and verify data integrity
        using var readStream = File.OpenRead(bundlePath);
        using var reader = new BannouBundleReader(readStream);
        reader.ReadHeader();

        var readData = reader.ReadAsset("large-asset");
        Assert.NotNull(readData);
        Assert.Equal(largeData, readData);
    }

    [Fact]
    public void RoundTrip_WithMetadata_PreservesMetadata()
    {
        // Arrange
        var bundlePath = Path.Combine(_tempDir, "metadata.bannou");
        var assetData = new byte[] { 1, 2, 3, 4 };
        var metadata = new Dictionary<string, object>
        {
            ["strideGuid"] = "abc123",
            ["width"] = 512,
            ["height"] = 256
        };

        // Act - Write
        using (var writeStream = File.Create(bundlePath))
        using (var writer = new BannouBundleWriter(writeStream))
        {
            writer.AddAsset("textured", "tex.png", "image/png", assetData, metadata: metadata);
            writer.Finalize("meta-bundle", "Metadata Bundle", "1.0.0", "author");
        }

        // Act - Read
        using var readStream = File.OpenRead(bundlePath);
        using var reader = new BannouBundleReader(readStream);
        reader.ReadHeader();

        var entry = reader.GetAssetEntry("textured");

        // Assert
        Assert.NotNull(entry);
        Assert.NotNull(entry.Metadata);
        Assert.Equal("abc123", entry.Metadata["strideGuid"]?.ToString());
    }

    [Fact]
    public void RoundTrip_WithTags_PreservesBundleTags()
    {
        // Arrange
        var bundlePath = Path.Combine(_tempDir, "tagged.bannou");
        var tags = new Dictionary<string, string>
        {
            ["vendor"] = "synty",
            ["category"] = "polygon",
            ["pack"] = "adventure"
        };

        // Act - Write
        using (var writeStream = File.Create(bundlePath))
        using (var writer = new BannouBundleWriter(writeStream))
        {
            writer.AddAsset("asset", "file.bin", "application/octet-stream", new byte[] { 0 });
            writer.Finalize("tagged-bundle", "Tagged Bundle", "1.0.0", "author", tags: tags);
        }

        // Act - Read
        using var readStream = File.OpenRead(bundlePath);
        using var reader = new BannouBundleReader(readStream);
        reader.ReadHeader();

        // Assert
        Assert.NotNull(reader.Manifest.Tags);
        Assert.Equal("synty", reader.Manifest.Tags["vendor"]);
        Assert.Equal("polygon", reader.Manifest.Tags["category"]);
        Assert.Equal("adventure", reader.Manifest.Tags["pack"]);
    }

    [Fact]
    public void ReadAsset_NonExistentId_ReturnsNull()
    {
        // Arrange
        var bundlePath = Path.Combine(_tempDir, "single.bannou");

        using (var writeStream = File.Create(bundlePath))
        using (var writer = new BannouBundleWriter(writeStream))
        {
            writer.AddAsset("exists", "file.bin", "application/octet-stream", new byte[] { 1 });
            writer.Finalize("bundle", "Bundle", "1.0.0", "author");
        }

        // Act
        using var readStream = File.OpenRead(bundlePath);
        using var reader = new BannouBundleReader(readStream);
        reader.ReadHeader();

        var result = reader.ReadAsset("does-not-exist");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ReadAssetAsync_ReturnsCorrectData()
    {
        // Arrange
        var bundlePath = Path.Combine(_tempDir, "async.bannou");
        var expectedData = Encoding.UTF8.GetBytes("Async test data");

        using (var writeStream = File.Create(bundlePath))
        using (var writer = new BannouBundleWriter(writeStream))
        {
            writer.AddAsset("async-asset", "async.txt", "text/plain", expectedData);
            writer.Finalize("async-bundle", "Async Bundle", "1.0.0", "author");
        }

        // Act
        await using var readStream = File.OpenRead(bundlePath);
        using var reader = new BannouBundleReader(readStream);
        await reader.ReadHeaderAsync();

        var result = await reader.ReadAssetAsync("async-asset");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedData, result);
    }
}
