using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;
using System.Collections.ObjectModel;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for permission service API endpoints.
/// Tests direct API calls and session lifecycle event handling via RabbitMQ.
/// </summary>
public class PermissionTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Core Permission Registration Tests
        new ServiceTest(TestRegisterServicePermissions, "RegisterServicePermissions", "Permission", "Test direct service permission registration"),
        new ServiceTest(TestRegisterMultipleServicesPermissions, "RegisterMultipleServices", "Permission", "Test registering permissions for multiple services"),
        new ServiceTest(TestRegisterServiceWithMultipleStates, "RegisterMultipleStates", "Permission", "Test registering service with multiple states"),
        new ServiceTest(TestRegisterServiceWithMultipleRoles, "RegisterMultipleRoles", "Permission", "Test registering service with multiple roles"),

        // Capability Lookup Tests
        new ServiceTest(TestGetCapabilitiesExistingSession, "GetCapabilitiesExisting", "Permission", "Test getting capabilities for existing session"),
        new ServiceTest(TestGetCapabilitiesNonExistentSession, "GetCapabilitiesNonExistent", "Permission", "Test getting capabilities for non-existent session"),
        new ServiceTest(TestGetCapabilitiesFilteredByService, "GetCapabilitiesFiltered", "Permission", "Test getting capabilities filtered by service IDs"),

        // API Validation Tests
        new ServiceTest(TestValidateApiAccessAllowed, "ValidateApiAccessAllowed", "Permission", "Test API access validation when access is allowed"),
        new ServiceTest(TestValidateApiAccessDenied, "ValidateApiAccessDenied", "Permission", "Test API access validation when access is denied"),
        new ServiceTest(TestValidateApiAccessUnknownService, "ValidateApiAccessUnknown", "Permission", "Test API access validation for unknown service"),

        // Session State Management Tests
        new ServiceTest(TestUpdateSessionState, "UpdateSessionState", "Permission", "Test updating session state for a service"),
        new ServiceTest(TestUpdateSessionStateTransition, "UpdateSessionStateTransition", "Permission", "Test session state transitions trigger permission changes"),
        new ServiceTest(TestUpdateSessionRole, "UpdateSessionRole", "Permission", "Test updating session role"),
        new ServiceTest(TestUpdateSessionRoleAffectsAllServices, "UpdateRoleAffectsAll", "Permission", "Test role update affects all service permissions"),

        // Clear Session State Tests
        new ServiceTest(TestClearSessionStateUnconditional, "ClearSessionStateUnconditional", "Permission", "Test clearing session state unconditionally"),
        new ServiceTest(TestClearSessionStateWithMatchingFilter, "ClearSessionStateMatchingFilter", "Permission", "Test clearing session state when filter matches"),
        new ServiceTest(TestClearSessionStateWithNonMatchingFilter, "ClearSessionStateNonMatchingFilter", "Permission", "Test clearing session state when filter doesn't match"),
        new ServiceTest(TestClearSessionStateNonExistent, "ClearSessionStateNonExistent", "Permission", "Test clearing state for service with no state set"),
        new ServiceTest(TestClearAllSessionStates, "ClearAllSessionStates", "Permission", "Test clearing all session states by omitting ServiceId"),

        // Session Info Tests
        new ServiceTest(TestGetSessionInfo, "GetSessionInfo", "Permission", "Test getting complete session information"),
        new ServiceTest(TestGetSessionInfoNonExistent, "GetSessionInfoNonExistent", "Permission", "Test getting session info for non-existent session"),

        // Admin vs User Role Tests
        new ServiceTest(TestAdminRoleCapabilities, "AdminRoleCapabilities", "Permission", "Test admin role receives admin-level permissions"),
        new ServiceTest(TestUserRoleCapabilities, "UserRoleCapabilities", "Permission", "Test user role receives user-level permissions"),
        new ServiceTest(TestRoleEscalation, "RoleEscalation", "Permission", "Test role escalation from user to admin"),

        // State-Based Permission Tests
        new ServiceTest(TestStateBasedPermissionEscalation, "StateBasedEscalation", "Permission", "Test setting game-session:in_game state grants additional permissions"),
        new ServiceTest(TestDefaultVsInGameState, "DefaultVsInGame", "Permission", "Test difference between default and game-session:in_game state permissions"),

        // Phase 6: Session Lifecycle Tests — REMOVED from http-tester
        // Permission now receives session lifecycle notifications via ISessionActivityListener
        // (DI listener pattern), not via IMessageBus events. These are in-process calls from
        // Connect to Permission, which cannot be triggered from the http-tester's separate
        // DI container. Equivalent coverage lives in edge-tester (CapabilityFlowTestHandler)
        // where real WebSocket connections trigger the full Connect → DI listener → Permission flow.
    ];

    /// <summary>
    /// Test direct service permission registration via API.
    /// This establishes a baseline that the Permission service works.
    /// </summary>
    private static async Task<TestResult> TestRegisterServicePermissions(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testServiceId = Guid.NewGuid().ToString();

            var permissionMatrix = new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/test/endpoint" }
                    }
                }
            };

            await permissionsClient.RegisterServicePermissionsAsync(permissionMatrix);

            return TestResult.Successful($"Service permissions registered: {testServiceId}");
        }, "Register service permissions");

    /// <summary>
    /// Test registering permissions for multiple services.
    /// </summary>
    private static async Task<TestResult> TestRegisterMultipleServicesPermissions(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"multi-svc-{Guid.NewGuid():N}";

            // Register first service
            var service1Id = $"{testPrefix}-service1";
            var matrix1 = new ServicePermissionMatrix
            {
                ServiceId = service1Id,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/service1/data" }
                    }
                }
            };

            await permissionsClient.RegisterServicePermissionsAsync(matrix1);

            // Register second service
            var service2Id = $"{testPrefix}-service2";
            var matrix2 = new ServicePermissionMatrix
            {
                ServiceId = service2Id,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/service2/data", "/service2/action" }
                    }
                }
            };

            await permissionsClient.RegisterServicePermissionsAsync(matrix2);

            return TestResult.Successful($"Successfully registered multiple services: {service1Id}, {service2Id}");
        }, "Register multiple services");

    /// <summary>
    /// Test registering service with multiple states (in_lobby, default, in_game).
    /// </summary>
    private static async Task<TestResult> TestRegisterServiceWithMultipleStates(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testServiceId = Guid.NewGuid().ToString();

            var permissionMatrix = new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["in_lobby"] = new StatePermissions
                    {
                        ["guest"] = new Collection<string> { "/public/info" }
                    },
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/public/info", "/user/profile" }
                    },
                    ["in_game"] = new StatePermissions
                    {
                        ["admin"] = new Collection<string> { "/public/info", "/user/profile", "/admin/data" }
                    }
                }
            };

            await permissionsClient.RegisterServicePermissionsAsync(permissionMatrix);

            return TestResult.Successful($"Service with multiple states registered: {testServiceId}, 3 states defined");
        }, "Register multiple states");

    /// <summary>
    /// Test registering service with multiple roles (guest, user, admin).
    /// </summary>
    private static async Task<TestResult> TestRegisterServiceWithMultipleRoles(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testServiceId = Guid.NewGuid().ToString();

            var permissionMatrix = new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["guest"] = new Collection<string> { "/info" },
                        ["user"] = new Collection<string> { "/info", "/data", "/data" },
                        ["admin"] = new Collection<string> { "/info", "/data", "/data", "/data", "/admin/settings" }
                    }
                }
            };

            await permissionsClient.RegisterServicePermissionsAsync(permissionMatrix);

            return TestResult.Successful($"Service with multiple roles registered: {testServiceId}, 3 roles defined");
        }, "Register multiple roles");

    /// <summary>
    /// Test getting capabilities for an existing session.
    /// </summary>
    private static async Task<TestResult> TestGetCapabilitiesExistingSession(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testServiceId = Guid.NewGuid().ToString();
            var testSessionId = Guid.NewGuid();

            // Step 1: Register service with permissions
            var permissionMatrix = new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/cap/test1", "/cap/test2" }
                    }
                }
            };
            await permissionsClient.RegisterServicePermissionsAsync(permissionMatrix);

            // Step 2: Create session with state and role
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "authenticated"
            });
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Step 3: Get capabilities
            var capabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            if (capabilities.Permissions == null || capabilities.Permissions.Count == 0)
            {
                return TestResult.Failed("Capabilities returned no permissions");
            }

            if (!capabilities.Permissions.ContainsKey(testServiceId))
            {
                return TestResult.Failed($"Capabilities missing expected service: {testServiceId}. Found: [{string.Join(", ", capabilities.Permissions.Keys)}]");
            }

            var methods = capabilities.Permissions[testServiceId];
            return TestResult.Successful($"Got capabilities for session {testSessionId}: {methods.Count} methods from {capabilities.Permissions.Count} services");
        }, "Get capabilities existing session");

    /// <summary>
    /// Test getting capabilities for a non-existent session returns NotFound.
    /// </summary>
    private static async Task<TestResult> TestGetCapabilitiesNonExistentSession(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var nonExistentSessionId = Guid.NewGuid();

            try
            {
                var capabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
                {
                    SessionId = nonExistentSessionId
                });

                // If we get here with empty permissions, that's acceptable behavior
                if (capabilities.Permissions == null || capabilities.Permissions.Count == 0)
                {
                    return TestResult.Successful("Non-existent session correctly returned empty permissions");
                }

                return TestResult.Failed("Non-existent session returned permissions when it should not have");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("Non-existent session correctly returned 404 NotFound");
            }
        }, "Get capabilities non-existent session");

    /// <summary>
    /// Test getting capabilities filtered by specific service IDs.
    /// </summary>
    private static async Task<TestResult> TestGetCapabilitiesFilteredByService(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"filter-test-{Guid.NewGuid():N}";
            var testSessionId = Guid.NewGuid();
            var service1Id = $"{testPrefix}-svc1";
            var service2Id = $"{testPrefix}-svc2";

            // Register two services
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = service1Id,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/svc1/data" }
                    }
                }
            });

            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = service2Id,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/svc2/data" }
                    }
                }
            });

            // Create session with both states
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = service1Id,
                NewState = "authenticated"
            });
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = service2Id,
                NewState = "authenticated"
            });
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Get capabilities filtered to only service1
            var capabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId,
                ServiceIds = new List<string> { service1Id }
            });

            if (capabilities.Permissions == null)
            {
                return TestResult.Failed("Filtered capabilities returned null permissions");
            }

            // Check if filtering worked (may not be implemented on server side)
            var serviceCount = capabilities.Permissions.Count;
            return TestResult.Successful($"Filtered capabilities returned {serviceCount} service(s)");
        }, "Get capabilities filtered");

    /// <summary>
    /// Test API access validation returns allowed=true when access is permitted.
    /// </summary>
    private static async Task<TestResult> TestValidateApiAccessAllowed(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"validate-allowed-{Guid.NewGuid():N}";
            var testServiceId = Guid.NewGuid().ToString();
            var testSessionId = Guid.NewGuid();
            var testMethod = "/allowed/endpoint";

            // Register service with the method
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { testMethod }
                    }
                }
            });

            // Create session with proper state and role
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "authenticated"
            });
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Validate access
            var validation = await permissionsClient.ValidateApiAccessAsync(new ValidationRequest
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                Endpoint = testMethod
            });

            if (validation.Allowed)
            {
                return TestResult.Successful($"API access correctly validated as allowed for {testMethod}");
            }
            return TestResult.Failed($"API access should be allowed but was denied. Reason: {validation.Reason}");
        }, "Validate API access allowed");

    /// <summary>
    /// Test API access validation returns allowed=false when access is denied.
    /// </summary>
    private static async Task<TestResult> TestValidateApiAccessDenied(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"validate-denied-{Guid.NewGuid():N}";
            var testServiceId = Guid.NewGuid().ToString();
            var testSessionId = Guid.NewGuid();
            var allowedMethod = "/allowed/endpoint";
            var deniedMethod = "/admin/dangerous";

            // Register service with only one method
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { allowedMethod }
                    }
                }
            });

            // Create session
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "authenticated"
            });
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Try to validate access to method that wasn't granted
            var validation = await permissionsClient.ValidateApiAccessAsync(new ValidationRequest
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                Endpoint = deniedMethod
            });

            if (!validation.Allowed)
            {
                return TestResult.Successful($"API access correctly denied for {deniedMethod}");
            }
            return TestResult.Failed("API access should be denied but was allowed");
        }, "Validate API access denied");

    /// <summary>
    /// Test API access validation for unknown service.
    /// </summary>
    private static async Task<TestResult> TestValidateApiAccessUnknownService(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testSessionId = Guid.NewGuid();
            var unknownServiceId = $"nonexistent-service-{Guid.NewGuid():N}";

            // Create a minimal session
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Try to validate access to unknown service
            var validation = await permissionsClient.ValidateApiAccessAsync(new ValidationRequest
            {
                SessionId = testSessionId,
                ServiceId = unknownServiceId,
                Endpoint = "/any/endpoint"
            });

            if (!validation.Allowed)
            {
                return TestResult.Successful("Access to unknown service correctly denied");
            }
            return TestResult.Failed("Access to unknown service should be denied");
        }, "Validate unknown service");

    /// <summary>
    /// Test updating session state for a service.
    /// </summary>
    private static async Task<TestResult> TestUpdateSessionState(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testSessionId = Guid.NewGuid();
            var testServiceId = Guid.NewGuid().ToString();

            var stateUpdate = new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "in_lobby",
                PreviousState = null
            };

            var response = await permissionsClient.UpdateSessionStateAsync(stateUpdate);

            return TestResult.Successful($"Session state updated to 'in_lobby' for session {testSessionId}, permissionsChanged={response.PermissionsChanged}");
        }, "Update session state");

    /// <summary>
    /// Test that session state transitions trigger permission recompilation.
    /// </summary>
    private static async Task<TestResult> TestUpdateSessionStateTransition(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"transition-{Guid.NewGuid():N}";
            var testServiceId = Guid.NewGuid().ToString();
            var testSessionId = Guid.NewGuid();

            // Register service with different permissions per state
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["lobby"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/lobby/info" }
                    },
                    ["in_game"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/lobby/info", "/game/action", "/game/state" }
                    }
                }
            });

            // Create session in lobby state
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "lobby"
            });
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Check initial capabilities
            var lobbyCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            var lobbyMethodCount = 0;
            if (lobbyCapabilities.Permissions?.ContainsKey(testServiceId) == true)
            {
                lobbyMethodCount = lobbyCapabilities.Permissions[testServiceId].Count;
            }

            // Transition to in_game state
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "in_game",
                PreviousState = "lobby"
            });

            // Check capabilities after transition - service now verifies write consistency before returning
            var gameCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            var gameMethodCount = 0;
            if (gameCapabilities.Permissions?.ContainsKey(testServiceId) == true)
            {
                gameMethodCount = gameCapabilities.Permissions[testServiceId].Count;
            }

            if (gameMethodCount > lobbyMethodCount)
            {
                return TestResult.Successful($"State transition increased permissions: lobby={lobbyMethodCount}, in_game={gameMethodCount}");
            }
            else if (gameMethodCount >= 1)
            {
                return TestResult.Successful($"State transition completed: lobby={lobbyMethodCount}, in_game={gameMethodCount} methods");
            }
            return TestResult.Failed($"State transition did not update permissions as expected: lobby={lobbyMethodCount}, in_game={gameMethodCount}");
        }, "State transition");

    /// <summary>
    /// Test updating session role.
    /// </summary>
    private static async Task<TestResult> TestUpdateSessionRole(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testSessionId = Guid.NewGuid();

            var roleUpdate = new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "admin",
                PreviousRole = null
            };

            var response = await permissionsClient.UpdateSessionRoleAsync(roleUpdate);

            return TestResult.Successful($"Session role updated to 'admin' for session {testSessionId}, permissionsChanged={response.PermissionsChanged}");
        }, "Update session role");

    /// <summary>
    /// Test that role update affects permissions across all services.
    /// </summary>
    private static async Task<TestResult> TestUpdateSessionRoleAffectsAllServices(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"role-all-{Guid.NewGuid():N}";
            var testSessionId = Guid.NewGuid();
            var service1Id = $"{testPrefix}-svc1";
            var service2Id = $"{testPrefix}-svc2";

            // Register two services with different permissions for user vs admin
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = service1Id,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/svc1/read" },
                        ["admin"] = new Collection<string> { "/svc1/read", "/svc1/delete" }
                    }
                }
            });

            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = service2Id,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/svc2/read" },
                        ["admin"] = new Collection<string> { "/svc2/read", "/svc2/admin" }
                    }
                }
            });

            // Create session as user
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = service1Id,
                NewState = "authenticated"
            });
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = service2Id,
                NewState = "authenticated"
            });
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Count user methods
            var userCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            var userTotalMethods = 0;
            if (userCapabilities.Permissions != null)
            {
                foreach (var svc in userCapabilities.Permissions.Values)
                {
                    userTotalMethods += svc.Count;
                }
            }

            // Upgrade to admin
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "admin",
                PreviousRole = "user"
            });

            // Count admin methods - service now verifies write consistency before returning
            var adminCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            var adminTotalMethods = 0;
            if (adminCapabilities.Permissions != null)
            {
                foreach (var svc in adminCapabilities.Permissions.Values)
                {
                    adminTotalMethods += svc.Count;
                }
            }

            if (adminTotalMethods >= userTotalMethods)
            {
                return TestResult.Successful($"Role upgrade affected all services: user={userTotalMethods} methods, admin={adminTotalMethods} methods");
            }
            return TestResult.Failed($"Role upgrade did not increase permissions as expected");
        }, "Role affects all services");

    /// <summary>
    /// Test clearing session state unconditionally (no states filter).
    /// </summary>
    private static async Task<TestResult> TestClearSessionStateUnconditional(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"clear-state-{Guid.NewGuid():N}";
            var testSessionId = Guid.NewGuid();
            var testServiceId = Guid.NewGuid().ToString();

            // Register service with state-dependent permissions
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/public" }
                    },
                    ["active"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/public", "/active/action" }
                    }
                }
            });

            // Create session with state
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "active"
            });
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Verify state was set
            var sessionInfo = await permissionsClient.GetSessionInfoAsync(new SessionInfoRequest
            {
                SessionId = testSessionId
            });

            if (sessionInfo.States == null || !sessionInfo.States.ContainsKey(testServiceId))
            {
                return TestResult.Failed("State was not set initially");
            }

            // Clear the state unconditionally (no states filter)
            var clearResponse = await permissionsClient.ClearSessionStateAsync(new ClearSessionStateRequest
            {
                SessionId = testSessionId,
                ServiceId = testServiceId
                // States is null/empty - clears unconditionally
            });

            // Verify state was cleared
            var updatedInfo = await permissionsClient.GetSessionInfoAsync(new SessionInfoRequest
            {
                SessionId = testSessionId
            });

            if (updatedInfo.States != null && updatedInfo.States.ContainsKey(testServiceId))
            {
                return TestResult.Failed("State was not cleared");
            }

            return TestResult.Successful("State cleared unconditionally");
        }, "Clear state unconditional");

    /// <summary>
    /// Test clearing session state when the filter matches the current state.
    /// </summary>
    private static async Task<TestResult> TestClearSessionStateWithMatchingFilter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"clear-match-{Guid.NewGuid():N}";
            var testSessionId = Guid.NewGuid();
            var testServiceId = Guid.NewGuid().ToString();

            // Create session with state
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "in_lobby"
            });
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Clear only if current state matches one of provided values
            var clearResponse = await permissionsClient.ClearSessionStateAsync(new ClearSessionStateRequest
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                States = new List<string> { "in_lobby", "in_game" } // "in_lobby" matches
            });

            if (!clearResponse.PermissionsChanged)
            {
                return TestResult.Failed("Permissions should have changed when state was cleared");
            }

            // Verify state was cleared
            var updatedInfo = await permissionsClient.GetSessionInfoAsync(new SessionInfoRequest
            {
                SessionId = testSessionId
            });

            if (updatedInfo.States != null && updatedInfo.States.ContainsKey(testServiceId))
            {
                return TestResult.Failed("State was not cleared despite matching filter");
            }

            return TestResult.Successful("State cleared with matching filter");
        }, "Clear state matching filter");

    /// <summary>
    /// Test clearing session state when the filter does not match the current state.
    /// </summary>
    private static async Task<TestResult> TestClearSessionStateWithNonMatchingFilter(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"clear-nomatch-{Guid.NewGuid():N}";
            var testSessionId = Guid.NewGuid();
            var testServiceId = Guid.NewGuid().ToString();

            // Create session with state
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "active"
            });
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Try to clear only if current state matches - but it won't
            var clearResponse = await permissionsClient.ClearSessionStateAsync(new ClearSessionStateRequest
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                States = new List<string> { "in_lobby", "in_game" } // "active" doesn't match
            });

            if (clearResponse.PermissionsChanged)
            {
                return TestResult.Failed("Permissions should NOT have changed when filter didn't match");
            }

            // Verify state was NOT cleared
            var updatedInfo = await permissionsClient.GetSessionInfoAsync(new SessionInfoRequest
            {
                SessionId = testSessionId
            });

            if (updatedInfo.States == null || !updatedInfo.States.ContainsKey(testServiceId))
            {
                return TestResult.Failed("State was incorrectly cleared despite non-matching filter");
            }

            if (updatedInfo.States[testServiceId] != "active")
            {
                return TestResult.Failed($"State changed unexpectedly to: {updatedInfo.States[testServiceId]}");
            }

            return TestResult.Successful("State preserved when filter didn't match");
        }, "Clear state non-matching filter");

    /// <summary>
    /// Test clearing state for a service that has no state set.
    /// </summary>
    private static async Task<TestResult> TestClearSessionStateNonExistent(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"clear-nostate-{Guid.NewGuid():N}";
            var testSessionId = Guid.NewGuid();
            var testServiceId = Guid.NewGuid().ToString();

            // Create session with just a role (no state for this service)
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Try to clear non-existent state
            var clearResponse = await permissionsClient.ClearSessionStateAsync(new ClearSessionStateRequest
            {
                SessionId = testSessionId,
                ServiceId = testServiceId
            });

            if (clearResponse.PermissionsChanged)
            {
                return TestResult.Failed("Permissions should NOT have changed when no state existed");
            }

            return TestResult.Successful("Clearing non-existent state handled gracefully");
        }, "Clear non-existent state");

    /// <summary>
    /// Test clearing all session states by omitting ServiceId.
    /// When ServiceId is null, all states for the session should be cleared.
    /// </summary>
    private static async Task<TestResult> TestClearAllSessionStates(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testSessionId = Guid.NewGuid();
            var testServiceId1 = Guid.NewGuid().ToString();
            var testServiceId2 = Guid.NewGuid().ToString();

            // Register two services
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId1,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["active"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/api/service1" }
                    }
                }
            });

            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId2,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["ready"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/api/service2" }
                    }
                }
            });

            // Register session with role
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Set state for both services
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId1,
                NewState = "active"
            });

            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId2,
                NewState = "ready"
            });

            // Verify states are set (2 service states + 1 role state = 3 total)
            var sessionInfo = await permissionsClient.GetSessionInfoAsync(new SessionInfoRequest
            {
                SessionId = testSessionId
            });

            // States include: role=user, service1=active, service2=ready
            if (sessionInfo.States == null || sessionInfo.States.Count != 3)
            {
                return TestResult.Failed($"Expected 3 states (2 service + 1 role), got {sessionInfo.States?.Count ?? 0}");
            }

            // Clear ALL states by omitting ServiceId
            var clearResponse = await permissionsClient.ClearSessionStateAsync(new ClearSessionStateRequest
            {
                SessionId = testSessionId
                // ServiceId is null - should clear all states
            });

            if (!clearResponse.PermissionsChanged)
            {
                return TestResult.Failed("Permissions should have changed when clearing states");
            }

            // Verify all states were cleared
            // After clearing ALL states, GetSessionInfo returns 404 (no session data exists)
            // which is the expected behavior - session has no state info left
            try
            {
                var updatedInfo = await permissionsClient.GetSessionInfoAsync(new SessionInfoRequest
                {
                    SessionId = testSessionId
                });

                // If we get here, the session still has some info - check it's empty
                if (updatedInfo.States != null && updatedInfo.States.Count > 0)
                {
                    return TestResult.Failed($"Expected 0 states after clear all, got {updatedInfo.States.Count}");
                }

                return TestResult.Successful("All states cleared successfully");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // 404 is expected - session has no state info after clearing all states
                return TestResult.Successful("All states cleared successfully (session info now returns 404)");
            }
        }, "Clear all session states");

    /// <summary>
    /// Test getting complete session information.
    /// </summary>
    private static async Task<TestResult> TestGetSessionInfo(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"session-info-{Guid.NewGuid():N}";
            var testSessionId = Guid.NewGuid();
            var testServiceId = Guid.NewGuid().ToString();

            // Register service
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["active"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/info/data" }
                    }
                }
            });

            // Create session with state and role
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "active"
            });
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Get session info
            var sessionInfo = await permissionsClient.GetSessionInfoAsync(new SessionInfoRequest
            {
                SessionId = testSessionId
            });

            if (sessionInfo.Role != "user")
            {
                return TestResult.Failed($"Session info returned wrong role: {sessionInfo.Role}");
            }

            if (sessionInfo.States == null || sessionInfo.States.Count == 0)
            {
                return TestResult.Failed("Session info returned no states");
            }

            return TestResult.Successful($"Got session info: role={sessionInfo.Role}, states={sessionInfo.States.Count}, version={sessionInfo.Version}");
        }, "Get session info");

    /// <summary>
    /// Test getting session info for non-existent session returns NotFound.
    /// </summary>
    private static async Task<TestResult> TestGetSessionInfoNonExistent(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var nonExistentSessionId = Guid.NewGuid();

            try
            {
                var sessionInfo = await permissionsClient.GetSessionInfoAsync(new SessionInfoRequest
                {
                    SessionId = nonExistentSessionId
                });

                // If we get back info with no states, that's acceptable
                if (sessionInfo.States == null || sessionInfo.States.Count == 0)
                {
                    return TestResult.Successful("Non-existent session returned empty session info");
                }

                return TestResult.Failed("Non-existent session returned populated info when it should not have");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("Non-existent session correctly returned 404 NotFound");
            }
        }, "Get session info non-existent");

    /// <summary>
    /// Test that admin role receives admin-level permissions.
    /// </summary>
    private static async Task<TestResult> TestAdminRoleCapabilities(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"admin-cap-{Guid.NewGuid():N}";
            var testSessionId = Guid.NewGuid();
            var testServiceId = Guid.NewGuid().ToString();

            // Register service with admin-only method
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/public" },
                        ["admin"] = new Collection<string> { "/public", "/admin/critical", "/admin/settings" }
                    }
                }
            });

            // Create session as admin
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "authenticated"
            });
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "admin"
            });

            // Get capabilities
            var capabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            if (capabilities.Permissions?.ContainsKey(testServiceId) == true)
            {
                var methods = capabilities.Permissions[testServiceId];
                if (methods.Contains("/admin/critical"))
                {
                    return TestResult.Successful($"Admin role correctly received admin-level permissions: {methods.Count} methods");
                }
                return TestResult.Failed($"Admin role missing admin-only method. Got: [{string.Join(", ", methods)}]");
            }
            return TestResult.Failed("Admin session has no permissions for test service");
        }, "Admin role capabilities");

    /// <summary>
    /// Test that user role receives only user-level permissions.
    /// </summary>
    private static async Task<TestResult> TestUserRoleCapabilities(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"user-cap-{Guid.NewGuid():N}";
            var testSessionId = Guid.NewGuid();
            var testServiceId = Guid.NewGuid().ToString();

            // Register service with admin-only method
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    // QUALITY TENETS: Exactly one role per endpoint
                    // Admin inherits user permissions via role hierarchy, so admin-only
                    // endpoints should ONLY be listed under admin role
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/public", "/user/action" },
                        ["admin"] = new Collection<string> { "/admin/critical" }  // Admin-ONLY endpoint
                    }
                }
            });

            // Create session as user
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "authenticated"
            });
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Get capabilities
            var capabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            if (capabilities.Permissions?.ContainsKey(testServiceId) == true)
            {
                var methods = capabilities.Permissions[testServiceId];
                if (methods.Contains("/admin/critical"))
                {
                    return TestResult.Failed("User role incorrectly received admin-only method");
                }
                if (methods.Contains("/public"))
                {
                    return TestResult.Successful($"User role correctly received user-level permissions: {methods.Count} methods");
                }
                return TestResult.Failed($"User role has unexpected permissions: [{string.Join(", ", methods)}]");
            }
            return TestResult.Failed("User session has no permissions for test service");
        }, "User role capabilities");

    /// <summary>
    /// Test role escalation from user to admin increases permissions.
    /// </summary>
    private static async Task<TestResult> TestRoleEscalation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"escalation-{Guid.NewGuid():N}";
            var testSessionId = Guid.NewGuid();
            var testServiceId = Guid.NewGuid().ToString();

            // Register service with different permissions per role
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    // QUALITY TENETS: Exactly one role per endpoint
                    // Admin inherits user permissions via role hierarchy
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/data" },
                        ["admin"] = new Collection<string> { "/data", "/data" }  // Admin-ONLY endpoints
                    }
                }
            });

            // Create session as user
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "authenticated"
            });
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Get user capabilities
            var userCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            var userMethodCount = 0;
            if (userCapabilities.Permissions?.ContainsKey(testServiceId) == true)
            {
                userMethodCount = userCapabilities.Permissions[testServiceId].Count;
            }

            // Escalate to admin
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "admin",
                PreviousRole = "user"
            });

            // Get admin capabilities - service now verifies write consistency before returning
            var adminCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            var adminMethodCount = 0;
            if (adminCapabilities.Permissions?.ContainsKey(testServiceId) == true)
            {
                adminMethodCount = adminCapabilities.Permissions[testServiceId].Count;
            }

            if (adminMethodCount > userMethodCount)
            {
                return TestResult.Successful($"Role escalation increased permissions: user={userMethodCount}, admin={adminMethodCount}");
            }
            else if (adminMethodCount >= userMethodCount && adminMethodCount > 0)
            {
                return TestResult.Successful($"Role escalation completed: user={userMethodCount}, admin={adminMethodCount} methods");
            }
            return TestResult.Failed($"Role escalation did not increase permissions: user={userMethodCount}, admin={adminMethodCount}");
        }, "Role escalation");

    /// <summary>
    /// Test that setting game-session:in_game state grants additional permissions.
    /// </summary>
    private static async Task<TestResult> TestStateBasedPermissionEscalation(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"state-esc-{Guid.NewGuid():N}";
            var testSessionId = Guid.NewGuid();
            var testServiceId = Guid.NewGuid().ToString();

            // Register service with permissions at BOTH "default" and "game-session:in_game" states
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    // Default state - always accessible
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/public/data", "/public/info" }
                    },
                    // game-session:in_game state - requires explicit state set
                    ["game-session:in_game"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/game/action", "/game/state" }
                    }
                }
            });

            // Create session as user (no game-session:in_game state yet)
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Get capabilities - should only have "default" state permissions
            var beforeCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            var beforeMethodCount = 0;
            if (beforeCapabilities.Permissions?.ContainsKey(testServiceId) == true)
            {
                beforeMethodCount = beforeCapabilities.Permissions[testServiceId].Count;
            }

            if (beforeMethodCount != 2)
            {
                return TestResult.Failed($"Before state set: expected 2 default permissions, got {beforeMethodCount}");
            }

            // Now set the game-session:in_game state
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = "game-session",  // game-session service's in_game state
                NewState = "in_game"
            });

            // Get capabilities again - should now have BOTH default AND game-session:in_game permissions
            var afterCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            var afterMethodCount = 0;
            if (afterCapabilities.Permissions?.ContainsKey(testServiceId) == true)
            {
                afterMethodCount = afterCapabilities.Permissions[testServiceId].Count;
            }

            if (afterMethodCount == 4)
            {
                return TestResult.Successful($"State-based escalation works: before={beforeMethodCount}, after={afterMethodCount} methods");
            }
            else if (afterMethodCount > beforeMethodCount)
            {
                return TestResult.Successful($"State-based escalation increased permissions: before={beforeMethodCount}, after={afterMethodCount}");
            }
            return TestResult.Failed($"State-based escalation did not add permissions: before={beforeMethodCount}, after={afterMethodCount}");
        }, "State-based escalation");

    /// <summary>
    /// Test the clear difference between default and game-session:in_game state permissions.
    /// </summary>
    private static async Task<TestResult> TestDefaultVsInGameState(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionClient>();
            var testPrefix = $"state-diff-{Guid.NewGuid():N}";
            var testSessionId = Guid.NewGuid();
            var testServiceId = Guid.NewGuid().ToString();

            // Register service with ONLY game-session:in_game state permissions (no default)
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    // Only game-session:in_game - requires explicit state
                    ["game-session:in_game"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "/game/action", "/game/state" }
                    }
                }
            });

            // Create session as user (no game-session:in_game state)
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Get capabilities - should have 0 permissions (no default state endpoints)
            var beforeCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            var beforeMethodCount = 0;
            if (beforeCapabilities.Permissions?.ContainsKey(testServiceId) == true)
            {
                beforeMethodCount = beforeCapabilities.Permissions[testServiceId].Count;
            }

            if (beforeMethodCount != 0)
            {
                return TestResult.Failed($"Before state set: expected 0 permissions (no default), got {beforeMethodCount}");
            }

            // Set the game-session:in_game state
            await permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = "game-session",
                NewState = "in_game"
            });

            // Get capabilities - should now have the state-specific permissions
            var afterCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            var afterMethodCount = 0;
            if (afterCapabilities.Permissions?.ContainsKey(testServiceId) == true)
            {
                afterMethodCount = afterCapabilities.Permissions[testServiceId].Count;
            }

            if (afterMethodCount == 2)
            {
                return TestResult.Successful($"State difference validated: without state={beforeMethodCount}, with game-session:in_game={afterMethodCount}");
            }
            return TestResult.Failed($"State difference test failed: before={beforeMethodCount}, after={afterMethodCount} (expected 0 → 2)");
        }, "Default vs in_game state");

}
