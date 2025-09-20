using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Auth;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Auth.Tests;

/// <summary>
/// Unit tests for AuthService
/// This test project can reference other service clients for integration testing.
/// </summary>
public class AuthServiceTests
{
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly Mock<AuthServiceConfiguration> _mockConfiguration;
    private readonly Mock<IAccountsClient> _mockAccountsClient;
    private readonly Mock<DaprClient> _mockDaprClient;

    public AuthServiceTests()
    {
        _mockLogger = new Mock<ILogger<AuthService>>();
        _mockConfiguration = new Mock<AuthServiceConfiguration>();
        _mockAccountsClient = new Mock<IAccountsClient>();
        _mockDaprClient = new Mock<DaprClient>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _mockConfiguration.Object,
            _mockLogger.Object);

        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullAccountsClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            null!,
            _mockDaprClient.Object,
            _mockConfiguration.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullDaprClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            null!,
            _mockConfiguration.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _mockConfiguration.Object,
            null!));
    }

    [Fact]
    public void AuthServiceConfiguration_ShouldBindFromEnvironmentVariables()
    {
        // Arrange
        var testSecret = "test-jwt-secret-from-env";
        var testIssuer = "test-issuer";
        var testAudience = "test-audience";
        var testExpiration = 120;

        try
        {
            // Set environment variables with BANNOU_ prefix
            Environment.SetEnvironmentVariable("BANNOU_JWTSECRET", testSecret);
            Environment.SetEnvironmentVariable("BANNOU_JWTISSUER", testIssuer);
            Environment.SetEnvironmentVariable("BANNOU_JWTAUDIENCE", testAudience);
            Environment.SetEnvironmentVariable("BANNOU_JWTEXPIRATIONMINUTES", testExpiration.ToString());

            // Act - Build configuration using the same method as dependency injection
            var config = BeyondImmersion.BannouService.Configuration.IServiceConfiguration.BuildConfiguration<AuthServiceConfiguration>();

            // Assert
            Assert.NotNull(config);
            Assert.Equal(testSecret, config.JwtSecret);
            Assert.Equal(testIssuer, config.JwtIssuer);
            Assert.Equal(testAudience, config.JwtAudience);
            Assert.Equal(testExpiration, config.JwtExpirationMinutes);
        }
        finally
        {
            // Clean up environment variables
            Environment.SetEnvironmentVariable("BANNOU_JWTSECRET", null);
            Environment.SetEnvironmentVariable("BANNOU_JWTISSUER", null);
            Environment.SetEnvironmentVariable("BANNOU_JWTAUDIENCE", null);
            Environment.SetEnvironmentVariable("BANNOU_JWTEXPIRATIONMINUTES", null);
        }
    }

    // TODO: Add service-specific unit tests here
    // For service-to-service communication tests, add references to other service client projects
    // Example: Add reference to lib-accounts project to test AuthService → AccountsClient integration
}
