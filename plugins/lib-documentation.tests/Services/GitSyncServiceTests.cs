using BeyondImmersion.BannouService.Documentation.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Documentation.Tests.Services;

/// <summary>
/// Unit tests for GitSyncService.
/// Tests repository sync operations, file matching, and commit detection.
/// Note: These tests mock LibGit2Sharp operations since we can't create real git repos in unit tests.
/// Integration tests should use actual git repositories.
/// </summary>
public class GitSyncServiceTests
{
    private readonly Mock<ILogger<GitSyncService>> _mockLogger;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly DocumentationServiceConfiguration _configuration;
    private readonly GitSyncService _service;

    public GitSyncServiceTests()
    {
        _mockLogger = new Mock<ILogger<GitSyncService>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _configuration = new DocumentationServiceConfiguration
        {
            GitStoragePath = Path.Combine(Path.GetTempPath(), "bannou-git-test-" + Guid.NewGuid().ToString("N")[..8])
        };
        _service = new GitSyncService(_mockLogger.Object, _configuration, _mockMessageBus.Object, _mockTelemetryProvider.Object);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<GitSyncService>();
        Assert.NotNull(_service);
    }

    #endregion

    #region SyncRepositoryAsync Validation Tests

    [Fact]
    public async Task SyncRepositoryAsync_WithEmptyRepositoryUrl_ShouldThrow()
    {
        // Arrange, Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.SyncRepositoryAsync("", "main", "/tmp/test"));
    }

    [Fact]
    public async Task SyncRepositoryAsync_WithInvalidUrl_ShouldReturnFailed()
    {
        // Arrange
        var localPath = Path.Combine(_configuration.GitStoragePath, "invalid-repo");

        // Act
        var result = await _service.SyncRepositoryAsync(
            "not-a-valid-url",
            "main",
            localPath);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task SyncRepositoryAsync_WithCancellation_ShouldThrowOrReturnFailed()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        // When token is already cancelled, Task.Run throws TaskCanceledException immediately
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.SyncRepositoryAsync(
                "https://github.com/test/repo.git",
                "main",
                "/tmp/test",
                cts.Token));
    }

    #endregion

    #region GetChangedFilesAsync Validation Tests

    [Fact]
    public async Task GetChangedFilesAsync_WithEmptyToCommit_ShouldThrow()
    {
        // Arrange, Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GetChangedFilesAsync("/tmp/test", null, ""));
    }

    [Fact]
    public async Task GetChangedFilesAsync_WithNonExistentRepo_ShouldReturnEmptyList()
    {
        // Arrange
        var localPath = Path.Combine(_configuration.GitStoragePath, "nonexistent-" + Guid.NewGuid());

        // Act
        var result = await _service.GetChangedFilesAsync(localPath, null, "abc123");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region GetMatchingFilesAsync Tests

    [Fact]
    public async Task GetMatchingFilesAsync_WithEmptyLocalPath_ShouldThrow()
    {
        // Arrange, Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GetMatchingFilesAsync("", ["*.md"], []));
    }

    [Fact]
    public async Task GetMatchingFilesAsync_WithNonExistentPath_ShouldReturnEmptyList()
    {
        // Arrange
        var localPath = Path.Combine(_configuration.GitStoragePath, "nonexistent-" + Guid.NewGuid());

        // Act
        var result = await _service.GetMatchingFilesAsync(localPath, ["*.md"], []);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMatchingFilesAsync_WithValidDirectory_ShouldMatchFiles()
    {
        // Arrange
        var testDir = Path.Combine(_configuration.GitStoragePath, "match-test-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);

        try
        {
            // Create test files
            await File.WriteAllTextAsync(Path.Combine(testDir, "doc1.md"), "# Document 1");
            await File.WriteAllTextAsync(Path.Combine(testDir, "doc2.md"), "# Document 2");
            await File.WriteAllTextAsync(Path.Combine(testDir, "readme.txt"), "Not a markdown file");

            // Act
            var result = await _service.GetMatchingFilesAsync(testDir, ["*.md"], []);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.All(result, f => Assert.EndsWith(".md", f));
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetMatchingFilesAsync_WithExcludePatterns_ShouldExcludeFiles()
    {
        // Arrange
        var testDir = Path.Combine(_configuration.GitStoragePath, "exclude-test-" + Guid.NewGuid());
        var subDir = Path.Combine(testDir, "node_modules");
        Directory.CreateDirectory(testDir);
        Directory.CreateDirectory(subDir);

        try
        {
            // Create test files
            await File.WriteAllTextAsync(Path.Combine(testDir, "doc1.md"), "# Document 1");
            await File.WriteAllTextAsync(Path.Combine(subDir, "excluded.md"), "# Excluded");

            // Act
            var result = await _service.GetMatchingFilesAsync(
                testDir,
                ["**/*.md"],
                ["node_modules/**"]);

            // Assert
            Assert.Single(result);
            Assert.DoesNotContain(result, f => f.Contains("node_modules"));
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    #endregion

    #region GetHeadCommitAsync Tests

    [Fact]
    public async Task GetHeadCommitAsync_WithEmptyPath_ShouldReturnNull()
    {
        // Arrange, Act
        var result = await _service.GetHeadCommitAsync("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetHeadCommitAsync_WithNonExistentPath_ShouldReturnNull()
    {
        // Arrange
        var localPath = Path.Combine(_configuration.GitStoragePath, "nonexistent-" + Guid.NewGuid());

        // Act
        var result = await _service.GetHeadCommitAsync(localPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetHeadCommitAsync_WithNonRepoPath_ShouldReturnNull()
    {
        // Arrange
        var testDir = Path.Combine(_configuration.GitStoragePath, "not-a-repo-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);

        try
        {
            // Act
            var result = await _service.GetHeadCommitAsync(testDir);

            // Assert
            Assert.Null(result);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    #endregion

    #region ReadFileContentAsync Tests

    [Fact]
    public async Task ReadFileContentAsync_WithNonExistentFile_ShouldReturnEmpty()
    {
        // Arrange
        var testDir = Path.Combine(_configuration.GitStoragePath, "read-test-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);

        try
        {
            // Act
            var result = await _service.ReadFileContentAsync(testDir, "nonexistent.md");

            // Assert
            Assert.Equal(string.Empty, result);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReadFileContentAsync_WithExistingFile_ShouldReturnContent()
    {
        // Arrange
        var testDir = Path.Combine(_configuration.GitStoragePath, "read-test-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);
        var expectedContent = "# Test Document\n\nThis is the content.";
        await File.WriteAllTextAsync(Path.Combine(testDir, "test.md"), expectedContent);

        try
        {
            // Act
            var result = await _service.ReadFileContentAsync(testDir, "test.md");

            // Assert
            Assert.Equal(expectedContent, result);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    #endregion

    #region RepositoryExists Tests

    [Fact]
    public void RepositoryExists_WithEmptyPath_ShouldReturnFalse()
    {
        // Arrange, Act
        var result = _service.RepositoryExists("");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RepositoryExists_WithNonExistentPath_ShouldReturnFalse()
    {
        // Arrange
        var localPath = Path.Combine(_configuration.GitStoragePath, "nonexistent-" + Guid.NewGuid());

        // Act
        var result = _service.RepositoryExists(localPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RepositoryExists_WithNonRepoDirectory_ShouldReturnFalse()
    {
        // Arrange
        var testDir = Path.Combine(_configuration.GitStoragePath, "not-a-repo-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);

        try
        {
            // Act
            var result = _service.RepositoryExists(testDir);

            // Assert
            Assert.False(result);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, recursive: true);
            }
        }
    }

    #endregion

    #region CleanupRepositoryAsync Tests

    [Fact]
    public async Task CleanupRepositoryAsync_WithNonExistentPath_ShouldNotThrow()
    {
        // Arrange
        var localPath = Path.Combine(_configuration.GitStoragePath, "nonexistent-" + Guid.NewGuid());

        // Act & Assert - Should not throw
        await _service.CleanupRepositoryAsync(localPath);
    }

    [Fact]
    public async Task CleanupRepositoryAsync_WithExistingDirectory_ShouldDeleteDirectory()
    {
        // Arrange
        var testDir = Path.Combine(_configuration.GitStoragePath, "cleanup-test-" + Guid.NewGuid());
        Directory.CreateDirectory(testDir);
        await File.WriteAllTextAsync(Path.Combine(testDir, "test.txt"), "content");

        // Act
        await _service.CleanupRepositoryAsync(testDir);

        // Assert
        Assert.False(Directory.Exists(testDir));
    }

    #endregion
}
