using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Permissions;
using BeyondImmersion.BannouService.Services;
using Dapr.Client;
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
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<IErrorEventEmitter> _mockErrorEmitter;
    private readonly Mock<IClientEventPublisher> _mockClientEventPublisher;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    // State store constants (must match PermissionsService)
    private const string STATE_STORE = "permissions-store";
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
        _mockDaprClient = new Mock<DaprClient>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockErrorEmitter = new Mock<IErrorEventEmitter>();
        _mockClientEventPublisher = new Mock<IClientEventPublisher>();
        _mockEventConsumer = new Mock<IEventConsumer>();

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
            _mockDaprClient.Object,
            _mockLockProvider.Object,
            _mockErrorEmitter.Object,
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
    public async Task RegisterServicePermissionsAsync_LockAcquisitionFails_ReturnsInternalServerError()
    {
        // Arrange
        var service = CreateService();

        // Set up lock to fail
        var failedLockResponse = new TestLockResponse { Success = false };
        _mockLockProvider
            .Setup(l => l.LockAsync(
                "permissions-store", // Changed from "lockstore" to match new Redis-based implementation
                "registered_services_lock",
                It.IsAny<string>(),
                30,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLockResponse);

        // Set up empty state for the idempotent check
        _mockDaprClient
            .Setup(d => d.GetStateAsync<string?>(
                STATE_STORE,
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(HashSet<string>)!);

        // Set up state transaction execution (will never be called, but good practice)
        _mockDaprClient
            .Setup(d => d.ExecuteStateTransactionAsync(
                STATE_STORE,
                It.IsAny<IReadOnlyList<StateTransactionRequest>>(),
                null,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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

        // Set up empty existing state (using default! to satisfy Moq's nullability requirements)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(HashSet<string>)!);

        // Set up empty existing hash
        _mockDaprClient
            .Setup(d => d.GetStateAsync<string?>(
                STATE_STORE,
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Set up state transaction execution
        _mockDaprClient
            .Setup(d => d.ExecuteStateTransactionAsync(
                STATE_STORE,
                It.IsAny<IReadOnlyList<StateTransactionRequest>>(),
                null,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Set up save state operations for different types
        _mockDaprClient
            .Setup(d => d.SaveStateAsync<HashSet<string>>(
                STATE_STORE,
                It.IsAny<string>(),
                It.IsAny<HashSet<string>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockDaprClient
            .Setup(d => d.SaveStateAsync<string>(
                STATE_STORE,
                It.IsAny<string>(),
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockDaprClient
            .Setup(d => d.SaveStateAsync<object>(
                STATE_STORE,
                It.IsAny<string>(),
                It.IsAny<object>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Set up event publishing
        _mockDaprClient
            .Setup(d => d.PublishEventAsync(
                "bannou-pubsub",
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Set up distributed lock to succeed
        // Create a test lock response that properly handles disposal without needing DaprClient
        var lockResponse = new TestLockResponse { Success = true };
        _mockLockProvider
            .Setup(l => l.LockAsync(
                "permissions-store", // Changed from "lockstore" to match new Redis-based implementation
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
        Assert.True(statusCode == StatusCodes.OK, $"Expected OK but got {statusCode}. Exception: {capturedException?.GetType().Name}: {capturedException?.Message}\nStack: {capturedException?.StackTrace}");
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal("orchestrator", response.ServiceId);

        // Verify state transaction was executed (stores permission matrix)
        _mockDaprClient.Verify(d => d.ExecuteStateTransactionAsync(
            STATE_STORE,
            It.IsAny<IReadOnlyList<StateTransactionRequest>>(),
            null,
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify registered services list was updated at least once with orchestrator
        _mockDaprClient.Verify(d => d.SaveStateAsync(
            STATE_STORE,
            REGISTERED_SERVICES_KEY,
            It.Is<HashSet<string>>(s => s.Contains("orchestrator")),
            null,
            null,
            It.IsAny<CancellationToken>()), Times.AtLeastOnce());
    }

    [Fact]
    public async Task UpdateSessionRoleAsync_AddsSessionToActiveList()
    {
        // Arrange
        var service = CreateService();
        var sessionId = "test-session-002";

        // Set up empty session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                statesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Set up empty active sessions list
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Set up empty registered services
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Set up empty session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
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

        // Verify session was added to active sessions via transaction
        _mockDaprClient.Verify(d => d.ExecuteStateTransactionAsync(
            STATE_STORE,
            It.Is<IReadOnlyList<StateTransactionRequest>>(requests =>
                requests.Any(r => r.Key == statesKey)),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateSessionRoleAsync_IncludesLowerRoleEndpoints()
    {
        // Arrange
        var service = CreateService();
        var sessionId = "session-hierarchy";
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);

        // Empty initial state/active sessions/permissions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE, statesKey, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE, ACTIVE_SESSIONS_KEY, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE, permissionsKey, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Registered services
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE, REGISTERED_SERVICES_KEY, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "svc" });

        // Permission matrix lookups per role (developer should inherit user)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.Is<string>(key => key.Contains("permissions:svc:default:user")),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "GET:/user" });

        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.Is<string>(key => key.Contains("permissions:svc:default:developer")),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "GET:/dev" });

        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.Is<string>(key => key.Contains("permissions:svc:default:anonymous")),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        Dictionary<string, object>? savedPermissions = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                permissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, object>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, value, opt, meta, ct) => savedPermissions = value)
            .Returns(Task.CompletedTask);

        _mockDaprClient
            .Setup(d => d.PublishEventAsync(
                "bannou-pubsub",
                "permissions.capabilities-updated",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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
        var sessionId = "session-state-role";
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);

        // Session has role=user and game-session:in_game state
        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["game-session"] = "in_game"
        };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE, statesKey, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE, REGISTERED_SERVICES_KEY, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "svc" });

        // Endpoint gated by game-session:in_game + role user
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.Is<string>(k => k.Contains("permissions:svc:game-session:in_game:user")),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "POST:/secure" });

        // No default endpoints
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.Is<string>(k => k.Contains("permissions:svc:default:user")),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        Dictionary<string, object>? saved = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                permissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, object>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, val, opt, meta, ct) => saved = val)
            .Returns(Task.CompletedTask);

        _mockDaprClient
            .Setup(d => d.PublishEventAsync(
                "bannou-pubsub",
                "permissions.capabilities-updated",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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

    [Fact]
    public async Task RecompileSessionPermissions_FindsRegisteredServices()
    {
        // Arrange
        var service = CreateService();
        var sessionId = "test-session-003";

        // Pre-populate registered services with "orchestrator"
        var registeredServices = new HashSet<string> { "orchestrator" };
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(registeredServices);

        // Pre-populate permission matrix: permissions:orchestrator:default:admin
        var adminEndpoints = new HashSet<string> { "GET:/orchestrator/health", "POST:/orchestrator/deploy" };
        var adminMatrixKey = string.Format(PERMISSION_MATRIX_KEY, "orchestrator", "default", "admin");
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                adminMatrixKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminEndpoints);

        // Set up session states with admin role
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                statesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "admin" });

        // Set up empty active sessions list
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Set up existing session permissions for version tracking
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
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
        _mockDaprClient.Verify(d => d.SaveStateAsync(
            STATE_STORE,
            permissionsKey,
            It.Is<Dictionary<string, object>>(data =>
                data.ContainsKey("orchestrator")),
            null,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_ReturnsCompiledPermissions()
    {
        // Arrange
        var service = CreateService();
        var sessionId = "test-session-004";

        // Pre-populate compiled permissions in state store
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
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
        var sessionId = "test-session-005";

        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(Dictionary<string, object>)!);

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
        var adminSessionId = "admin-session-006";
        var userSessionId = "user-session-007";

        // Set up registered services
        var registeredServices = new HashSet<string> { "orchestrator" };
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(registeredServices);

        // Admin-only endpoints at permissions:orchestrator:default:admin
        var adminEndpoints = new HashSet<string> { "GET:/orchestrator/health", "POST:/orchestrator/deploy" };
        var adminMatrixKey = string.Format(PERMISSION_MATRIX_KEY, "orchestrator", "default", "admin");
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                adminMatrixKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminEndpoints);

        // User endpoints - empty for orchestrator (no user access)
        var userMatrixKey = string.Format(PERMISSION_MATRIX_KEY, "orchestrator", "default", "user");
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                userMatrixKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(HashSet<string>)!);

        // Set up admin session states
        var adminStatesKey = string.Format(SESSION_STATES_KEY, adminSessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                adminStatesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "admin" });

        // Set up user session states
        var userStatesKey = string.Format(SESSION_STATES_KEY, userSessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                userStatesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "user" });

        // Set up empty active sessions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Set up empty permissions for version tracking
        var adminPermissionsKey = string.Format(SESSION_PERMISSIONS_KEY, adminSessionId);
        var userPermissionsKey = string.Format(SESSION_PERMISSIONS_KEY, userSessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                adminPermissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                userPermissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 0 });

        // Track what permissions get saved
        Dictionary<string, object>? savedAdminPermissions = null;
        Dictionary<string, object>? savedUserPermissions = null;

        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                adminPermissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, object>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedAdminPermissions = data);

        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                userPermissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, object>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedUserPermissions = data);

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
        var sessionId = "test-session-008";

        // Pre-populate session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        var compiledPermissions = new Dictionary<string, object>
        {
            ["orchestrator"] = BannouJson.SerializeToElement(new List<string>
            {
                "GET:/orchestrator/health",
                "POST:/orchestrator/deploy"
            })
        };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
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
        var sessionId = "test-session-009";

        // Pre-populate session permissions with limited access
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        var compiledPermissions = new Dictionary<string, object>
        {
            ["accounts"] = BannouJson.SerializeToElement(new List<string>
            {
                "GET:/accounts/profile"
            })
        };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
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
        var sessionId = "session-connect-001";
        var accountId = "account-001";

        // Setup empty activeConnections
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty registered services (for recompile)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup session states for recompile
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                statesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup session permissions for recompile
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Track what gets saved to activeConnections
        HashSet<string>? savedConnections = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                It.IsAny<HashSet<string>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, HashSet<string>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, value, opt, meta, ct) => savedConnections = value)
            .Returns(Task.CompletedTask);

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(
            sessionId, accountId, roles: null, authorizations: null);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(sessionId, response.SessionId);
        Assert.Contains("registered", response.Message);

        // Verify session was added to activeConnections
        Assert.NotNull(savedConnections);
        Assert.Contains(sessionId, savedConnections!);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_PreservesExistingConnections()
    {
        // Arrange
        var service = CreateService();
        var newSessionId = "session-connect-002";
        var accountId = "account-002";
        var existingSessionId = "session-existing-001";

        // Setup activeConnections with an existing session
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { existingSessionId });

        // Setup activeSessions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { existingSessionId });

        // Setup registered services
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup session states
        var statesKey = string.Format(SESSION_STATES_KEY, newSessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                statesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, newSessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        HashSet<string>? savedConnections = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                It.IsAny<HashSet<string>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, HashSet<string>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, value, opt, meta, ct) => savedConnections = value)
            .Returns(Task.CompletedTask);

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(newSessionId, accountId, roles: null, authorizations: null);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.True(response?.Success);

        // Verify both sessions are in activeConnections
        Assert.NotNull(savedConnections);
        Assert.Contains(existingSessionId, savedConnections!);
        Assert.Contains(newSessionId, savedConnections!);
        Assert.Equal(2, savedConnections!.Count);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_DoesNotDuplicateExistingSession()
    {
        // Arrange
        var service = CreateService();
        var sessionId = "session-connect-003";
        var accountId = "account-003";

        // Setup activeConnections with the same session already present
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { sessionId });

        // Setup activeSessions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { sessionId });

        // Setup registered services
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                statesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["role"] = "user" });

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object> { ["version"] = 1 });

        // Act
        var (statusCode, response) = await service.HandleSessionConnectedAsync(sessionId, accountId, roles: null, authorizations: null);

        // Assert - should succeed but not duplicate
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.True(response?.Success);

        // Verify SaveStateAsync was NOT called for activeConnections (no change needed)
        _mockDaprClient.Verify(d => d.SaveStateAsync(
            STATE_STORE,
            ACTIVE_CONNECTIONS_KEY,
            It.IsAny<HashSet<string>>(),
            null,
            null,
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSessionConnectedAsync_PublishesSessionCapabilitiesEvent()
    {
        // Arrange
        var service = CreateService();
        var sessionId = "session-connect-004";
        var accountId = "account-004";

        // Setup empty activeConnections
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup activeSessions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                statesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
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
        var sessionId = "session-connect-005";
        var accountId = "account-005";

        // Setup empty activeConnections
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                statesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Track what gets saved to activeSessions
        HashSet<string>? savedSessions = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                It.IsAny<HashSet<string>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, HashSet<string>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, value, opt, meta, ct) => savedSessions = value)
            .Returns(Task.CompletedTask);

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
        var sessionId = "session-connect-roles-001";
        var accountId = "account-roles-001";
        var roles = new List<string> { "user", "admin" };

        // Setup empty activeConnections
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty session states (will be populated)
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                statesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Track what gets saved to session states
        Dictionary<string, string>? savedSessionStates = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                statesKey,
                It.IsAny<Dictionary<string, string>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, string>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, value, opt, meta, ct) => savedSessionStates = value)
            .Returns(Task.CompletedTask);

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
        var sessionId = "session-connect-roles-002";
        var accountId = "account-roles-002";
        var roles = new List<string> { "user" };

        // Setup empty activeConnections
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                statesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Track what gets saved to session states
        Dictionary<string, string>? savedSessionStates = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                statesKey,
                It.IsAny<Dictionary<string, string>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, string>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, value, opt, meta, ct) => savedSessionStates = value)
            .Returns(Task.CompletedTask);

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
        var sessionId = "session-connect-roles-003";
        var accountId = "account-roles-003";

        // Setup empty activeConnections
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                statesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Track what gets saved to session states
        Dictionary<string, string>? savedSessionStates = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                statesKey,
                It.IsAny<Dictionary<string, string>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, string>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, value, opt, meta, ct) => savedSessionStates = value)
            .Returns(Task.CompletedTask);

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
        var sessionId = "session-connect-auth-001";
        var accountId = "account-auth-001";
        var authorizations = new List<string> { "arcadia:authorized", "omega:registered" };

        // Setup empty activeConnections
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                statesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Track what gets saved to session states
        Dictionary<string, string>? savedSessionStates = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                statesKey,
                It.IsAny<Dictionary<string, string>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, string>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, value, opt, meta, ct) => savedSessionStates = value)
            .Returns(Task.CompletedTask);

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
        var sessionId = "session-connect-both-001";
        var accountId = "account-both-001";
        var roles = new List<string> { "admin" };
        var authorizations = new List<string> { "arcadia:authorized" };

        // Setup empty activeConnections
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty activeSessions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup registered services
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup empty session states
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                statesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Setup session permissions
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE,
                permissionsKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Track what gets saved to session states
        Dictionary<string, string>? savedSessionStates = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                statesKey,
                It.IsAny<Dictionary<string, string>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, string>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, value, opt, meta, ct) => savedSessionStates = value)
            .Returns(Task.CompletedTask);

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
        var sessionId = "session-disconnect-001";

        // Setup activeConnections with the session
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { sessionId, "other-session" });

        // Track what gets saved
        HashSet<string>? savedConnections = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                It.IsAny<HashSet<string>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, HashSet<string>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, value, opt, meta, ct) => savedConnections = value)
            .Returns(Task.CompletedTask);

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
        var sessionId = "session-disconnect-002";

        // Setup activeConnections with the session
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { sessionId });

        // Act - reconnectable = true
        var (statusCode, response) = await service.HandleSessionDisconnectedAsync(sessionId, reconnectable: true);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify activeSessions was NOT modified (reconnectable sessions keep their state)
        _mockDaprClient.Verify(d => d.GetStateAsync<HashSet<string>>(
            STATE_STORE,
            ACTIVE_SESSIONS_KEY,
            null,
            null,
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSessionDisconnectedAsync_NotReconnectable_ClearsActiveSessions()
    {
        // Arrange
        var service = CreateService();
        var sessionId = "session-disconnect-003";

        // Setup activeConnections with the session
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { sessionId });

        // Setup activeSessions with the session
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { sessionId, "other-session" });

        // Setup for ClearSessionStateAsync (called internally)
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE,
                statesKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

        // Track what gets saved to activeSessions
        HashSet<string>? savedSessions = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                It.IsAny<HashSet<string>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, HashSet<string>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, value, opt, meta, ct) => savedSessions = value)
            .Returns(Task.CompletedTask);

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
        var sessionId = "session-disconnect-004";

        // Setup activeConnections without the session
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "other-session" });

        // Act
        var (statusCode, response) = await service.HandleSessionDisconnectedAsync(sessionId, reconnectable: true);

        // Assert - should still succeed (idempotent)
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.True(response?.Success);

        // Verify SaveStateAsync was NOT called (no change needed)
        _mockDaprClient.Verify(d => d.SaveStateAsync(
            STATE_STORE,
            ACTIVE_CONNECTIONS_KEY,
            It.IsAny<HashSet<string>>(),
            null,
            null,
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleSessionDisconnectedAsync_EmptyConnections_ReturnsSuccess()
    {
        // Arrange
        var service = CreateService();
        var sessionId = "session-disconnect-005";

        // Setup empty activeConnections
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<string?>(
                STATE_STORE,
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Setup registered services (empty initially, will be updated)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Setup activeSessions with 3 sessions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "session-1", "session-2", "session-3" });

        // Setup activeConnections with only 1 session (the connected one)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "session-1" });

        // Setup session states/permissions for recompile
        foreach (var sessionId in new[] { "session-1", "session-2", "session-3" })
        {
            var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
            _mockDaprClient
                .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                    STATE_STORE,
                    statesKey,
                    null,
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, string> { ["role"] = "user" });

            var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
            _mockDaprClient
                .Setup(d => d.GetStateAsync<Dictionary<string, object>>(
                    STATE_STORE,
                    permissionsKey,
                    null,
                    null,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, object>());
        }

        // Setup state transactions
        _mockDaprClient
            .Setup(d => d.ExecuteStateTransactionAsync(
                STATE_STORE,
                It.IsAny<IReadOnlyList<StateTransactionRequest>>(),
                null,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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

        // Verify capability refresh was published to connected session (session-1)
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            "session-1",
            It.Is<BaseClientEvent>(e => e is SessionCapabilitiesEvent),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // CRITICAL: Capability refresh should NOT be published to non-connected sessions
        // Publishing to sessions without WebSocket connections causes RabbitMQ exchange not_found errors
        // which crash the entire pub/sub channel. This is the core bug Phase 6 was designed to fix.
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            "session-2",
            It.IsAny<SessionCapabilitiesEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);

        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            "session-3",
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<string?>(
                STATE_STORE,
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                REGISTERED_SERVICES_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Empty activeSessions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_SESSIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Empty activeConnections
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                ACTIVE_CONNECTIONS_KEY,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        _mockDaprClient
            .Setup(d => d.ExecuteStateTransactionAsync(
                STATE_STORE,
                It.IsAny<IReadOnlyList<StateTransactionRequest>>(),
                null,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var permissions = new ServicePermissionMatrix
        {
            ServiceId = "empty-test-service",
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

        // Verify no capability refresh events were published (no connected sessions)
        _mockClientEventPublisher.Verify(p => p.PublishToSessionAsync(
            It.IsAny<string>(),
            It.IsAny<SessionCapabilitiesEvent>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region State Key Matching Tests

    /// <summary>
    /// Critical regression test: Verifies that same-service state keys match between
    /// registration and lookup. The BuildPermissionMatrix must use just the state value
    /// (e.g., "ringing") for same-service states, not "service:state" (e.g., "voice:ringing").
    ///
    /// This test was added after discovering a bug where:
    /// - Registration stored at: permissions:voice:voice:ringing:user (wrong)
    /// - Lookup searched for: permissions:voice:ringing:user (correct)
    ///
    /// The fix ensures both use the same key format.
    /// </summary>
    [Fact]
    public async Task RecompilePermissions_SameServiceStateKey_MatchesRegistration()
    {
        // Arrange
        var service = CreateService();
        var sessionId = "session-state-key-matching";
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);

        // Session has voice=ringing state (same-service state for voice service)
        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["voice"] = "ringing"  // voice service sets voice:ringing state
        };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE, statesKey, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE, REGISTERED_SERVICES_KEY, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "voice" });

        // CRITICAL: The state key for same-service must be just "ringing", not "voice:ringing"
        // This is the key that BuildPermissionMatrix should produce for voice service
        // registering permissions for voice:ringing state
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.Is<string>(k => k == "permissions:voice:ringing:user"),  // Same-service: just state value
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "POST:/voice/peer/answer" });

        // Default endpoints (no state required)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.Is<string>(k => k == "permissions:voice:default:user"),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        Dictionary<string, object>? saved = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                permissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, object>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, val, opt, meta, ct) => saved = val)
            .Returns(Task.CompletedTask);

        _mockDaprClient
            .Setup(d => d.PublishEventAsync(
                "bannou-pubsub",
                "permissions.capabilities-updated",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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
        var sessionId = "session-cross-service-state";
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);

        // Session has voice=ringing state
        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["voice"] = "ringing"
        };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE, statesKey, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE, REGISTERED_SERVICES_KEY, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "game-session" });

        // Cross-service: game-session service checking voice state requires "voice:ringing" key
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.Is<string>(k => k == "permissions:game-session:voice:ringing:user"),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "POST:/sessions/voice-enabled-action" });

        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.Is<string>(k => k == "permissions:game-session:default:user"),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        Dictionary<string, object>? saved = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                permissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, object>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, val, opt, meta, ct) => saved = val)
            .Returns(Task.CompletedTask);

        _mockDaprClient
            .Setup(d => d.PublishEventAsync(
                "bannou-pubsub",
                "permissions.capabilities-updated",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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
        var sessionId = "session-game-in-game";
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);

        // Session has game-session=in_game state (set when player joins)
        var sessionStates = new Dictionary<string, string>
        {
            ["role"] = "user",
            ["game-session"] = "in_game"
        };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE, statesKey, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionStates);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE, REGISTERED_SERVICES_KEY, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "game-session" });

        // Same-service state key: just "in_game" (not "game-session:in_game")
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.Is<string>(k => k == "permissions:game-session:in_game:user"),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>
            {
                "POST:/sessions/leave",
                "POST:/sessions/chat",
                "POST:/sessions/actions"
            });

        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.Is<string>(k => k == "permissions:game-session:default:user"),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>
            {
                "GET:/sessions/list",
                "POST:/sessions/create",
                "POST:/sessions/get",
                "POST:/sessions/join"
            });

        Dictionary<string, object>? saved = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                permissionsKey,
                It.IsAny<Dictionary<string, object>>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, object>, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, val, opt, meta, ct) => saved = val)
            .Returns(Task.CompletedTask);

        _mockDaprClient
            .Setup(d => d.PublishEventAsync(
                "bannou-pubsub",
                "permissions.capabilities-updated",
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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

    #endregion
}
