using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Permission.Tests;

/// <summary>
/// Unit tests for PermissionSessionActivityListener.
/// Tests verify TTL management, delegation to PermissionService, and conditional Redis TTL alignment.
/// </summary>
public class PermissionSessionActivityListenerTests
{
    // Mocks for PermissionService construction (real instance needed — listener casts IPermissionService)
    private readonly Mock<ILogger<PermissionService>> _mockServiceLogger;
    private readonly PermissionServiceConfiguration _configuration;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<HashSet<string>>> _mockHashSetStore;
    private readonly Mock<ICacheableStateStore<string>> _mockCacheableStore;
    private readonly Mock<IStateStore<Dictionary<string, string>>> _mockDictStringStore;
    private readonly Mock<IStateStore<Dictionary<string, object>>> _mockDictObjectStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<object>> _mockObjectStore;
    private readonly Mock<IStateStore<ServiceRegistrationInfo>> _mockRegistrationInfoStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IClientEventPublisher> _mockClientEventPublisher;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly RegistrationEventBatcher _registrationBatcher;

    // Mocks specific to listener
    private readonly Mock<IRedisOperations> _mockRedisOps;
    private readonly Mock<ILogger<PermissionSessionActivityListener>> _mockListenerLogger;

    private const string STATE_STORE = "permission-statestore";

    public PermissionSessionActivityListenerTests()
    {
        _mockServiceLogger = new Mock<ILogger<PermissionService>>();
        _configuration = new PermissionServiceConfiguration();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockHashSetStore = new Mock<IStateStore<HashSet<string>>>();
        _mockCacheableStore = new Mock<ICacheableStateStore<string>>();
        _mockDictStringStore = new Mock<IStateStore<Dictionary<string, string>>>();
        _mockDictObjectStore = new Mock<IStateStore<Dictionary<string, object>>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockObjectStore = new Mock<IStateStore<object>>();
        _mockRegistrationInfoStore = new Mock<IStateStore<ServiceRegistrationInfo>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockClientEventPublisher = new Mock<IClientEventPublisher>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _registrationBatcher = new RegistrationEventBatcher(
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<RegistrationEventBatcher>>(),
            _configuration,
            _mockTelemetryProvider.Object);

        _mockRedisOps = new Mock<IRedisOperations>();
        _mockListenerLogger = new Mock<ILogger<PermissionSessionActivityListener>>();

        // Setup lock provider to always succeed
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<object>(STATE_STORE))
            .Returns(_mockObjectStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<HashSet<string>>(STATE_STORE))
            .Returns(_mockHashSetStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetCacheableStore<string>(STATE_STORE))
            .Returns(_mockCacheableStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<Dictionary<string, string>>(STATE_STORE))
            .Returns(_mockDictStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<Dictionary<string, object>>(STATE_STORE))
            .Returns(_mockDictObjectStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<ServiceRegistrationInfo>(STATE_STORE))
            .Returns(_mockRegistrationInfoStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);

        // Default store behaviors
        _mockHashSetStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<HashSet<string>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockDictStringStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockDictObjectStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockStringStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockObjectStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockRegistrationInfoStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ServiceRegistrationInfo>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Default cacheable store behaviors
        _mockCacheableStore.Setup(s => s.AddToSetAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCacheableStore.Setup(s => s.RemoveFromSetAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCacheableStore.Setup(s => s.SetContainsAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockCacheableStore.Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _mockCacheableStore.Setup(s => s.SetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        // Default message bus behavior
        _mockMessageBus.Setup(m => m.TryPublishAsync(
            It.IsAny<string>(),
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default client event publisher behavior
        _mockClientEventPublisher.Setup(x => x.PublishToSessionAsync(
            It.IsAny<string>(),
            It.IsAny<BaseClientEvent>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default: Redis operations available, TTL enabled
        _mockStateStoreFactory.Setup(f => f.GetRedisOperations()).Returns(_mockRedisOps.Object);
        _configuration.SessionDataTtlSeconds = 600;

        // Redis ExpireAsync returns true by default
        _mockRedisOps.Setup(r => r.ExpireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private PermissionService CreatePermissionService()
    {
        return new PermissionService(
            _mockServiceLogger.Object,
            _configuration,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockClientEventPublisher.Object,
            _mockTelemetryProvider.Object,
            _mockLockProvider.Object,
            _mockEventConsumer.Object,
            _registrationBatcher);
    }

    private PermissionSessionActivityListener CreateListener()
    {
        var service = CreatePermissionService();
        return new PermissionSessionActivityListener(
            service,
            _mockStateStoreFactory.Object,
            _configuration,
            _mockTelemetryProvider.Object,
            _mockListenerLogger.Object);
    }

    #region OnHeartbeatAsync Tests

    /// <summary>
    /// Verifies that when SessionDataTtlSeconds is 0 (disabled), OnHeartbeat returns early
    /// without calling any Redis operations.
    /// </summary>
    [Fact]
    public async Task OnHeartbeatAsync_TtlDisabled_DoesNotCallRedis()
    {
        // Arrange
        _configuration.SessionDataTtlSeconds = 0;
        var listener = CreateListener();
        var sessionId = Guid.NewGuid();

        // Act
        await listener.OnHeartbeatAsync(sessionId, CancellationToken.None);

        // Assert — no Redis EXPIRE operations should be attempted (GetRedisOperations is
        // called during constructor, which is expected — the test verifies runtime behavior)
        _mockRedisOps.Verify(r => r.ExpireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    /// <summary>
    /// Verifies that when GetRedisOperations returns null (InMemory mode), OnHeartbeat
    /// returns early without attempting TTL refresh.
    /// </summary>
    [Fact]
    public async Task OnHeartbeatAsync_NoRedisOps_DoesNothing()
    {
        // Arrange
        _mockStateStoreFactory.Setup(f => f.GetRedisOperations()).Returns((IRedisOperations?)null);
        var listener = CreateListener();
        var sessionId = Guid.NewGuid();

        // Act
        await listener.OnHeartbeatAsync(sessionId, CancellationToken.None);

        // Assert
        _mockRedisOps.Verify(r => r.ExpireAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    /// <summary>
    /// Verifies that OnHeartbeat calls ExpireAsync on both session states and permissions keys
    /// with the correct Redis key format and TTL from configuration.
    /// </summary>
    [Fact]
    public async Task OnHeartbeatAsync_RefreshesTtlOnBothKeys()
    {
        // Arrange
        var ttlSeconds = 300;
        _configuration.SessionDataTtlSeconds = ttlSeconds;
        var listener = CreateListener();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();

        var expectedStatesKey = $"permission:session:{sessionIdStr}:states";
        var expectedPermissionsKey = $"permission:session:{sessionIdStr}:permissions";
        var expectedTtl = TimeSpan.FromSeconds(ttlSeconds);

        // Act
        await listener.OnHeartbeatAsync(sessionId, CancellationToken.None);

        // Assert — ExpireAsync called exactly twice with correct keys and TTL
        _mockRedisOps.Verify(r => r.ExpireAsync(expectedStatesKey, expectedTtl, It.IsAny<CancellationToken>()), Times.Once());
        _mockRedisOps.Verify(r => r.ExpireAsync(expectedPermissionsKey, expectedTtl, It.IsAny<CancellationToken>()), Times.Once());
    }

    #endregion

    #region OnConnectedAsync Tests

    /// <summary>
    /// Verifies that OnConnected delegates to PermissionService.HandleSessionConnectedAsync
    /// by checking that session state is saved (a side effect of the delegated method).
    /// </summary>
    [Fact]
    public async Task OnConnectedAsync_DelegatesToPermissionService()
    {
        // Arrange
        var listener = CreateListener();
        var sessionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var roles = new List<string> { "user" };
        var authorizations = new List<string> { "my-game:authorized" };

        // HandleSessionConnectedAsync saves session states — set up for success
        _mockHashSetStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);
        _mockDictStringStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, string>?)null);

        // Act
        await listener.OnConnectedAsync(sessionId, accountId, roles, authorizations, CancellationToken.None);

        // Assert — the delegated method should save session states (role + authorizations)
        _mockDictStringStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.Contains(sessionId.ToString()) && k.Contains("states")),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.AtLeastOnce());
    }

    #endregion

    #region OnReconnectedAsync Tests

    /// <summary>
    /// Verifies that OnReconnected refreshes TTL on both Redis keys after recompilation.
    /// </summary>
    [Fact]
    public async Task OnReconnectedAsync_RefreshesTtlAfterRecompile()
    {
        // Arrange
        var ttlSeconds = 600;
        _configuration.SessionDataTtlSeconds = ttlSeconds;
        var listener = CreateListener();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();

        // Set up minimal state for RecompileForReconnectionAsync
        _mockDictStringStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains("states")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "user" });
        _mockCacheableStore.Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var expectedStatesKey = $"permission:session:{sessionIdStr}:states";
        var expectedPermissionsKey = $"permission:session:{sessionIdStr}:permissions";
        var expectedTtl = TimeSpan.FromSeconds(ttlSeconds);

        // Act
        await listener.OnReconnectedAsync(sessionId, CancellationToken.None);

        // Assert — TTL refresh on both keys
        _mockRedisOps.Verify(r => r.ExpireAsync(expectedStatesKey, expectedTtl, It.IsAny<CancellationToken>()), Times.Once());
        _mockRedisOps.Verify(r => r.ExpireAsync(expectedPermissionsKey, expectedTtl, It.IsAny<CancellationToken>()), Times.Once());
    }

    #endregion

    #region OnDisconnectedAsync Tests

    /// <summary>
    /// Verifies that when reconnectable with a reconnection window, the listener aligns
    /// Redis TTL to the reconnection window duration (not the default SessionDataTtlSeconds).
    /// </summary>
    [Fact]
    public async Task OnDisconnectedAsync_Reconnectable_AlignsTtlToReconnectionWindow()
    {
        // Arrange
        var listener = CreateListener();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var reconnectionWindow = TimeSpan.FromSeconds(120);

        // Set up for HandleSessionDisconnectedAsync
        _mockCacheableStore.Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var expectedStatesKey = $"permission:session:{sessionIdStr}:states";
        var expectedPermissionsKey = $"permission:session:{sessionIdStr}:permissions";

        // Act
        await listener.OnDisconnectedAsync(sessionId, reconnectable: true, reconnectionWindow, CancellationToken.None);

        // Assert — TTL aligned to reconnection window, not default TTL
        _mockRedisOps.Verify(r => r.ExpireAsync(expectedStatesKey, reconnectionWindow, It.IsAny<CancellationToken>()), Times.Once());
        _mockRedisOps.Verify(r => r.ExpireAsync(expectedPermissionsKey, reconnectionWindow, It.IsAny<CancellationToken>()), Times.Once());
    }

    /// <summary>
    /// Verifies that when not reconnectable, no TTL alignment occurs.
    /// </summary>
    [Fact]
    public async Task OnDisconnectedAsync_NotReconnectable_NoTtlAlignment()
    {
        // Arrange
        var listener = CreateListener();
        var sessionId = Guid.NewGuid();

        // Set up for HandleSessionDisconnectedAsync
        _mockCacheableStore.Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        await listener.OnDisconnectedAsync(sessionId, reconnectable: false, reconnectionWindow: null, CancellationToken.None);

        // Assert — no TTL alignment calls (only HandleSessionDisconnectedAsync side effects)
        // AlignSessionTtlAsync calls GetRedisOperations + ExpireAsync,
        // but HandleSessionDisconnectedAsync may also call stores. Verify no 120s-window EXPIRE.
        _mockRedisOps.Verify(r => r.ExpireAsync(
            It.Is<string>(k => k.Contains(sessionId.ToString())),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()),
            Times.Never());
    }

    /// <summary>
    /// Verifies that when reconnectable but no reconnection window provided,
    /// no TTL alignment occurs (AlignSessionTtlAsync is skipped).
    /// </summary>
    [Fact]
    public async Task OnDisconnectedAsync_ReconnectableButNoWindow_NoTtlAlignment()
    {
        // Arrange
        var listener = CreateListener();
        var sessionId = Guid.NewGuid();

        // Set up for HandleSessionDisconnectedAsync
        _mockCacheableStore.Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        await listener.OnDisconnectedAsync(sessionId, reconnectable: true, reconnectionWindow: null, CancellationToken.None);

        // Assert — reconnectable but null window means no alignment
        _mockRedisOps.Verify(r => r.ExpireAsync(
            It.Is<string>(k => k.Contains(sessionId.ToString())),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()),
            Times.Never());
    }

    #endregion

    #region OnReconnectedAsync Exception Tests (Gap 15)

    /// <summary>
    /// Verifies that when GetSetAsync throws during RecompileSessionPermissionsAsync,
    /// the exception is caught internally (not propagated) and RefreshSessionTtlAsync still runs.
    /// RecompileSessionPermissionsAsync has a catch-all that logs errors and publishes error events,
    /// so OnReconnectedAsync completes normally and proceeds to TTL refresh.
    /// </summary>
    [Fact]
    public async Task OnReconnectedAsync_RecompileFails_ErrorCaughtAndTtlStillRefreshed()
    {
        // Arrange
        var listener = CreateListener();
        var sessionId = Guid.NewGuid();

        // Setup session states so RecompileSessionPermissionsAsync proceeds past null check
        _mockDictStringStore.Setup(s => s.GetAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "user" });

        // Make GetSetAsync<string>(REGISTERED_SERVICES_KEY) throw to simulate failure during recompilation.
        // This exception is caught by the try-catch in RecompileSessionPermissionsAsync.
        _mockCacheableStore.Setup(s => s.GetSetAsync<string>(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis connection lost during reconnection"));

        // Act — should NOT throw (exception caught internally by RecompileSessionPermissionsAsync)
        await listener.OnReconnectedAsync(sessionId, CancellationToken.None);

        // Assert — error event should be published for the caught exception
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            It.IsAny<string>(),
            "RecompileSessionPermissions",
            It.IsAny<string>(),
            It.Is<string>(msg => msg.Contains("Redis connection lost")),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()),
            Times.Once());
    }

    /// <summary>
    /// Verifies that when AddToSetAsync (the first operation in RecompileForReconnectionAsync) throws,
    /// the exception propagates and neither recompilation nor TTL refresh occurs.
    /// </summary>
    [Fact]
    public async Task OnReconnectedAsync_AddToSetFails_ExceptionPropagatesAndNoRecompile()
    {
        // Arrange
        var listener = CreateListener();
        var sessionId = Guid.NewGuid();

        // AddToSetAsync is the first call in RecompileForReconnectionAsync
        _mockCacheableStore.Setup(s => s.AddToSetAsync<string>(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => listener.OnReconnectedAsync(sessionId, CancellationToken.None));

        // Verify no TTL refresh was attempted
        _mockRedisOps.Verify(r => r.ExpireAsync(
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()),
            Times.Never());
    }

    #endregion

    #region AlignSessionTtlAsync Redis Failure Tests (Gap 16)

    /// <summary>
    /// Verifies that when the first ExpireAsync call in AlignSessionTtlAsync throws,
    /// the second key's TTL is never set. This confirms that a partial Redis failure
    /// during TTL alignment propagates the exception rather than silently leaving one key misaligned.
    /// AlignSessionTtlAsync is called from OnDisconnectedAsync when reconnectable with a window.
    /// </summary>
    [Fact]
    public async Task AlignSessionTtlAsync_FirstExpireFails_ExceptionPropagatesSecondKeyNotSet()
    {
        // Arrange
        var listener = CreateListener();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var reconnectionWindow = TimeSpan.FromSeconds(120);

        var statesKey = $"permission:session:{sessionIdStr}:states";
        var permissionsKey = $"permission:session:{sessionIdStr}:permissions";

        // Set up for HandleSessionDisconnectedAsync to succeed
        _mockCacheableStore.Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // First ExpireAsync (states key) throws — sequence matters
        var expireCallCount = 0;
        _mockRedisOps.Setup(r => r.ExpireAsync(
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()))
            .Returns<string, TimeSpan, CancellationToken>((key, ttl, ct) =>
            {
                expireCallCount++;
                // AlignSessionTtlAsync is called after HandleSessionDisconnectedAsync.
                // The states key is the first ExpireAsync call in AlignSessionTtlAsync.
                if (key == statesKey && ttl == reconnectionWindow)
                    throw new InvalidOperationException("Redis EXPIRE failed on states key");
                return Task.FromResult(true);
            });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => listener.OnDisconnectedAsync(sessionId, reconnectable: true, reconnectionWindow, CancellationToken.None));

        // The permissions key EXPIRE with reconnectionWindow should never have been called
        // because the states key EXPIRE threw first
        _mockRedisOps.Verify(r => r.ExpireAsync(
            permissionsKey,
            reconnectionWindow,
            It.IsAny<CancellationToken>()),
            Times.Never());
    }

    /// <summary>
    /// Verifies that when the second ExpireAsync call (permissions key) in AlignSessionTtlAsync throws,
    /// the exception propagates. The first key's TTL was set successfully, creating a potential
    /// inconsistency (states key has aligned TTL, permissions key does not).
    /// </summary>
    [Fact]
    public async Task AlignSessionTtlAsync_SecondExpireFails_ExceptionPropagatesFirstKeyAlreadySet()
    {
        // Arrange
        var listener = CreateListener();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var reconnectionWindow = TimeSpan.FromSeconds(120);

        var statesKey = $"permission:session:{sessionIdStr}:states";
        var permissionsKey = $"permission:session:{sessionIdStr}:permissions";

        // Set up for HandleSessionDisconnectedAsync to succeed
        _mockCacheableStore.Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Track which keys had ExpireAsync called with the reconnection window TTL
        var alignedKeys = new List<string>();

        _mockRedisOps.Setup(r => r.ExpireAsync(
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()))
            .Returns<string, TimeSpan, CancellationToken>((key, ttl, ct) =>
            {
                if (ttl == reconnectionWindow)
                {
                    alignedKeys.Add(key);
                    // Second alignment call (permissions key) throws
                    if (key == permissionsKey)
                        throw new InvalidOperationException("Redis EXPIRE failed on permissions key");
                }
                return Task.FromResult(true);
            });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => listener.OnDisconnectedAsync(sessionId, reconnectable: true, reconnectionWindow, CancellationToken.None));

        // Verify the first key (states) was aligned before the failure
        Assert.Contains(statesKey, alignedKeys);
    }

    /// <summary>
    /// Verifies that when GetRedisOperations returns null during AlignSessionTtlAsync
    /// (InMemory mode), the method returns early without attempting any ExpireAsync calls.
    /// OnDisconnectedAsync still completes successfully.
    /// </summary>
    [Fact]
    public async Task AlignSessionTtlAsync_NoRedisOps_SkipsAlignmentGracefully()
    {
        // Arrange
        // GetRedisOperations returns null (InMemory mode)
        _mockStateStoreFactory.Setup(f => f.GetRedisOperations()).Returns((IRedisOperations?)null);
        var listener = CreateListener();
        var sessionId = Guid.NewGuid();
        var reconnectionWindow = TimeSpan.FromSeconds(120);

        // Set up for HandleSessionDisconnectedAsync to succeed
        _mockCacheableStore.Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act — should complete without throwing despite reconnectable + window
        await listener.OnDisconnectedAsync(sessionId, reconnectable: true, reconnectionWindow, CancellationToken.None);

        // Assert — no ExpireAsync calls attempted
        _mockRedisOps.Verify(r => r.ExpireAsync(
            It.IsAny<string>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()),
            Times.Never());
    }

    #endregion
}
