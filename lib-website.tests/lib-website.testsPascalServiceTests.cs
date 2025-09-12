using BeyondImmersion.BannouService.Website;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Website.Tests;

/// <summary>
/// Unit tests for WebsiteService
/// </summary>
public class WebsiteServiceTests
{
    private readonly Mock<ILogger<WebsiteService>> _mockLogger;
    private readonly Mock<WebsiteServiceConfiguration> _mockConfiguration;

    public WebsiteServiceTests()
    {
        _mockLogger = new Mock<ILogger<WebsiteService>>();
        _mockConfiguration = new Mock<WebsiteServiceConfiguration>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var exception = Record.Exception(() => new WebsiteService(
            _mockConfiguration.Object,
            _mockLogger.Object));

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new WebsiteService(
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new WebsiteService(
            _mockConfiguration.Object,
            null!));
    }

    // TODO: Add service-specific unit tests here
    // This is where tests for service methods should be implemented
    // For service-to-service communication, use the generated client interfaces
    // Example: Mock IAccountsClient for testing AuthService integration
}
