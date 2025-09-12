using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests;

/// <summary>
/// Unit tests for BehaviorService
/// </summary>
public class BehaviorServiceTests
{
    private readonly Mock<ILogger<BehaviorService>> _mockLogger;
    private readonly Mock<BehaviorServiceConfiguration> _mockConfiguration;

    public BehaviorServiceTests()
    {
        _mockLogger = new Mock<ILogger<BehaviorService>>();
        _mockConfiguration = new Mock<BehaviorServiceConfiguration>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var exception = Record.Exception(() => new BehaviorService(
            _mockConfiguration.Object,
            _mockLogger.Object));
            
        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BehaviorService(
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BehaviorService(
            _mockConfiguration.Object,
            null!));
    }

    // TODO: Add service-specific unit tests here
    // This is where tests for service methods should be implemented
    // For service-to-service communication, use the generated client interfaces
    // Example: Mock IAccountsClient for testing AuthService integration
}
