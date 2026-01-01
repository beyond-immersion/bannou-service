using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for BannouSessionManager.
/// Tests distributed session management using mocked state stores and message bus.
/// </summary>
public class BannouSessionManagerTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<BannouSessionManager>> _mockLogger;
    private readonly BannouSessionManager _sessionManager;

    // Mock stores for different data types
    private readonly Mock<IStateStore<Dictionary<string, Guid>>> _mockMappingsStore;
    private readonly Mock<IStateStore<ConnectionStateData>> _mockConnectionStore;
    private readonly Mock<IStateStore<SessionHeartbeat>> _mockHeartbeatStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;

    public BannouSessionManagerTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<BannouSessionManager>>();

        // Set up type-specific stores
        _mockMappingsStore = new Mock<IStateStore<Dictionary<string, Guid>>>();
        _mockConnectionStore = new Mock<IStateStore<ConnectionStateData>>();
        _mockHeartbeatStore = new Mock<IStateStore<SessionHeartbeat>>();
        _mockStringStore = new Mock<IStateStore<string>>();

        // Default store behaviors
        _mockMappingsStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, Guid>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockConnectionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ConnectionStateData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockHeartbeatStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SessionHeartbeat>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockMappingsStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockConnectionStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockHeartbeatStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockStringStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Configure state store factory to return appropriate stores
        _mockStateStoreFactory
            .Setup(f => f.GetStore<Dictionary<string, Guid>>(It.IsAny<string>()))
            .Returns(_mockMappingsStore.Object);

        _mockStateStoreFactory
            .Setup(f => f.GetStore<ConnectionStateData>(It.IsAny<string>()))
            .Returns(_mockConnectionStore.Object);

        _mockStateStoreFactory
            .Setup(f => f.GetStore<SessionHeartbeat>(It.IsAny<string>()))
            .Returns(_mockHeartbeatStore.Object);

        _mockStateStoreFactory
            .Setup(f => f.GetStore<string>(It.IsAny<string>()))
            .Returns(_mockStringStore.Object);

        // Default message bus behavior
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<SessionEvent>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _sessionManager = new BannouSessionManager(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var manager = new BannouSessionManager(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object);

        // Assert
        Assert.NotNull(manager);
    }

    [Fact]
    public void Constructor_WithNullStateStoreFactory_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new BannouSessionManager(
            null!,
            _mockMessageBus.Object,
            _mockLogger.Object));
        Assert.Equal("stateStoreFactory", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullMessageBus_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new BannouSessionManager(
            _mockStateStoreFactory.Object,
            null!,
            _mockLogger.Object));
        Assert.Equal("messageBus", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => new BannouSessionManager(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            null!));
        Assert.Equal("logger", ex.ParamName);
    }

    #endregion

    #region SetSessionServiceMappingsAsync Tests

    [Fact]
    public async Task SetSessionServiceMappingsAsync_WithValidParameters_ShouldSaveToStore()
    {
        // Arrange
        var sessionId = "test-session-123";
        var mappings = new Dictionary<string, Guid>
        {
            { "accounts", Guid.NewGuid() },
            { "auth", Guid.NewGuid() }
        };

        // Act
        await _sessionManager.SetSessionServiceMappingsAsync(sessionId, mappings);

        // Assert
        _mockMappingsStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.Contains(sessionId)),
            mappings,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetSessionServiceMappingsAsync_WithCustomTtl_ShouldUseProvidedTtl()
    {
        // Arrange
        var sessionId = "test-session";
        var mappings = new Dictionary<string, Guid>();
        var customTtl = TimeSpan.FromMinutes(30);

        StateOptions? capturedOptions = null;
        _mockMappingsStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, Guid>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, Guid>, StateOptions?, CancellationToken>((k, v, o, ct) => capturedOptions = o)
            .ReturnsAsync("etag-1");

        // Act
        await _sessionManager.SetSessionServiceMappingsAsync(sessionId, mappings, customTtl);

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.Equal((int)customTtl.TotalSeconds, capturedOptions.Ttl);
    }

    [Fact]
    public async Task SetSessionServiceMappingsAsync_WhenStoreThrows_ShouldPropagateException()
    {
        // Arrange
        var sessionId = "test-session";
        var mappings = new Dictionary<string, Guid>();

        _mockMappingsStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, Guid>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Store error"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sessionManager.SetSessionServiceMappingsAsync(sessionId, mappings));
    }

    #endregion

    #region GetSessionServiceMappingsAsync Tests

    [Fact]
    public async Task GetSessionServiceMappingsAsync_WithExistingSession_ShouldReturnMappings()
    {
        // Arrange
        var sessionId = "test-session-123";
        var expectedMappings = new Dictionary<string, Guid>
        {
            { "accounts", Guid.NewGuid() }
        };

        _mockMappingsStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(sessionId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMappings);

        // Act
        var result = await _sessionManager.GetSessionServiceMappingsAsync(sessionId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedMappings, result);
    }

    [Fact]
    public async Task GetSessionServiceMappingsAsync_WithNonExistingSession_ShouldReturnNull()
    {
        // Arrange
        var sessionId = "non-existent-session";

        _mockMappingsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, Guid>?)null);

        // Act
        var result = await _sessionManager.GetSessionServiceMappingsAsync(sessionId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetSessionServiceMappingsAsync_WhenStoreThrows_ShouldPropagateException()
    {
        // Arrange
        var sessionId = "test-session";

        _mockMappingsStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Store error"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sessionManager.GetSessionServiceMappingsAsync(sessionId));
    }

    #endregion

    #region SetConnectionStateAsync Tests

    [Fact]
    public async Task SetConnectionStateAsync_WithValidParameters_ShouldSaveToStore()
    {
        // Arrange
        var sessionId = "test-session-123";
        var stateData = new ConnectionStateData
        {
            SessionId = sessionId,
            AccountId = "account-123",
            ConnectedAt = DateTimeOffset.UtcNow
        };

        // Act
        await _sessionManager.SetConnectionStateAsync(sessionId, stateData);

        // Assert
        _mockConnectionStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.Contains(sessionId)),
            stateData,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetConnectionStateAsync_WithCustomTtl_ShouldUseProvidedTtl()
    {
        // Arrange
        var sessionId = "test-session";
        var stateData = new ConnectionStateData { SessionId = sessionId };
        var customTtl = TimeSpan.FromHours(2);

        StateOptions? capturedOptions = null;
        _mockConnectionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ConnectionStateData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ConnectionStateData, StateOptions?, CancellationToken>((k, v, o, ct) => capturedOptions = o)
            .ReturnsAsync("etag-1");

        // Act
        await _sessionManager.SetConnectionStateAsync(sessionId, stateData, customTtl);

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.Equal((int)customTtl.TotalSeconds, capturedOptions.Ttl);
    }

    #endregion

    #region GetConnectionStateAsync Tests

    [Fact]
    public async Task GetConnectionStateAsync_WithExistingSession_ShouldReturnState()
    {
        // Arrange
        var sessionId = "test-session-123";
        var expectedState = new ConnectionStateData
        {
            SessionId = sessionId,
            AccountId = "account-123"
        };

        _mockConnectionStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(sessionId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedState);

        // Act
        var result = await _sessionManager.GetConnectionStateAsync(sessionId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(sessionId, result.SessionId);
    }

    [Fact]
    public async Task GetConnectionStateAsync_WithNonExistingSession_ShouldReturnNull()
    {
        // Arrange
        var sessionId = "non-existent-session";

        _mockConnectionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectionStateData?)null);

        // Act
        var result = await _sessionManager.GetConnectionStateAsync(sessionId);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region UpdateSessionHeartbeatAsync Tests

    [Fact]
    public async Task UpdateSessionHeartbeatAsync_WithValidParameters_ShouldSaveHeartbeat()
    {
        // Arrange
        var sessionId = "test-session";
        var instanceId = "instance-001";

        SessionHeartbeat? capturedHeartbeat = null;
        _mockHeartbeatStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SessionHeartbeat>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SessionHeartbeat, StateOptions?, CancellationToken>((k, h, o, ct) => capturedHeartbeat = h)
            .ReturnsAsync("etag-1");

        // Act
        await _sessionManager.UpdateSessionHeartbeatAsync(sessionId, instanceId);

        // Assert
        Assert.NotNull(capturedHeartbeat);
        Assert.Equal(sessionId, capturedHeartbeat.SessionId);
        Assert.Equal(instanceId, capturedHeartbeat.InstanceId);
        Assert.Equal(1, capturedHeartbeat.ConnectionCount);
    }

    [Fact]
    public async Task UpdateSessionHeartbeatAsync_WhenStoreThrows_ShouldNotPropagateException()
    {
        // Arrange
        var sessionId = "test-session";
        var instanceId = "instance-001";

        _mockHeartbeatStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SessionHeartbeat>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Store error"));

        // Act - should not throw (heartbeat failures shouldn't break main functionality)
        await _sessionManager.UpdateSessionHeartbeatAsync(sessionId, instanceId);

        // Assert - no exception means test passed
        Assert.True(true);
    }

    #endregion

    #region SetReconnectionTokenAsync Tests

    [Fact]
    public async Task SetReconnectionTokenAsync_WithValidParameters_ShouldSaveToken()
    {
        // Arrange
        var reconnectionToken = "reconnect-token-123";
        var sessionId = "test-session";
        var window = TimeSpan.FromMinutes(5);

        StateOptions? capturedOptions = null;
        _mockStringStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, StateOptions?, CancellationToken>((k, v, o, ct) => capturedOptions = o)
            .ReturnsAsync("etag-1");

        // Act
        await _sessionManager.SetReconnectionTokenAsync(reconnectionToken, sessionId, window);

        // Assert
        _mockStringStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.Contains(reconnectionToken)),
            sessionId,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.NotNull(capturedOptions);
        Assert.Equal((int)window.TotalSeconds, capturedOptions.Ttl);
    }

    #endregion

    #region ValidateReconnectionTokenAsync Tests

    [Fact]
    public async Task ValidateReconnectionTokenAsync_WithValidToken_ShouldReturnSessionId()
    {
        // Arrange
        var reconnectionToken = "valid-token";
        var expectedSessionId = "session-123";

        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(reconnectionToken)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSessionId);

        // Act
        var result = await _sessionManager.ValidateReconnectionTokenAsync(reconnectionToken);

        // Assert
        Assert.Equal(expectedSessionId, result);
    }

    [Fact]
    public async Task ValidateReconnectionTokenAsync_WithInvalidToken_ShouldReturnNull()
    {
        // Arrange
        var reconnectionToken = "invalid-token";

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _sessionManager.ValidateReconnectionTokenAsync(reconnectionToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateReconnectionTokenAsync_WithEmptyString_ShouldReturnNull()
    {
        // Arrange
        var reconnectionToken = "token";

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        // Act
        var result = await _sessionManager.ValidateReconnectionTokenAsync(reconnectionToken);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region RemoveReconnectionTokenAsync Tests

    [Fact]
    public async Task RemoveReconnectionTokenAsync_WithValidToken_ShouldDeleteFromStore()
    {
        // Arrange
        var reconnectionToken = "token-to-remove";

        // Act
        await _sessionManager.RemoveReconnectionTokenAsync(reconnectionToken);

        // Assert
        _mockStringStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(reconnectionToken)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveReconnectionTokenAsync_WhenStoreThrows_ShouldNotPropagateException()
    {
        // Arrange
        var reconnectionToken = "token";

        _mockStringStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delete failed"));

        // Act - should not throw
        await _sessionManager.RemoveReconnectionTokenAsync(reconnectionToken);

        // Assert - no exception means test passed
        Assert.True(true);
    }

    #endregion

    #region InitiateReconnectionWindowAsync Tests

    [Fact]
    public async Task InitiateReconnectionWindowAsync_WithExistingSession_ShouldUpdateStateAndToken()
    {
        // Arrange
        var sessionId = "test-session";
        var reconnectionToken = "reconnect-token";
        var window = TimeSpan.FromMinutes(5);
        var userRoles = new List<string> { "user", "player" };

        var existingState = new ConnectionStateData
        {
            SessionId = sessionId,
            AccountId = "account-123",
            ConnectedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        _mockConnectionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingState);

        ConnectionStateData? capturedState = null;
        _mockConnectionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ConnectionStateData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ConnectionStateData, StateOptions?, CancellationToken>((k, s, o, ct) => capturedState = s)
            .ReturnsAsync("etag-1");

        // Act
        await _sessionManager.InitiateReconnectionWindowAsync(sessionId, reconnectionToken, window, userRoles);

        // Assert
        Assert.NotNull(capturedState);
        Assert.Equal(reconnectionToken, capturedState.ReconnectionToken);
        Assert.NotNull(capturedState.ReconnectionExpiresAt);
        Assert.NotNull(capturedState.DisconnectedAt);
        Assert.Equal(userRoles, capturedState.UserRoles);

        // Verify reconnection token was stored
        _mockStringStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.Contains(reconnectionToken)),
            sessionId,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitiateReconnectionWindowAsync_WithNonExistingSession_ShouldNotThrow()
    {
        // Arrange
        var sessionId = "non-existent";
        var reconnectionToken = "token";
        var window = TimeSpan.FromMinutes(5);

        _mockConnectionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectionStateData?)null);

        // Act - should not throw
        await _sessionManager.InitiateReconnectionWindowAsync(sessionId, reconnectionToken, window, null);

        // Assert - no reconnection token should be stored for non-existent session
        _mockStringStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region RestoreSessionFromReconnectionAsync Tests

    [Fact]
    public async Task RestoreSessionFromReconnectionAsync_WithValidSession_ShouldRestoreState()
    {
        // Arrange
        var sessionId = "test-session";
        var reconnectionToken = "valid-token";

        var existingState = new ConnectionStateData
        {
            SessionId = sessionId,
            AccountId = "account-123",
            ReconnectionToken = reconnectionToken,
            ReconnectionExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            DisconnectedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        _mockConnectionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingState);

        // Act
        var result = await _sessionManager.RestoreSessionFromReconnectionAsync(sessionId, reconnectionToken);

        // Assert
        Assert.NotNull(result);
        Assert.Null(result.DisconnectedAt);
        Assert.Null(result.ReconnectionExpiresAt);
        Assert.Null(result.ReconnectionToken);

        // Verify reconnection token was removed
        _mockStringStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(reconnectionToken)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreSessionFromReconnectionAsync_WithNonExistingSession_ShouldReturnNull()
    {
        // Arrange
        var sessionId = "non-existent";
        var reconnectionToken = "token";

        _mockConnectionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectionStateData?)null);

        // Act
        var result = await _sessionManager.RestoreSessionFromReconnectionAsync(sessionId, reconnectionToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RestoreSessionFromReconnectionAsync_WithExpiredWindow_ShouldReturnNull()
    {
        // Arrange
        var sessionId = "test-session";
        var reconnectionToken = "token";

        var existingState = new ConnectionStateData
        {
            SessionId = sessionId,
            ReconnectionToken = reconnectionToken,
            ReconnectionExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5), // Expired
            DisconnectedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        _mockConnectionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingState);

        // Act
        var result = await _sessionManager.RestoreSessionFromReconnectionAsync(sessionId, reconnectionToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RestoreSessionFromReconnectionAsync_WithWrongToken_ShouldReturnNull()
    {
        // Arrange
        var sessionId = "test-session";
        var correctToken = "correct-token";
        var wrongToken = "wrong-token";

        var existingState = new ConnectionStateData
        {
            SessionId = sessionId,
            ReconnectionToken = correctToken,
            ReconnectionExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            DisconnectedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        _mockConnectionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingState);

        // Act
        var result = await _sessionManager.RestoreSessionFromReconnectionAsync(sessionId, wrongToken);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region RemoveSessionAsync Tests

    [Fact]
    public async Task RemoveSessionAsync_WithValidSession_ShouldDeleteAllRelatedKeys()
    {
        // Arrange
        var sessionId = "test-session";

        // Act
        await _sessionManager.RemoveSessionAsync(sessionId);

        // Assert - all three stores should have delete called
        _mockConnectionStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(sessionId)),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockMappingsStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(sessionId)),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockHeartbeatStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(sessionId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveSessionAsync_WhenStoreThrows_ShouldNotPropagateException()
    {
        // Arrange
        var sessionId = "test-session";

        _mockConnectionStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Delete failed"));

        // Act - should not throw
        await _sessionManager.RemoveSessionAsync(sessionId);

        // Assert - no exception means test passed
        Assert.True(true);
    }

    #endregion

    #region PublishSessionEventAsync Tests

    [Fact]
    public async Task PublishSessionEventAsync_WithValidParameters_ShouldPublishEvent()
    {
        // Arrange
        var eventType = "connected";
        var sessionId = "test-session";
        var eventData = new { reason = "new connection" };

        SessionEvent? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<SessionEvent>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string, SessionEvent, PublishOptions?, Guid?, CancellationToken>((t, e, o, g, ct) => capturedEvent = e)
            .ReturnsAsync(true);

        // Act
        await _sessionManager.PublishSessionEventAsync(eventType, sessionId, eventData);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(eventType, capturedEvent.EventType);
        Assert.Equal(sessionId, capturedEvent.SessionId);
        Assert.NotNull(capturedEvent.Data);
    }

    [Fact]
    public async Task PublishSessionEventAsync_WithNullEventData_ShouldPublishEvent()
    {
        // Arrange
        var eventType = "disconnected";
        var sessionId = "test-session";

        // Act
        await _sessionManager.PublishSessionEventAsync(eventType, sessionId);

        // Assert
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            It.IsAny<string>(),
            It.IsAny<SessionEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishSessionEventAsync_WhenMessageBusThrows_ShouldNotPropagateException()
    {
        // Arrange
        var eventType = "error";
        var sessionId = "test-session";

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<SessionEvent>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Publish failed"));

        // Act - should not throw
        await _sessionManager.PublishSessionEventAsync(eventType, sessionId);

        // Assert - no exception means test passed
        Assert.True(true);
    }

    #endregion
}
