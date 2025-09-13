using BeyondImmersion.BannouService.Game-session;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Game-session.Tests;

/// <summary>
/// Unit tests for Game-sessionService
/// This test project can reference other service clients for integration testing.
/// </summary>
public class Game-sessionServiceTests
{
    private readonly Mock<ILogger<Game-sessionService>> _mockLogger;
    private readonly Mock<Game-sessionServiceConfiguration> _mockConfiguration;

    public Game-sessionServiceTests()
    {
        _mockLogger = new Mock<ILogger<Game-sessionService>>();
        _mockConfiguration = new Mock<Game-sessionServiceConfiguration>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var exception = Record.Exception(() => new Game-sessionService(
            _mockConfiguration.Object,
            _mockLogger.Object));

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Game-sessionService(
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Game-sessionService(
            _mockConfiguration.Object,
            null!));
    }

    // TODO: Add service-specific unit tests here
    // For service-to-service communication tests, add references to other service client projects
    // Example: Add reference to lib-accounts project to test AuthService → AccountsClient integration
}
