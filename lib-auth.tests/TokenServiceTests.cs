using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Subscriptions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using Xunit;

namespace BeyondImmersion.BannouService.Auth.Tests;

/// <summary>
/// Unit tests for TokenService.
/// Tests JWT generation, refresh token management, and token validation.
/// </summary>
public class TokenServiceTests
{
    private const string STATE_STORE = "auth-statestore";

    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<ISubscriptionsClient> _mockSubscriptionsClient;
    private readonly Mock<ISessionService> _mockSessionService;
    private readonly Mock<ILogger<TokenService>> _mockLogger;
    private readonly AuthServiceConfiguration _configuration;
    private readonly TokenService _service;

    public TokenServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockSubscriptionsClient = new Mock<ISubscriptionsClient>();
        _mockSessionService = new Mock<ISessionService>();
        _mockLogger = new Mock<ILogger<TokenService>>();

        _configuration = new AuthServiceConfiguration
        {
            JwtSecret = "test-jwt-secret-at-least-32-characters-long-for-security",
            JwtIssuer = "test-issuer",
            JwtAudience = "test-audience",
            JwtExpirationMinutes = 60
        };

        // Setup state store factory to return the string store
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);

        _service = new TokenService(
            _mockStateStoreFactory.Object,
            _mockSubscriptionsClient.Object,
            _mockSessionService.Object,
            _configuration,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        Assert.NotNull(_service);
    }

    [Fact]
    public void Constructor_WithNullStateStoreFactory_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenService(
            null!,
            _mockSubscriptionsClient.Object,
            _mockSessionService.Object,
            _configuration,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullSubscriptionsClient_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenService(
            _mockStateStoreFactory.Object,
            null!,
            _mockSessionService.Object,
            _configuration,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullSessionService_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenService(
            _mockStateStoreFactory.Object,
            _mockSubscriptionsClient.Object,
            null!,
            _configuration,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenService(
            _mockStateStoreFactory.Object,
            _mockSubscriptionsClient.Object,
            _mockSessionService.Object,
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TokenService(
            _mockStateStoreFactory.Object,
            _mockSubscriptionsClient.Object,
            _mockSessionService.Object,
            _configuration,
            null!));
    }

    #endregion

    #region GenerateRefreshToken Tests

    [Fact]
    public void GenerateRefreshToken_ShouldReturnNonEmptyString()
    {
        // Act
        var token = _service.GenerateRefreshToken();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturnUniqueTokens()
    {
        // Act
        var token1 = _service.GenerateRefreshToken();
        var token2 = _service.GenerateRefreshToken();

        // Assert
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void GenerateRefreshToken_ShouldReturn32CharacterGuidFormat()
    {
        // Act
        var token = _service.GenerateRefreshToken();

        // Assert - Guid.ToString("N") produces 32 hex characters
        Assert.Equal(32, token.Length);
        Assert.True(token.All(c => char.IsLetterOrDigit(c)));
    }

    #endregion

    #region GenerateSecureToken Tests

    [Fact]
    public void GenerateSecureToken_ShouldReturnNonEmptyString()
    {
        // Act
        var token = _service.GenerateSecureToken();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public void GenerateSecureToken_ShouldReturnUniqueTokens()
    {
        // Act
        var token1 = _service.GenerateSecureToken();
        var token2 = _service.GenerateSecureToken();

        // Assert
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void GenerateSecureToken_ShouldReturnUrlSafeBase64()
    {
        // Act
        var token = _service.GenerateSecureToken();

        // Assert - Should not contain + or / (replaced with - and _), no = padding
        Assert.DoesNotContain("+", token);
        Assert.DoesNotContain("/", token);
        Assert.DoesNotContain("=", token);
    }

    #endregion

    #region StoreRefreshTokenAsync Tests

    [Fact]
    public async Task StoreRefreshTokenAsync_ShouldCallStateStoreWithCorrectKey()
    {
        // Arrange
        var accountId = "test-account-id";
        var refreshToken = "test-refresh-token";
        var expectedKey = $"refresh_token:{refreshToken}";

        _mockStringStore.Setup(s => s.SaveAsync(
            expectedKey,
            accountId,
            It.IsAny<StateOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        await _service.StoreRefreshTokenAsync(accountId, refreshToken);

        // Assert - TTL is in seconds: 7 days = 604800 seconds
        _mockStringStore.Verify(s => s.SaveAsync(
            expectedKey,
            accountId,
            It.Is<StateOptions>(o => o.Ttl == (int)TimeSpan.FromDays(7).TotalSeconds),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ValidateRefreshTokenAsync Tests

    [Fact]
    public async Task ValidateRefreshTokenAsync_WithValidToken_ShouldReturnAccountId()
    {
        // Arrange
        var refreshToken = "valid-refresh-token";
        var expectedAccountId = "test-account-id";
        var expectedKey = $"refresh_token:{refreshToken}";

        _mockStringStore.Setup(s => s.GetAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedAccountId);

        // Act
        var result = await _service.ValidateRefreshTokenAsync(refreshToken);

        // Assert
        Assert.Equal(expectedAccountId, result);
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_WithInvalidToken_ShouldReturnNull()
    {
        // Arrange
        var refreshToken = "invalid-refresh-token";
        var expectedKey = $"refresh_token:{refreshToken}";

        _mockStringStore.Setup(s => s.GetAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _service.ValidateRefreshTokenAsync(refreshToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateRefreshTokenAsync_WhenStateStoreThrows_ShouldReturnNull()
    {
        // Arrange
        var refreshToken = "error-token";
        var expectedKey = $"refresh_token:{refreshToken}";

        _mockStringStore.Setup(s => s.GetAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("State store error"));

        // Act
        var result = await _service.ValidateRefreshTokenAsync(refreshToken);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region RemoveRefreshTokenAsync Tests

    [Fact]
    public async Task RemoveRefreshTokenAsync_ShouldCallDeleteWithCorrectKey()
    {
        // Arrange
        var refreshToken = "token-to-remove";
        var expectedKey = $"refresh_token:{refreshToken}";

        _mockStringStore.Setup(s => s.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.RemoveRefreshTokenAsync(refreshToken);

        // Assert
        _mockStringStore.Verify(s => s.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveRefreshTokenAsync_WhenDeleteFails_ShouldNotThrow()
    {
        // Arrange
        var refreshToken = "error-token";
        var expectedKey = $"refresh_token:{refreshToken}";

        _mockStringStore.Setup(s => s.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Delete failed"));

        // Act & Assert - Should not throw
        await _service.RemoveRefreshTokenAsync(refreshToken);
    }

    #endregion

    #region GenerateAccessTokenAsync Tests

    [Fact]
    public async Task GenerateAccessTokenAsync_WithNullAccount_ShouldThrow()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.GenerateAccessTokenAsync(null!));
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_WithMissingJwtSecret_ShouldThrow()
    {
        // Arrange
        var account = CreateTestAccount();
        var configWithNoSecret = new AuthServiceConfiguration
        {
            JwtSecret = "",
            JwtIssuer = "test-issuer",
            JwtAudience = "test-audience",
            JwtExpirationMinutes = 60
        };

        var service = new TokenService(
            _mockStateStoreFactory.Object,
            _mockSubscriptionsClient.Object,
            _mockSessionService.Object,
            configWithNoSecret,
            _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateAccessTokenAsync(account));
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_WithMissingJwtIssuer_ShouldThrow()
    {
        // Arrange
        var account = CreateTestAccount();
        var configWithNoIssuer = new AuthServiceConfiguration
        {
            JwtSecret = "test-jwt-secret-at-least-32-characters-long-for-security",
            JwtIssuer = "",
            JwtAudience = "test-audience",
            JwtExpirationMinutes = 60
        };

        var service = new TokenService(
            _mockStateStoreFactory.Object,
            _mockSubscriptionsClient.Object,
            _mockSessionService.Object,
            configWithNoIssuer,
            _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateAccessTokenAsync(account));
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_WithMissingJwtAudience_ShouldThrow()
    {
        // Arrange
        var account = CreateTestAccount();
        var configWithNoAudience = new AuthServiceConfiguration
        {
            JwtSecret = "test-jwt-secret-at-least-32-characters-long-for-security",
            JwtIssuer = "test-issuer",
            JwtAudience = "",
            JwtExpirationMinutes = 60
        };

        var service = new TokenService(
            _mockStateStoreFactory.Object,
            _mockSubscriptionsClient.Object,
            _mockSessionService.Object,
            configWithNoAudience,
            _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateAccessTokenAsync(account));
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_WithValidAccount_ShouldReturnJwt()
    {
        // Arrange
        var account = CreateTestAccount();

        _mockSubscriptionsClient.Setup(c => c.GetCurrentSubscriptionsAsync(
            It.IsAny<GetCurrentSubscriptionsRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentSubscriptionsResponse
            {
                Authorizations = new List<string> { "auth1", "auth2" }
            });

        _mockSessionService.Setup(s => s.SaveSessionAsync(
            It.IsAny<string>(),
            It.IsAny<SessionDataModel>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockSessionService.Setup(s => s.AddSessionToAccountIndexAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockSessionService.Setup(s => s.AddSessionIdReverseIndexAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var token = await _service.GenerateAccessTokenAsync(account);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(token));
        // JWT has 3 parts separated by dots
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_WhenSubscriptionsNotFound_ShouldStillGenerateToken()
    {
        // Arrange
        var account = CreateTestAccount();

        _mockSubscriptionsClient.Setup(c => c.GetCurrentSubscriptionsAsync(
            It.IsAny<GetCurrentSubscriptionsRequest>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, "", new Dictionary<string, IEnumerable<string>>(), null));

        _mockSessionService.Setup(s => s.SaveSessionAsync(
            It.IsAny<string>(),
            It.IsAny<SessionDataModel>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockSessionService.Setup(s => s.AddSessionToAccountIndexAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockSessionService.Setup(s => s.AddSessionIdReverseIndexAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var token = await _service.GenerateAccessTokenAsync(account);

        // Assert - Should still generate a valid JWT
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_WhenSubscriptionsThrowsOtherError_ShouldRethrow()
    {
        // Arrange
        var account = CreateTestAccount();

        _mockSubscriptionsClient.Setup(c => c.GetCurrentSubscriptionsAsync(
            It.IsAny<GetCurrentSubscriptionsRequest>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Server error"));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() =>
            _service.GenerateAccessTokenAsync(account));
    }

    [Fact]
    public async Task GenerateAccessTokenAsync_ShouldSaveSessionWithCorrectData()
    {
        // Arrange
        var account = CreateTestAccount();
        SessionDataModel? capturedSession = null;

        _mockSubscriptionsClient.Setup(c => c.GetCurrentSubscriptionsAsync(
            It.IsAny<GetCurrentSubscriptionsRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CurrentSubscriptionsResponse
            {
                Authorizations = new List<string> { "auth1" }
            });

        _mockSessionService.Setup(s => s.SaveSessionAsync(
            It.IsAny<string>(),
            It.IsAny<SessionDataModel>(),
            It.IsAny<int?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, SessionDataModel, int?, CancellationToken>((key, session, ttl, ct) =>
            {
                capturedSession = session;
            })
            .Returns(Task.CompletedTask);

        _mockSessionService.Setup(s => s.AddSessionToAccountIndexAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockSessionService.Setup(s => s.AddSessionIdReverseIndexAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service.GenerateAccessTokenAsync(account);

        // Assert
        Assert.NotNull(capturedSession);
        Assert.Equal(account.AccountId, capturedSession.AccountId);
        Assert.Equal(account.Email, capturedSession.Email);
        Assert.Equal(account.DisplayName, capturedSession.DisplayName);
        Assert.Contains("auth1", capturedSession.Authorizations);
    }

    #endregion

    #region ValidateTokenAsync Tests

    [Fact]
    public async Task ValidateTokenAsync_WithNullToken_ShouldReturnUnauthorized()
    {
        // Act
        var (status, response) = await _service.ValidateTokenAsync(null!);

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithEmptyToken_ShouldReturnUnauthorized()
    {
        // Act
        var (status, response) = await _service.ValidateTokenAsync("");

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithInvalidJwt_ShouldReturnUnauthorized()
    {
        // Arrange
        var invalidToken = "not.a.valid.jwt.token";

        // Act
        var (status, response) = await _service.ValidateTokenAsync(invalidToken);

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);
    }

    #endregion

    #region ExtractSessionKeyFromJwtAsync Tests

    [Fact]
    public async Task ExtractSessionKeyFromJwtAsync_WithInvalidJwt_ShouldReturnNull()
    {
        // Arrange
        var invalidToken = "not.valid.jwt";

        // Act
        var result = await _service.ExtractSessionKeyFromJwtAsync(invalidToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ExtractSessionKeyFromJwtAsync_WithEmptyString_ShouldReturnNull()
    {
        // Act
        var result = await _service.ExtractSessionKeyFromJwtAsync("");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Helper Methods

    private static AccountResponse CreateTestAccount()
    {
        return new AccountResponse
        {
            AccountId = Guid.NewGuid(),
            Email = "test@example.com",
            DisplayName = "Test User",
            EmailVerified = true,
            Roles = new List<string> { "user" },
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
