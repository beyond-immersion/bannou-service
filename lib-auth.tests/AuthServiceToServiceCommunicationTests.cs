using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Accounts.Client;
using BeyondImmersion.BannouService.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Auth.Tests;

/// <summary>
/// Unit tests demonstrating correct service-to-service communication using generated NSwag clients.
/// This test shows the CORRECT architecture pattern for distributed service calls.
/// AuthService depends on AccountsService, so this test can reference both.
/// </summary>
public class AuthServiceToServiceCommunicationTests
{
    [Fact]
    public async Task AuthService_RegisterAsync_UsesAccountsClient_ForServiceToServiceCommunication()
    {
        // Arrange - Setup mocks for dependencies
        var mockAccountsClient = new Mock<IAccountsClient>();
        var mockConfiguration = new Mock<AuthServiceConfiguration>();
        var mockLogger = new Mock<ILogger<AuthService>>();

        // Setup mock response from AccountsClient (generated client)
        var expectedAccount = new AccountResponse
        {
            AccountId = "test-account-id",
            Email = "test@example.com",
            DisplayName = "testuser",
            Provider = Provider.Email,
            EmailVerified = false,
            Roles = new[] { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // ✅ CORRECT: Mock the generated client interface
        mockAccountsClient
            .Setup(client => client.CreateAccountAsync(It.IsAny<CreateAccountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedAccount);

        // Create AuthService with generated client injection
        var authService = new AuthService(
            mockAccountsClient.Object, // ✅ CORRECT: Inject generated client
            mockConfiguration.Object,
            mockLogger.Object);

        var registerRequest = new RegisterRequest
        {
            Username = "testuser",
            Password = "TestPassword123!",
            Email = "test@example.com"
        };

        // Act - Call the service method
        var result = await authService.RegisterAsync(registerRequest, CancellationToken.None);

        // Assert - Verify correct service-to-service communication
        Assert.NotNull(result);
        Assert.IsType<RegisterResponse>(result.Value);

        var registerResponse = result.Value;
        Assert.NotNull(registerResponse.Access_token);
        Assert.NotNull(registerResponse.Refresh_token);
        Assert.Contains("eyJ", registerResponse.Access_token); // JWT format

        // ✅ CRITICAL: Verify that the generated client was used for service-to-service call
        mockAccountsClient.Verify(
            client => client.CreateAccountAsync(
                It.Is<CreateAccountRequest>(req =>
                    req.DisplayName == "testuser" &&
                    req.Email == "test@example.com" &&
                    req.Provider == Provider.Email),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "AuthService MUST use generated AccountsClient for service-to-service communication");

        // Log verification - ensure proper structured logging
        mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Successfully registered user: testuser")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AuthService_LoginAsync_UsesAccountsClient_ForAccountRetrieval()
    {
        // Arrange - Setup dependencies for login test
        var mockAccountsClient = new Mock<IAccountsClient>();
        var mockConfiguration = new Mock<AuthServiceConfiguration>();
        var mockLogger = new Mock<ILogger<AuthService>>();

        // Setup mock account retrieval via client
        var expectedAccount = new AccountResponse
        {
            AccountId = "existing-account-id",
            Email = "existing@example.com",
            DisplayName = "existinguser",
            Provider = Provider.Email,
            EmailVerified = true,
            Roles = new[] { "user" },
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // ✅ CORRECT: Mock the GetAccountByEmailAsync method on generated client
        mockAccountsClient
            .Setup(client => client.GetAccountByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedAccount);

        var authService = new AuthService(
            mockAccountsClient.Object, // ✅ CORRECT: Generated client injection
            mockConfiguration.Object,
            mockLogger.Object);

        // Act - Perform login operation
        var result = await authService.LoginWithCredentialsGetAsync(
            "existing@example.com",
            "password",
            CancellationToken.None);

        // Assert - Verify successful authentication via client
        Assert.NotNull(result);
        Assert.IsType<LoginResponse>(result.Value);

        var loginResponse = result.Value;
        Assert.NotNull(loginResponse.Access_token);
        Assert.NotNull(loginResponse.Refresh_token);

        // ✅ CRITICAL: Verify service-to-service call was made through generated client
        mockAccountsClient.Verify(
            client => client.GetAccountByEmailAsync(
                "existing@example.com",
                It.IsAny<CancellationToken>()),
            Times.Once,
            "AuthService MUST use generated AccountsClient to retrieve account information");

        // Verify authentication success logging
        mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Successfully authenticated user")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AuthService_LoginAsync_HandlesAccountNotFound_WhenClientReturnsNull()
    {
        // Arrange - Setup scenario where account doesn't exist
        var mockAccountsClient = new Mock<IAccountsClient>();
        var mockConfiguration = new Mock<AuthServiceConfiguration>();
        var mockLogger = new Mock<ILogger<AuthService>>();

        // ✅ CORRECT: Mock client to return null (account not found)
        mockAccountsClient
            .Setup(client => client.GetAccountByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountResponse?)null);

        var authService = new AuthService(
            mockAccountsClient.Object,
            mockConfiguration.Object,
            mockLogger.Object);

        // Act - Attempt login with non-existent account
        var result = await authService.LoginWithCredentialsGetAsync(
            "nonexistent@example.com",
            "password",
            CancellationToken.None);

        // Assert - Verify proper error handling
        Assert.NotNull(result);
        Assert.IsType<UnauthorizedObjectResult>(result.Result);

        var unauthorizedResult = (UnauthorizedObjectResult)result.Result;
        var errorResponse = Assert.IsType<AuthErrorResponse>(unauthorizedResult.Value);

        Assert.Equal(AuthErrorResponseError.AUTHENTICATION_FAILED, errorResponse.Error);
        Assert.Equal("Invalid credentials", errorResponse.Message);

        // ✅ CRITICAL: Verify client was still called (service communication attempted)
        mockAccountsClient.Verify(
            client => client.GetAccountByEmailAsync(
                "nonexistent@example.com",
                It.IsAny<CancellationToken>()),
            Times.Once,
            "AuthService should attempt account lookup via client even for non-existent accounts");
    }
}

/// <summary>
/// Architecture documentation embedded in test class:
///
/// ✅ CORRECT Service-to-Service Communication Pattern:
/// 1. Services inject generated clients (IAccountsClient, IAuthClient, etc.)
/// 2. Generated clients inherit from DaprServiceClientBase
/// 3. DaprServiceClientBase uses ServiceAppMappingResolver for dynamic routing
/// 4. Clients automatically route to correct service instances via Dapr
/// 5. In development: all clients route to "bannou" (single node)
/// 6. In production: clients route to dedicated service instances
///
/// ❌ WRONG Anti-Pattern:
/// - Never inject service interfaces directly (IAccountsService) from other services
/// - Never use HttpClient directly for service-to-service calls
/// - Never bypass the generated client architecture
///
/// Benefits of Generated Client Architecture:
/// - Type-safe service-to-service communication
/// - Automatic service discovery and routing
/// - Schema-driven API contracts ensure consistency
/// - Testable via mocking interfaces
/// - Scales from single node to distributed deployment
///
/// Service Dependency Pattern:
/// - AuthService depends on AccountsService (can reference accounts client)
/// - AccountsService does NOT depend on AuthService (cannot reference auth)
/// - Each service's unit test project can reference the services it depends on
/// - This creates proper dependency direction and prevents circular references
/// </summary>
