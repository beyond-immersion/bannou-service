using BeyondImmersion.Bannou.AssetLoader.Download;
using Xunit;

namespace BeyondImmersion.Bannou.AssetLoader.Tests.Download;

/// <summary>
/// Unit tests for DownloadOptions.
/// Verifies default values and configuration.
/// </summary>
public class DownloadOptionsTests
{
    #region Default Values Tests

    /// <summary>
    /// Verifies that default MaxRetries is 3.
    /// </summary>
    [Fact]
    public void MaxRetries_DefaultValue_Is3()
    {
        // Arrange & Act
        var options = new DownloadOptions();

        // Assert
        Assert.Equal(3, options.MaxRetries);
    }

    /// <summary>
    /// Verifies that default RetryDelay is 1 second.
    /// </summary>
    [Fact]
    public void RetryDelay_DefaultValue_Is1Second()
    {
        // Arrange & Act
        var options = new DownloadOptions();

        // Assert
        Assert.Equal(TimeSpan.FromSeconds(1), options.RetryDelay);
    }

    /// <summary>
    /// Verifies that default Timeout is 5 minutes.
    /// </summary>
    [Fact]
    public void Timeout_DefaultValue_Is5Minutes()
    {
        // Arrange & Act
        var options = new DownloadOptions();

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(5), options.Timeout);
    }

    /// <summary>
    /// Verifies that default BufferSize is 81920 (80KB).
    /// </summary>
    [Fact]
    public void BufferSize_DefaultValue_Is80KB()
    {
        // Arrange & Act
        var options = new DownloadOptions();

        // Assert
        Assert.Equal(81920, options.BufferSize);
    }

    /// <summary>
    /// Verifies that default VerifyHash is true.
    /// </summary>
    [Fact]
    public void VerifyHash_DefaultValue_IsTrue()
    {
        // Arrange & Act
        var options = new DownloadOptions();

        // Assert
        Assert.True(options.VerifyHash);
    }

    /// <summary>
    /// Verifies that default UserAgent is null.
    /// </summary>
    [Fact]
    public void UserAgent_DefaultValue_IsNull()
    {
        // Arrange & Act
        var options = new DownloadOptions();

        // Assert
        Assert.Null(options.UserAgent);
    }

    #endregion

    #region Custom Values Tests

    /// <summary>
    /// Verifies that custom values are respected.
    /// </summary>
    [Fact]
    public void CustomValues_AreRespected()
    {
        // Arrange & Act
        var options = new DownloadOptions
        {
            MaxRetries = 5,
            RetryDelay = TimeSpan.FromSeconds(2),
            Timeout = TimeSpan.FromMinutes(10),
            BufferSize = 16384,
            VerifyHash = false,
            UserAgent = "TestAgent/1.0"
        };

        // Assert
        Assert.Equal(5, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(2), options.RetryDelay);
        Assert.Equal(TimeSpan.FromMinutes(10), options.Timeout);
        Assert.Equal(16384, options.BufferSize);
        Assert.False(options.VerifyHash);
        Assert.Equal("TestAgent/1.0", options.UserAgent);
    }

    #endregion
}
