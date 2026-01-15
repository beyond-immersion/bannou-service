using BeyondImmersion.Bannou.AssetBundler.Stride.Compilation;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Stride.Tests.Compilation;

/// <summary>
/// Tests for StrideBuildException.
/// </summary>
public class StrideBuildExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        // Arrange & Act
        var exception = new StrideBuildException("Build failed");

        // Assert
        Assert.Equal("Build failed", exception.Message);
    }

    [Fact]
    public void Constructor_WithInnerException_SetsInnerException()
    {
        // Arrange
        var inner = new InvalidOperationException("Inner error");

        // Act
        var exception = new StrideBuildException("Build failed", inner);

        // Assert
        Assert.Equal("Build failed", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void FromBuildOutput_CreatesExceptionWithFormattedMessage()
    {
        // Arrange
        var exitCode = 1;
        var stderr = "error CS0001: Compilation error";

        // Act
        var exception = StrideBuildException.FromBuildOutput(exitCode, stderr);

        // Assert
        Assert.Contains("exit code", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1", exception.Message);
        Assert.Contains("Compilation error", exception.Message);
    }

    [Fact]
    public void FromBuildOutput_WithStandardOutput_IncludesErrorLines()
    {
        // Arrange
        var stdout = "Building project...\nerror MSB1234: Build failed\nCompleted";

        // Act
        var exception = StrideBuildException.FromBuildOutput(1, "", stdout);

        // Assert
        Assert.Contains("Build errors", exception.Message);
        Assert.Contains("MSB1234", exception.Message);
    }

    [Fact]
    public void ExitCode_CanBeSetViaInitializer()
    {
        // Arrange & Act
        var exception = new StrideBuildException("Build failed") { ExitCode = 2 };

        // Assert
        Assert.Equal(2, exception.ExitCode);
    }

    [Fact]
    public void ErrorOutput_CanBeSetViaInitializer()
    {
        // Arrange
        var stderr = "error: something went wrong";

        // Act
        var exception = new StrideBuildException("Build failed") { ErrorOutput = stderr };

        // Assert
        Assert.Equal(stderr, exception.ErrorOutput);
    }

    [Fact]
    public void FailedAssets_CanBeSetViaInitializer()
    {
        // Arrange
        var failedAssets = new List<string> { "model.fbx", "texture.png" };

        // Act
        var exception = new StrideBuildException("Build failed") { FailedAssets = failedAssets };

        // Assert
        Assert.NotNull(exception.FailedAssets);
        Assert.Equal(2, exception.FailedAssets.Count);
        Assert.Contains("model.fbx", exception.FailedAssets);
    }

    [Fact]
    public void IsStrideBuildException_CanBeCaught()
    {
        // Arrange & Act & Assert
        var caught = false;
        try
        {
            throw new StrideBuildException("Test exception");
        }
        catch (StrideBuildException)
        {
            caught = true;
        }

        Assert.True(caught);
    }

    [Fact]
    public void InheritsFromException_CanBeCaughtAsException()
    {
        // Arrange & Act & Assert
        var caught = false;
        try
        {
            throw new StrideBuildException("Test exception");
        }
        catch (Exception)
        {
            caught = true;
        }

        Assert.True(caught);
    }
}
