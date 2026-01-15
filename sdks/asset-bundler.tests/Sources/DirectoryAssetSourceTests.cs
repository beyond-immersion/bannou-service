using System.Text;
using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using BeyondImmersion.Bannou.AssetBundler.Sources;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Tests.Sources;

/// <summary>
/// Tests for DirectoryAssetSource.
/// </summary>
public class DirectoryAssetSourceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _workingDir;

    public DirectoryAssetSourceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dir-source-tests-{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_tempDir, "source");
        _workingDir = Path.Combine(_tempDir, "working");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_workingDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Constructor_ValidDirectory_SetsProperties()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_sourceDir, "test.png"), new byte[] { 1, 2, 3 });
        var dir = new DirectoryInfo(_sourceDir);

        // Act
        var source = new DirectoryAssetSource(
            dir,
            sourceId: "test-source",
            name: "Test Source",
            version: "1.0.0");

        // Assert
        Assert.Equal("test-source", source.SourceId);
        Assert.Equal("Test Source", source.Name);
        Assert.Equal("1.0.0", source.Version);
        Assert.NotEmpty(source.ContentHash); // Hash should be computed
    }

    [Fact]
    public async Task ExtractAsync_SingleFile_ExtractsCorrectly()
    {
        // Arrange (use .json which passes the filter, unlike .txt)
        var content = "{\"hello\": \"world\"}";
        File.WriteAllText(Path.Combine(_sourceDir, "hello.json"), content);

        var source = new DirectoryAssetSource(
            new DirectoryInfo(_sourceDir),
            "test", "Test", "1.0");

        // Act
        var result = await source.ExtractAsync(new DirectoryInfo(_workingDir));

        // Assert
        Assert.Single(result.Assets);
        Assert.Equal("hello.json", result.Assets[0].Filename);
        Assert.True(File.Exists(result.Assets[0].FilePath));
        Assert.Equal(content, File.ReadAllText(result.Assets[0].FilePath));
    }

    [Fact]
    public async Task ExtractAsync_NestedDirectories_PreservesStructure()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_sourceDir, "models"));
        Directory.CreateDirectory(Path.Combine(_sourceDir, "textures"));
        File.WriteAllBytes(Path.Combine(_sourceDir, "models", "character.fbx"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(_sourceDir, "textures", "skin.png"), new byte[] { 4, 5, 6 });

        var source = new DirectoryAssetSource(
            new DirectoryInfo(_sourceDir),
            "nested", "Nested", "1.0");

        // Act
        var result = await source.ExtractAsync(new DirectoryInfo(_workingDir));

        // Assert
        Assert.Equal(2, result.Assets.Count);
        Assert.Contains(result.Assets, a => a.Filename.Contains("character.fbx"));
        Assert.Contains(result.Assets, a => a.Filename.Contains("skin.png"));
    }

    [Fact]
    public async Task ExtractAsync_WithTypeInferencer_SetsAssetType()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_sourceDir, "model.fbx"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(_sourceDir, "texture.png"), new byte[] { 2 });

        var source = new DirectoryAssetSource(
            new DirectoryInfo(_sourceDir),
            "typed", "Typed", "1.0");

        var inferencer = DefaultTypeInferencer.Instance;

        // Act
        var result = await source.ExtractAsync(new DirectoryInfo(_workingDir), inferencer);

        // Assert
        var modelAsset = result.Assets.FirstOrDefault(a => a.Filename.Contains("model.fbx"));
        var textureAsset = result.Assets.FirstOrDefault(a => a.Filename.Contains("texture.png"));

        Assert.NotNull(modelAsset);
        Assert.NotNull(textureAsset);
        Assert.Equal(Extraction.AssetType.Model, modelAsset.AssetType);
        Assert.Equal(Extraction.AssetType.Texture, textureAsset.AssetType);
    }

    [Fact]
    public async Task ExtractAsync_EmptyDirectory_ReturnsEmptyAssets()
    {
        // Arrange
        var source = new DirectoryAssetSource(
            new DirectoryInfo(_sourceDir),
            "empty", "Empty", "1.0");

        // Act
        var result = await source.ExtractAsync(new DirectoryInfo(_workingDir));

        // Assert
        Assert.Empty(result.Assets);
    }

    [Fact]
    public void ContentHash_ChangesWhenFileChanges()
    {
        // Arrange (use .json which passes the filter)
        File.WriteAllText(Path.Combine(_sourceDir, "file.json"), "original");
        var dir = new DirectoryInfo(_sourceDir);

        var source1 = new DirectoryAssetSource(dir, "test", "Test", "1.0");
        var hash1 = source1.ContentHash;

        // Modify file
        File.WriteAllText(Path.Combine(_sourceDir, "file.json"), "modified");
        var source2 = new DirectoryAssetSource(dir, "test", "Test", "1.0");
        var hash2 = source2.ContentHash;

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Tags_EmptyByDefault()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_sourceDir, "file.png"), new byte[] { 1 });

        // Act
        var source = new DirectoryAssetSource(
            new DirectoryInfo(_sourceDir),
            "test", "Test", "1.0");

        // Assert
        Assert.NotNull(source.Tags);
        Assert.Empty(source.Tags);
    }

    [Fact]
    public void Tags_CanBeProvided()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_sourceDir, "file.png"), new byte[] { 1 });
        var tags = new Dictionary<string, string>
        {
            ["vendor"] = "synty",
            ["category"] = "polygon"
        };

        // Act
        var source = new DirectoryAssetSource(
            new DirectoryInfo(_sourceDir),
            "test", "Test", "1.0",
            tags: tags);

        // Assert
        Assert.Equal("synty", source.Tags["vendor"]);
        Assert.Equal("polygon", source.Tags["category"]);
    }

    [Fact]
    public async Task ExtractAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_sourceDir, "file.png"), new byte[] { 1 });
        var source = new DirectoryAssetSource(
            new DirectoryInfo(_sourceDir),
            "test", "Test", "1.0");

        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => source.ExtractAsync(new DirectoryInfo(_workingDir), ct: cts.Token));
    }

    [Fact]
    public async Task ExtractAsync_GeneratesUniqueAssetIds()
    {
        // Arrange (use .json which passes the filter)
        File.WriteAllText(Path.Combine(_sourceDir, "file1.json"), "a");
        File.WriteAllText(Path.Combine(_sourceDir, "file2.json"), "b");

        var source = new DirectoryAssetSource(
            new DirectoryInfo(_sourceDir),
            "test", "Test", "1.0");

        // Act
        var result = await source.ExtractAsync(new DirectoryInfo(_workingDir));

        // Assert
        var ids = result.Assets.Select(a => a.AssetId).ToList();
        Assert.Equal(2, ids.Distinct().Count()); // All IDs unique
    }
}
