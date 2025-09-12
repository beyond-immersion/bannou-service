using BeyondImmersion.BannouService.Connect;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for ConnectService
/// This test project can reference other service clients for integration testing.
/// </summary>
public class ConnectServiceTests
{
    private readonly Mock<ILogger<ConnectService>> _mockLogger;
    private readonly Mock<ConnectServiceConfiguration> _mockConfiguration;

    public ConnectServiceTests()
    {
        _mockLogger = new Mock<ILogger<ConnectService>>();
        _mockConfiguration = new Mock<ConnectServiceConfiguration>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var exception = Record.Exception(() => new ConnectService(
            _mockConfiguration.Object,
            _mockLogger.Object));

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ConnectService(
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ConnectService(
            _mockConfiguration.Object,
            null!));
    }

    // TODO: Add service-specific unit tests here
    // For service-to-service communication tests, add references to other service client projects
    // Example: Add reference to lib-accounts project to test AuthService → AccountsClient integration
}
