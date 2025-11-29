using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests;

/// <summary>
/// Unit tests for BehaviorService
/// This test project can reference other service clients for integration testing.
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
        var service = new BehaviorService(
            _mockLogger.Object,
            _mockConfiguration.Object);

        Assert.NotNull(service);
    }


    // TODO: Add service-specific unit tests here
    // For service-to-service communication tests, add references to other service client projects
    // Example: Add reference to lib-accounts project to test AuthService → AccountsClient integration
}
