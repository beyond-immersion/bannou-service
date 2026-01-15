using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using BeyondImmersion.Bannou.AssetBundler.State;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Tests.State;

/// <summary>
/// Tests for BundlerStateManager.
/// </summary>
public class BundlerStateManagerTests : IDisposable
{
    private readonly string _tempDir;

    public BundlerStateManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"state-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private class FakeAssetSource : IAssetSource
    {
        public string SourceId { get; init; } = "test-source";
        public string Name { get; init; } = "Test Source";
        public string Version { get; init; } = "1.0.0";
        public string ContentHash { get; init; } = "abc123";
        public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();

        public Task<Extraction.ExtractionResult> ExtractAsync(
            DirectoryInfo workingDir,
            IAssetTypeInferencer? typeInferencer = null,
            CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }

    [Fact]
    public void NeedsProcessing_NewSource_ReturnsTrue()
    {
        // Arrange
        var stateManager = new BundlerStateManager(new DirectoryInfo(_tempDir));
        var source = new FakeAssetSource { SourceId = "new-source" };

        // Act
        var result = stateManager.NeedsProcessing(source);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void NeedsProcessing_ProcessedSource_ReturnsFalse()
    {
        // Arrange
        var stateManager = new BundlerStateManager(new DirectoryInfo(_tempDir));
        var source = new FakeAssetSource
        {
            SourceId = "processed-source",
            ContentHash = "hash123"
        };

        stateManager.RecordProcessed(new SourceProcessingRecord
        {
            SourceId = source.SourceId,
            ContentHash = source.ContentHash,
            Version = source.Version,
            ProcessedAt = DateTimeOffset.UtcNow,
            BundlePath = "/path/to/bundle.bannou",
            AssetCount = 10
        });

        // Act
        var result = stateManager.NeedsProcessing(source);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void NeedsProcessing_ChangedHash_ReturnsTrue()
    {
        // Arrange
        var stateManager = new BundlerStateManager(new DirectoryInfo(_tempDir));

        // Record with original hash
        stateManager.RecordProcessed(new SourceProcessingRecord
        {
            SourceId = "changing-source",
            ContentHash = "original-hash",
            Version = "1.0",
            ProcessedAt = DateTimeOffset.UtcNow,
            BundlePath = "/path/bundle.bannou",
            AssetCount = 5
        });

        // Source with different hash
        var source = new FakeAssetSource
        {
            SourceId = "changing-source",
            ContentHash = "new-hash"
        };

        // Act
        var result = stateManager.NeedsProcessing(source);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GetRecord_ExistingSource_ReturnsRecord()
    {
        // Arrange
        var stateManager = new BundlerStateManager(new DirectoryInfo(_tempDir));
        var record = new SourceProcessingRecord
        {
            SourceId = "existing-source",
            ContentHash = "hash",
            Version = "1.0",
            ProcessedAt = DateTimeOffset.UtcNow,
            BundlePath = "/path/bundle.bannou",
            AssetCount = 15
        };
        stateManager.RecordProcessed(record);

        // Act
        var result = stateManager.GetRecord("existing-source");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(record.SourceId, result.SourceId);
        Assert.Equal(record.ContentHash, result.ContentHash);
        Assert.Equal(record.AssetCount, result.AssetCount);
    }

    [Fact]
    public void GetRecord_NonExistentSource_ReturnsNull()
    {
        // Arrange
        var stateManager = new BundlerStateManager(new DirectoryInfo(_tempDir));

        // Act
        var result = stateManager.GetRecord("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void StatePersists_AcrossInstances()
    {
        // Arrange
        var stateDir = new DirectoryInfo(Path.Combine(_tempDir, "persistent"));
        stateDir.Create();

        var record = new SourceProcessingRecord
        {
            SourceId = "persistent-source",
            ContentHash = "persistent-hash",
            Version = "2.0",
            ProcessedAt = DateTimeOffset.UtcNow,
            BundlePath = "/bundle.bannou",
            AssetCount = 20
        };

        // Write state
        var stateManager1 = new BundlerStateManager(stateDir);
        stateManager1.RecordProcessed(record);

        // Read state with new instance
        var stateManager2 = new BundlerStateManager(stateDir);

        // Act
        var result = stateManager2.GetRecord("persistent-source");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("persistent-hash", result.ContentHash);
        Assert.Equal(20, result.AssetCount);
    }

    [Fact]
    public void RecordProcessed_OverwritesExistingRecord()
    {
        // Arrange
        var stateManager = new BundlerStateManager(new DirectoryInfo(_tempDir));

        stateManager.RecordProcessed(new SourceProcessingRecord
        {
            SourceId = "overwrite-source",
            ContentHash = "old-hash",
            Version = "1.0",
            ProcessedAt = DateTimeOffset.UtcNow,
            BundlePath = "/old/path.bannou",
            AssetCount = 5
        });

        stateManager.RecordProcessed(new SourceProcessingRecord
        {
            SourceId = "overwrite-source",
            ContentHash = "new-hash",
            Version = "2.0",
            ProcessedAt = DateTimeOffset.UtcNow,
            BundlePath = "/new/path.bannou",
            AssetCount = 10
        });

        // Act
        var result = stateManager.GetRecord("overwrite-source");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("new-hash", result.ContentHash);
        Assert.Equal("2.0", result.Version);
        Assert.Equal(10, result.AssetCount);
    }

    [Fact]
    public void ComputeHash_FileHash_IsConsistent()
    {
        // Arrange
        var filePath = Path.Combine(_tempDir, "test-file.txt");
        File.WriteAllText(filePath, "Test content for hashing");
        var fileInfo = new FileInfo(filePath);

        // Act
        var hash1 = BundlerStateManager.ComputeHash(fileInfo);
        var hash2 = BundlerStateManager.ComputeHash(fileInfo);

        // Assert
        Assert.NotEmpty(hash1);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentFiles_ProduceDifferentHashes()
    {
        // Arrange
        var file1 = Path.Combine(_tempDir, "file1.txt");
        var file2 = Path.Combine(_tempDir, "file2.txt");
        File.WriteAllText(file1, "Content 1");
        File.WriteAllText(file2, "Content 2");

        // Act
        var hash1 = BundlerStateManager.ComputeHash(new FileInfo(file1));
        var hash2 = BundlerStateManager.ComputeHash(new FileInfo(file2));

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeDirectoryHash_IsConsistent()
    {
        // Arrange
        var dirPath = Path.Combine(_tempDir, "test-dir");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "a.txt"), "A");
        File.WriteAllText(Path.Combine(dirPath, "b.txt"), "B");
        var dirInfo = new DirectoryInfo(dirPath);

        // Act
        var hash1 = BundlerStateManager.ComputeDirectoryHash(dirInfo);
        var hash2 = BundlerStateManager.ComputeDirectoryHash(dirInfo);

        // Assert
        Assert.NotEmpty(hash1);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeDirectoryHash_ChangesWhenContentChanges()
    {
        // Arrange
        var dirPath = Path.Combine(_tempDir, "changing-dir");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "file.txt"), "Original");
        var dirInfo = new DirectoryInfo(dirPath);

        var hash1 = BundlerStateManager.ComputeDirectoryHash(dirInfo);

        // Modify content
        File.WriteAllText(Path.Combine(dirPath, "file.txt"), "Modified");

        var hash2 = BundlerStateManager.ComputeDirectoryHash(dirInfo);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void RecordProcessed_WithUploadInfo_PersistsUploadInfo()
    {
        // Arrange
        var stateManager = new BundlerStateManager(new DirectoryInfo(_tempDir));
        var record = new SourceProcessingRecord
        {
            SourceId = "uploaded-source",
            ContentHash = "hash",
            Version = "1.0",
            ProcessedAt = DateTimeOffset.UtcNow,
            BundlePath = "/bundle.bannou",
            AssetCount = 5,
            UploadedAt = DateTimeOffset.UtcNow,
            UploadedBundleId = "bundle-uuid-123"
        };

        // Act
        stateManager.RecordProcessed(record);
        var result = stateManager.GetRecord("uploaded-source");

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.UploadedAt);
        Assert.Equal("bundle-uuid-123", result.UploadedBundleId);
    }
}
