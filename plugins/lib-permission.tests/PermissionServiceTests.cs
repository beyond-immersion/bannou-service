using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
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
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    // State store constants (must match PermissionService)
    private const string STATE_STORE = "permission-statestore";
    private const string ACTIVE_SESSIONS_KEY = "active_sessions";
    private const string ACTIVE_CONNECTIONS_KEY = "active_connections";
    private const string REGISTERED_SERVICES_KEY = "registered_services";
    private const string SESSION_STATES_KEY = "session:{0}:states";
    private const string SESSION_PERMISSIONS_KEY = "session:{0}:permissions";
    private const string PERMISSION_MATRIX_KEY = "permissions:{0}:{1}:{2}";

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
        _mockEventConsumer = new Mock<IEventConsumer>();

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
        _mockCacheableStore.Setup(s => s.RemoveFromSetAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockCacheableStore.Setup(s => s.SetContainsAsync<string>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockCacheableStore.Setup(s => s.GetSetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _mockCacheableStore.Setup(s => s.SetCountAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
        Assert.Equal("orchestrator", response.ServiceId);

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
        Assert.Equal(sessionId, response.SessionId);
        Assert.Contains("admin", response.Message);

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
        Assert.Equal(sessionId, response.SessionId);
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
        Assert.Equal(sessionId, response.SessionId);
        Assert.Contains("registered", response.Message);

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
        Assert.Contains("reconnectable", response.Message);

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
            It.IsAny<CancellationToken>()), Times.Never);
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
        Assert.Contains("cleared", response.Message);

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
}
