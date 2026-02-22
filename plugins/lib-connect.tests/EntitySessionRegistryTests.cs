using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for EntitySessionRegistry.
/// Tests dual-index Redis operations using mocked cacheable state stores and message bus.
/// </summary>
public class EntitySessionRegistryTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IClientEventPublisher> _mockClientEventPublisher;
    private readonly Mock<ILogger<EntitySessionRegistry>> _mockLogger;
    private readonly ConnectServiceConfiguration _configuration;
    private readonly EntitySessionRegistry _registry;

    // Mock stores
    private readonly Mock<ICacheableStateStore<string>> _mockCacheStore;
    private readonly Mock<IStateStore<SessionHeartbeat>> _mockHeartbeatStore;

    public EntitySessionRegistryTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockClientEventPublisher = new Mock<IClientEventPublisher>();
        _mockLogger = new Mock<ILogger<EntitySessionRegistry>>();
        _configuration = new ConnectServiceConfiguration();

        _mockCacheStore = new Mock<ICacheableStateStore<string>>();
        _mockHeartbeatStore = new Mock<IStateStore<SessionHeartbeat>>();

        // Default store behaviors
        _mockCacheStore
            .Setup(s => s.AddToSetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCacheStore
            .Setup(s => s.RemoveFromSetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCacheStore
            .Setup(s => s.DeleteSetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCacheStore
            .Setup(s => s.SetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
        _mockCacheStore
            .Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Configure state store factory
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<string>(It.IsAny<string>()))
            .Returns(_mockCacheStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<SessionHeartbeat>(It.IsAny<string>()))
            .Returns(_mockHeartbeatStore.Object);

        // Default message bus behavior
        _mockMessageBus
            .Setup(m => m.TryPublishErrorAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
                It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _registry = new EntitySessionRegistry(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockClientEventPublisher.Object,
            _configuration,
            _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<EntitySessionRegistry>();
        Assert.NotNull(_registry);
    }

    #endregion

    #region RegisterAsync Tests

    [Fact]
    public async Task RegisterAsync_WhenCalled_AddsToForwardAndReverseIndexes()
    {
        // Arrange
        var entityType = "character";
        var entityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        await _registry.RegisterAsync(entityType, entityId, sessionId);

        // Assert - forward index: entity-sessions:{entityType}:{entityId} -> sessionId
        _mockCacheStore.Verify(s => s.AddToSetAsync(
            It.Is<string>(k => k.Contains("entity-sessions:") && k.Contains(entityType) && k.Contains(entityId.ToString("N"))),
            sessionId,
            It.Is<StateOptions?>(o => o != null && o.Ttl == _configuration.SessionTtlSeconds),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - reverse index: session-entities:{sessionId} -> "{entityType}:{entityId}"
        _mockCacheStore.Verify(s => s.AddToSetAsync(
            It.Is<string>(k => k.Contains("session-entities:") && k.Contains(sessionId)),
            It.Is<string>(v => v.Contains(entityType) && v.Contains(entityId.ToString("N"))),
            It.Is<StateOptions?>(o => o != null && o.Ttl == _configuration.SessionTtlSeconds),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WithMultipleSessionsForSameEntity_AddsAll()
    {
        // Arrange
        var entityType = "character";
        var entityId = Guid.NewGuid();
        var sessionId1 = Guid.NewGuid().ToString();
        var sessionId2 = Guid.NewGuid().ToString();

        // Act
        await _registry.RegisterAsync(entityType, entityId, sessionId1);
        await _registry.RegisterAsync(entityType, entityId, sessionId2);

        // Assert - both sessions added to forward index
        _mockCacheStore.Verify(s => s.AddToSetAsync(
            It.Is<string>(k => k.Contains("entity-sessions:")),
            sessionId1,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockCacheStore.Verify(s => s.AddToSetAsync(
            It.Is<string>(k => k.Contains("entity-sessions:")),
            sessionId2,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WhenStoreThrows_SwallowsExceptionAndPublishesError()
    {
        // Arrange
        _mockCacheStore
            .Setup(s => s.AddToSetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Store error"));

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() =>
            _registry.RegisterAsync("character", Guid.NewGuid(), Guid.NewGuid().ToString()));
        Assert.Null(exception);

        // Verify error event was published
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "connect",
            "EntitySessionRegistry.Register",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region UnregisterAsync Tests

    [Fact]
    public async Task UnregisterAsync_WhenCalled_RemovesFromForwardAndReverseIndexes()
    {
        // Arrange
        var entityType = "character";
        var entityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString();

        // Act
        await _registry.UnregisterAsync(entityType, entityId, sessionId);

        // Assert - forward index removal
        _mockCacheStore.Verify(s => s.RemoveFromSetAsync(
            It.Is<string>(k => k.Contains("entity-sessions:") && k.Contains(entityType)),
            sessionId,
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - reverse index removal
        _mockCacheStore.Verify(s => s.RemoveFromSetAsync(
            It.Is<string>(k => k.Contains("session-entities:") && k.Contains(sessionId)),
            It.Is<string>(v => v.Contains(entityType) && v.Contains(entityId.ToString("N"))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnregisterAsync_WhenLastSessionRemoved_DeletesForwardKey()
    {
        // Arrange
        var entityType = "character";
        var entityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString();

        _mockCacheStore
            .Setup(s => s.SetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        // Act
        await _registry.UnregisterAsync(entityType, entityId, sessionId);

        // Assert - empty forward set should be deleted
        _mockCacheStore.Verify(s => s.DeleteSetAsync(
            It.Is<string>(k => k.Contains("entity-sessions:")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnregisterAsync_WhenSessionsRemain_DoesNotDeleteForwardKey()
    {
        // Arrange
        var entityType = "character";
        var entityId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString();

        _mockCacheStore
            .Setup(s => s.SetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2L);

        // Act
        await _registry.UnregisterAsync(entityType, entityId, sessionId);

        // Assert - forward set should NOT be deleted
        _mockCacheStore.Verify(s => s.DeleteSetAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnregisterAsync_WhenStoreThrows_SwallowsException()
    {
        // Arrange
        _mockCacheStore
            .Setup(s => s.RemoveFromSetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Store error"));

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() =>
            _registry.UnregisterAsync("character", Guid.NewGuid(), Guid.NewGuid().ToString()));
        Assert.Null(exception);
    }

    #endregion

    #region GetSessionsForEntityAsync Tests

    [Fact]
    public async Task GetSessionsForEntityAsync_WithNoBindings_ReturnsEmptySet()
    {
        // Arrange
        _mockCacheStore
            .Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var result = await _registry.GetSessionsForEntityAsync("character", Guid.NewGuid());

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSessionsForEntityAsync_WithLiveSessions_ReturnsAllLive()
    {
        // Arrange
        var entityType = "character";
        var entityId = Guid.NewGuid();
        var sessionId1 = Guid.NewGuid().ToString();
        var sessionId2 = Guid.NewGuid().ToString();

        _mockCacheStore
            .Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { sessionId1, sessionId2 });

        // Both sessions have heartbeats (alive)
        _mockHeartbeatStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionHeartbeat { SessionId = Guid.NewGuid() });

        // Act
        var result = await _registry.GetSessionsForEntityAsync(entityType, entityId);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(sessionId1, result);
        Assert.Contains(sessionId2, result);
    }

    [Fact]
    public async Task GetSessionsForEntityAsync_WithMixedLiveAndStale_FiltersStaleViaHeartbeat()
    {
        // Arrange
        var entityType = "character";
        var entityId = Guid.NewGuid();
        var liveSessionId = Guid.NewGuid().ToString();
        var staleSessionId = Guid.NewGuid().ToString();

        _mockCacheStore
            .Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { liveSessionId, staleSessionId });

        // Live session has heartbeat, stale does not
        _mockHeartbeatStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(liveSessionId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionHeartbeat { SessionId = Guid.Parse(liveSessionId) });
        _mockHeartbeatStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(staleSessionId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionHeartbeat?)null);

        // Act
        var result = await _registry.GetSessionsForEntityAsync(entityType, entityId);

        // Assert - only live session returned
        Assert.Single(result);
        Assert.Contains(liveSessionId, result);
        Assert.DoesNotContain(staleSessionId, result);

        // Assert - stale session removed from forward index
        _mockCacheStore.Verify(s => s.RemoveFromSetAsync(
            It.IsAny<string>(),
            staleSessionId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSessionsForEntityAsync_WhenStoreThrows_ReturnsEmptySet()
    {
        // Arrange
        _mockCacheStore
            .Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Store error"));

        // Act
        var result = await _registry.GetSessionsForEntityAsync("character", Guid.NewGuid());

        // Assert - graceful degradation
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region UnregisterSessionAsync Tests

    [Fact]
    public async Task UnregisterSessionAsync_WithNoBindings_ReturnsEarly()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString();

        _mockCacheStore
            .Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        await _registry.UnregisterSessionAsync(sessionId);

        // Assert - no forward index removals attempted
        _mockCacheStore.Verify(s => s.RemoveFromSetAsync(
            It.Is<string>(k => k.Contains("entity-sessions:")),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnregisterSessionAsync_WithBindings_CleansUpAllEntityBindings()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString();
        var entityId1 = Guid.NewGuid();
        var entityId2 = Guid.NewGuid();
        var binding1 = $"character:{entityId1:N}";
        var binding2 = $"inventory:{entityId2:N}";

        _mockCacheStore
            .Setup(s => s.GetSetAsync<string>(
                It.Is<string>(k => k.Contains("session-entities:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { binding1, binding2 });

        // Simulate non-empty forward sets so they're not deleted
        _mockCacheStore
            .Setup(s => s.SetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        // Act
        await _registry.UnregisterSessionAsync(sessionId);

        // Assert - session removed from each entity's forward index
        _mockCacheStore.Verify(s => s.RemoveFromSetAsync(
            It.Is<string>(k => k.Contains("entity-sessions:character:")),
            sessionId,
            It.IsAny<CancellationToken>()), Times.Once);
        _mockCacheStore.Verify(s => s.RemoveFromSetAsync(
            It.Is<string>(k => k.Contains("entity-sessions:inventory:")),
            sessionId,
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - reverse index key deleted
        _mockCacheStore.Verify(s => s.DeleteSetAsync(
            It.Is<string>(k => k.Contains("session-entities:") && k.Contains(sessionId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnregisterSessionAsync_WhenForwardSetEmpty_DeletesForwardSet()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString();
        var entityId = Guid.NewGuid();
        var binding = $"character:{entityId:N}";

        _mockCacheStore
            .Setup(s => s.GetSetAsync<string>(
                It.Is<string>(k => k.Contains("session-entities:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { binding });

        // Forward set is now empty after removal
        _mockCacheStore
            .Setup(s => s.SetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        // Act
        await _registry.UnregisterSessionAsync(sessionId);

        // Assert - empty forward set deleted
        _mockCacheStore.Verify(s => s.DeleteSetAsync(
            It.Is<string>(k => k.Contains("entity-sessions:")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnregisterSessionAsync_WhenIndividualBindingFails_ContinuesWithRemaining()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString();
        var entityId1 = Guid.NewGuid();
        var entityId2 = Guid.NewGuid();
        var binding1 = $"character:{entityId1:N}";
        var binding2 = $"inventory:{entityId2:N}";

        _mockCacheStore
            .Setup(s => s.GetSetAsync<string>(
                It.Is<string>(k => k.Contains("session-entities:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { binding1, binding2 });

        // First binding's forward removal fails
        var callCount = 0;
        _mockCacheStore
            .Setup(s => s.RemoveFromSetAsync(
                It.Is<string>(k => k.Contains("entity-sessions:")),
                sessionId,
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                    return Task.FromException<bool>(new InvalidOperationException("First binding failed"));
                return Task.FromResult(true);
            });

        _mockCacheStore
            .Setup(s => s.SetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        // Act - should not throw
        var exception = await Record.ExceptionAsync(() => _registry.UnregisterSessionAsync(sessionId));
        Assert.Null(exception);

        // Assert - both bindings attempted (best-effort), reverse key still deleted
        _mockCacheStore.Verify(s => s.RemoveFromSetAsync(
            It.Is<string>(k => k.Contains("entity-sessions:")),
            sessionId,
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        _mockCacheStore.Verify(s => s.DeleteSetAsync(
            It.Is<string>(k => k.Contains("session-entities:")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnregisterSessionAsync_WhenStoreThrows_SwallowsException()
    {
        // Arrange
        var sessionId = Guid.NewGuid().ToString();

        _mockCacheStore
            .Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Store error"));

        // Act & Assert - should not throw
        var exception = await Record.ExceptionAsync(() => _registry.UnregisterSessionAsync(sessionId));
        Assert.Null(exception);
    }

    #endregion
}
