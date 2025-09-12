using BeyondImmersion.BannouService.Connect;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for ConnectService
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
    // This is where tests for service methods should be implemented
    // For service-to-service communication, use the generated client interfaces
    // Example: Mock IAccountsClient for testing AuthService integration
}
