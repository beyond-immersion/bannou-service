using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
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
    private readonly Mock<ICacheableStateStore<string>> _mockCacheableStringStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<SessionDataModel>> _mockSessionStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IEdgeRevocationService> _mockEdgeRevocationService;
    private readonly Mock<ILogger<SessionService>> _mockLogger;
    private readonly AuthServiceConfiguration _configuration;
    private readonly SessionService _service;

    public SessionServiceTests()
    {
        // Configure JWT settings in Program.Configuration (used by auth services)
        TestConfigurationHelper.ConfigureJwt();

        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockCacheableStringStore = new Mock<ICacheableStateStore<string>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockSessionStore = new Mock<IStateStore<SessionDataModel>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockEdgeRevocationService = new Mock<IEdgeRevocationService>();
        _mockLogger = new Mock<ILogger<SessionService>>();

        _configuration = new AuthServiceConfiguration
        {
            JwtExpirationMinutes = 60
        };

        // Setup edge revocation service (disabled by default for tests)
        _mockEdgeRevocationService.Setup(e => e.IsEnabled).Returns(false);

        // Setup state store factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetCacheableStore<string>(STATE_STORE))
            .Returns(_mockCacheableStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<SessionDataModel>(STATE_STORE))
            .Returns(_mockSessionStore.Object);

        var telemetryProvider = new NullTelemetryProvider();

        _service = new SessionService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _configuration,
            _mockEdgeRevocationService.Object,
            telemetryProvider,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        Assert.NotNull(_service);
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
    public async Task AddSessionToAccountIndexAsync_WithNewSession_ShouldAddToSet()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionKey = "new-session-key";
        var indexKey = $"account-sessions:{accountId}";

        _mockCacheableStringStore.Setup(s => s.AddToSetAsync(
            indexKey,
            sessionKey,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.AddSessionToAccountIndexAsync(accountId, sessionKey);

        // Assert - Atomic SADD called with correct key, value, and TTL
        _mockCacheableStringStore.Verify(s => s.AddToSetAsync(
            indexKey,
            sessionKey,
            It.Is<StateOptions>(o => o != null && o.Ttl > 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddSessionToAccountIndexAsync_WithExistingSession_ShouldCallAddToSetIdempotently()
    {
        // Arrange - Redis SADD is idempotent; duplicate adds are handled atomically
        var accountId = Guid.NewGuid();
        var sessionKey = "existing-session-key";
        var indexKey = $"account-sessions:{accountId}";

        // Returns false = item already existed in set
        _mockCacheableStringStore.Setup(s => s.AddToSetAsync(
            indexKey,
            sessionKey,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _service.AddSessionToAccountIndexAsync(accountId, sessionKey);

        // Assert - AddToSetAsync is still called (set handles deduplication atomically)
        _mockCacheableStringStore.Verify(s => s.AddToSetAsync(
            indexKey,
            sessionKey,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region RemoveSessionFromAccountIndexAsync Tests

    [Fact]
    public async Task RemoveSessionFromAccountIndexAsync_ShouldRemoveFromSet()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionKeyToRemove = "session-to-remove";
        var indexKey = $"account-sessions:{accountId}";

        _mockCacheableStringStore.Setup(s => s.RemoveFromSetAsync(
            indexKey,
            sessionKeyToRemove,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.RemoveSessionFromAccountIndexAsync(accountId, sessionKeyToRemove);

        // Assert - Atomic SREM called with correct key and value
        _mockCacheableStringStore.Verify(s => s.RemoveFromSetAsync(
            indexKey,
            sessionKeyToRemove,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveSessionFromAccountIndexAsync_WithLastSession_SetDeletedAutomatically()
    {
        // Arrange - Redis automatically deletes the key when the set becomes empty
        var accountId = Guid.NewGuid();
        var sessionKeyToRemove = "last-session";
        var indexKey = $"account-sessions:{accountId}";

        _mockCacheableStringStore.Setup(s => s.RemoveFromSetAsync(
            indexKey,
            sessionKeyToRemove,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.RemoveSessionFromAccountIndexAsync(accountId, sessionKeyToRemove);

        // Assert - Only RemoveFromSetAsync is called; Redis handles empty set cleanup
        _mockCacheableStringStore.Verify(s => s.RemoveFromSetAsync(
            indexKey,
            sessionKeyToRemove,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region AddSessionIdReverseIndexAsync Tests

    [Fact]
    public async Task AddSessionIdReverseIndexAsync_ShouldSaveWithCorrectTtl()
    {
        // Arrange
        var sessionId = Guid.NewGuid();
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
        var sessionId = Guid.NewGuid();
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
        var sessionId = Guid.NewGuid();
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
        var sessionId = Guid.NewGuid();
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
        var accountId = Guid.NewGuid();
        var indexKey = $"account-sessions:{accountId}";
        var expectedKeys = new List<string> { "session1", "session2" };

        _mockCacheableStringStore.Setup(s => s.GetSetAsync<string>(indexKey, It.IsAny<CancellationToken>()))
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
        var accountId = Guid.NewGuid();
        var indexKey = $"account-sessions:{accountId}";

        // GetSetAsync returns empty list when set doesn't exist
        _mockCacheableStringStore.Setup(s => s.GetSetAsync<string>(indexKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

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
        var accountId = Guid.NewGuid();
        var indexKey = $"account-sessions:{accountId}";

        _mockCacheableStringStore.Setup(s => s.DeleteAsync(indexKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.DeleteAccountSessionsIndexAsync(accountId);

        // Assert
        _mockCacheableStringStore.Verify(s => s.DeleteAsync(indexKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region InvalidateAllSessionsForAccountAsync Tests

    [Fact]
    public async Task InvalidateAllSessionsForAccountAsync_WithNoSessions_ShouldNotThrow()
    {
        // Arrange
        var accountId = Guid.NewGuid();

        _mockCacheableStringStore.Setup(s => s.GetSetAsync<string>(
            $"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act & Assert - Should not throw
        await _service.InvalidateAllSessionsForAccountAsync(accountId);
    }

    [Fact]
    public async Task InvalidateAllSessionsForAccountAsync_WithSessions_ShouldDeleteAllAndPublishEvent()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        // Session keys must be valid GUID strings because PublishSessionInvalidatedEventAsync
        // parses them back to Guids using Guid.TryParse
        var sessionKey1 = Guid.NewGuid().ToString("N");
        var sessionKey2 = Guid.NewGuid().ToString("N");
        var sessionKeys = new List<string> { sessionKey1, sessionKey2 };

        _mockCacheableStringStore.Setup(s => s.GetSetAsync<string>(
            $"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionKeys);

        _mockSessionStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockCacheableStringStore.Setup(s => s.DeleteAsync(
            $"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockMessageBus.Setup(m => m.TryPublishAsync(
            It.IsAny<string>(),
            It.IsAny<SessionInvalidatedEvent>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.InvalidateAllSessionsForAccountAsync(accountId);

        // Assert - Should delete both sessions
        _mockSessionStore.Verify(s => s.DeleteAsync($"session:{sessionKey1}", It.IsAny<CancellationToken>()), Times.Once);
        _mockSessionStore.Verify(s => s.DeleteAsync($"session:{sessionKey2}", It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Should delete the index
        _mockCacheableStringStore.Verify(s => s.DeleteAsync($"account-sessions:{accountId}", It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Should publish event
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "session.invalidated",
            It.Is<SessionInvalidatedEvent>(e =>
                e.AccountId == accountId &&
                e.SessionIds.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region PublishSessionInvalidatedEventAsync Tests

    [Fact]
    public async Task PublishSessionInvalidatedEventAsync_ShouldPublishToCorrectTopic()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();
        // Session keys are stored as Guid.ToString("N") format
        var sessionIds = new List<string> { sessionId1.ToString("N"), sessionId2.ToString("N") };
        var expectedSessionGuids = new List<Guid> { sessionId1, sessionId2 };
        var reason = SessionInvalidatedEventReason.AccountDeleted;

        _mockMessageBus.Setup(m => m.TryPublishAsync(
            "session.invalidated",
            It.IsAny<SessionInvalidatedEvent>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.PublishSessionInvalidatedEventAsync(accountId, sessionIds, reason);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "session.invalidated",
            It.Is<SessionInvalidatedEvent>(e =>
                e.AccountId == accountId &&
                e.SessionIds.SequenceEqual(expectedSessionGuids) &&
                e.Reason == reason &&
                e.DisconnectClients == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region PublishSessionUpdatedEventAsync Tests

    [Fact]
    public async Task PublishSessionUpdatedEventAsync_ShouldPublishToCorrectTopic()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var roles = new List<string> { "user", "admin" };
        var authorizations = new List<string> { "auth1" };
        var reason = SessionUpdatedEventReason.RoleChanged;

        _mockMessageBus.Setup(m => m.TryPublishAsync(
            "session.updated",
            It.IsAny<SessionUpdatedEvent>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.PublishSessionUpdatedEventAsync(accountId, sessionId, roles, authorizations, reason);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "session.updated",
            It.Is<SessionUpdatedEvent>(e =>
                e.AccountId == accountId &&
                e.SessionId == sessionId &&
                e.Roles.SequenceEqual(roles) &&
                e.Authorizations.SequenceEqual(authorizations) &&
                e.Reason == reason),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetAccountSessionsAsync Error Handling Tests

    [Fact]
    public async Task GetAccountSessionsAsync_WhenStateStoreThrows_ShouldPublishErrorAndRethrow()
    {
        // Arrange
        var accountId = Guid.NewGuid();

        _mockCacheableStringStore.Setup(s => s.GetSetAsync<string>(
            $"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis connection failed"));

        string? capturedServiceName = null;
        string? capturedOperation = null;

        _mockMessageBus.Setup(m => m.TryPublishErrorAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string?, string?, ServiceErrorEventSeverity, object?, string?, Guid?, CancellationToken>(
                (svc, op, _, _, _, _, _, _, _, _, _) =>
                {
                    capturedServiceName = svc;
                    capturedOperation = op;
                })
            .ReturnsAsync(true);

        // Act & Assert - Should re-throw (not mask the error)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.GetAccountSessionsAsync(accountId));

        // Assert - Error event was published before re-throw
        Assert.Equal("auth", capturedServiceName);
        Assert.Equal("GetAccountSessions", capturedOperation);
    }

    #endregion

    #region AddSessionToAccountIndexAsync Error Handling Tests

    [Fact]
    public async Task AddSessionToAccountIndexAsync_WhenStateStoreThrows_ShouldPublishErrorAndNotRethrow()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionKey = "error-session-key";

        _mockCacheableStringStore.Setup(s => s.AddToSetAsync(
            $"account-sessions:{accountId}",
            sessionKey,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis write failed"));

        string? capturedServiceName = null;
        string? capturedOperation = null;

        _mockMessageBus.Setup(m => m.TryPublishErrorAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string?, string?, ServiceErrorEventSeverity, object?, string?, Guid?, CancellationToken>(
                (svc, op, _, _, _, _, _, _, _, _, _) =>
                {
                    capturedServiceName = svc;
                    capturedOperation = op;
                })
            .ReturnsAsync(true);

        // Act - Should NOT throw (catch swallows, publishes error, does not re-throw)
        await _service.AddSessionToAccountIndexAsync(accountId, sessionKey);

        // Assert - Error event was published
        Assert.Equal("auth", capturedServiceName);
        Assert.Equal("AddSessionToAccountIndex", capturedOperation);
    }

    #endregion

    #region InvalidateAllSessionsForAccountAsync Edge Revocation Tests

    [Fact]
    public async Task InvalidateAllSessionsForAccountAsync_WithEdgeRevocationEnabled_ShouldPushRevocations()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionKey = Guid.NewGuid().ToString("N");
        var sessionKeys = new List<string> { sessionKey };

        var sessionData = CreateTestSessionData();
        sessionData.AccountId = accountId;
        sessionData.Jti = "test-jti-for-revocation";
        sessionData.ExpiresAt = DateTimeOffset.UtcNow.AddHours(1);

        _mockCacheableStringStore.Setup(s => s.GetSetAsync<string>(
            $"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionKeys);

        _mockSessionStore.Setup(s => s.GetAsync($"session:{sessionKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionData);

        _mockSessionStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockCacheableStringStore.Setup(s => s.DeleteAsync(
            $"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Enable edge revocation
        _mockEdgeRevocationService.Setup(e => e.IsEnabled).Returns(true);

        string? capturedJti = null;
        Guid capturedAccountId = Guid.Empty;

        _mockEdgeRevocationService.Setup(e => e.RevokeTokenAsync(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, Guid, TimeSpan, string, CancellationToken>(
                (jti, acctId, _, _, _) =>
                {
                    capturedJti = jti;
                    capturedAccountId = acctId;
                })
            .Returns(Task.CompletedTask);

        _mockMessageBus.Setup(m => m.TryPublishAsync(
            It.IsAny<string>(),
            It.IsAny<SessionInvalidatedEvent>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.InvalidateAllSessionsForAccountAsync(accountId);

        // Assert - Edge revocation was called with correct JTI and account ID
        Assert.Equal("test-jti-for-revocation", capturedJti);
        Assert.Equal(accountId, capturedAccountId);
    }

    [Fact]
    public async Task InvalidateAllSessionsForAccountAsync_WithEdgeRevocationDisabled_ShouldSkipRevocations()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionKey = Guid.NewGuid().ToString("N");
        var sessionKeys = new List<string> { sessionKey };

        var sessionData = CreateTestSessionData();
        sessionData.AccountId = accountId;
        sessionData.Jti = "test-jti-no-revocation";
        sessionData.ExpiresAt = DateTimeOffset.UtcNow.AddHours(1);

        _mockCacheableStringStore.Setup(s => s.GetSetAsync<string>(
            $"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionKeys);

        _mockSessionStore.Setup(s => s.GetAsync($"session:{sessionKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionData);

        _mockSessionStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockCacheableStringStore.Setup(s => s.DeleteAsync(
            $"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Edge revocation is disabled (default from constructor)
        _mockEdgeRevocationService.Setup(e => e.IsEnabled).Returns(false);

        _mockMessageBus.Setup(m => m.TryPublishAsync(
            It.IsAny<string>(),
            It.IsAny<SessionInvalidatedEvent>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.InvalidateAllSessionsForAccountAsync(accountId);

        // Assert - RevokeTokenAsync should NOT have been called
        _mockEdgeRevocationService.Verify(e => e.RevokeTokenAsync(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // Assert - Session deletion and event publishing still happened
        _mockSessionStore.Verify(s => s.DeleteAsync($"session:{sessionKey}", It.IsAny<CancellationToken>()), Times.Once);
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "session.invalidated",
            It.IsAny<SessionInvalidatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetAccountSessionsAsync Tests

    [Fact]
    public async Task GetAccountSessionsAsync_WithNoSessions_ShouldReturnEmptyList()
    {
        // Arrange
        var accountId = Guid.NewGuid();

        _mockCacheableStringStore.Setup(s => s.GetSetAsync<string>(
            $"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _service.GetAccountSessionsAsync(accountId);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAccountSessionsAsync_WithActiveSessions_ShouldReturnSessionInfoList()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionKeys = new List<string> { "session1" };
        var sessionData = CreateTestSessionData();
        sessionData.ExpiresAt = DateTimeOffset.UtcNow.AddHours(1); // Active session

        _mockCacheableStringStore.Setup(s => s.GetSetAsync<string>(
            $"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionKeys);

        _mockSessionStore.Setup(s => s.GetAsync("session:session1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionData);

        // Act
        var result = await _service.GetAccountSessionsAsync(accountId);

        // Assert
        Assert.Single(result);
        Assert.Equal(sessionData.SessionId, result[0].SessionId);
    }

    [Fact]
    public async Task GetAccountSessionsAsync_WithExpiredSessions_ShouldFilterThemOut()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionKeys = new List<string> { "expired-session" };
        var expiredSession = CreateTestSessionData();
        expiredSession.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1); // Expired

        _mockCacheableStringStore.Setup(s => s.GetSetAsync<string>(
            $"account-sessions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionKeys);

        _mockSessionStore.Setup(s => s.GetAsync("session:expired-session", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredSession);

        // Setup for cleanup via RemoveFromSetAsync (expired sessions are cleaned up)
        _mockCacheableStringStore.Setup(s => s.RemoveFromSetAsync(
            $"account-sessions:{accountId}",
            "expired-session",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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
            SessionId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
    }

    #endregion
}
