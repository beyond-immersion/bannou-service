using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Permissions;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Permissions.Tests;

/// <summary>
/// Test implementation of ILockResponse for unit testing.
/// </summary>
internal class TestLockResponse : ILockResponse
{
    public bool Success { get; init; }

    public ValueTask DisposeAsync()
    {
        // No actual disposal needed for test
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Unit tests for PermissionsService.
/// Tests verify permission registration, session management, capability compilation, and role-based access.
/// </summary>
public class PermissionsServiceTests
{
    private readonly Mock<ILogger<PermissionsService>> _mockLogger;
    private readonly Mock<PermissionsServiceConfiguration> _mockConfiguration;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<HashSet<string>>> _mockHashSetStore;
    private readonly Mock<IStateStore<Dictionary<string, string>>> _mockDictStringStore;
    private readonly Mock<IStateStore<Dictionary<string, object>>> _mockDictObjectStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<object>> _mockObjectStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IClientEventPublisher> _mockClientEventPublisher;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    // State store constants (must match PermissionsService)
    private const string STATE_STORE = "permissions-statestore";
    private const string ACTIVE_SESSIONS_KEY = "active_sessions";
    private const string ACTIVE_CONNECTIONS_KEY = "active_connections"; // Phase 6: tracks WebSocket-connected sessions
    private const string REGISTERED_SERVICES_KEY = "registered_services";
    private const string SESSION_STATES_KEY = "session:{0}:states";
    private const string SESSION_PERMISSIONS_KEY = "session:{0}:permissions";
    private const string PERMISSION_MATRIX_KEY = "permissions:{0}:{1}:{2}";

    public PermissionsServiceTests()
    {
        _mockLogger = new Mock<ILogger<PermissionsService>>();
        _mockConfiguration = new Mock<PermissionsServiceConfiguration>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockHashSetStore = new Mock<IStateStore<HashSet<string>>>();
        _mockDictStringStore = new Mock<IStateStore<Dictionary<string, string>>>();
        _mockDictObjectStore = new Mock<IStateStore<Dictionary<string, object>>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockObjectStore = new Mock<IStateStore<object>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockClientEventPublisher = new Mock<IClientEventPublisher>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup factory to return typed stores
        // Note: object setup must come FIRST (most general) to avoid Castle proxy matching issues
        // where the object proxy is incorrectly returned for more specific type calls
        _mockStateStoreFactory.Setup(f => f.GetStore<object>(STATE_STORE))
            .Returns(_mockObjectStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<HashSet<string>>(STATE_STORE))
            .Returns(_mockHashSetStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<Dictionary<string, string>>(STATE_STORE))
            .Returns(_mockDictStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<Dictionary<string, object>>(STATE_STORE))
            .Returns(_mockDictObjectStore.Object);
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

    private PermissionsService CreateService()
    {
        return new PermissionsService(
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLockProvider.Object,
            _mockClientEventPublisher.Object,
            _mockEventConsumer.Object);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var service = CreateService();
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PermissionsService(null!, _mockConfiguration.Object, _mockStateStoreFactory.Object, _mockMessageBus.Object, _mockLockProvider.Object, _mockClientEventPublisher.Object, _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PermissionsService(_mockLogger.Object, null!, _mockStateStoreFactory.Object, _mockMessageBus.Object, _mockLockProvider.Object, _mockClientEventPublisher.Object, _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullStateStoreFactory_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PermissionsService(_mockLogger.Object, _mockConfiguration.Object, null!, _mockMessageBus.Object, _mockLockProvider.Object, _mockClientEventPublisher.Object, _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullMessageBus_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PermissionsService(_mockLogger.Object, _mockConfiguration.Object, _mockStateStoreFactory.Object, null!, _mockLockProvider.Object, _mockClientEventPublisher.Object, _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullLockProvider_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PermissionsService(_mockLogger.Object, _mockConfiguration.Object, _mockStateStoreFactory.Object, _mockMessageBus.Object, null!, _mockClientEventPublisher.Object, _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullClientEventPublisher_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PermissionsService(_mockLogger.Object, _mockConfiguration.Object, _mockStateStoreFactory.Object, _mockMessageBus.Object, _mockLockProvider.Object, null!, _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullEventConsumer_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new PermissionsService(_mockLogger.Object, _mockConfiguration.Object, _mockStateStoreFactory.Object, _mockMessageBus.Object, _mockLockProvider.Object, _mockClientEventPublisher.Object, null!));
    }

    [Fact]
    public async Task RegisterServicePermissionsAsync_LockAcquisitionFails_ReturnsInternalServerError()
    {
        // Arrange
        var service = CreateService();

        // Set up lock to fail
        var failedLockResponse = new TestLockResponse { Success = false };
        _mockLockProvider
            .Setup(l => l.LockAsync(
                STATE_STORE,
                "registered_services_lock",
                It.IsAny<string>(),
                30,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLockResponse);

        // Set up empty state for the idempotent check
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockHashSetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        var permissions = new ServicePermissionMatrix
        {
            ServiceId = "test-service",
            Version = "1.0.0",
            Permissions = new Dictionary<string, StatePermissions>
            {
                ["default"] = new StatePermissions
                {
                    ["user"] = new System.Collections.ObjectModel.Collection<string>
                    {
                        "GET:/test/endpoint"
                    }
                }
            }
        };

        // Act
        var (statusCode, response) = await service.RegisterServicePermissionsAsync(permissions);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, statusCode);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Contains("lock", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterServicePermissionsAsync_StoresPermissionMatrix()
    {
        // Arrange
        // Capture error logs to see what exception is being thrown
        Exception? capturedException = null;
        _mockLogger
            .Setup(l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(invocation =>
            {
                capturedException = invocation.Arguments[3] as Exception;
            }));

        var service = CreateService();

        // Set up empty existing state
        _mockHashSetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

        // Set up empty existing hash
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Set up distributed lock to succeed
        var lockResponse = new TestLockResponse { Success = true };
        _mockLockProvider
            .Setup(l => l.LockAsync(
                STATE_STORE,
                "registered_services_lock",
                It.IsAny<string>(),
                30,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockResponse);

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
                        "GET:/orchestrator/health",
                        "POST:/orchestrator/deploy"
                    }
                }
            }
        };

        // Act
        var (statusCode, response) = await service.RegisterServicePermissionsAsync(permissions);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal("orchestrator", response.ServiceId);

        // Verify registered services list was updated at least once with orchestrator
        _mockHashSetStore.Verify(s => s.SaveAsync(
            REGISTERED_SERVICES_KEY,
            It.Is<HashSet<string>>(set => set.Contains("orchestrator")),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce());
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

        // Set up empty active sessions list
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Set up empty registered services
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

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
        Assert.True(response.Success);
        Assert.Equal(sessionId, response.SessionId);
        Assert.Contains("admin", response.Message);

        // Verify session states were saved
        _mockDictStringStore.Verify(s => s.SaveAsync(
            statesKey,
            It.IsAny<Dictionary<string, string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
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

        // Empty initial state/active sessions/permissions
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Registered services
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "svc" });

        // Permission matrix lookups per role (developer should inherit user)
        _mockHashSetStore
            .Setup(s => s.GetAsync(It.Is<string>(key => key.Contains("permissions:svc:default:user")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "GET:/user" });

        _mockHashSetStore
            .Setup(s => s.GetAsync(It.Is<string>(key => key.Contains("permissions:svc:default:developer")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "GET:/dev" });

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
        Assert.True(response?.Success);
        Assert.NotNull(savedPermissions);
        Assert.True(savedPermissions!.TryGetValue("svc", out var endpointsObj));
        var endpoints = endpointsObj as IEnumerable<string>;
        Assert.NotNull(endpoints);
        Assert.Contains("GET:/user", endpoints!);
        Assert.Contains("GET:/dev", endpoints!);
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

        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "svc" });

        // Endpoint gated by game-session:in_game + role user
        _mockHashSetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains("permissions:svc:game-session:in_game:user")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "POST:/secure" });

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
        Assert.Contains("POST:/secure", endpoints!);
    }

    /// <summary>
    /// Verifies same-service state key matching: when voice service
    /// registers permissions for its own voice:ringing state (same-service),
    /// the state lookup key should be just "ringing", not "voice:ringing".
    ///
    /// Bug scenario this prevented:
    /// - Registration stored: permissions:voice:ringing:user (state key = ringing)
    /// - Lookup searched for: permissions:voice:ringing:user (correct)
    ///
    /// The fix ensures both use the same key format.
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
            ["voice"] = "ringing"  // voice service sets voice:ringing state
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "voice" });

        // CRITICAL: The state key for same-service must be just "ringing", not "voice:ringing"
        // This is the key that BuildPermissionMatrix should produce for voice service
        // registering permissions for voice:ringing state
        _mockHashSetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k == "permissions:voice:ringing:user"),  // Same-service: just state value
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "POST:/voice/peer/answer" });

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
        Assert.Contains("POST:/voice/peer/answer", endpoints!);
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

        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "game-session" });

        // Cross-service: game-session service checking voice state requires "voice:ringing" key
        _mockHashSetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k == "permissions:game-session:voice:ringing:user"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "POST:/sessions/voice-enabled-action" });

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
        Assert.Contains("POST:/sessions/voice-enabled-action", endpoints!);
    }

    /// <summary>
    /// Verifies game-session:in_game state works correctly for game-session service.
    /// When a user joins a game session, the game-session service sets game-session:in_game,
    /// and endpoints requiring that state should become accessible.
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

        // Session has game-session=in_game state (set when player joins)
        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["game-session"] = "in_game"
        };

        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "game-session" });

        // Same-service state key: just "in_game" (not "game-session:in_game")
        _mockHashSetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k == "permissions:game-session:in_game:user"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>
            {
                "POST:/sessions/leave",
                "POST:/sessions/chat",
                "POST:/sessions/actions"
            });

        _mockHashSetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k == "permissions:game-session:default:user"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>
            {
                "GET:/sessions/list",
                "POST:/sessions/create",
                "POST:/sessions/get",
                "POST:/sessions/join"
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
        Assert.Contains("GET:/sessions/list", endpoints!);
        Assert.Contains("POST:/sessions/join", endpoints!);
        Assert.Contains("POST:/sessions/leave", endpoints!);  // Requires in_game state
        Assert.Contains("POST:/sessions/chat", endpoints!);   // Requires in_game state
        Assert.Contains("POST:/sessions/actions", endpoints!); // Requires in_game state
    }

    [Fact]
    public async Task RecompileSessionPermissions_FindsRegisteredServices()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();

        // Pre-populate registered services with "orchestrator"
        var registeredServices = new HashSet<string> { "orchestrator" };
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(registeredServices);

        // Pre-populate permission matrix: permissions:orchestrator:default:admin
        var adminEndpoints = new HashSet<string> { "GET:/orchestrator/health", "POST:/orchestrator/deploy" };
        var adminMatrixKey = string.Format(PERMISSION_MATRIX_KEY, "orchestrator", "default", "admin");
        _mockHashSetStore
            .Setup(s => s.GetAsync(adminMatrixKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminEndpoints);

        // Set up session states with admin role
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "admin" });

        // Set up empty active sessions list
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

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
        Assert.True(response.Success);

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
                "GET:/orchestrator/health",
                "POST:/orchestrator/deploy"
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
        Assert.Contains("GET:/orchestrator/health", response.Permissions["orchestrator"]);
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

        // Set up registered services
        var registeredServices = new HashSet<string> { "orchestrator" };
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(registeredServices);

        // Admin-only endpoints at permissions:orchestrator:default:admin
        var adminEndpoints = new HashSet<string> { "GET:/orchestrator/health", "POST:/orchestrator/deploy" };
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

        // Set up empty active sessions
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

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
        var adminRoleUpdate = new SessionRoleUpdate
        {
            SessionId = adminSessionId,
            NewRole = "admin"
        };
        await service.UpdateSessionRoleAsync(adminRoleUpdate);

        // Act - Set user role
        var userRoleUpdate = new SessionRoleUpdate
        {
            SessionId = userSessionId,
            NewRole = "user"
        };
        await service.UpdateSessionRoleAsync(userRoleUpdate);

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
                "GET:/orchestrator/health",
                "POST:/orchestrator/deploy"
            })
        };

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(compiledPermissions);

        var request = new ValidationRequest
        {
            SessionId = sessionId,
            ServiceId = "orchestrator",
            Method = "GET:/orchestrator/health"
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
            ["accounts"] = BannouJson.SerializeToElement(new List<string>
            {
                "GET:/accounts/profile"
            })
        };

        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(compiledPermissions);

        var request = new ValidationRequest
        {
            SessionId = sessionId,
            ServiceId = "orchestrator",
            Method = "POST:/orchestrator/deploy"
        };

        // Act
        var (statusCode, response) = await service.ValidateApiAccessAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.False(response.Allowed);
    }

    #region Phase 6: Session Connection Tracking Tests

    /// <summary>
    /// Tests for HandleSessionConnectedAsync - Phase 6 session connection tracking.
    /// These methods ensure safe capability publishing by tracking which sessions have active WebSocket connections.
    /// </summary>

    [Fact]
    public async Task HandleSessionConnectedAsync_AddsSessionToActiveConnections()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid();
        var sessionIdStr = sessionId.ToString();
        var accountId = "account-001";

        // Setup empty activeConnections
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty registered services (for recompile)
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup session states for recompile
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup session permissions for recompile
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Track what gets saved to activeConnections
        HashSet<string>? savedConnections = null;
        _mockHashSetStore
            .Setup(s => s.SaveAsync(
                ACTIVE_CONNECTIONS_KEY,
                It.IsAny<HashSet<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, HashSet<string>, StateOptions?, CancellationToken>(
                (key, value, ttl, ct) => savedConnections = value)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(
            sessionIdStr, accountId, roles: null, authorizations: null);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(sessionId, response.SessionId);
        Assert.Contains("registered", response.Message);

        // Verify session was added to activeConnections
        Assert.NotNull(savedConnections);
        Assert.Contains(sessionIdStr, savedConnections!);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_PreservesExistingConnections()
    {
        // Arrange
        var service = CreateService();
        var newSessionId = Guid.NewGuid();
        var newSessionIdStr = newSessionId.ToString();
        var accountId = "account-002";
        var existingSessionId = Guid.NewGuid();
        var existingSessionIdStr = existingSessionId.ToString();

        // Setup activeConnections with an existing session
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { existingSessionIdStr });

        // Setup activeSessions
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { existingSessionIdStr });

        // Setup registered services
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup session states
        var statesKey = string.Format(SESSION_STATES_KEY, newSessionIdStr);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, newSessionIdStr);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        HashSet<string>? savedConnections = null;
        _mockHashSetStore
            .Setup(s => s.SaveAsync(
                ACTIVE_CONNECTIONS_KEY,
                It.IsAny<HashSet<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, HashSet<string>, StateOptions?, CancellationToken>(
                (key, value, ttl, ct) => savedConnections = value)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(newSessionIdStr, accountId, roles: null, authorizations: null);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.True(response?.Success);

        // Verify both sessions are in activeConnections
        Assert.NotNull(savedConnections);
        Assert.Contains(existingSessionIdStr, savedConnections!);
        Assert.Contains(newSessionIdStr, savedConnections!);
        Assert.Equal(2, savedConnections!.Count);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_DoesNotDuplicateExistingSession()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();
        var accountId = "account-003";

        // Setup activeConnections with the same session already present
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { sessionId });

        // Setup activeSessions
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { sessionId });

        // Setup registered services
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "user" });

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 1 });

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(sessionId, accountId, roles: null, authorizations: null);

        // Assert - should succeed but not duplicate
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.True(response?.Success);

        // Verify SaveStateAsync was NOT called for activeConnections (no change needed)
        _mockHashSetStore.Verify(s => s.SaveAsync(
            ACTIVE_CONNECTIONS_KEY,
            It.IsAny<HashSet<string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_PublishesSessionCapabilitiesEvent()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();
        var accountId = "account-004";

        // Setup empty activeConnections
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup activeSessions
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(sessionId, accountId, roles: null, authorizations: null);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.True(response?.Success);

        // Verify SessionCapabilitiesEvent was published via client event publisher
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            sessionId,
            It.Is<BaseClientEvent>(e => e is SessionCapabilitiesEvent),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_AlsoAddsToActiveSessions()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();
        var accountId = "account-005";

        // Setup empty activeConnections
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDictObjectStore
            .Setup(s => s.GetAsync(permissionsKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Track what gets saved to activeSessions
        HashSet<string>? savedSessions = null;
        _mockHashSetStore
            .Setup(s => s.SaveAsync(
                ACTIVE_SESSIONS_KEY,
                It.IsAny<HashSet<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, HashSet<string>, StateOptions?, CancellationToken>(
                (key, value, ttl, ct) => savedSessions = value)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(sessionId, accountId, roles: null, authorizations: null);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify session was added to activeSessions
        Assert.NotNull(savedSessions);
        Assert.Contains(sessionId, savedSessions!);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_WithRoles_StoresRoleInSessionStates()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();
        var accountId = "account-roles-001";
        var roles = new List<string> { "user", "admin" };

        // Setup empty activeConnections
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty session states (will be populated)
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

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
        Assert.True(response.Success);

        // Verify role was stored in session states
        // Admin should take priority over user (highest priority role wins)
        Assert.NotNull(savedSessionStates);
        Assert.True(savedSessionStates!.ContainsKey("role"), "Session states should contain 'role' key");
        Assert.Equal("admin", savedSessionStates["role"]); // Admin is highest priority
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_WithUserRole_StoresUserRole()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();
        var accountId = "account-roles-002";
        var roles = new List<string> { "user" };

        // Setup empty activeConnections
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

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
        Assert.NotNull(savedSessionStates);
        Assert.Equal("user", savedSessionStates!["role"]);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_WithNoRoles_StoresAnonymousRole()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();
        var accountId = "account-roles-003";

        // Setup empty activeConnections
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

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

        // Act - no roles or authorizations
        var (statusCode, response) = await service.HandleSessionConnectedAsync(
            sessionId, accountId, roles: null, authorizations: null);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedSessionStates);
        Assert.Equal("anonymous", savedSessionStates!["role"]); // Default role when none provided
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_WithAuthorizations_StoresAuthorizationsInSessionStates()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();
        var accountId = "account-auth-001";
        var authorizations = new List<string> { "arcadia:authorized", "omega:registered" };

        // Setup empty activeConnections
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

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

        // Authorization states should be stored as serviceId=state
        // Format: "arcadia:authorized" -> sessionStates["arcadia"] = "authorized"
        Assert.True(savedSessionStates!.ContainsKey("arcadia"), "Should have 'arcadia' authorization state");
        Assert.Equal("authorized", savedSessionStates["arcadia"]);

        Assert.True(savedSessionStates.ContainsKey("omega"), "Should have 'omega' authorization state");
        Assert.Equal("registered", savedSessionStates["omega"]);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_WithRolesAndAuthorizations_StoresBoth()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();
        var accountId = "account-both-001";
        var roles = new List<string> { "admin" };
        var authorizations = new List<string> { "arcadia:authorized" };

        // Setup empty activeConnections
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

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
            sessionId, accountId, roles: roles, authorizations: authorizations);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedSessionStates);

        // Should have both role and authorization state
        Assert.Equal("admin", savedSessionStates!["role"]);
        Assert.Equal("authorized", savedSessionStates["arcadia"]);
    }

    #endregion

    #region Phase 6: Session Disconnection Tests

    [Fact]
    public async Task HandleSessionDisconnectedAsync_RemovesSessionFromActiveConnections()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();

        // Setup activeConnections with the session
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { sessionId, "other-session" });

        // Track what gets saved
        HashSet<string>? savedConnections = null;
        _mockHashSetStore
            .Setup(s => s.SaveAsync(
                ACTIVE_CONNECTIONS_KEY,
                It.IsAny<HashSet<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, HashSet<string>, StateOptions?, CancellationToken>(
                (key, value, ttl, ct) => savedConnections = value)
            .ReturnsAsync("etag");

        // Act - reconnectable = true (just removes from connections, keeps state)
        var (statusCode, response) = await service.HandleSessionDisconnectedAsync(sessionId, reconnectable: true);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.True(response?.Success);
        Assert.Contains("reconnectable", response?.Message ?? "");

        // Verify session was removed from activeConnections
        Assert.NotNull(savedConnections);
        Assert.DoesNotContain(sessionId, savedConnections!);
        Assert.Contains("other-session", savedConnections!);
    }

    [Fact]
    public async Task HandleSessionDisconnectedAsync_Reconnectable_KeepsActiveSessions()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();

        // Setup activeConnections with the session
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { sessionId });

        // Act - reconnectable = true
        var (statusCode, response) = await service.HandleSessionDisconnectedAsync(sessionId, reconnectable: true);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify activeSessions was NOT modified (reconnectable sessions keep their state)
        _mockHashSetStore.Verify(s => s.GetAsync(
            ACTIVE_SESSIONS_KEY,
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSessionDisconnectedAsync_NotReconnectable_ClearsActiveSessions()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();

        // Setup activeConnections with the session
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { sessionId });

        // Setup activeSessions with the session
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { sessionId, "other-session" });

        // Setup for ClearSessionStateAsync (called internally)
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDictStringStore
            .Setup(s => s.GetAsync(statesKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Track what gets saved to activeSessions
        HashSet<string>? savedSessions = null;
        _mockHashSetStore
            .Setup(s => s.SaveAsync(
                ACTIVE_SESSIONS_KEY,
                It.IsAny<HashSet<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, HashSet<string>, StateOptions?, CancellationToken>(
                (key, value, ttl, ct) => savedSessions = value)
            .ReturnsAsync("etag");

        // Act - reconnectable = false (clears all state)
        var (statusCode, response) = await service.HandleSessionDisconnectedAsync(sessionId, reconnectable: false);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.True(response?.Success);
        Assert.Contains("cleared", response?.Message ?? "");

        // Verify session was removed from activeSessions
        Assert.NotNull(savedSessions);
        Assert.DoesNotContain(sessionId, savedSessions!);
        Assert.Contains("other-session", savedSessions!);
    }

    [Fact]
    public async Task HandleSessionDisconnectedAsync_SessionNotInConnections_ReturnsSuccess()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();

        // Setup activeConnections without the session
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "other-session" });

        // Act
        var (statusCode, response) = await service.HandleSessionDisconnectedAsync(sessionId, reconnectable: true);

        // Assert - should still succeed (idempotent)
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.True(response?.Success);

        // Verify SaveStateAsync was NOT called (no change needed)
        _mockHashSetStore.Verify(s => s.SaveAsync(
            ACTIVE_CONNECTIONS_KEY,
            It.IsAny<HashSet<string>>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSessionDisconnectedAsync_EmptyConnections_ReturnsSuccess()
    {
        // Arrange
        var service = CreateService();
        var sessionId = Guid.NewGuid().ToString();

        // Setup empty activeConnections
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Act
        var (statusCode, response) = await service.HandleSessionDisconnectedAsync(sessionId, reconnectable: true);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.True(response?.Success);
    }

    #endregion

    #region Phase 6: RegisterServicePermissionsAsync ActiveConnections Tests

    [Fact]
    public async Task RegisterServicePermissionsAsync_PublishesOnlyToActiveConnections()
    {
        // Arrange
        var service = CreateService();
        var session1 = Guid.NewGuid().ToString();
        var session2 = Guid.NewGuid().ToString();
        var session3 = Guid.NewGuid().ToString();

        // Set up a successful lock
        var lockResponse = new TestLockResponse { Success = true };
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockResponse);

        // Setup empty stored hash (first registration)
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Setup registered services (empty initially, will be updated)
        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup activeSessions with 3 sessions
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { session1, session2, session3 });

        // Setup activeConnections with only 1 session (the connected one)
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { session1 });

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
                    ["user"] = new System.Collections.ObjectModel.Collection<string> { "GET:/test" }
                }
            }
        };

        // Act
        var (statusCode, response) = await service.RegisterServicePermissionsAsync(permissions);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.True(response?.Success);

        // Verify capability refresh was published to connected session (session1)
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            session1,
            It.Is<BaseClientEvent>(e => e is SessionCapabilitiesEvent),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // CRITICAL: Capability refresh should NOT be published to non-connected sessions
        // Publishing to sessions without WebSocket connections causes RabbitMQ exchange not_found errors
        // which crash the entire pub/sub channel. This is the core bug Phase 6 was designed to fix.
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            session2,
            It.IsAny<SessionCapabilitiesEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);

        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            session3,
            It.IsAny<SessionCapabilitiesEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegisterServicePermissionsAsync_EmptyActiveConnections_DoesNotPublish()
    {
        // Arrange
        var service = CreateService();

        var lockResponse = new TestLockResponse { Success = true };
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(lockResponse);

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockHashSetStore
            .Setup(s => s.GetAsync(REGISTERED_SERVICES_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Empty activeSessions
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_SESSIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Empty activeConnections
        _mockHashSetStore
            .Setup(s => s.GetAsync(ACTIVE_CONNECTIONS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        var permissions = new ServicePermissionMatrix
        {
            ServiceId = "test-service",
            Version = "1.0.0",
            Permissions = new Dictionary<string, StatePermissions>
            {
                ["default"] = new StatePermissions
                {
                    ["user"] = new System.Collections.ObjectModel.Collection<string> { "GET:/test" }
                }
            }
        };

        // Act
        var (statusCode, response) = await service.RegisterServicePermissionsAsync(permissions);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.True(response?.Success);

        // Verify no capability events were published
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            It.IsAny<string>(),
            It.IsAny<SessionCapabilitiesEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion
}
