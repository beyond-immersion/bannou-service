using System.IO.Compression;
using System.Text;
using BeyondImmersion.Bannou.AssetBundler.Sources;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Tests.Sources;

/// <summary>
/// Tests for ZipArchiveAssetSource.
/// </summary>
public class ZipArchiveAssetSourceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _workingDir;

    public ZipArchiveAssetSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"zip-source-tests-{Guid.NewGuid():N}");
        _workingDir = Path.Combine(_tempDir, "working");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_workingDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateZip(string name, params (string path, byte[] content)[] entries)
    {
        var zipPath = Path.Combine(_tempDir, $"{name}.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (path, content) in entries)
        {
            var entry = archive.CreateEntry(path);
            using var stream = entry.Open();
            stream.Write(content, 0, content.Length);
        }
        return zipPath;
    }

    [Fact]
    public void Constructor_ValidZip_SetsProperties()
    {
        // Arrange (use .json which passes the filter)
        var zipPath = CreateZip("test", ("file.json", "content"u8.ToArray()));
        var zipFile = new FileInfo(zipPath);

        // Act
        var source = new ZipArchiveAssetSource(
            zipFile,
            sourceId: "test-zip",
            name: "Test Zip",
            version: "1.0.0");

        // Assert
        Assert.Equal("test-zip", source.SourceId);
        Assert.Equal("Test Zip", source.Name);
        Assert.Equal("1.0.0", source.Version);
        Assert.NotEmpty(source.ContentHash);
    }

    [Fact]
    public async Task ExtractAsync_SingleFile_ExtractsCorrectly()
    {
        // Arrange (use .json which passes the filter)
        var content = Encoding.UTF8.GetBytes("{\"message\": \"Hello from ZIP!\"}");
        var zipPath = CreateZip("single", ("hello.json", content));

        var source = new ZipArchiveAssetSource(
            new FileInfo(zipPath),
            "test", "Test", "1.0");

        // Act
        var result = await source.ExtractAsync(new DirectoryInfo(_workingDir));

        // Assert
        Assert.Single(result.Assets);
        Assert.Equal("hello.json", result.Assets[0].Filename);
        Assert.True(File.Exists(result.Assets[0].FilePath));
        Assert.Equal(content, File.ReadAllBytes(result.Assets[0].FilePath));
    }

    [Fact]
    public async Task ExtractAsync_NestedStructure_PreservesPaths()
    {
        // Arrange
        var zipPath = CreateZip("nested",
            ("models/character.fbx", new byte[] { 1, 2, 3 }),
            ("textures/diffuse/skin.png", new byte[] { 4, 5, 6 }),
            ("audio/footstep.wav", new byte[] { 7, 8, 9 }));

        var source = new ZipArchiveAssetSource(
            new FileInfo(zipPath),
            "nested", "Nested", "1.0");

        // Act
        var result = await source.ExtractAsync(new DirectoryInfo(_workingDir));

        // Assert
        Assert.Equal(3, result.Assets.Count);
        Assert.Contains(result.Assets, a => a.Filename.Contains("character.fbx"));
        Assert.Contains(result.Assets, a => a.Filename.Contains("skin.png"));
        Assert.Contains(result.Assets, a => a.Filename.Contains("footstep.wav"));
    }

    [Fact]
    public async Task ExtractAsync_WithTypeInferencer_SetsAssetTypes()
    {
        // Arrange
        var zipPath = CreateZip("typed",
            ("model.fbx", new byte[] { 1 }),
            ("texture.png", new byte[] { 2 }),
            ("sound.wav", new byte[] { 3 }));

        var source = new ZipArchiveAssetSource(
            new FileInfo(zipPath),
            "typed", "Typed", "1.0");

        var inferencer = DefaultTypeInferencer.Instance;

        // Act
        var result = await source.ExtractAsync(new DirectoryInfo(_workingDir), inferencer);

        // Assert
        var model = result.Assets.FirstOrDefault(a => a.Filename.Contains("model.fbx"));
        var texture = result.Assets.FirstOrDefault(a => a.Filename.Contains("texture.png"));
        var sound = result.Assets.FirstOrDefault(a => a.Filename.Contains("sound.wav"));

        Assert.NotNull(model);
        Assert.NotNull(texture);
        Assert.NotNull(sound);
        Assert.Equal(Extraction.AssetType.Model, model.AssetType);
        Assert.Equal(Extraction.AssetType.Texture, texture.AssetType);
        Assert.Equal(Extraction.AssetType.Audio, sound.AssetType);
    }

    [Fact]
    public async Task ExtractAsync_EmptyZip_ReturnsEmptyAssets()
    {
        // Arrange
        var zipPath = Path.Combine(_tempDir, "empty.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Create empty archive
        }

        var source = new ZipArchiveAssetSource(
            new FileInfo(zipPath),
            "empty", "Empty", "1.0");

        // Act
        var result = await source.ExtractAsync(new DirectoryInfo(_workingDir));

        // Assert
        Assert.Empty(result.Assets);
    }

    [Fact]
    public async Task ExtractAsync_SkipsDirectoryEntries()
    {
        // Arrange (use .json which passes the filter)
        var zipPath = Path.Combine(_tempDir, "withdirs.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Directory entry (ends with /)
            archive.CreateEntry("folder/");
            // Actual file
            var entry = archive.CreateEntry("folder/file.json");
            using var stream = entry.Open();
            stream.Write("content"u8);
        }

        var source = new ZipArchiveAssetSource(
            new FileInfo(zipPath),
            "withdirs", "With Dirs", "1.0");

        // Act
        var result = await source.ExtractAsync(new DirectoryInfo(_workingDir));

        // Assert
        Assert.Single(result.Assets); // Only the file, not the directory
    }

    [Fact]
    public void ContentHash_ComputedFromFileContent()
    {
        // Arrange (use .json which passes the filter)
        var zipPath1 = CreateZip("zip1", ("file.json", "content1"u8.ToArray()));
        var zipPath2 = CreateZip("zip2", ("file.json", "content2"u8.ToArray()));

        var source1 = new ZipArchiveAssetSource(new FileInfo(zipPath1), "s1", "S1", "1.0");
        var source2 = new ZipArchiveAssetSource(new FileInfo(zipPath2), "s2", "S2", "1.0");

        // Assert
        Assert.NotEqual(source1.ContentHash, source2.ContentHash);
    }

    [Fact]
    public async Task ExtractAsync_LargeFile_ExtractsCorrectly()
    {
        // Arrange
        var largeContent = new byte[1_000_000]; // 1MB
        new Random(42).NextBytes(largeContent);
        var zipPath = CreateZip("large", ("large.bin", largeContent));

        var source = new ZipArchiveAssetSource(
            new FileInfo(zipPath),
            "large", "Large", "1.0");

        // Act
        var result = await source.ExtractAsync(new DirectoryInfo(_workingDir));

        // Assert
        Assert.Single(result.Assets);
        var extractedContent = File.ReadAllBytes(result.Assets[0].FilePath);
        Assert.Equal(largeContent, extractedContent);
    }

    [Fact]
    public async Task ExtractAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        // Arrange (use .json which passes the filter)
        var zipPath = CreateZip("cancel", ("file.json", "content"u8.ToArray()));
        var source = new ZipArchiveAssetSource(
            new FileInfo(zipPath),
            "cancel", "Cancel", "1.0");

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => source.ExtractAsync(new DirectoryInfo(_workingDir), ct: cts.Token));
    }

    [Fact]
    public void Tags_CanBeProvided()
    {
        // Arrange (use .json which passes the filter)
        var zipPath = CreateZip("tagged", ("file.json", "content"u8.ToArray()));
        var tags = new Dictionary<string, string>
        {
            ["vendor"] = "synty",
            ["pack"] = "adventure"
        };

        // Act
        var source = new ZipArchiveAssetSource(
            new FileInfo(zipPath),
            "tagged", "Tagged", "1.0",
            tags: tags);

        // Assert
        Assert.Equal("synty", source.Tags["vendor"]);
        Assert.Equal("adventure", source.Tags["pack"]);
    }
}
