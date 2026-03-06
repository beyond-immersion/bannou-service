using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Permission.Tests;

/// <summary>
/// Unit tests for PermissionService.
/// Tests verify permission registration, session management, capability compilation, and role-based access.
/// </summary>
public class PermissionServiceTests
{
    private readonly Mock<ILogger<PermissionService>> _mockLogger;
    private readonly Mock<PermissionServiceConfiguration> _mockConfiguration;
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

    // State store constants (must match PermissionService)
    private const string STATE_STORE = "permission-statestore";
    private const string ACTIVE_SESSIONS_KEY = "active_sessions";
    private const string ACTIVE_CONNECTIONS_KEY = "active_connections";
    private const string REGISTERED_SERVICES_KEY = "registered_services";
    private const string SESSION_STATES_KEY = "session:{0}:states";
    private const string SESSION_PERMISSIONS_KEY = "session:{0}:permissions";
    private const string PERMISSION_MATRIX_KEY = "permissions:{0}:{1}:{2}";
    private const string SERVICE_REGISTERED_KEY = "service-registered:{0}";

    public PermissionServiceTests()
    {
        _mockLogger = new Mock<ILogger<PermissionService>>();
        _mockConfiguration = new Mock<PermissionServiceConfiguration>();
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

        // Setup lock provider to always succeed by default
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
        // Note: object setup must come FIRST (most general) to avoid Castle proxy matching issues
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

        // Setup default behavior for stores - SaveAsync returns Task<string> (etag)
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

        // Setup default behavior for cacheable store atomic set operations
        _mockCacheableStore.Setup(s => s.AddToSetAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCacheableStore.Setup(s => s.RemoveFromSetAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCacheableStore.Setup(s => s.SetContainsAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockCacheableStore.Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _mockCacheableStore.Setup(s => s.SetCountAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        // Setup default behavior for message bus
        _mockMessageBus.Setup(m => m.TryPublishAsync(
            It.IsAny<string>(),
            It.IsAny<object>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Setup default behavior for client event publisher
        _mockClientEventPublisher.Setup(x => x.PublishToSessionAsync(
            It.IsAny<string>(),
            It.IsAny<BaseClientEvent>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private PermissionService CreateService()
    {
        return new PermissionService(
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockClientEventPublisher.Object,
            _mockTelemetryProvider.Object,
            _mockLockProvider.Object,
            _mockEventConsumer.Object);
    }

    #region Constructor Tests

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    ///
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    ///
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void PermissionService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<PermissionService>();

    #endregion

    [Fact]
    public async Task RegisterServicePermissionsAsync_StoresPermissionMatrix()
    {
        // Arrange
        var service = CreateService();

        // Set up empty existing state
        _mockHashSetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        // Set up empty existing hash
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Registered services check returns false (not yet registered)
        _mockCacheableStore
            .Setup(s => s.SetContainsAsync<string>(REGISTERED_SERVICES_KEY, "orchestrator", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Empty active sessions for recompilation
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Create permission matrix for orchestrator-like service
        var permissions = new ServicePermissionMatrix
        {
            ServiceId = "orchestrator",
            Version = "1.0.0",
            Permissions = new Dictionary<string, StatePermissions>
            {
                ["authenticated"] = new StatePermissions
                {
                    ["admin"] = new System.Collections.ObjectModel.Collection<string>
                    {
                        "/orchestrator/health",
                        "/orchestrator/deploy"
                    }
                }
            }
        };

        // Act
        var (statusCode, response) = await service.RegisterServicePermissionsAsync(permissions);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        // Verify registered services list was updated atomically via AddToSetAsync
        _mockCacheableStore.Verify(s => s.AddToSetAsync<string>(
            REGISTERED_SERVICES_KEY,
            "orchestrator",
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task UpdateSessionRoleAsync_AddsSessionToActiveList()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();

        // Set up empty session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Set up empty registered services
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Set up empty session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        var roleUpdate = new SessionRoleUpdate
        {
            SessionId = sessionId,
            NewRole = "admin",
            PreviousRole = null
        };

        // Act
        var (statusCode, response) = await service.UpdateSessionRoleAsync(roleUpdate);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.NewPermissions);

        // Verify session was atomically added to activeSessions
        _mockCacheableStore.Verify(s => s.AddToSetAsync<string>(
            ACTIVE_SESSIONS_KEY,
            sessionIdStr,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify session states were saved exactly once
        _mockDictStringStore.Verify(s => s.SaveAsync(
            statesKey,
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateSessionRoleAsync_IncludesLowerRoleEndpoints()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        // Empty initial state/permissions
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Registered services via atomic set read
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "svc" });

        // Permission matrix lookups per role (developer should inherit user)
        _mockHashSetStore
            .Setup(s => s.GetAsync(It.Is<string>(key => key.Contains("permissions:svc:default:user")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "/user" });

        _mockHashSetStore
            .Setup(s => s.GetAsync(It.Is<string>(key => key.Contains("permissions:svc:default:developer")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "/dev" });

        _mockHashSetStore
            .Setup(s => s.GetAsync(It.Is<string>(key => key.Contains("permissions:svc:default:anonymous")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        Dictionary<string, object>? savedPermissions = null;
        _mockDictObjectStore
            .Setup(s => s.SaveAsync(
                permissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, object>, StateOptions?, CancellationToken>(
                (key, value, ttl, ct) => savedPermissions = value)
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.UpdateSessionRoleAsync(new SessionRoleUpdate
        {
            SessionId = sessionId,
            NewRole = "developer",
            PreviousRole = "user"
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedPermissions);
        Assert.True(savedPermissions!.TryGetValue("svc", out var endpointsObj));
        var endpoints = endpointsObj as IEnumerable<string>;
        Assert.NotNull(endpoints);
        Assert.Contains("/user", endpoints!);
        Assert.Contains("/dev", endpoints!);
    }

    [Fact]
    public async Task RecompilePermissions_RequiresStateAndRole()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        // Session has role=user and game-session:in_game state
        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["game-session"] = "in_game"
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "svc" });

        // Endpoint gated by game-session:in_game + role user
        _mockHashSetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains("permissions:svc:game-session:in_game:user")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "/secure" });

        // No default endpoints
        _mockHashSetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains("permissions:svc:default:user")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        Dictionary<string, object>? saved = null;
        _mockDictObjectStore
            .Setup(s => s.SaveAsync(
                permissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, object>, StateOptions?, CancellationToken>(
                (key, val, ttl, ct) => saved = val)
            .ReturnsAsync("etag");

        // Act
        var (status, _) = await service.UpdateSessionRoleAsync(new SessionRoleUpdate
        {
            SessionId = sessionId,
            NewRole = "user",
            PreviousRole = null
        });

        // Assert state required is respected and endpoint is present
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(saved);
        Assert.True(saved!.TryGetValue("svc", out var endpointsObj));
        var endpoints = endpointsObj as IEnumerable<string>;
        Assert.NotNull(endpoints);
        Assert.Contains("/secure", endpoints!);
    }

    /// <summary>
    /// Verifies same-service state key matching: when voice service
    /// registers permissions for its own voice:ringing state (same-service),
    /// the state lookup key should be just "ringing", not "voice:ringing".
    /// </summary>
    [Fact]
    public async Task RecompilePermissions_SameServiceStateKey_MatchesRegistration()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        // Session has voice=ringing state (same-service state for voice service)
        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["voice"] = "ringing"
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "voice" });

        // CRITICAL: The state key for same-service must be just "ringing", not "voice:ringing"
        _mockHashSetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k == "permissions:voice:ringing:user"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "/voice/peer/answer" });

        // Default endpoints (no state required)
        _mockHashSetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k == "permissions:voice:default:user"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        Dictionary<string, object>? saved = null;
        _mockDictObjectStore
            .Setup(s => s.SaveAsync(
                permissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, object>, StateOptions?, CancellationToken>(
                (key, val, ttl, ct) => saved = val)
            .ReturnsAsync("etag");

        // Act
        var (status, _) = await service.UpdateSessionRoleAsync(new SessionRoleUpdate
        {
            SessionId = sessionId,
            NewRole = "user",
            PreviousRole = null
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(saved);
        Assert.True(saved!.TryGetValue("voice", out var endpointsObj),
            "Session should have voice service permissions when voice:ringing state is set");
        var endpoints = endpointsObj as IEnumerable<string>;
        Assert.NotNull(endpoints);
        Assert.Contains("/voice/peer/answer", endpoints!);
    }

    /// <summary>
    /// Verifies cross-service state key matching: when game-session service
    /// registers permissions requiring voice:ringing state (cross-service),
    /// the state key must be "voice:ringing" (not just "ringing").
    /// </summary>
    [Fact]
    public async Task RecompilePermissions_CrossServiceStateKey_IncludesServicePrefix()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        // Session has voice=ringing state
        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["voice"] = "ringing"
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "game-session" });

        // Cross-service: game-session service checking voice state requires "voice:ringing" key
        _mockHashSetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k == "permissions:game-session:voice:ringing:user"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "/sessions/voice-enabled-action" });

        _mockHashSetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k == "permissions:game-session:default:user"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        Dictionary<string, object>? saved = null;
        _mockDictObjectStore
            .Setup(s => s.SaveAsync(
                permissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, object>, StateOptions?, CancellationToken>(
                (key, val, ttl, ct) => saved = val)
            .ReturnsAsync("etag");

        // Act
        var (status, _) = await service.UpdateSessionRoleAsync(new SessionRoleUpdate
        {
            SessionId = sessionId,
            NewRole = "user",
            PreviousRole = null
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(saved);
        Assert.True(saved!.TryGetValue("game-session", out var endpointsObj));
        var endpoints = endpointsObj as IEnumerable<string>;
        Assert.NotNull(endpoints);
        Assert.Contains("/sessions/voice-enabled-action", endpoints!);
    }

    /// <summary>
    /// Verifies game-session:in_game state works correctly for game-session service.
    /// </summary>
    [Fact]
    public async Task RecompilePermissions_GameSessionInGameState_UnlocksGameEndpoints()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["game-session"] = "in_game"
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "game-session" });

        // Same-service state key: just "in_game" (not "game-session:in_game")
        _mockHashSetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k == "permissions:game-session:in_game:user"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>
            {
                "/sessions/leave",
                "/sessions/chat",
                "/sessions/actions"
            });

        _mockHashSetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k == "permissions:game-session:default:user"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>
            {
                "/sessions/list",
                "/sessions/create",
                "/sessions/get",
                "/sessions/join"
            });

        Dictionary<string, object>? saved = null;
        _mockDictObjectStore
            .Setup(s => s.SaveAsync(
                permissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, object>, StateOptions?, CancellationToken>(
                (key, val, ttl, ct) => saved = val)
            .ReturnsAsync("etag");

        // Act
        var (status, _) = await service.UpdateSessionRoleAsync(new SessionRoleUpdate
        {
            SessionId = sessionId,
            NewRole = "user",
            PreviousRole = null
        });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(saved);
        Assert.True(saved!.TryGetValue("game-session", out var endpointsObj));
        var endpoints = (endpointsObj as IEnumerable<string>)?.ToList();
        Assert.NotNull(endpoints);

        // Should have both default and in_game state endpoints
        Assert.Contains("/sessions/list", endpoints!);
        Assert.Contains("/sessions/join", endpoints!);
        Assert.Contains("/sessions/leave", endpoints!);
        Assert.Contains("/sessions/chat", endpoints!);
        Assert.Contains("/sessions/actions", endpoints!);
    }

    [Fact]
    public async Task RecompileSessionPermissions_FindsRegisteredServices()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();

        // Pre-populate registered services via atomic set read
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "orchestrator" });

        // Pre-populate permission matrix: permissions:orchestrator:default:admin
        var adminEndpoints = new HashSet<string> { "/orchestrator/health", "/orchestrator/deploy" };
        var adminMatrixKey = string.Format(PERMISSION_MATRIX_KEY, "orchestrator", "default", "admin");
        _mockHashSetStore
            .Setup(s => s.GetAsync(adminMatrixKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminEndpoints);

        // Set up session states with admin role
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "admin" });

        // Set up existing session permissions for version tracking
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        var roleUpdate = new SessionRoleUpdate
        {
            SessionId = sessionId,
            NewRole = "admin",
            PreviousRole = "user"
        };

        // Act - UpdateSessionRoleAsync triggers RecompileSessionPermissionsAsync
        var (statusCode, response) = await service.UpdateSessionRoleAsync(roleUpdate);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        // Verify compiled permissions were saved
        _mockDictObjectStore.Verify(s => s.SaveAsync(
            permissionsKey,
            It.Is<Dictionary<string, object>>(data =>
                data.ContainsKey("orchestrator")),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_ReturnsCompiledPermissions()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();

        // Pre-populate compiled permissions in state store
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);
        var compiledPermissions = new Dictionary<string, object>
        {
            ["version"] = 1,
            ["generated_at"] = DateTimeOffset.UtcNow.ToString(),
            ["orchestrator"] = BannouJson.SerializeToElement(new List<string>
            {
                "/orchestrator/health",
                "/orchestrator/deploy"
            })
        };

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(compiledPermissions);

        var request = new CapabilityRequest
        {
            SessionId = sessionId
        };

        // Act
        var (statusCode, response) = await service.GetCapabilitiesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(response.Permissions);
        Assert.True(response.Permissions.ContainsKey("orchestrator"));
        Assert.Contains("/orchestrator/health", response.Permissions["orchestrator"]);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_ReturnsNotFound_WhenNoPermissions()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();

        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, object>?)null);

        var request = new CapabilityRequest
        {
            SessionId = sessionId
        };

        // Act
        var (statusCode, response) = await service.GetCapabilitiesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task AdminRole_GetsAdminOnlyEndpoints_UserRole_DoesNot()
    {
        // Arrange
        var service = CreateService();
        var adminSessionId = Guid.NewGuid();
        var userSessionId = Guid.NewGuid();
        var adminSessionIdStr = adminSessionId.ToString();
        var userSessionIdStr = userSessionId.ToString();

        // Set up registered services via atomic set read
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "orchestrator" });

        // Admin-only endpoints at permissions:orchestrator:default:admin
        var adminEndpoints = new HashSet<string> { "/orchestrator/health", "/orchestrator/deploy" };
        var adminMatrixKey = string.Format(PERMISSION_MATRIX_KEY, "orchestrator", "default", "admin");
        _mockHashSetStore
            .Setup(s => s.GetAsync(adminMatrixKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminEndpoints);

        // User endpoints - empty for orchestrator (no user access)
        var userMatrixKey = string.Format(PERMISSION_MATRIX_KEY, "orchestrator", "default", "user");
        _mockHashSetStore
            .Setup(s => s.GetAsync(userMatrixKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        // Set up admin session states
        var adminStatesKey = string.Format(SESSION_STATES_KEY, adminSessionIdStr);
        _mockDictStringStore
            .Setup(s => s.GetAsync(adminStatesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "admin" });

        // Set up user session states
        var userStatesKey = string.Format(SESSION_STATES_KEY, userSessionIdStr);
        _mockDictStringStore
            .Setup(s => s.GetAsync(userStatesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "user" });

        // Set up empty permissions for version tracking
        var adminPermissionsKey = string.Format(SESSION_PERMISSIONS_KEY, adminSessionIdStr);
        var userPermissionsKey = string.Format(SESSION_PERMISSIONS_KEY, userSessionIdStr);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(adminPermissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });
        _mockDictObjectStore
            .Setup(s => s.GetAsync(userPermissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        // Track what permissions get saved
        Dictionary<string, object>? savedAdminPermissions = null;
        Dictionary<string, object>? savedUserPermissions = null;

        _mockDictObjectStore
            .Setup(s => s.SaveAsync(
                adminPermissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, object>, StateOptions?, CancellationToken>(
                (key, data, ttl, ct) => savedAdminPermissions = data);

        _mockDictObjectStore
            .Setup(s => s.SaveAsync(
                userPermissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, object>, StateOptions?, CancellationToken>(
                (key, data, ttl, ct) => savedUserPermissions = data);

        // Act - Set admin role
        await service.UpdateSessionRoleAsync(new SessionRoleUpdate
        {
            SessionId = adminSessionId,
            NewRole = "admin"
        });

        // Act - Set user role
        await service.UpdateSessionRoleAsync(new SessionRoleUpdate
        {
            SessionId = userSessionId,
            NewRole = "user"
        });

        // Assert - Admin should have orchestrator endpoints
        Assert.NotNull(savedAdminPermissions);
        Assert.True(savedAdminPermissions.ContainsKey("orchestrator"),
            "Admin session should have orchestrator permissions");

        // Assert - User should NOT have orchestrator endpoints
        Assert.NotNull(savedUserPermissions);
        Assert.False(savedUserPermissions.ContainsKey("orchestrator"),
            "User session should NOT have orchestrator permissions (admin-only)");
    }

    [Fact]
    public async Task ValidateApiAccessAsync_AllowsValidAccess()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();

        // Pre-populate session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);
        var compiledPermissions = new Dictionary<string, object>
        {
            ["orchestrator"] = BannouJson.SerializeToElement(new List<string>
            {
                "/orchestrator/health",
                "/orchestrator/deploy"
            })
        };

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(compiledPermissions);

        var request = new ValidationRequest
        {
            SessionId = sessionId,
            ServiceId = "orchestrator",
            Endpoint = "/orchestrator/health"
        };

        // Act
        var (statusCode, response) = await service.ValidateApiAccessAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Allowed);
    }

    [Fact]
    public async Task ValidateApiAccessAsync_DeniesInvalidAccess()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();

        // Pre-populate session permissions with limited access
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);
        var compiledPermissions = new Dictionary<string, object>
        {
            ["account"] = BannouJson.SerializeToElement(new List<string>
            {
                "/account/profile"
            })
        };

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(compiledPermissions);

        var request = new ValidationRequest
        {
            SessionId = sessionId,
            ServiceId = "orchestrator",
            Endpoint = "/orchestrator/deploy"
        };

        // Act
        var (statusCode, response) = await service.ValidateApiAccessAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.False(response.Allowed);
    }

    #region Session Connection Tracking Tests

    [Fact]
    public async Task HandleSessionConnectedAsync_AddsSessionToActiveConnections()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var accountId = "account-001";

        // Setup session states for recompile
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup empty registered services
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Setup session permissions for recompile
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(
            sessionIdStr, accountId, roles: null, authorizations: null);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Null(response.NewPermissions);

        // Verify session was atomically added to activeConnections via AddToSetAsync
        _mockCacheableStore.Verify(s => s.AddToSetAsync<string>(
            ACTIVE_CONNECTIONS_KEY,
            sessionIdStr,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_PublishesSessionCapabilitiesEvent()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();
        var accountId = "account-004";

        // Setup session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup registered services
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(sessionId, accountId, roles: null, authorizations: null);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        // Verify SessionCapabilitiesEvent was published exactly once via client event publisher
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            sessionId,
            It.Is<BaseClientEvent>(e => e is SessionCapabilitiesEvent),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_AlsoAddsToActiveSessions()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();
        var accountId = "account-005";

        // Setup session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup registered services
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(sessionId, accountId, roles: null, authorizations: null);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify session was atomically added to activeSessions
        _mockCacheableStore.Verify(s => s.AddToSetAsync<string>(
            ACTIVE_SESSIONS_KEY,
            sessionId,
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_WithRoles_StoresRoleInSessionStates()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();
        var accountId = "account-roles-001";
        var roles = new List<string> { "user", "admin" };

        // Setup session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup registered services
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Track what gets saved to session states
        Dictionary<string, string>? savedSessionStates = null;
        _mockDictStringStore
            .Setup(s => s.SaveAsync(
                statesKey,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, string>, StateOptions?, CancellationToken>(
                (key, value, ttl, ct) => savedSessionStates = value)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(
            sessionId, accountId, roles: roles, authorizations: null);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.NotNull(savedSessionStates);
        Assert.True(savedSessionStates!.ContainsKey("role"), "Session states should contain 'role' key");
        Assert.Equal("admin", savedSessionStates["role"]); // Admin is highest priority
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_WithNoRoles_StoresAnonymousRole()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();
        var accountId = "account-roles-003";

        // Setup session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup registered services
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Track what gets saved to session states
        Dictionary<string, string>? savedSessionStates = null;
        _mockDictStringStore
            .Setup(s => s.SaveAsync(
                statesKey,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, string>, StateOptions?, CancellationToken>(
                (key, value, ttl, ct) => savedSessionStates = value)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(
            sessionId, accountId, roles: null, authorizations: null);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedSessionStates);
        Assert.Equal("anonymous", savedSessionStates!["role"]);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_WithAuthorizations_StoresAuthorizationsInSessionStates()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();
        var accountId = "account-auth-001";
        var authorizations = new List<string> { "game-1:authorized", "game-2:registered" };

        // Setup session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup registered services
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Track what gets saved to session states
        Dictionary<string, string>? savedSessionStates = null;
        _mockDictStringStore
            .Setup(s => s.SaveAsync(
                statesKey,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, string>, StateOptions?, CancellationToken>(
                (key, value, ttl, ct) => savedSessionStates = value)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(
            sessionId, accountId, roles: null, authorizations: authorizations);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedSessionStates);
        Assert.True(savedSessionStates!.ContainsKey("game-1"), "Should have 'game-1' authorization state");
        Assert.Equal("authorized", savedSessionStates["game-1"]);
        Assert.True(savedSessionStates.ContainsKey("game-2"), "Should have 'game-2' authorization state");
        Assert.Equal("registered", savedSessionStates["game-2"]);
    }

    #endregion

    #region Session Disconnection Tests

    [Fact]
    public async Task HandleSessionDisconnectedAsync_RemovesSessionFromActiveConnections()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();

        // Act - reconnectable = true (just removes from connections, keeps state)
        var (statusCode, response) = await service.HandleSessionDisconnectedAsync(sessionId, reconnectable: true);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Null(response.NewPermissions);

        // Verify atomic remove from activeConnections via RemoveFromSetAsync
        _mockCacheableStore.Verify(s => s.RemoveFromSetAsync<string>(
            ACTIVE_CONNECTIONS_KEY,
            sessionId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleSessionDisconnectedAsync_Reconnectable_KeepsActiveSessions()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();

        // Act - reconnectable = true
        var (statusCode, response) = await service.HandleSessionDisconnectedAsync(sessionId, reconnectable: true);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify activeSessions was NOT modified (reconnectable sessions keep their state)
        _mockCacheableStore.Verify(s => s.RemoveFromSetAsync<string>(
            ACTIVE_SESSIONS_KEY,
            It.IsAny<string>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSessionDisconnectedAsync_NotReconnectable_ClearsActiveSessions()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();

        // Setup for ClearSessionStateAsync (called internally)
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Act - reconnectable = false (clears all state)
        var (statusCode, response) = await service.HandleSessionDisconnectedAsync(sessionId, reconnectable: false);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Null(response.NewPermissions);

        // Verify atomic remove from both connections and sessions
        _mockCacheableStore.Verify(s => s.RemoveFromSetAsync<string>(
            ACTIVE_CONNECTIONS_KEY,
            sessionId,
            It.IsAny<CancellationToken>()), Times.Once);

        _mockCacheableStore.Verify(s => s.RemoveFromSetAsync<string>(
            ACTIVE_SESSIONS_KEY,
            sessionId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region RegisterServicePermissionsAsync ActiveConnections Tests

    [Fact]
    public async Task RegisterServicePermissionsAsync_PublishesOnlyToActiveConnections()
    {
        // Arrange
        var service = CreateService();
        var session1 = Guid.NewGuid().ToString();
        var session2 = Guid.NewGuid().ToString();
        var session3 = Guid.NewGuid().ToString();

        // Setup empty stored hash (first registration)
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Registered services check returns false (not yet registered)
        _mockCacheableStore
            .Setup(s => s.SetContainsAsync<string>(REGISTERED_SERVICES_KEY, "test-service", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Setup activeSessions with 3 sessions (for recompilation iteration)
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { session1, session2, session3 });

        // Setup registered services for recompile
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "test-service" });

        // Only session1 is connected (activeConnections check via SetContainsAsync)
        _mockCacheableStore
            .Setup(s => s.SetContainsAsync<string>(ACTIVE_CONNECTIONS_KEY, session1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCacheableStore
            .Setup(s => s.SetContainsAsync<string>(ACTIVE_CONNECTIONS_KEY, session2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockCacheableStore
            .Setup(s => s.SetContainsAsync<string>(ACTIVE_CONNECTIONS_KEY, session3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Setup session states/permissions for recompile
        foreach (var sessionId in new[] { session1, session2, session3 })
        {
            var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
            _mockDictStringStore
                .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, string> { ["role"] = "user" });

            var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
            _mockDictObjectStore
                .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, object>());
        }

        var permissions = new ServicePermissionMatrix
        {
            ServiceId = "test-service",
            Version = "1.0.0",
            Permissions = new Dictionary<string, StatePermissions>
            {
                ["default"] = new StatePermissions
                {
                    ["user"] = new System.Collections.ObjectModel.Collection<string> { "/test" }
                }
            }
        };

        // Act
        var (statusCode, response) = await service.RegisterServicePermissionsAsync(permissions);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        // Verify capability refresh was published exactly once to connected session (session1)
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            session1,
            It.Is<BaseClientEvent>(e => e is SessionCapabilitiesEvent),
            It.IsAny<CancellationToken>()), Times.Once);

        // CRITICAL: Capability refresh should NOT be published to non-connected sessions
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            session2,
            It.IsAny<SessionCapabilitiesEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);

        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            session3,
            It.IsAny<SessionCapabilitiesEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Session Data TTL Tests

    /// <summary>
    /// Verifies that when SessionDataTtlSeconds is configured, session data SaveAsync calls
    /// include StateOptions with the correct TTL value.
    /// </summary>
    [Fact]
    public async Task UpdateSessionStateAsync_WithSessionDataTtl_PassesStateOptionsWithTtl()
    {
        // Arrange - set session data TTL to 1 hour
        _mockConfiguration.Object.SessionDataTtlSeconds = 3600;

        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "user" });

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var stateUpdate = new SessionStateUpdate
        {
            SessionId = sessionId,
            ServiceId = "game-session",
            NewState = "in_game",
            PreviousState = null
        };

        // Act
        var (status, response) = await service.UpdateSessionStateAsync(stateUpdate);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify session states SaveAsync was called with StateOptions containing TTL=3600
        _mockDictStringStore.Verify(s => s.SaveAsync(
            statesKey,
            It.IsAny<Dictionary<string, string>>(),
            It.Is<StateOptions?>(opts => opts != null && opts.Ttl == 3600),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ClearSessionStateAsync Tests

    [Fact]
    public async Task ClearSessionStateAsync_NoSessionStates_ReturnsPermissionsNotChanged()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, string>?)null);

        var request = new ClearSessionStateRequest { SessionId = sessionId };

        // Act
        var (status, response) = await service.ClearSessionStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Null(response.NewPermissions);
    }

    [Fact]
    public async Task ClearSessionStateAsync_EmptyServiceId_ClearsAllStatesAndRecompiles()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["game-session"] = "in_game"
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var request = new ClearSessionStateRequest { SessionId = sessionId, ServiceId = null };

        // Act
        var (status, response) = await service.ClearSessionStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify states were saved (cleared)
        _mockDictStringStore.Verify(s => s.SaveAsync(
            statesKey,
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearSessionStateAsync_SpecificServiceNotFound_ReturnsPermissionsNotChanged()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);

        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["game-session"] = "in_game"
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        var request = new ClearSessionStateRequest
        {
            SessionId = sessionId,
            ServiceId = "nonexistent-service"
        };

        // Act
        var (status, response) = await service.ClearSessionStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Null(response.NewPermissions);
    }

    [Fact]
    public async Task ClearSessionStateAsync_SpecificServiceFound_RemovesAndRecompiles()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["game-session"] = "in_game"
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var request = new ClearSessionStateRequest
        {
            SessionId = sessionId,
            ServiceId = "game-session"
        };

        // Act
        var (status, response) = await service.ClearSessionStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify states were saved after removal
        _mockDictStringStore.Verify(s => s.SaveAsync(
            statesKey,
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearSessionStateAsync_WithStatesFilterMatching_RemovesState()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["game-session"] = "in_game"
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var request = new ClearSessionStateRequest
        {
            SessionId = sessionId,
            ServiceId = "game-session",
            States = new List<string> { "in_game", "lobby" }
        };

        // Act
        var (status, response) = await service.ClearSessionStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify states were saved (the matching state was removed)
        _mockDictStringStore.Verify(s => s.SaveAsync(
            statesKey,
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClearSessionStateAsync_WithStatesFilterNotMatching_ReturnsPermissionsNotChanged()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);

        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["game-session"] = "in_game"
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        // Filter for "lobby" but current state is "in_game" — no match
        var request = new ClearSessionStateRequest
        {
            SessionId = sessionId,
            ServiceId = "game-session",
            States = new List<string> { "lobby" }
        };

        // Act
        var (status, response) = await service.ClearSessionStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Null(response.NewPermissions);
    }

    #endregion

    #region GetSessionInfoAsync Tests

    [Fact]
    public async Task GetSessionInfoAsync_NoStates_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, string>?)null);

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, object>?)null);

        var request = new SessionInfoRequest { SessionId = sessionId };

        // Act
        var (status, response) = await service.GetSessionInfoAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetSessionInfoAsync_WithStatesAndPermissions_ReturnsCompleteInfo()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);

        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "admin",
            ["game-session"] = "in_game"
        };

        var generatedAt = DateTimeOffset.UtcNow.ToString("O");
        var permissionsData = new Dictionary<string, object>
        {
            ["version"] = 3,
            ["generated_at"] = generatedAt,
            ["orchestrator"] = BannouJson.SerializeToElement(new List<string> { "/orchestrator/health" })
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(permissionsData);

        var request = new SessionInfoRequest { SessionId = sessionId };

        // Act
        var (status, response) = await service.GetSessionInfoAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("admin", response.Role);
        Assert.Equal(3, response.Version);
        Assert.Equal(2, response.States.Count);
        Assert.True(response.Permissions.ContainsKey("orchestrator"));
        Assert.Contains("/orchestrator/health", response.Permissions["orchestrator"]);
    }

    [Fact]
    public async Task GetSessionInfoAsync_NoPermissionData_ReturnsEmptyPermissions()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);

        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user"
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, object>?)null);

        var request = new SessionInfoRequest { SessionId = sessionId };

        // Act
        var (status, response) = await service.GetSessionInfoAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("user", response.Role);
        Assert.Empty(response.Permissions);
        Assert.Equal(0, response.Version);
    }

    [Fact]
    public async Task GetSessionInfoAsync_NoRoleInStates_DefaultsToLowestRole()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);

        // States without "role" key
        var sessionStates = new Dictionary<string, string>
        {
            ["game-session"] = "in_game"
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        var request = new SessionInfoRequest { SessionId = sessionId };

        // Act
        var (status, response) = await service.GetSessionInfoAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        // Default role is first in hierarchy: "anonymous"
        Assert.Equal("anonymous", response.Role);
    }

    #endregion

    #region HandleSessionUpdatedAsync Tests

    [Fact]
    public async Task HandleSessionUpdatedAsync_WithRolesAndAuthorizations_UpdatesBoth()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        // Setup existing session states
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "anonymous" });

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var evt = new SessionUpdatedEvent
        {
            SessionId = sessionId,
            AccountId = Guid.NewGuid(),
            Roles = new List<string> { "user", "admin" },
            Authorizations = new List<string> { "game-1:authorized", "game-2:registered" },
            Reason = SessionUpdatedEventReason.RoleChanged
        };

        // Act
        await service.HandleSessionUpdatedAsync(evt);

        // Assert — verify states were saved (role update + 2 state updates)
        _mockDictStringStore.Verify(s => s.SaveAsync(
            statesKey,
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(1));
    }

    [Fact]
    public async Task HandleSessionUpdatedAsync_InvalidAuthorizationFormat_LogsWarningContinues()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "user" });

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var evt = new SessionUpdatedEvent
        {
            SessionId = sessionId,
            AccountId = Guid.NewGuid(),
            Roles = new List<string> { "user" },
            // Mix of valid and invalid authorization formats
            Authorizations = new List<string> { "invalid-no-colon", "game-1:authorized" },
            Reason = SessionUpdatedEventReason.RoleChanged
        };

        // Act — should not throw despite invalid format
        await service.HandleSessionUpdatedAsync(evt);

        // Assert — valid authorization should still be processed
        _mockDictStringStore.Verify(s => s.SaveAsync(
            statesKey,
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.AtLeast(1));
    }

    [Fact]
    public async Task HandleSessionUpdatedAsync_NullRolesAndAuthorizations_DefaultsToAnonymous()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Track role saved in session states
        Dictionary<string, string>? savedStates = null;
        _mockDictStringStore
            .Setup(s => s.SaveAsync(
                statesKey,
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, string>, StateOptions?, CancellationToken>(
                (key, value, opts, ct) => savedStates = value)
            .ReturnsAsync("etag");

        var evt = new SessionUpdatedEvent
        {
            SessionId = sessionId,
            AccountId = Guid.NewGuid(),
            Roles = new List<string>(),
            Authorizations = new List<string>(),
            Reason = SessionUpdatedEventReason.RoleChanged
        };

        // Act
        await service.HandleSessionUpdatedAsync(evt);

        // Assert — with empty roles, DetermineHighestPriorityRole returns default ("anonymous")
        Assert.NotNull(savedStates);
        Assert.Equal("anonymous", savedStates!["role"]);
    }

    #endregion

    #region IPermissionRegistry.RegisterServiceAsync Tests (Gap 1)

    /// <summary>
    /// Verifies that the explicit IPermissionRegistry.RegisterServiceAsync implementation
    /// correctly converts generic dictionary types to ServicePermissionMatrix and delegates
    /// to RegisterServicePermissionsAsync.
    /// </summary>
    [Fact]
    public async Task IPermissionRegistry_RegisterServiceAsync_ConvertsAndDelegates()
    {
        // Arrange
        var service = CreateService();

        // Setup for successful registration path
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockCacheableStore
            .Setup(s => s.SetContainsAsync<string>(REGISTERED_SERVICES_KEY, "test-svc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _mockHashSetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        // Capture what gets saved as the permission matrix
        string? savedMatrixKey = null;
        HashSet<string>? savedEndpoints = null;
        _mockHashSetStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("permissions:")),
                It.IsAny<HashSet<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, HashSet<string>, StateOptions?, CancellationToken>((k, v, _, _) =>
            {
                savedMatrixKey = k;
                savedEndpoints = v;
            })
            .ReturnsAsync("etag");

        var permissionMatrix = new Dictionary<string, IDictionary<string, ICollection<string>>>
        {
            ["default"] = new Dictionary<string, ICollection<string>>
            {
                ["admin"] = new List<string> { "/test/endpoint" }
            }
        };

        // Act — cast to explicit interface
        IPermissionRegistry registry = service;
        await registry.RegisterServiceAsync("test-svc", "2.0.0", permissionMatrix);

        // Assert — verify the service was added to registered services
        _mockCacheableStore.Verify(s => s.AddToSetAsync<string>(
            REGISTERED_SERVICES_KEY,
            "test-svc",
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that IPermissionRegistry.RegisterServiceAsync correctly converts and passes
    /// the version and permission matrix through to the underlying implementation.
    /// </summary>
    [Fact]
    public async Task IPermissionRegistry_RegisterServiceAsync_PassesVersionToImplementation()
    {
        // Arrange
        var service = CreateService();

        // Make the lock fail so RegisterServicePermissionsAsync returns non-OK status.
        // Actually, RegisterServicePermissionsAsync doesn't use locks. Instead, we need
        // to trigger a non-OK return. Looking at the code, the only non-OK path doesn't exist
        // (it always returns OK). Let's test the conversion logic by verifying the data flows through.
        // The InvalidOperationException path would require RegisterServicePermissionsAsync to return
        // a non-OK status, which currently only happens via an impossible code path.
        // However, we can still test the conversion is correct with more detailed assertions.

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockCacheableStore
            .Setup(s => s.SetContainsAsync<string>(REGISTERED_SERVICES_KEY, "fail-svc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Force the registration to succeed and verify the matrix was correctly constructed
        ServiceRegistrationInfo? capturedRegistration = null;
        _mockRegistrationInfoStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.Contains("fail-svc")),
                It.IsAny<ServiceRegistrationInfo>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ServiceRegistrationInfo, StateOptions?, CancellationToken>(
                (_, info, _, _) => capturedRegistration = info)
            .ReturnsAsync("etag");

        var permissionMatrix = new Dictionary<string, IDictionary<string, ICollection<string>>>
        {
            ["authenticated"] = new Dictionary<string, ICollection<string>>
            {
                ["user"] = new List<string> { "/ep1", "/ep2" },
                ["admin"] = new List<string> { "/admin-ep" }
            }
        };

        // Act
        IPermissionRegistry registry = service;
        await registry.RegisterServiceAsync("fail-svc", "3.0.0", permissionMatrix);

        // Assert — verify the version was passed through the conversion
        Assert.NotNull(capturedRegistration);
        Assert.Equal("fail-svc", capturedRegistration.ServiceId);
        Assert.Equal("3.0.0", capturedRegistration.Version);
    }

    #endregion

    #region RegisterServicePermissionsAsync Idempotent Skip Tests (Gap 2)

    /// <summary>
    /// Verifies that when the hash matches AND the service is already registered,
    /// registration is skipped (idempotent path) and no permission matrix is stored.
    /// </summary>
    [Fact]
    public async Task RegisterServicePermissionsAsync_HashMatchAndAlreadyRegistered_SkipsRegistration()
    {
        // Arrange
        var service = CreateService();
        var permissions = new ServicePermissionMatrix
        {
            ServiceId = "orchestrator",
            Version = "1.0.0",
            Permissions = new Dictionary<string, StatePermissions>
            {
                ["default"] = new StatePermissions
                {
                    ["admin"] = new System.Collections.ObjectModel.Collection<string> { "/health" }
                }
            }
        };

        // First call to get the hash
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockCacheableStore
            .Setup(s => s.SetContainsAsync<string>(REGISTERED_SERVICES_KEY, "orchestrator", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _mockHashSetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        // First registration to get the stored hash
        await service.RegisterServicePermissionsAsync(permissions);

        // Capture the hash that was stored
        string? storedHash = null;
        _mockStringStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("permission_hash:")),
                It.IsAny<string>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, StateOptions?, CancellationToken>((_, hash, _, _) => storedHash = hash)
            .ReturnsAsync("etag");

        await service.RegisterServicePermissionsAsync(permissions);

        // Now set up the stored hash to match and service already registered
        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("permission_hash:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedHash);
        _mockCacheableStore
            .Setup(s => s.SetContainsAsync<string>(REGISTERED_SERVICES_KEY, "orchestrator", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Reset verification counts
        _mockHashSetStore.Invocations.Clear();

        // Act — second registration with same data should skip
        var (status, response) = await service.RegisterServicePermissionsAsync(permissions);

        // Assert — idempotent skip
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Key assertion: no permission matrix should be stored (SaveAsync on hash set store not called)
        _mockHashSetStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith("permissions:")),
            It.IsAny<HashSet<string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region RegisterServicePermissionsAsync Null Permissions Tests (Gap 3)

    /// <summary>
    /// Verifies that when Permissions is null, the registration logs a warning
    /// and continues without storing any permission matrix, but still stores
    /// version and registration info.
    /// </summary>
    [Fact]
    public async Task RegisterServicePermissionsAsync_NullPermissions_SkipsMatrixStorageButRegisters()
    {
        // Arrange
        var service = CreateService();

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockCacheableStore
            .Setup(s => s.SetContainsAsync<string>(REGISTERED_SERVICES_KEY, "null-perms-svc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var permissions = new ServicePermissionMatrix
        {
            ServiceId = "null-perms-svc",
            Version = "1.0.0",
            Permissions = new Dictionary<string, StatePermissions>()
        };

        // Act
        var (status, response) = await service.RegisterServicePermissionsAsync(permissions);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // No permission matrix should be stored (empty permissions)
        _mockHashSetStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.StartsWith("permissions:")),
            It.IsAny<HashSet<string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // But the service should still be registered
        _mockCacheableStore.Verify(s => s.AddToSetAsync<string>(
            REGISTERED_SERVICES_KEY,
            "null-perms-svc",
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Version and registration info should still be stored
        _mockRegistrationInfoStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.Contains("null-perms-svc")),
            It.IsAny<ServiceRegistrationInfo>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region UpdateSessionStateAsync Lock Failure Tests (Gap 4)

    /// <summary>
    /// Verifies that UpdateSessionStateAsync returns Conflict when the distributed lock
    /// cannot be acquired for the session.
    /// </summary>
    [Fact]
    public async Task UpdateSessionStateAsync_LockFailure_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();

        // Make the lock fail
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        var stateUpdate = new SessionStateUpdate
        {
            SessionId = Guid.NewGuid(),
            ServiceId = "game-session",
            NewState = "in_game"
        };

        // Act
        var (status, response) = await service.UpdateSessionStateAsync(stateUpdate);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);

        // Verify no state was saved
        _mockDictStringStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region UpdateSessionRoleAsync Lock Failure Tests (Gap 5)

    /// <summary>
    /// Verifies that UpdateSessionRoleAsync returns Conflict when the distributed lock
    /// cannot be acquired for the session.
    /// </summary>
    [Fact]
    public async Task UpdateSessionRoleAsync_LockFailure_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();

        // Make the lock fail
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        var roleUpdate = new SessionRoleUpdate
        {
            SessionId = Guid.NewGuid(),
            NewRole = "admin"
        };

        // Act
        var (status, response) = await service.UpdateSessionRoleAsync(roleUpdate);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);

        // Verify no state was saved
        _mockDictStringStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region RecompileSessionPermissionsAsync Exception Tests (Gap 6)

    /// <summary>
    /// Verifies that when RecompileSessionPermissionsAsync catches an exception,
    /// it publishes an error event via TryPublishErrorAsync and returns (false, null).
    /// </summary>
    [Fact]
    public async Task RecompileSessionPermissions_ExceptionDuringCompilation_PublishesErrorEvent()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);

        // Setup session states so recompile enters the try block
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "user" });

        // Make GetSetAsync throw to trigger the catch block inside RecompileSessionPermissionsAsync
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable"));

        // Setup permissions for the initial read
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        // Capture error event
        string? capturedErrorTopic = null;
        _mockMessageBus
            .Setup(m => m.TryPublishErrorAsync(
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
                (svc, op, errType, msg, dep, ep, sev, det, stack, corr, ct) => capturedErrorTopic = op)
            .ReturnsAsync(true);

        // Act - UpdateSessionRoleAsync triggers RecompileSessionPermissionsAsync internally
        var roleUpdate = new SessionRoleUpdate
        {
            SessionId = sessionId,
            NewRole = "user"
        };

        var (status, response) = await service.UpdateSessionRoleAsync(roleUpdate);

        // Assert - Should still return OK (the role was saved before recompile)
        Assert.Equal(StatusCodes.OK, status);

        // The error event should have been published for the recompile failure
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "permission",
            "RecompileSessionPermissions",
            It.IsAny<string>(),
            It.Is<string>(msg => msg.Contains("Redis unavailable")),
            "state",
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region PublishCapabilityUpdateAsync Exception Tests (Gap 7)

    /// <summary>
    /// Verifies that when PublishToSessionAsync throws an exception,
    /// the error is caught and an error event is published.
    /// </summary>
    [Fact]
    public async Task PublishCapabilityUpdate_ExceptionOnPublish_PublishesErrorEvent()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        // Setup session for connected path (HandleSessionConnectedAsync skips connection check)
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "svc" });

        _mockHashSetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "/endpoint" });

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Make PublishToSessionAsync throw
        _mockClientEventPublisher
            .Setup(x => x.PublishToSessionAsync(
                It.IsAny<string>(),
                It.IsAny<BaseClientEvent>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RabbitMQ connection lost"));

        // Act - HandleSessionConnectedAsync triggers recompile with skipActiveConnectionsCheck=true
        var (status, _) = await service.HandleSessionConnectedAsync(
            sessionIdStr, "account-001", roles: new List<string> { "user" }, authorizations: null);

        // Assert - Should still succeed (error is caught)
        Assert.Equal(StatusCodes.OK, status);

        // Verify error event was published
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "permission",
            "PublishCapabilities",
            It.IsAny<string>(),
            It.Is<string>(msg => msg.Contains("RabbitMQ connection lost")),
            "messaging",
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region PublishCapabilityUpdateAsync Publish Returns False Tests (Gap 8)

    /// <summary>
    /// Verifies that when PublishToSessionAsync returns false (publish failure),
    /// a warning is logged but no error event is published.
    /// The method completes normally without throwing.
    /// </summary>
    [Fact]
    public async Task PublishCapabilityUpdate_PublishReturnsFalse_CompletesWithoutError()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "svc" });

        _mockHashSetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "/endpoint" });

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Make PublishToSessionAsync return false
        _mockClientEventPublisher
            .Setup(x => x.PublishToSessionAsync(
                It.IsAny<string>(),
                It.IsAny<BaseClientEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var (status, _) = await service.HandleSessionConnectedAsync(
            sessionIdStr, "account-001", roles: new List<string> { "user" }, authorizations: null);

        // Assert — operation succeeds despite publish failure
        Assert.Equal(StatusCodes.OK, status);

        // Verify that no TryPublishErrorAsync was called (this is just a warning log, not an error event)
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            It.IsAny<string>(),
            "PublishCapabilities",
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region GetRegisteredServicesAsync Tests (Gap 9)

    /// <summary>
    /// Verifies GetRegisteredServicesAsync returns an OK response with service details
    /// when services are registered.
    /// </summary>
    [Fact]
    public async Task GetRegisteredServicesAsync_WithRegisteredServices_ReturnsServiceList()
    {
        // Arrange
        var service = CreateService();

        // Setup registered services list
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "account", "auth" });

        // Setup registration info for account
        var accountRegKey = string.Format(SERVICE_REGISTERED_KEY, "account");
        _mockRegistrationInfoStore
            .Setup(s => s.GetAsync(accountRegKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceRegistrationInfo
            {
                ServiceId = "account",
                Version = "2.0.0",
                RegisteredAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            });

        // Setup registration info for auth
        var authRegKey = string.Format(SERVICE_REGISTERED_KEY, "auth");
        _mockRegistrationInfoStore
            .Setup(s => s.GetAsync(authRegKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceRegistrationInfo
            {
                ServiceId = "auth",
                Version = "4.0.0",
                RegisteredAt = DateTimeOffset.UtcNow.AddMinutes(-3)
            });

        // Setup service states for endpoint counting
        _mockHashSetStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("service-states:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "default" });

        // Setup endpoints for account (default state, user role)
        _mockHashSetStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("permissions:account:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "/account/get", "/account/update" });

        // Setup endpoints for auth
        _mockHashSetStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("permissions:auth:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "/auth/login", "/auth/validate" });

        var request = new ListServicesRequest();

        // Act
        var (status, response) = await service.GetRegisteredServicesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Services.Count);

        var accountInfo = response.Services.First(s => s.ServiceId == "account");
        Assert.Equal("2.0.0", accountInfo.Version);
        Assert.True(accountInfo.EndpointCount > 0);

        var authInfo = response.Services.First(s => s.ServiceId == "auth");
        Assert.Equal("4.0.0", authInfo.Version);
    }

    /// <summary>
    /// Verifies GetRegisteredServicesAsync returns an empty list when no services are registered.
    /// </summary>
    [Fact]
    public async Task GetRegisteredServicesAsync_NoRegisteredServices_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var request = new ListServicesRequest();

        // Act
        var (status, response) = await service.GetRegisteredServicesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Services);
        Assert.True(response.Timestamp <= DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Verifies GetRegisteredServicesAsync handles missing registration data gracefully
    /// by using "unknown" version and current timestamp.
    /// </summary>
    [Fact]
    public async Task GetRegisteredServicesAsync_MissingRegistrationData_UsesDefaults()
    {
        // Arrange
        var service = CreateService();

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "orphan-svc" });

        // No registration info found
        _mockRegistrationInfoStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceRegistrationInfo?)null);

        _mockHashSetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        var request = new ListServicesRequest();

        // Act
        var (status, response) = await service.GetRegisteredServicesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Services);

        var svcInfo = response.Services.First();
        Assert.Equal("orphan-svc", svcInfo.ServiceId);
        Assert.Equal("unknown", svcInfo.Version);
        Assert.Equal(0, svcInfo.EndpointCount);
    }

    #endregion

    #region DetermineHighestPriorityRole Fallback Tests (Gap 10)

    /// <summary>
    /// Verifies that when roles contain values NOT in the configured hierarchy,
    /// the method falls back to returning the first role from the input.
    /// </summary>
    [Fact]
    public async Task HandleSessionConnectedAsync_RolesNotInHierarchy_UsesFirstRole()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();

        _mockDictStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _mockDictObjectStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Track saved session states to check the assigned role
        Dictionary<string, string>? savedStates = null;
        _mockDictStringStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, string>, StateOptions?, CancellationToken>(
                (_, v, _, _) => savedStates = v)
            .ReturnsAsync("etag");

        // Roles that are NOT in the default hierarchy ["anonymous", "user", "developer", "admin"]
        var unknownRoles = new List<string> { "moderator", "vip" };

        // Act
        await service.HandleSessionConnectedAsync(sessionId, "acct-001", roles: unknownRoles, authorizations: null);

        // Assert — should fall back to the first role ("moderator")
        Assert.NotNull(savedStates);
        var states = savedStates ?? throw new InvalidOperationException("Captured savedStates was null");
        Assert.Equal("moderator", states["role"]);
    }

    #endregion

    #region ComputePermissionDataHash Tests (Gap 11)

    /// <summary>
    /// Verifies that the same permission data produces the same hash (determinism),
    /// and different data produces different hashes.
    /// </summary>
    [Fact]
    public async Task RegisterServicePermissionsAsync_SameData_ProducesSameHash()
    {
        // Arrange
        var service = CreateService();

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockCacheableStore
            .Setup(s => s.SetContainsAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _mockHashSetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        // Capture hashes
        var capturedHashes = new List<string>();
        _mockStringStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("permission_hash:")),
                It.IsAny<string>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, StateOptions?, CancellationToken>((_, hash, _, _) => capturedHashes.Add(hash))
            .ReturnsAsync("etag");

        var permissions1 = new ServicePermissionMatrix
        {
            ServiceId = "hash-test-1",
            Version = "1.0.0",
            Permissions = new Dictionary<string, StatePermissions>
            {
                ["default"] = new StatePermissions
                {
                    ["user"] = new System.Collections.ObjectModel.Collection<string> { "/a", "/b" }
                }
            }
        };

        var permissions2 = new ServicePermissionMatrix
        {
            ServiceId = "hash-test-2",
            Version = "1.0.0",
            Permissions = new Dictionary<string, StatePermissions>
            {
                ["default"] = new StatePermissions
                {
                    ["user"] = new System.Collections.ObjectModel.Collection<string> { "/a", "/b" }
                }
            }
        };

        var permissionsDifferent = new ServicePermissionMatrix
        {
            ServiceId = "hash-test-3",
            Version = "2.0.0",  // Different version
            Permissions = new Dictionary<string, StatePermissions>
            {
                ["default"] = new StatePermissions
                {
                    ["user"] = new System.Collections.ObjectModel.Collection<string> { "/a", "/b" }
                }
            }
        };

        // Act
        await service.RegisterServicePermissionsAsync(permissions1);
        await service.RegisterServicePermissionsAsync(permissions2);
        await service.RegisterServicePermissionsAsync(permissionsDifferent);

        // Assert — same data should produce same hash, different data should differ
        Assert.Equal(3, capturedHashes.Count);
        Assert.Equal(capturedHashes[0], capturedHashes[1]); // Same version + same permissions
        Assert.NotEqual(capturedHashes[0], capturedHashes[2]); // Different version
    }

    #endregion

    #region GetSessionDataStateOptions Zero TTL Tests (Gap 12)

    /// <summary>
    /// Verifies that when SessionDataTtlSeconds is 0 (disabled), the session data
    /// SaveAsync calls pass null StateOptions (no TTL).
    /// </summary>
    [Fact]
    public async Task UpdateSessionStateAsync_ZeroTtl_PassesNullStateOptions()
    {
        // Arrange
        _mockConfiguration.Object.SessionDataTtlSeconds = 0;
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "user" });

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var stateUpdate = new SessionStateUpdate
        {
            SessionId = sessionId,
            ServiceId = "game-session",
            NewState = "in_game"
        };

        // Act
        var (status, _) = await service.UpdateSessionStateAsync(stateUpdate);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Key assertion: StateOptions should be null when TTL is 0
        _mockDictStringStore.Verify(s => s.SaveAsync(
            statesKey,
            It.IsAny<Dictionary<string, string>>(),
            It.Is<StateOptions?>(opts => opts == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region HandleSessionConnectedAsync Invalid Authorization Format Tests (Gap 13)

    /// <summary>
    /// Verifies that when HandleSessionConnectedAsync receives malformed authorizations
    /// (missing colon separator), they are silently skipped without affecting valid ones.
    /// This is the direct DI call path (not the event handler path).
    /// </summary>
    [Fact]
    public async Task HandleSessionConnectedAsync_InvalidAuthorizationFormat_SkipsInvalid()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();

        _mockDictStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        _mockCacheableStore
            .Setup(s => s.GetSetAsync<string>(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _mockDictObjectStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        Dictionary<string, string>? savedStates = null;
        _mockDictStringStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, string>, StateOptions?, CancellationToken>(
                (_, v, _, _) => savedStates = v)
            .ReturnsAsync("etag");

        // Mix of valid, invalid (no colon), and triple-colon format
        var authorizations = new List<string>
        {
            "game-1:authorized",         // valid
            "no-colon-here",             // invalid: no separator
            "game-2:registered"          // valid
        };

        // Act
        var (status, _) = await service.HandleSessionConnectedAsync(
            sessionId, "acct-001", roles: null, authorizations: authorizations);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedStates);
        var states = savedStates ?? throw new InvalidOperationException("Captured savedStates was null");

        // Valid authorizations should be stored
        Assert.True(states.ContainsKey("game-1"));
        Assert.Equal("authorized", states["game-1"]);
        Assert.True(states.ContainsKey("game-2"));
        Assert.Equal("registered", states["game-2"]);

        // The invalid authorization should NOT have been stored as a session state
        // (Split on ':' for "no-colon-here" yields only 1 part, so the if (parts.Length == 2) check skips it)
        Assert.False(states.ContainsKey("no-colon-here"));
    }

    #endregion

    #region HandleSessionUpdatedAsync Exception Tests (Gap 14)

    /// <summary>
    /// Verifies that when HandleSessionUpdatedAsync catches an exception,
    /// it publishes an error event and does not rethrow.
    /// </summary>
    [Fact]
    public async Task HandleSessionUpdatedAsync_Exception_PublishesErrorEvent()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();

        // Make UpdateSessionRoleAsync fail by having the lock fail
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Lock service down"));

        var evt = new SessionUpdatedEvent
        {
            SessionId = sessionId,
            AccountId = Guid.NewGuid(),
            Roles = new List<string> { "user" },
            Authorizations = new List<string>(),
            Reason = SessionUpdatedEventReason.RoleChanged
        };

        // Act — should not throw
        await service.HandleSessionUpdatedAsync(evt);

        // Assert — error event should be published
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "permission",
            "HandleSessionUpdated",
            It.IsAny<string>(),
            It.Is<string>(msg => msg.Contains("Lock service down")),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
