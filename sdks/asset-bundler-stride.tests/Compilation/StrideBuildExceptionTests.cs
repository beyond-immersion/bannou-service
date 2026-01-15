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
    public void FromBuildOutput_CreatesExceptionWithDetails()
    {
        // Arrange
        var exitCode = 1;
        var stderr = "error CS0001: Compilation error";
        var stdout = "Build started...";

        // Act
        var exception = StrideBuildException.FromBuildOutput(exitCode, stderr, stdout);

        // Assert
        Assert.Contains("exit code", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(exitCode, exception.ExitCode);
        Assert.Equal(stderr, exception.ErrorOutput);
        Assert.Equal(stdout, exception.StandardOutput);
    }

    [Fact]
    public void ExitCode_IsAccessible()
    {
        // Arrange & Act
        var exception = StrideBuildException.FromBuildOutput(2, "error", "output");

        // Assert
        Assert.Equal(2, exception.ExitCode);
    }

    [Fact]
    public void ErrorOutput_IsAccessible()
    {
        // Arrange
        var stderr = "error: something went wrong";

        // Act
        var exception = StrideBuildException.FromBuildOutput(1, stderr, "");

        // Assert
        Assert.Equal(stderr, exception.ErrorOutput);
    }

    [Fact]
    public void StandardOutput_IsAccessible()
    {
        // Arrange
        var stdout = "Building project...\nCompiling assets...";

        // Act
        var exception = StrideBuildException.FromBuildOutput(1, "", stdout);

        // Assert
        Assert.Equal(stdout, exception.StandardOutput);
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
