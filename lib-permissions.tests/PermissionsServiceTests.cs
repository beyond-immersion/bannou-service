using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Permissions;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace BeyondImmersion.BannouService.Permissions.Tests;

/// <summary>
/// Unit tests for PermissionsService.
/// Tests verify permission registration, session management, capability compilation, and role-based access.
/// </summary>
public class PermissionsServiceTests
{
    private readonly Mock<ILogger<PermissionsService>> _mockLogger;
    private readonly Mock<PermissionsServiceConfiguration> _mockConfiguration;
    private readonly Mock<DaprClient> _mockDaprClient;

    // State store constants (must match PermissionsService)
    private const string STATE_STORE = "permissions-store";
    private const string ACTIVE_SESSIONS_KEY = "active_sessions";
    private const string REGISTERED_SERVICES_KEY = "registered_services";
    private const string SESSION_STATES_KEY = "session:{0}:states";
    private const string SESSION_PERMISSIONS_KEY = "session:{0}:permissions";
    private const string PERMISSION_MATRIX_KEY = "permissions:{0}:{1}:{2}";

    public PermissionsServiceTests()
    {
        _mockLogger = new Mock<ILogger<PermissionsService>>();
        _mockConfiguration = new Mock<PermissionsServiceConfiguration>();
        _mockDaprClient = new Mock<DaprClient>();
    }

    private PermissionsService CreateService()
    {
        return new PermissionsService(
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockDaprClient.Object);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var service = CreateService();
        Assert.NotNull(service);
    }

    [Fact]
    public async Task RegisterServicePermissionsAsync_StoresPermissionMatrix()
    {
        // Arrange
        var service = CreateService();
        var sessionId = "test-session-001";

        // Set up empty existing state
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                It.IsAny<string>(),
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

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

        // Verify state transaction was executed
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

        // Pre-populate permission matrix: permissions:orchestrator:authenticated:admin
        var adminEndpoints = new HashSet<string> { "GET:/orchestrator/health", "POST:/orchestrator/deploy" };
        var adminMatrixKey = string.Format(PERMISSION_MATRIX_KEY, "orchestrator", "authenticated", "admin");
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
            ["orchestrator"] = JsonSerializer.SerializeToElement(new List<string>
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

        // Admin-only endpoints at permissions:orchestrator:authenticated:admin
        var adminEndpoints = new HashSet<string> { "GET:/orchestrator/health", "POST:/orchestrator/deploy" };
        var adminMatrixKey = string.Format(PERMISSION_MATRIX_KEY, "orchestrator", "authenticated", "admin");
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                adminMatrixKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminEndpoints);

        // User endpoints - empty for orchestrator (no user access)
        var userMatrixKey = string.Format(PERMISSION_MATRIX_KEY, "orchestrator", "authenticated", "user");
        _mockDaprClient
            .Setup(d => d.GetStateAsync<HashSet<string>>(
                STATE_STORE,
                userMatrixKey,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((HashSet<string>?)null);

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
            ["orchestrator"] = JsonSerializer.SerializeToElement(new List<string>
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
            ["accounts"] = JsonSerializer.SerializeToElement(new List<string>
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
}
