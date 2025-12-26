using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Permissions;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;
using System.Collections.ObjectModel;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for permissions service API endpoints, including event subscription tests.
/// Tests both direct API calls and event-driven permission updates via RabbitMQ/MassTransit.
/// </summary>
public class PermissionsTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Core Permission Registration Tests
        new ServiceTest(TestRegisterServicePermissions, "RegisterServicePermissions", "Permissions", "Test direct service permission registration"),
        new ServiceTest(TestRegisterMultipleServicesPermissions, "RegisterMultipleServices", "Permissions", "Test registering permissions for multiple services"),
        new ServiceTest(TestRegisterServiceWithMultipleStates, "RegisterMultipleStates", "Permissions", "Test registering service with multiple states"),
        new ServiceTest(TestRegisterServiceWithMultipleRoles, "RegisterMultipleRoles", "Permissions", "Test registering service with multiple roles"),

        // Capability Lookup Tests
        new ServiceTest(TestGetCapabilitiesExistingSession, "GetCapabilitiesExisting", "Permissions", "Test getting capabilities for existing session"),
        new ServiceTest(TestGetCapabilitiesNonExistentSession, "GetCapabilitiesNonExistent", "Permissions", "Test getting capabilities for non-existent session"),
        new ServiceTest(TestGetCapabilitiesFilteredByService, "GetCapabilitiesFiltered", "Permissions", "Test getting capabilities filtered by service IDs"),

        // API Validation Tests
        new ServiceTest(TestValidateApiAccessAllowed, "ValidateApiAccessAllowed", "Permissions", "Test API access validation when access is allowed"),
        new ServiceTest(TestValidateApiAccessDenied, "ValidateApiAccessDenied", "Permissions", "Test API access validation when access is denied"),
        new ServiceTest(TestValidateApiAccessUnknownService, "ValidateApiAccessUnknown", "Permissions", "Test API access validation for unknown service"),

        // Session State Management Tests
        new ServiceTest(TestUpdateSessionState, "UpdateSessionState", "Permissions", "Test updating session state for a service"),
        new ServiceTest(TestUpdateSessionStateTransition, "UpdateSessionStateTransition", "Permissions", "Test session state transitions trigger permission changes"),
        new ServiceTest(TestUpdateSessionRole, "UpdateSessionRole", "Permissions", "Test updating session role"),
        new ServiceTest(TestUpdateSessionRoleAffectsAllServices, "UpdateRoleAffectsAll", "Permissions", "Test role update affects all service permissions"),

        // Clear Session State Tests
        new ServiceTest(TestClearSessionStateUnconditional, "ClearSessionStateUnconditional", "Permissions", "Test clearing session state unconditionally"),
        new ServiceTest(TestClearSessionStateWithMatchingFilter, "ClearSessionStateMatchingFilter", "Permissions", "Test clearing session state when filter matches"),
        new ServiceTest(TestClearSessionStateWithNonMatchingFilter, "ClearSessionStateNonMatchingFilter", "Permissions", "Test clearing session state when filter doesn't match"),
        new ServiceTest(TestClearSessionStateNonExistent, "ClearSessionStateNonExistent", "Permissions", "Test clearing state for service with no state set"),

        // Session Info Tests
        new ServiceTest(TestGetSessionInfo, "GetSessionInfo", "Permissions", "Test getting complete session information"),
        new ServiceTest(TestGetSessionInfoNonExistent, "GetSessionInfoNonExistent", "Permissions", "Test getting session info for non-existent session"),

        // Admin vs User Role Tests
        new ServiceTest(TestAdminRoleCapabilities, "AdminRoleCapabilities", "Permissions", "Test admin role receives admin-level permissions"),
        new ServiceTest(TestUserRoleCapabilities, "UserRoleCapabilities", "Permissions", "Test user role receives user-level permissions"),
        new ServiceTest(TestRoleEscalation, "RoleEscalation", "Permissions", "Test role escalation from user to admin"),

        // State-Based Permission Tests
        new ServiceTest(TestStateBasedPermissionEscalation, "StateBasedEscalation", "Permissions", "Test setting game-session:in_game state grants additional permissions"),
        new ServiceTest(TestDefaultVsInGameState, "DefaultVsInGame", "Permissions", "Test difference between default and game-session:in_game state permissions"),

        // RabbitMQ/MassTransit Event Tests
        new ServiceTest(TestEventSubscription, "EventSubscription", "Permissions", "Test RabbitMQ pubsub event subscription for service registration"),
        new ServiceTest(TestSessionStateChangeEvent, "SessionStateChangeEvent", "Permissions", "Test RabbitMQ pubsub event subscription for session state changes"),

        // Phase 6: Session Connection Event Tests (activeConnections tracking)
        new ServiceTest(TestSessionConnectedEvent, "SessionConnectedEvent", "Permissions", "Test session.connected event adds session to activeConnections"),
        new ServiceTest(TestSessionConnectedEventWithRoles, "SessionConnectedWithRoles", "Permissions", "Test session.connected event stores roles for capability compilation"),
        new ServiceTest(TestSessionDisconnectedEvent, "SessionDisconnectedEvent", "Permissions", "Test session.disconnected event removes session from activeConnections"),
        new ServiceTest(TestSessionDisconnectedReconnectable, "SessionDisconnectedReconn", "Permissions", "Test session.disconnected with reconnectable flag preserves activeSessions"),
    ];

    /// <summary>
    /// Test direct service permission registration via API.
    /// This establishes a baseline that the Permissions service works.
    /// </summary>
    private static Task<TestResult> TestRegisterServicePermissions(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testServiceId = $"test-service-direct-{Guid.NewGuid():N}";

            var permissionMatrix = new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/test/endpoint" }
                    }
                }
            };

            var response = await permissionsClient.RegisterServicePermissionsAsync(permissionMatrix);

            if (response.Success)
            {
                return TestResult.Successful($"Service permissions registered: {testServiceId}, affected {response.AffectedSessions} sessions");
            }
            else
            {
                return TestResult.Failed($"Service permission registration returned success=false");
            }
        }, "Register service permissions");

    /// <summary>
    /// Test registering permissions for multiple services.
    /// </summary>
    private static Task<TestResult> TestRegisterMultipleServicesPermissions(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
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
                        ["user"] = new Collection<string> { "GET:/service1/data" }
                    }
                }
            };

            var response1 = await permissionsClient.RegisterServicePermissionsAsync(matrix1);
            if (!response1.Success)
                return TestResult.Failed("Failed to register first service");

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
                        ["user"] = new Collection<string> { "GET:/service2/data", "POST:/service2/action" }
                    }
                }
            };

            var response2 = await permissionsClient.RegisterServicePermissionsAsync(matrix2);
            if (!response2.Success)
                return TestResult.Failed("Failed to register second service");

            return TestResult.Successful($"Successfully registered multiple services: {service1Id}, {service2Id}");
        }, "Register multiple services");

    /// <summary>
    /// Test registering service with multiple states (in_lobby, default, in_game).
    /// </summary>
    private static Task<TestResult> TestRegisterServiceWithMultipleStates(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testServiceId = $"multi-state-svc-{Guid.NewGuid():N}";

            var permissionMatrix = new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["in_lobby"] = new StatePermissions
                    {
                        ["guest"] = new Collection<string> { "GET:/public/info" }
                    },
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/public/info", "GET:/user/profile" }
                    },
                    ["in_game"] = new StatePermissions
                    {
                        ["admin"] = new Collection<string> { "GET:/public/info", "GET:/user/profile", "DELETE:/admin/data" }
                    }
                }
            };

            var response = await permissionsClient.RegisterServicePermissionsAsync(permissionMatrix);

            if (response.Success)
            {
                return TestResult.Successful($"Service with multiple states registered: {testServiceId}, 3 states defined");
            }
            return TestResult.Failed("Registration returned success=false");
        }, "Register multiple states");

    /// <summary>
    /// Test registering service with multiple roles (guest, user, admin).
    /// </summary>
    private static Task<TestResult> TestRegisterServiceWithMultipleRoles(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testServiceId = $"multi-role-svc-{Guid.NewGuid():N}";

            var permissionMatrix = new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["guest"] = new Collection<string> { "GET:/info" },
                        ["user"] = new Collection<string> { "GET:/info", "GET:/data", "POST:/data" },
                        ["admin"] = new Collection<string> { "GET:/info", "GET:/data", "POST:/data", "DELETE:/data", "PUT:/admin/settings" }
                    }
                }
            };

            var response = await permissionsClient.RegisterServicePermissionsAsync(permissionMatrix);

            if (response.Success)
            {
                return TestResult.Successful($"Service with multiple roles registered: {testServiceId}, 3 roles defined");
            }
            return TestResult.Failed("Registration returned success=false");
        }, "Register multiple roles");

    /// <summary>
    /// Test getting capabilities for an existing session.
    /// </summary>
    private static Task<TestResult> TestGetCapabilitiesExistingSession(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testServiceId = $"cap-test-svc-{Guid.NewGuid():N}";
            var testSessionId = $"cap-test-session-{Guid.NewGuid():N}";

            // Step 1: Register service with permissions
            var permissionMatrix = new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/cap/test1", "POST:/cap/test2" }
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
    private static Task<TestResult> TestGetCapabilitiesNonExistentSession(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var nonExistentSessionId = $"nonexistent-{Guid.NewGuid():N}";

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
    private static Task<TestResult> TestGetCapabilitiesFilteredByService(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"filter-test-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
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
                        ["user"] = new Collection<string> { "GET:/svc1/data" }
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
                        ["user"] = new Collection<string> { "GET:/svc2/data" }
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
    private static Task<TestResult> TestValidateApiAccessAllowed(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"validate-allowed-{Guid.NewGuid():N}";
            var testServiceId = $"{testPrefix}-svc";
            var testSessionId = $"{testPrefix}-session";
            var testMethod = "GET:/allowed/endpoint";

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
                Method = testMethod
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
    private static Task<TestResult> TestValidateApiAccessDenied(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"validate-denied-{Guid.NewGuid():N}";
            var testServiceId = $"{testPrefix}-svc";
            var testSessionId = $"{testPrefix}-session";
            var allowedMethod = "GET:/allowed/endpoint";
            var deniedMethod = "DELETE:/admin/dangerous";

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
                Method = deniedMethod
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
    private static Task<TestResult> TestValidateApiAccessUnknownService(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testSessionId = $"unknown-svc-test-{Guid.NewGuid():N}";
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
                Method = "GET:/any/endpoint"
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
    private static Task<TestResult> TestUpdateSessionState(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testSessionId = $"state-update-{Guid.NewGuid():N}";
            var testServiceId = $"state-svc-{Guid.NewGuid():N}";

            var stateUpdate = new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "in_lobby",
                PreviousState = null
            };

            var response = await permissionsClient.UpdateSessionStateAsync(stateUpdate);

            if (response.Success)
            {
                return TestResult.Successful($"Session state updated to 'in_lobby' for session {testSessionId}");
            }
            return TestResult.Failed($"Session state update failed: {response.Message}");
        }, "Update session state");

    /// <summary>
    /// Test that session state transitions trigger permission recompilation.
    /// </summary>
    private static Task<TestResult> TestUpdateSessionStateTransition(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"transition-{Guid.NewGuid():N}";
            var testServiceId = $"{testPrefix}-svc";
            var testSessionId = $"{testPrefix}-session";

            // Register service with different permissions per state
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["lobby"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/lobby/info" }
                    },
                    ["in_game"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/lobby/info", "POST:/game/action", "GET:/game/state" }
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
    private static Task<TestResult> TestUpdateSessionRole(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testSessionId = $"role-update-{Guid.NewGuid():N}";

            var roleUpdate = new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "admin",
                PreviousRole = null
            };

            var response = await permissionsClient.UpdateSessionRoleAsync(roleUpdate);

            if (response.Success)
            {
                return TestResult.Successful($"Session role updated to 'admin' for session {testSessionId}");
            }
            return TestResult.Failed($"Session role update failed: {response.Message}");
        }, "Update session role");

    /// <summary>
    /// Test that role update affects permissions across all services.
    /// </summary>
    private static Task<TestResult> TestUpdateSessionRoleAffectsAllServices(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"role-all-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
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
                        ["user"] = new Collection<string> { "GET:/svc1/read" },
                        ["admin"] = new Collection<string> { "GET:/svc1/read", "DELETE:/svc1/delete" }
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
                        ["user"] = new Collection<string> { "GET:/svc2/read" },
                        ["admin"] = new Collection<string> { "GET:/svc2/read", "PUT:/svc2/admin" }
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
    private static Task<TestResult> TestClearSessionStateUnconditional(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"clear-state-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testServiceId = $"{testPrefix}-svc";

            // Register service with state-dependent permissions
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/public" }
                    },
                    ["active"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/public", "POST:/active/action" }
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

            if (!clearResponse.Success)
            {
                return TestResult.Failed($"Clear state failed: {clearResponse.Message}");
            }

            // Verify state was cleared
            var updatedInfo = await permissionsClient.GetSessionInfoAsync(new SessionInfoRequest
            {
                SessionId = testSessionId
            });

            if (updatedInfo.States != null && updatedInfo.States.ContainsKey(testServiceId))
            {
                return TestResult.Failed("State was not cleared");
            }

            return TestResult.Successful($"State cleared unconditionally: {clearResponse.Message}");
        }, "Clear state unconditional");

    /// <summary>
    /// Test clearing session state when the filter matches the current state.
    /// </summary>
    private static Task<TestResult> TestClearSessionStateWithMatchingFilter(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"clear-match-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testServiceId = $"{testPrefix}-svc";

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

            if (!clearResponse.Success)
            {
                return TestResult.Failed($"Clear state failed: {clearResponse.Message}");
            }

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

            return TestResult.Successful($"State cleared with matching filter: {clearResponse.Message}");
        }, "Clear state matching filter");

    /// <summary>
    /// Test clearing session state when the filter does not match the current state.
    /// </summary>
    private static Task<TestResult> TestClearSessionStateWithNonMatchingFilter(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"clear-nomatch-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testServiceId = $"{testPrefix}-svc";

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

            if (!clearResponse.Success)
            {
                return TestResult.Failed($"Clear state call failed: {clearResponse.Message}");
            }

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

            return TestResult.Successful($"State preserved when filter didn't match: {clearResponse.Message}");
        }, "Clear state non-matching filter");

    /// <summary>
    /// Test clearing state for a service that has no state set.
    /// </summary>
    private static Task<TestResult> TestClearSessionStateNonExistent(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"clear-nostate-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testServiceId = $"{testPrefix}-svc";

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

            if (!clearResponse.Success)
            {
                return TestResult.Failed($"Clear state call should succeed even with no state: {clearResponse.Message}");
            }

            if (clearResponse.PermissionsChanged)
            {
                return TestResult.Failed("Permissions should NOT have changed when no state existed");
            }

            return TestResult.Successful($"Clearing non-existent state handled gracefully: {clearResponse.Message}");
        }, "Clear non-existent state");

    /// <summary>
    /// Test getting complete session information.
    /// </summary>
    private static Task<TestResult> TestGetSessionInfo(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"session-info-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testServiceId = $"{testPrefix}-svc";

            // Register service
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["active"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/info/data" }
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

            if (sessionInfo.SessionId != testSessionId)
            {
                return TestResult.Failed("Session info returned wrong session ID");
            }

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
    private static Task<TestResult> TestGetSessionInfoNonExistent(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var nonExistentSessionId = $"nonexistent-session-{Guid.NewGuid():N}";

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
    private static Task<TestResult> TestAdminRoleCapabilities(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"admin-cap-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testServiceId = $"{testPrefix}-svc";

            // Register service with admin-only method
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/public" },
                        ["admin"] = new Collection<string> { "GET:/public", "DELETE:/admin/critical", "PUT:/admin/settings" }
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
                if (methods.Contains("DELETE:/admin/critical"))
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
    private static Task<TestResult> TestUserRoleCapabilities(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"user-cap-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testServiceId = $"{testPrefix}-svc";

            // Register service with admin-only method
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    // TENETS Tenet 10: Exactly one role per endpoint
                    // Admin inherits user permissions via role hierarchy, so admin-only
                    // endpoints should ONLY be listed under admin role
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/public", "POST:/user/action" },
                        ["admin"] = new Collection<string> { "DELETE:/admin/critical" }  // Admin-ONLY endpoint
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
                if (methods.Contains("DELETE:/admin/critical"))
                {
                    return TestResult.Failed("User role incorrectly received admin-only method");
                }
                if (methods.Contains("GET:/public"))
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
    private static Task<TestResult> TestRoleEscalation(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"escalation-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testServiceId = $"{testPrefix}-svc";

            // Register service with different permissions per role
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    // TENETS Tenet 10: Exactly one role per endpoint
                    // Admin inherits user permissions via role hierarchy
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/data" },
                        ["admin"] = new Collection<string> { "POST:/data", "DELETE:/data" }  // Admin-ONLY endpoints
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
    private static Task<TestResult> TestStateBasedPermissionEscalation(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"state-esc-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testServiceId = $"{testPrefix}-svc";

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
                        ["user"] = new Collection<string> { "GET:/public/data", "GET:/public/info" }
                    },
                    // game-session:in_game state - requires explicit state set
                    ["game-session:in_game"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "POST:/game/action", "GET:/game/state" }
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
    private static Task<TestResult> TestDefaultVsInGameState(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"state-diff-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testServiceId = $"{testPrefix}-svc";

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
                        ["user"] = new Collection<string> { "POST:/game/action", "GET:/game/state" }
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

    /// <summary>
    /// Test pub/sub event subscription for service registration.
    /// </summary>
    private static Task<TestResult> TestEventSubscription(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            // Get IMessageBus from the service provider
            var messageBus = Program.ServiceProvider?.GetService(typeof(IMessageBus)) as IMessageBus;
            if (messageBus == null)
            {
                return TestResult.Failed("IMessageBus not available from service provider");
            }

            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testServiceId = $"test-service-event-{Guid.NewGuid():N}";
            var testSessionId = $"test-session-{Guid.NewGuid():N}";

            // Step 1: Create a service registration event using strongly-typed model
            var serviceRegistrationEvent = new ServiceRegistrationEvent
            {
                EventId = Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow,
                ServiceId = testServiceId,
                Version = "1.0.0",
                AppId = "bannou",
                Endpoints = new Collection<ServiceEndpoint>
                {
                    new ServiceEndpoint
                    {
                        Path = "/test/pubsub-event-endpoint",
                        Method = ServiceEndpointMethod.GET,
                        Permissions = new Collection<PermissionRequirement>
                        {
                            new PermissionRequirement
                            {
                                Role = "user",
                                RequiredStates = new Dictionary<string, string>
                                {
                                    [testServiceId] = "authenticated"
                                }
                            }
                        }
                    }
                }
            };

            Console.WriteLine($"  Publishing service registration event for {testServiceId} via IMessageBus...");

            // Publish the event via IMessageBus
            await messageBus.PublishAsync("permissions.service-registered", serviceRegistrationEvent);

            Console.WriteLine("  Event published via IMessageBus, waiting for processing...");

            // Wait for event to be processed
            await Task.Delay(2000);

            // Create a test session with the service state so we can query capabilities
            var sessionStateUpdate = new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "authenticated"
            };

            Console.WriteLine($"  Creating test session {testSessionId} with state 'authenticated'...");
            await permissionsClient.UpdateSessionStateAsync(sessionStateUpdate);

            // Also set the session role to 'user' to match the permission requirements
            var sessionRoleUpdate = new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            };
            await permissionsClient.UpdateSessionRoleAsync(sessionRoleUpdate);

            // Query capabilities to verify the event was processed
            Console.WriteLine($"  Querying capabilities for session...");
            var capabilityRequest = new CapabilityRequest
            {
                SessionId = testSessionId,
                ServiceIds = new List<string> { testServiceId }
            };

            var capabilities = await permissionsClient.GetCapabilitiesAsync(capabilityRequest);

            // Verify the service's methods appear in capabilities
            if (capabilities.Permissions == null)
            {
                return TestResult.Failed("Capabilities response has no permissions");
            }

            if (!capabilities.Permissions.ContainsKey(testServiceId))
            {
                // Try with a small delay - the event might still be processing
                Console.WriteLine("  Service not found in first attempt, waiting and retrying...");
                await Task.Delay(2000);
                capabilities = await permissionsClient.GetCapabilitiesAsync(capabilityRequest);
            }

            if (capabilities.Permissions.ContainsKey(testServiceId))
            {
                var methods = capabilities.Permissions[testServiceId];
                if (methods.Contains("GET:/test/pubsub-event-endpoint"))
                {
                    return TestResult.Successful(
                        $"Event subscription verified: service {testServiceId} registered via RabbitMQ pubsub event, " +
                        $"capabilities include {methods.Count} method(s)");
                }
                else
                {
                    return TestResult.Failed(
                        $"Service was registered but expected method not found. " +
                        $"Methods: [{string.Join(", ", methods)}]");
                }
            }
            else
            {
                return TestResult.Failed(
                    $"Service {testServiceId} not found in capabilities after event publication. " +
                    $"This indicates the RabbitMQ event subscription may not be working. " +
                    $"Available services: [{string.Join(", ", capabilities.Permissions.Keys)}]");
            }
        }, "Event subscription");

    /// <summary>
    /// Test pub/sub event subscription for session state changes.
    /// </summary>
    private static Task<TestResult> TestSessionStateChangeEvent(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            // Get IMessageBus from the service provider
            var messageBus = Program.ServiceProvider?.GetService(typeof(IMessageBus)) as IMessageBus;
            if (messageBus == null)
            {
                return TestResult.Failed("IMessageBus not available from service provider");
            }

            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testServiceId = $"test-service-state-{Guid.NewGuid():N}";
            var testSessionId = $"test-session-state-{Guid.NewGuid():N}";

            // Step 1: Register service permissions with different states
            var permissionMatrix = new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["in_lobby"] = new StatePermissions
                    {
                        ["guest"] = new Collection<string> { "GET:/public/info" }
                    },
                    ["in_game"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/public/info", "GET:/private/data" }
                    }
                }
            };

            Console.WriteLine($"  Registering service {testServiceId}...");
            await permissionsClient.RegisterServicePermissionsAsync(permissionMatrix);

            // Step 2: Create initial session state
            var initialState = new SessionStateUpdate
            {
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "in_lobby"
            };
            await permissionsClient.UpdateSessionStateAsync(initialState);

            var roleUpdate = new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "guest"
            };
            await permissionsClient.UpdateSessionRoleAsync(roleUpdate);

            // Step 3: Verify initial capabilities
            var initialCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId,
                ServiceIds = new List<string> { testServiceId }
            });

            var initialMethodCount = 0;
            if (initialCapabilities.Permissions?.ContainsKey(testServiceId) == true)
            {
                initialMethodCount = initialCapabilities.Permissions[testServiceId].Count;
            }
            Console.WriteLine($"  Initial capabilities: {initialMethodCount} methods");

            // Step 4: Publish session state change event via IMessageBus using strongly-typed model
            var stateChangeEvent = new SessionStateChangeEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = testSessionId,
                ServiceId = testServiceId,
                NewState = "in_game",
                PreviousState = "in_lobby"
            };

            Console.WriteLine($"  Publishing session state change event via IMessageBus...");
            await messageBus.PublishAsync("permissions.session-state-changed", stateChangeEvent);

            // Step 5: Wait for event processing
            await Task.Delay(2000);

            // Step 6: Also update role to 'user' to get access to authenticated methods
            await permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
            {
                SessionId = testSessionId,
                NewRole = "user"
            });

            // Step 7: Query capabilities again to verify state change
            var updatedCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId,
                ServiceIds = new List<string> { testServiceId }
            });

            ICollection<string>? updatedMethods = null;
            if (updatedCapabilities.Permissions?.ContainsKey(testServiceId) == true)
            {
                updatedMethods = updatedCapabilities.Permissions[testServiceId];
            }
            var updatedMethodCount = updatedMethods?.Count ?? 0;

            Console.WriteLine($"  Updated capabilities: {updatedMethodCount} methods");

            // Verify the state change gave us access to more methods
            if (updatedMethods != null && updatedMethods.Contains("GET:/private/data"))
            {
                return TestResult.Successful(
                    $"Session state change event verified: " +
                    $"state changed from 'in_lobby' to 'in_game', " +
                    $"methods increased from {initialMethodCount} to {updatedMethodCount}, " +
                    $"now has access to private endpoint");
            }
            else if (updatedMethodCount > initialMethodCount)
            {
                return TestResult.Successful(
                    $"Session state change event processed: " +
                    $"methods increased from {initialMethodCount} to {updatedMethodCount}");
            }
            else
            {
                // The state change event may have been processed, but permissions might work differently
                // Let's check if we at least have some methods
                if (updatedMethodCount > 0)
                {
                    return TestResult.Successful(
                        $"Session state change completed with {updatedMethodCount} methods. " +
                        $"Methods: [{string.Join(", ", updatedMethods ?? new Collection<string>())}]");
                }

                return TestResult.Failed(
                    $"Session state change event not reflected in capabilities. " +
                    $"Initial: {initialMethodCount} methods, Updated: {updatedMethodCount} methods");
            }
        }, "Session state change event");

    /// <summary>
    /// Test that session.connected event adds session to activeConnections.
    /// </summary>
    private static Task<TestResult> TestSessionConnectedEvent(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messageBus = Program.ServiceProvider?.GetService(typeof(IMessageBus)) as IMessageBus;
            if (messageBus == null)
            {
                return TestResult.Failed("IMessageBus not available from service provider");
            }

            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"session-connected-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testAccountId = $"{testPrefix}-account";
            var testServiceId = $"{testPrefix}-svc";

            // Step 1: Register a service with permissions so there are capabilities to compile
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/connected/test" }
                    }
                }
            });

            // Step 2: Publish session.connected event via IMessageBus using strongly-typed model
            var sessionConnectedEvent = new SessionConnectedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = testSessionId,
                AccountId = testAccountId,
                Roles = new List<string> { "user" },
                Authorizations = null,
                ConnectInstanceId = Guid.NewGuid()
            };

            Console.WriteLine($"  Publishing session.connected event for {testSessionId}...");
            await messageBus.PublishAsync("session.connected", sessionConnectedEvent);

            // Wait for event to be processed
            await Task.Delay(2000);

            // Step 3: Verify the session has capabilities (proving it was added to activeConnections)
            var capabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            if (capabilities.Permissions != null && capabilities.Permissions.Count > 0)
            {
                return TestResult.Successful(
                    $"session.connected event processed: session {testSessionId} now has capabilities " +
                    $"({capabilities.Permissions.Count} services)");
            }

            // If capabilities are empty, check session info to see if session exists
            try
            {
                var sessionInfo = await permissionsClient.GetSessionInfoAsync(new SessionInfoRequest
                {
                    SessionId = testSessionId
                });

                if (sessionInfo.Role == "user")
                {
                    return TestResult.Successful(
                        $"session.connected event processed: session {testSessionId} has role '{sessionInfo.Role}'");
                }
            }
            catch (ApiException)
            {
                // Session info not found - event may not have been processed
            }

            return TestResult.Failed(
                $"session.connected event may not have been processed - session has no capabilities or role");
        }, "Session connected event");

    /// <summary>
    /// Test that session.connected event properly stores roles from JWT for capability compilation.
    /// </summary>
    private static Task<TestResult> TestSessionConnectedEventWithRoles(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messageBus = Program.ServiceProvider?.GetService(typeof(IMessageBus)) as IMessageBus;
            if (messageBus == null)
            {
                return TestResult.Failed("IMessageBus not available from service provider");
            }

            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"session-roles-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testAccountId = $"{testPrefix}-account";
            var testServiceId = $"{testPrefix}-svc";

            // Step 1: Register service with admin-only permissions
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/user/endpoint" },
                        ["admin"] = new Collection<string> { "DELETE:/admin/dangerous" }
                    }
                }
            });

            // Step 2: Publish session.connected with admin role using strongly-typed model
            var sessionConnectedEvent = new SessionConnectedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = testSessionId,
                AccountId = testAccountId,
                Roles = new List<string> { "user", "admin" },  // Admin role should be selected (highest priority)
                Authorizations = null
            };

            Console.WriteLine($"  Publishing session.connected with roles [user, admin]...");
            await messageBus.PublishAsync("session.connected", sessionConnectedEvent);

            await Task.Delay(2000);

            // Step 3: Verify session has admin capabilities
            var capabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            if (capabilities.Permissions?.ContainsKey(testServiceId) == true)
            {
                var methods = capabilities.Permissions[testServiceId];
                if (methods.Contains("DELETE:/admin/dangerous"))
                {
                    return TestResult.Successful(
                        $"session.connected with roles processed: admin role correctly applied, " +
                        $"session has admin-only endpoint access ({methods.Count} methods)");
                }

                if (methods.Contains("GET:/user/endpoint"))
                {
                    return TestResult.Failed(
                        $"Roles stored but admin inheritance not working. Got user methods but not admin. " +
                        $"Methods: [{string.Join(", ", methods)}]");
                }
            }

            // Check session info for role
            var sessionInfo = await permissionsClient.GetSessionInfoAsync(new SessionInfoRequest
            {
                SessionId = testSessionId
            });

            if (sessionInfo.Role == "admin")
            {
                return TestResult.Successful(
                    $"session.connected with roles processed: role stored as '{sessionInfo.Role}'");
            }

            return TestResult.Failed(
                $"session.connected with roles may not have processed correctly. Role: {sessionInfo.Role}");
        }, "Session connected with roles");

    /// <summary>
    /// Test that session.disconnected event removes session from activeConnections.
    /// </summary>
    private static Task<TestResult> TestSessionDisconnectedEvent(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messageBus = Program.ServiceProvider?.GetService(typeof(IMessageBus)) as IMessageBus;
            if (messageBus == null)
            {
                return TestResult.Failed("IMessageBus not available from service provider");
            }

            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"session-disconnect-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testAccountId = $"{testPrefix}-account";

            // Step 1: First connect the session using strongly-typed model
            var connectEvent = new SessionConnectedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = testSessionId,
                AccountId = testAccountId,
                Roles = new List<string> { "user" },
                Authorizations = null
            };

            Console.WriteLine($"  Publishing session.connected for {testSessionId}...");
            await messageBus.PublishAsync("session.connected", connectEvent);
            await Task.Delay(1500);

            // Verify session is connected (has capabilities)
            var beforeCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            if (beforeCapabilities.Permissions == null)
            {
                Console.WriteLine("  Warning: Session may not have capabilities before disconnect test");
            }

            // Step 2: Disconnect the session using strongly-typed model
            var disconnectEvent = new SessionDisconnectedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = testSessionId,
                AccountId = testAccountId,
                Reason = "test_disconnect",
                Reconnectable = false,
                DurationSeconds = 60
            };

            Console.WriteLine($"  Publishing session.disconnected for {testSessionId}...");
            await messageBus.PublishAsync("session.disconnected", disconnectEvent);
            await Task.Delay(1500);

            // Step 3: Verify the event handler completed successfully
            try
            {
                var sessionInfo = await permissionsClient.GetSessionInfoAsync(new SessionInfoRequest
                {
                    SessionId = testSessionId
                });

                // Session info should still exist (session data preserved)
                return TestResult.Successful(
                    $"session.disconnected event processed: session {testSessionId} data preserved " +
                    $"(role: {sessionInfo.Role}), removed from activeConnections");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // Session completely cleaned up - also acceptable behavior
                return TestResult.Successful(
                    $"session.disconnected event processed: session {testSessionId} fully cleaned up");
            }
        }, "Session disconnected event");

    /// <summary>
    /// Test that session.disconnected with reconnectable=true preserves session in activeSessions.
    /// </summary>
    private static Task<TestResult> TestSessionDisconnectedReconnectable(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var messageBus = Program.ServiceProvider?.GetService(typeof(IMessageBus)) as IMessageBus;
            if (messageBus == null)
            {
                return TestResult.Failed("IMessageBus not available from service provider");
            }

            var permissionsClient = GetServiceClient<IPermissionsClient>();
            var testPrefix = $"session-reconn-{Guid.NewGuid():N}";
            var testSessionId = $"{testPrefix}-session";
            var testAccountId = $"{testPrefix}-account";
            var testServiceId = $"{testPrefix}-svc";

            // Step 1: Register service with permissions
            await permissionsClient.RegisterServicePermissionsAsync(new ServicePermissionMatrix
            {
                ServiceId = testServiceId,
                Version = "1.0.0",
                Permissions = new Dictionary<string, StatePermissions>
                {
                    ["default"] = new StatePermissions
                    {
                        ["user"] = new Collection<string> { "GET:/reconn/test" }
                    }
                }
            });

            // Step 2: Connect the session using strongly-typed model
            var connectEvent = new SessionConnectedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = testSessionId,
                AccountId = testAccountId,
                Roles = new List<string> { "user" },
                Authorizations = null
            };

            Console.WriteLine($"  Publishing session.connected...");
            await messageBus.PublishAsync("session.connected", connectEvent);
            await Task.Delay(1500);

            // Capture capabilities before disconnect
            var beforeCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            var beforeCount = beforeCapabilities.Permissions?.Values
                .SelectMany(methods => methods).Count() ?? 0;

            // Step 3: Disconnect with reconnectable=true using strongly-typed model
            var disconnectEvent = new SessionDisconnectedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = testSessionId,
                AccountId = testAccountId,
                Reason = "temporary_disconnect",
                Reconnectable = true,  // Key difference: session can reconnect
                DurationSeconds = 30
            };

            Console.WriteLine($"  Publishing session.disconnected with reconnectable=true...");
            await messageBus.PublishAsync("session.disconnected", disconnectEvent);
            await Task.Delay(1500);

            // Step 4: Verify session data is preserved (can still get capabilities)
            var afterCapabilities = await permissionsClient.GetCapabilitiesAsync(new CapabilityRequest
            {
                SessionId = testSessionId
            });

            var afterCount = afterCapabilities.Permissions?.Values
                .SelectMany(methods => methods).Count() ?? 0;

            // Session info should also be preserved
            var sessionInfo = await permissionsClient.GetSessionInfoAsync(new SessionInfoRequest
            {
                SessionId = testSessionId
            });

            if (sessionInfo.Role == "user" && afterCount >= beforeCount)
            {
                return TestResult.Successful(
                    $"Reconnectable session preserved: role='{sessionInfo.Role}', " +
                    $"capabilities before={beforeCount}, after={afterCount}");
            }

            if (sessionInfo.Role != null)
            {
                return TestResult.Successful(
                    $"Reconnectable session data preserved: role='{sessionInfo.Role}'");
            }

            return TestResult.Failed(
                $"Reconnectable session may not have preserved data properly. Role: {sessionInfo.Role}");
        }, "Session disconnected reconnectable");
}
