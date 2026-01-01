using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Auth.Tests;

/// <summary>
/// Unit tests for SessionService.
/// Tests session lifecycle management including storage, indexing, and invalidation.
/// </summary>
public class SessionServiceTests
{
    private const string STATE_STORE = "auth-statestore";

    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<SessionDataModel>> _mockSessionStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<SessionService>> _mockLogger;
    private readonly AuthServiceConfiguration _configuration;
    private readonly SessionService _service;

    public SessionServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockSessionStore = new Mock<IStateStore<SessionDataModel>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<SessionService>>();

        _configuration = new AuthServiceConfiguration
        {
            JwtSecret = "test-jwt-secret-at-least-32-characters-long-for-security",
            JwtIssuer = "test-issuer",
            JwtAudience = "test-audience",
            JwtExpirationMinutes = 60
        };

        // Setup state store factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE))
            .Returns(_mockListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<SessionDataModel>(STATE_STORE))
            .Returns(_mockSessionStore.Object);

        _service = new SessionService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
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
        Assert.Throws<ArgumentNullException>(() => new SessionService(
            null!,
            _mockMessageBus.Object,
            _configuration,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullMessageBus_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SessionService(
            _mockStateStoreFactory.Object,
            null!,
            _configuration,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SessionService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SessionService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _configuration,
            null!));
    }

    #endregion

    #region GetSessionAsync Tests

    [Fact]
    public async Task GetSessionAsync_WithExistingSession_ShouldReturnSessionData()
    {
        // Arrange
        var sessionKey = "test-session-key";
        var expectedSession = CreateTestSessionData();

        _mockSessionStore.Setup(s => s.GetAsync($"session:{sessionKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSession);

        // Act
        var result = await _service.GetSessionAsync(sessionKey);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedSession.AccountId, result.AccountId);
        Assert.Equal(expectedSession.Email, result.Email);
    }

    [Fact]
    public async Task GetSessionAsync_WithNonExistentSession_ShouldReturnNull()
    {
        // Arrange
        var sessionKey = "non-existent-key";

        _mockSessionStore.Setup(s => s.GetAsync($"session:{sessionKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionDataModel?)null);

        // Act
        var result = await _service.GetSessionAsync(sessionKey);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region SaveSessionAsync Tests

    [Fact]
    public async Task SaveSessionAsync_ShouldCallStateStoreWithCorrectKey()
    {
        // Arrange
        var sessionKey = "test-session-key";
        var sessionData = CreateTestSessionData();

        _mockSessionStore.Setup(s => s.SaveAsync(
            $"session:{sessionKey}",
            sessionData,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        await _service.SaveSessionAsync(sessionKey, sessionData);

        // Assert
        _mockSessionStore.Verify(s => s.SaveAsync(
            $"session:{sessionKey}",
            sessionData,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveSessionAsync_WithTtl_ShouldIncludeTtlInOptions()
    {
        // Arrange
        var sessionKey = "test-session-key";
        var sessionData = CreateTestSessionData();
        var ttlSeconds = 3600;

        _mockSessionStore.Setup(s => s.SaveAsync(
            $"session:{sessionKey}",
            sessionData,
            It.Is<StateOptions>(o => o != null && o.Ttl == ttlSeconds),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        await _service.SaveSessionAsync(sessionKey, sessionData, ttlSeconds);

        // Assert
        _mockSessionStore.Verify(s => s.SaveAsync(
            $"session:{sessionKey}",
            sessionData,
            It.Is<StateOptions>(o => o != null && o.Ttl == ttlSeconds),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteSessionAsync Tests

    [Fact]
    public async Task DeleteSessionAsync_ShouldCallDeleteWithCorrectKey()
    {
        // Arrange
        var sessionKey = "test-session-key";

        _mockSessionStore.Setup(s => s.DeleteAsync($"session:{sessionKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.DeleteSessionAsync(sessionKey);

        // Assert
        _mockSessionStore.Verify(s => s.DeleteAsync($"session:{sessionKey}", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region AddSessionToAccountIndexAsync Tests

    [Fact]
    public async Task AddSessionToAccountIndexAsync_WithNewSession_ShouldAddToIndex()
    {
        // Arrange
        var accountId = "test-account-id";
        var sessionKey = "new-session-key";
        var indexKey = $"account-sessions:{accountId}";

        _mockListStore.Setup(s => s.GetAsync(indexKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        _mockListStore.Setup(s => s.SaveAsync(
            indexKey,
            It.Is<List<string>>(l => l.Contains(sessionKey)),
            It.IsAny<StateOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        await _service.AddSessionToAccountIndexAsync(accountId, sessionKey);

        // Assert
        _mockListStore.Verify(s => s.SaveAsync(
            indexKey,
            It.Is<List<string>>(l => l.Contains(sessionKey)),
            It.IsAny<StateOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddSessionToAccountIndexAsync_WithExistingSession_ShouldNotDuplicate()
    {
        // Arrange
        var accountId = "test-account-id";
        var sessionKey = "existing-session-key";
        var indexKey = $"account-sessions:{accountId}";

        _mockListStore.Setup(s => s.GetAsync(indexKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { sessionKey });

        // Act
        await _service.AddSessionToAccountIndexAsync(accountId, sessionKey);

        // Assert - Save should not be called since session already exists
        _mockListStore.Verify(s => s.SaveAsync(
            indexKey,
            It.IsAny<List<string>>(),
            It.IsAny<StateOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region RemoveSessionFromAccountIndexAsync Tests

    [Fact]
    public async Task RemoveSessionFromAccountIndexAsync_WithMultipleSessions_ShouldRemoveOne()
    {
        // Arrange
        var accountId = "test-account-id";
        var sessionKeyToRemove = "session-to-remove";
        var indexKey = $"account-sessions:{accountId}";
        var existingSessions = new List<string> { sessionKeyToRemove, "other-session" };

        _mockListStore.Setup(s => s.GetAsync(indexKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSessions);

        _mockListStore.Setup(s => s.SaveAsync(
            indexKey,
            It.Is<List<string>>(l => !l.Contains(sessionKeyToRemove) && l.Contains("other-session")),
            It.IsAny<StateOptions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        await _service.RemoveSessionFromAccountIndexAsync(accountId, sessionKeyToRemove);

        // Assert
        _mockListStore.Verify(s => s.SaveAsync(
            indexKey,
            It.Is<List<string>>(l => !l.Contains(sessionKeyToRemove)),
            It.IsAny<StateOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveSessionFromAccountIndexAsync_WithLastSession_ShouldDeleteIndex()
    {
        // Arrange
        var accountId = "test-account-id";
        var sessionKeyToRemove = "last-session";
        var indexKey = $"account-sessions:{accountId}";

        _mockListStore.Setup(s => s.GetAsync(indexKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { sessionKeyToRemove });

        _mockListStore.Setup(s => s.DeleteAsync(indexKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.RemoveSessionFromAccountIndexAsync(accountId, sessionKeyToRemove);

        // Assert - Should delete the index entirely
        _mockListStore.Verify(s => s.DeleteAsync(indexKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region AddSessionIdReverseIndexAsync Tests

    [Fact]
    public async Task AddSessionIdReverseIndexAsync_ShouldSaveWithCorrectTtl()
    {
        // Arrange
        var sessionId = "test-session-id";
        var sessionKey = "test-session-key";
        var ttlSeconds = 3600;
        var indexKey = $"session-id-index:{sessionId}";

        _mockStringStore.Setup(s => s.SaveAsync(
            indexKey,
            sessionKey,
            It.Is<StateOptions>(o => o.Ttl == ttlSeconds),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        await _service.AddSessionIdReverseIndexAsync(sessionId, sessionKey, ttlSeconds);

        // Assert
        _mockStringStore.Verify(s => s.SaveAsync(
            indexKey,
            sessionKey,
            It.Is<StateOptions>(o => o.Ttl == ttlSeconds),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region RemoveSessionIdReverseIndexAsync Tests

    [Fact]
    public async Task RemoveSessionIdReverseIndexAsync_ShouldDeleteWithCorrectKey()
    {
        // Arrange
        var sessionId = "test-session-id";
        var indexKey = $"session-id-index:{sessionId}";

        _mockStringStore.Setup(s => s.DeleteAsync(indexKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.RemoveSessionIdReverseIndexAsync(sessionId);

        // Assert
        _mockStringStore.Verify(s => s.DeleteAsync(indexKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region FindSessionKeyBySessionIdAsync Tests

    [Fact]
    public async Task FindSessionKeyBySessionIdAsync_WithExistingIndex_ShouldReturnSessionKey()
    {
        // Arrange
        var sessionId = "test-session-id";
        var expectedSessionKey = "test-session-key";
        var indexKey = $"session-id-index:{sessionId}";

        _mockStringStore.Setup(s => s.GetAsync(indexKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSessionKey);

        // Act
        var result = await _service.FindSessionKeyBySessionIdAsync(sessionId);

        // Assert
        Assert.Equal(expectedSessionKey, result);
    }

    [Fact]
    public async Task FindSessionKeyBySessionIdAsync_WithNonExistentIndex_ShouldReturnNull()
    {
        // Arrange
        var sessionId = "non-existent-session-id";
        var indexKey = $"session-id-index:{sessionId}";

        _mockStringStore.Setup(s => s.GetAsync(indexKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _service.FindSessionKeyBySessionIdAsync(sessionId);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetSessionKeysForAccountAsync Tests

    [Fact]
    public async Task GetSessionKeysForAccountAsync_WithExistingIndex_ShouldReturnList()
    {
        // Arrange
        var accountId = "test-account-id";
        var indexKey = $"account-sessions:{accountId}";
        var expectedKeys = new List<string> { "session1", "session2" };

        _mockListStore.Setup(s => s.GetAsync(indexKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedKeys);

        // Act
        var result = await _service.GetSessionKeysForAccountAsync(accountId);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("session1", result);
        Assert.Contains("session2", result);
    }

    [Fact]
    public async Task GetSessionKeysForAccountAsync_WithNoIndex_ShouldReturnEmptyList()
    {
        // Arrange
        var accountId = "test-account-id";
        var indexKey = $"account-sessions:{accountId}";

        _mockListStore.Setup(s => s.GetAsync(indexKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        // Act
        var result = await _service.GetSessionKeysForAccountAsync(accountId);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region DeleteAccountSessionsIndexAsync Tests

    [Fact]
    public async Task DeleteAccountSessionsIndexAsync_ShouldDeleteWithCorrectKey()
    {
        // Arrange
        var accountId = "test-account-id";
        var indexKey = $"account-sessions:{accountId}";

        _mockListStore.Setup(s => s.DeleteAsync(indexKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.DeleteAccountSessionsIndexAsync(accountId);

        // Assert
        _mockListStore.Verify(s => s.DeleteAsync(indexKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region InvalidateAllSessionsForAccountAsync Tests

    [Fact]
    public async Task InvalidateAllSessionsForAccountAsync_WithNoSessions_ShouldNotThrow()
    {
        // Arrange
        var accountId = Guid.NewGuid();

        _mockListStore.Setup(s => s.GetAsync($"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        // Act & Assert - Should not throw
        await _service.InvalidateAllSessionsForAccountAsync(accountId);
    }

    [Fact]
    public async Task InvalidateAllSessionsForAccountAsync_WithSessions_ShouldDeleteAllAndPublishEvent()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionKeys = new List<string> { "session1", "session2" };

        _mockListStore.Setup(s => s.GetAsync($"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionKeys);

        _mockSessionStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockListStore.Setup(s => s.DeleteAsync($"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockMessageBus.Setup(m => m.TryPublishAsync(
            It.IsAny<string>(),
            It.IsAny<SessionInvalidatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _service.InvalidateAllSessionsForAccountAsync(accountId);

        // Assert - Should delete both sessions
        _mockSessionStore.Verify(s => s.DeleteAsync("session:session1", It.IsAny<CancellationToken>()), Times.Once);
        _mockSessionStore.Verify(s => s.DeleteAsync("session:session2", It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Should delete the index
        _mockListStore.Verify(s => s.DeleteAsync($"account-sessions:{accountId}", It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Should publish event
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "session.invalidated",
            It.Is<SessionInvalidatedEvent>(e =>
                e.AccountId == accountId &&
                e.SessionIds.Count == 2),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region PublishSessionInvalidatedEventAsync Tests

    [Fact]
    public async Task PublishSessionInvalidatedEventAsync_ShouldPublishToCorrectTopic()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionIds = new List<string> { "session1", "session2" };
        var reason = SessionInvalidatedEventReason.Account_deleted;

        _mockMessageBus.Setup(m => m.TryPublishAsync(
            "session.invalidated",
            It.IsAny<SessionInvalidatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _service.PublishSessionInvalidatedEventAsync(accountId, sessionIds, reason);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "session.invalidated",
            It.Is<SessionInvalidatedEvent>(e =>
                e.AccountId == accountId &&
                e.SessionIds.SequenceEqual(sessionIds) &&
                e.Reason == reason &&
                e.DisconnectClients == true),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region PublishSessionUpdatedEventAsync Tests

    [Fact]
    public async Task PublishSessionUpdatedEventAsync_ShouldPublishToCorrectTopic()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionIdGuid = Guid.NewGuid();
        var sessionId = sessionIdGuid.ToString();
        var roles = new List<string> { "user", "admin" };
        var authorizations = new List<string> { "auth1" };
        var reason = SessionUpdatedEventReason.Role_changed;

        _mockMessageBus.Setup(m => m.TryPublishAsync(
            "session.updated",
            It.IsAny<SessionUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _service.PublishSessionUpdatedEventAsync(accountId, sessionId, roles, authorizations, reason);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "session.updated",
            It.Is<SessionUpdatedEvent>(e =>
                e.AccountId == accountId &&
                e.SessionId == sessionIdGuid &&
                e.Roles.SequenceEqual(roles) &&
                e.Authorizations.SequenceEqual(authorizations) &&
                e.Reason == reason),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetAccountSessionsAsync Tests

    [Fact]
    public async Task GetAccountSessionsAsync_WithNoSessions_ShouldReturnEmptyList()
    {
        // Arrange
        var accountId = "test-account-id";

        _mockListStore.Setup(s => s.GetAsync($"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        // Act
        var result = await _service.GetAccountSessionsAsync(accountId);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAccountSessionsAsync_WithActiveSessions_ShouldReturnSessionInfoList()
    {
        // Arrange
        var accountId = "test-account-id";
        var sessionKeys = new List<string> { "session1" };
        var sessionData = CreateTestSessionData();
        sessionData.ExpiresAt = DateTimeOffset.UtcNow.AddHours(1); // Active session

        _mockListStore.Setup(s => s.GetAsync($"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionKeys);

        _mockSessionStore.Setup(s => s.GetAsync("session:session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionData);

        // Act
        var result = await _service.GetAccountSessionsAsync(accountId);

        // Assert
        Assert.Single(result);
        Assert.Equal(Guid.Parse(sessionData.SessionId), result[0].SessionId);
    }

    [Fact]
    public async Task GetAccountSessionsAsync_WithExpiredSessions_ShouldFilterThemOut()
    {
        // Arrange
        var accountId = "test-account-id";
        var sessionKeys = new List<string> { "expired-session" };
        var expiredSession = CreateTestSessionData();
        expiredSession.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1); // Expired

        _mockListStore.Setup(s => s.GetAsync($"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionKeys);

        _mockSessionStore.Setup(s => s.GetAsync("session:expired-session", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredSession);

        // Setup for cleanup operation
        _mockListStore.Setup(s => s.GetAsync($"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionKeys);

        // Act
        var result = await _service.GetAccountSessionsAsync(accountId);

        // Assert - Expired sessions should be filtered out
        Assert.Empty(result);
    }

    #endregion

    #region Helper Methods

    private static SessionDataModel CreateTestSessionData()
    {
        return new SessionDataModel
        {
            AccountId = Guid.NewGuid(),
            Email = "test@example.com",
            DisplayName = "Test User",
            Roles = new List<string> { "user" },
            Authorizations = new List<string> { "auth1" },
            SessionId = Guid.NewGuid().ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
    }

    #endregion
}
