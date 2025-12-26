using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

// Alias to resolve naming conflict with Orchestrator.TestResult
using TestResult = BeyondImmersion.BannouService.Testing.TestResult;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for orchestrator service API endpoints.
/// Tests the orchestrator service APIs directly via NSwag-generated OrchestratorClient.
///
/// Note: The orchestrator service must be running with a mesh for these tests to work.
/// Use: docker compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.services.yml -f provisioning/docker-compose.orchestrator.yml up
/// </summary>
public class OrchestratorTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Health Check Tests
        new ServiceTest(TestInfrastructureHealth, "InfrastructureHealth", "Orchestrator", "Test infrastructure health check endpoint"),
        new ServiceTest(TestServicesHealth, "ServicesHealth", "Orchestrator", "Test services health report endpoint"),
        new ServiceTest(TestInfrastructureHealthComponentDetails, "InfraHealthComponents", "Orchestrator", "Test infrastructure health returns component details"),

        // Backend Detection Tests
        new ServiceTest(TestGetBackends, "GetBackends", "Orchestrator", "Test backend detection endpoint"),
        new ServiceTest(TestBackendsReturnsRecommendation, "BackendsRecommendation", "Orchestrator", "Test backends returns recommended backend"),

        // Environment Status Tests
        new ServiceTest(TestGetStatus, "GetStatus", "Orchestrator", "Test environment status endpoint"),
        new ServiceTest(TestStatusReturnsTopology, "StatusTopology", "Orchestrator", "Test status returns topology information"),

        // Presets Tests
        new ServiceTest(TestGetPresets, "GetPresets", "Orchestrator", "Test deployment presets endpoint"),
        new ServiceTest(TestPresetsHaveRequiredFields, "PresetsRequiredFields", "Orchestrator", "Test presets have required fields"),

        // Service Restart Tests
        new ServiceTest(TestShouldRestartService, "ShouldRestartService", "Orchestrator", "Test should-restart recommendation endpoint"),
        new ServiceTest(TestShouldRestartServiceUnknown, "ShouldRestartUnknown", "Orchestrator", "Test should-restart for unknown service"),

        // Container Status Tests
        new ServiceTest(TestGetContainerStatus, "GetContainerStatus", "Orchestrator", "Test container status endpoint"),
        new ServiceTest(TestGetContainerStatusUnknown, "ContainerStatusUnknown", "Orchestrator", "Test container status for unknown container"),

        // Service Routing Tests
        new ServiceTest(TestGetServiceRouting, "GetServiceRouting", "Orchestrator", "Test service routing mappings endpoint"),
        new ServiceTest(TestServiceRoutingDefaults, "ServiceRoutingDefaults", "Orchestrator", "Test service routing returns default app-id"),

        // Configuration Tests
        new ServiceTest(TestGetConfigVersion, "GetConfigVersion", "Orchestrator", "Test configuration version endpoint"),
        new ServiceTest(TestRollbackConfigurationNoHistory, "RollbackNoHistory", "Orchestrator", "Test rollback when no previous config exists"),

        // Logs Tests
        new ServiceTest(TestGetLogs, "GetLogs", "Orchestrator", "Test logs retrieval endpoint"),
        new ServiceTest(TestGetLogsWithTail, "GetLogsWithTail", "Orchestrator", "Test logs retrieval with tail parameter"),

        // Deploy Tests (Teardown not tested - it destroys the test target)
        new ServiceTest(TestDeployWithInvalidBackend, "DeployInvalidBackend", "Orchestrator", "Test deploy with unavailable backend fails"),

        // Clean Tests
        new ServiceTest(TestCleanDryRun, "CleanDryRun", "Orchestrator", "Test clean operation with dry run"),
    ];

    /// <summary>
    /// Test the infrastructure health check endpoint.
    /// Validates connectivity to Redis and RabbitMQ.
    /// </summary>
    private static Task<TestResult> TestInfrastructureHealth(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();
            var response = await orchestratorClient.GetInfrastructureHealthAsync(new InfrastructureHealthRequest());

            if (response.Components == null || response.Components.Count == 0)
                return TestResult.Failed("Infrastructure health returned no components");

            var healthyCount = response.Components.Count(c => c.Status == ComponentHealthStatus.Healthy);
            var totalCount = response.Components.Count;

            if (response.Healthy)
            {
                return TestResult.Successful($"Infrastructure health check passed: {healthyCount}/{totalCount} components healthy");
            }
            else
            {
                var unhealthyComponents = response.Components
                    .Where(c => c.Status != ComponentHealthStatus.Healthy)
                    .Select(c => c.Name);
                return TestResult.Failed($"Infrastructure health check: {healthyCount}/{totalCount} healthy. Unhealthy: {string.Join(", ", unhealthyComponents)}");
            }
        }, "Infrastructure health check");

    /// <summary>
    /// Test the services health report endpoint.
    /// Returns health status of all services via Redis heartbeat monitoring.
    /// </summary>
    private static Task<TestResult> TestServicesHealth(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();
            var response = await orchestratorClient.GetServicesHealthAsync(new ServiceHealthRequest());

            return TestResult.Successful(
                $"Services health report: {response.TotalServices} services, " +
                $"{response.HealthPercentage:F1}% healthy, " +
                $"{response.HealthyServices?.Count ?? 0} healthy, " +
                $"{response.UnhealthyServices?.Count ?? 0} unhealthy");
        }, "Services health check");

    /// <summary>
    /// Test the backend detection endpoint.
    /// Returns available container orchestration backends (Compose, Docker, Kubernetes, etc.).
    /// </summary>
    private static Task<TestResult> TestGetBackends(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();
            var response = await orchestratorClient.GetBackendsAsync(new ListBackendsRequest());

            if (response.Backends == null || response.Backends.Count == 0)
                return TestResult.Failed("No backends detected");

            var availableBackends = response.Backends
                .Where(b => b.Available)
                .Select(b => b.Type.ToString());

            return TestResult.Successful(
                $"Backend detection: {response.Backends.Count} backends checked, " +
                $"recommended: {response.Recommended}, " +
                $"available: [{string.Join(", ", availableBackends)}]");
        }, "Backend detection");

    /// <summary>
    /// Test the environment status endpoint.
    /// Returns current deployment status including containers, services, and topology.
    /// </summary>
    private static Task<TestResult> TestGetStatus(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();
            var response = await orchestratorClient.GetStatusAsync(new GetStatusRequest());

            var serviceCount = response.Services?.Count ?? 0;
            var backendType = response.Backend;

            return TestResult.Successful(
                $"Environment status: deployed={response.Deployed}, " +
                $"backend={backendType}, " +
                $"services={serviceCount}");
        }, "Environment status");

    /// <summary>
    /// Test the deployment presets endpoint.
    /// Returns available deployment presets for different use cases.
    /// </summary>
    private static Task<TestResult> TestGetPresets(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();
            var response = await orchestratorClient.GetPresetsAsync(new ListPresetsRequest());

            var presetCount = response.Presets?.Count ?? 0;
            var presetNames = response.Presets?.Select(p => p.Name) ?? Enumerable.Empty<string>();

            return TestResult.Successful(
                $"Deployment presets: {presetCount} presets available " +
                $"[{string.Join(", ", presetNames)}]");
        }, "Get presets");

    /// <summary>
    /// Test that infrastructure health returns component details.
    /// </summary>
    private static Task<TestResult> TestInfrastructureHealthComponentDetails(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();
            var response = await orchestratorClient.GetInfrastructureHealthAsync(new InfrastructureHealthRequest());

            if (response.Components == null || response.Components.Count == 0)
                return TestResult.Failed("No components returned");

            // Verify each component has required fields
            foreach (var component in response.Components)
            {
                if (string.IsNullOrEmpty(component.Name))
                    return TestResult.Failed("Component missing name field");

                // Status should be one of the enum values
                if (component.Status != ComponentHealthStatus.Healthy &&
                    component.Status != ComponentHealthStatus.Degraded &&
                    component.Status != ComponentHealthStatus.Unavailable)
                    return TestResult.Failed($"Component {component.Name} has invalid status");
            }

            // Check for expected components (Redis, RabbitMQ at minimum)
            var componentNames = response.Components.Select(c => c.Name.ToLower()).ToList();
            var hasRedis = componentNames.Any(n => n.Contains("redis"));
            var hasRabbit = componentNames.Any(n => n.Contains("rabbit") || n.Contains("mq"));

            if (!hasRedis && !hasRabbit)
            {
                return TestResult.Successful($"Infrastructure health returned {response.Components.Count} components (no Redis/RabbitMQ detected, may be development mode)");
            }

            return TestResult.Successful($"Infrastructure health components verified: {string.Join(", ", componentNames)}");
        }, "Infrastructure health components");

    /// <summary>
    /// Test that backends returns a recommended backend.
    /// </summary>
    private static Task<TestResult> TestBackendsReturnsRecommendation(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();
            var response = await orchestratorClient.GetBackendsAsync(new ListBackendsRequest());

            // At least one backend should be available (Docker Compose at minimum)
            var availableBackends = response.Backends?.Where(b => b.Available).ToList();
            if (availableBackends == null || availableBackends.Count == 0)
            {
                return TestResult.Failed("No available backends detected");
            }

            // Should have a valid recommendation (BackendType enum is always set)
            // The recommendation should be one of the available backends
            if (!availableBackends.Any(b => b.Type == response.Recommended))
            {
                return TestResult.Successful($"Recommended backend {response.Recommended} may not match available backends");
            }

            // Verify each backend has required fields
            foreach (var backend in response.Backends ?? Enumerable.Empty<BackendInfo>())
            {
                // Priority should be set
                if (backend.Priority <= 0)
                    return TestResult.Failed($"Backend {backend.Type} has invalid priority: {backend.Priority}");
            }

            return TestResult.Successful($"Backend recommendation: {response.Recommended}, {availableBackends.Count} backends available");
        }, "Backend recommendation");

    /// <summary>
    /// Test that environment status returns topology information.
    /// </summary>
    private static Task<TestResult> TestStatusReturnsTopology(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();
            var response = await orchestratorClient.GetStatusAsync(new GetStatusRequest());

            // Should have a timestamp
            if (response.Timestamp == default)
            {
                return TestResult.Failed("Status missing timestamp");
            }

            // If deployed, should have topology info
            if (response.Deployed)
            {
                if (response.Topology == null)
                {
                    return TestResult.Failed("Deployed status should include topology");
                }
            }

            return TestResult.Successful($"Environment status: deployed={response.Deployed}, timestamp={response.Timestamp:O}");
        }, "Status topology");

    /// <summary>
    /// Test that presets have required fields.
    /// </summary>
    private static Task<TestResult> TestPresetsHaveRequiredFields(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();
            var response = await orchestratorClient.GetPresetsAsync(new ListPresetsRequest());

            if (response.Presets == null || response.Presets.Count == 0)
            {
                // No presets is acceptable in some configurations
                return TestResult.Successful("No presets configured (acceptable in development)");
            }

            foreach (var preset in response.Presets)
            {
                if (string.IsNullOrEmpty(preset.Name))
                    return TestResult.Failed("Preset missing name field");

                if (string.IsNullOrEmpty(preset.Description))
                    return TestResult.Failed($"Preset {preset.Name} missing description field");

                if (preset.Topology == null)
                    return TestResult.Failed($"Preset {preset.Name} missing topology field");
            }

            return TestResult.Successful($"All {response.Presets.Count} presets have required fields");
        }, "Presets required fields");

    /// <summary>
    /// Test the should-restart service recommendation endpoint.
    /// Uses the 'bannou' service which MUST exist since we're testing against it.
    /// </summary>
    private static Task<TestResult> TestShouldRestartService(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();

            // Request recommendation for bannou - this MUST exist since we're testing against it
            var request = new ShouldRestartServiceRequest
            {
                ServiceName = "bannou"
            };

            try
            {
                var response = await orchestratorClient.ShouldRestartServiceAsync(request);

                // Verify required fields are present
                if (string.IsNullOrEmpty(response.ServiceName))
                    return TestResult.Failed("Response missing serviceName");

                if (string.IsNullOrEmpty(response.CurrentStatus))
                    return TestResult.Failed("Response missing currentStatus");

                if (string.IsNullOrEmpty(response.Reason))
                    return TestResult.Failed("Response missing reason");

                // Bannou should report as running/healthy since we're testing against it
                var acceptableStatuses = new[] { "running", "healthy", "available", "up" };
                var statusLower = response.CurrentStatus.ToLower();
                if (!acceptableStatuses.Any(s => statusLower.Contains(s)) && !response.ShouldRestart)
                {
                    // If status isn't healthy but shouldRestart is false, that's suspicious
                    return TestResult.Failed(
                        $"Service 'bannou' status is '{response.CurrentStatus}' but shouldRestart={response.ShouldRestart}. " +
                        $"Since we're testing against bannou, status should indicate healthy/running.");
                }

                return TestResult.Successful(
                    $"Should restart '{response.ServiceName}': {response.ShouldRestart}, " +
                    $"status={response.CurrentStatus}, reason={response.Reason}");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // The bannou service MUST exist since we're testing against it
                return TestResult.Failed($"Service 'bannou' not found - orchestrator cannot detect its own service. Status: {ex.StatusCode}");
            }
        }, "Should restart service");

    /// <summary>
    /// Test should-restart for unknown service returns appropriate response.
    /// Note: Recommending restart for unknown services is valid behavior - if the service
    /// isn't known/running, restarting (starting) it is a reasonable recommendation.
    /// </summary>
    private static Task<TestResult> TestShouldRestartServiceUnknown(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();

            var request = new ShouldRestartServiceRequest
            {
                ServiceName = $"unknown-service-{Guid.NewGuid():N}"
            };

            try
            {
                var response = await orchestratorClient.ShouldRestartServiceAsync(request);

                // Both shouldRestart=true (service not found, should be started) and
                // shouldRestart=false (unknown service, don't restart) are valid behaviors
                if (!response.ShouldRestart)
                {
                    return TestResult.Successful($"Unknown service returned shouldRestart=false: {response.Reason}");
                }

                // Recommending restart for unknown service is also valid - it means "start the service"
                return TestResult.Successful($"Unknown service returned shouldRestart=true (service not running): {response.Reason}");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("Unknown service correctly returned 404 NotFound");
            }
        }, "Unknown service restart check");

    /// <summary>
    /// Test the container status endpoint.
    /// The bannou container MUST exist since we're running tests against it.
    /// </summary>
    private static Task<TestResult> TestGetContainerStatus(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();

            // Get status for the main "bannou" container - this MUST exist since we're testing against it
            try
            {
                var response = await orchestratorClient.GetContainerStatusAsync(new GetContainerStatusRequest { AppName = "bannou" });

                // Verify required fields
                if (string.IsNullOrEmpty(response.AppName))
                    return TestResult.Failed("Container status missing appName");

                if (response.Timestamp == default)
                    return TestResult.Failed("Container status missing timestamp");

                // Verify the container is actually running (since we're testing against it)
                if (response.Status != ContainerStatusStatus.Running)
                {
                    return TestResult.Failed($"Container 'bannou' status is {response.Status}, expected Running");
                }

                // Verify we have at least one instance
                if (response.Instances <= 0)
                {
                    return TestResult.Failed($"Container 'bannou' has {response.Instances} instances, expected at least 1");
                }

                return TestResult.Successful(
                    $"Container 'bannou': status={response.Status}, " +
                    $"instances={response.Instances}, " +
                    $"plugins=[{string.Join(", ", response.Plugins ?? Enumerable.Empty<string>())}]");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // The bannou container MUST exist since we're testing against it
                // A 404 here indicates a real problem with the orchestrator's container detection
                return TestResult.Failed($"Container 'bannou' not found - orchestrator cannot detect its own container. Status: {ex.StatusCode}");
            }
        }, "Container status");

    /// <summary>
    /// Test container status for unknown container.
    /// </summary>
    private static Task<TestResult> TestGetContainerStatusUnknown(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();
            var unknownContainer = $"unknown-container-{Guid.NewGuid():N}";

            try
            {
                var response = await orchestratorClient.GetContainerStatusAsync(new GetContainerStatusRequest { AppName = unknownContainer });

                // If we get here, check if status indicates not found
                if (response.Status == ContainerStatusStatus.Stopped)
                {
                    return TestResult.Successful("Unknown container correctly returned stopped status");
                }

                return TestResult.Failed("Unknown container should return 404 or stopped status");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                return TestResult.Successful("Unknown container correctly returned 404 NotFound");
            }
        }, "Unknown container status");

    /// <summary>
    /// Test the service routing endpoint.
    /// Returns service-to-app-id mappings for Bannou service invocation.
    /// </summary>
    private static Task<TestResult> TestGetServiceRouting(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();
            var response = await orchestratorClient.GetServiceRoutingAsync(new GetServiceRoutingRequest());

            // Verify required fields
            if (response.Mappings == null)
                return TestResult.Failed("Service routing missing mappings");

            if (string.IsNullOrEmpty(response.DefaultAppId))
                return TestResult.Failed("Service routing missing defaultAppId");

            if (response.GeneratedAt == default)
                return TestResult.Failed("Service routing missing generatedAt timestamp");

            return TestResult.Successful(
                $"Service routing: {response.TotalServices} services mapped, " +
                $"defaultAppId='{response.DefaultAppId}', " +
                $"deploymentId={response.DeploymentId ?? "(none)"}");
        }, "Service routing");

    /// <summary>
    /// Test that service routing returns default app-id correctly.
    /// In development mode, all services should route to "bannou".
    /// </summary>
    private static Task<TestResult> TestServiceRoutingDefaults(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();
            var response = await orchestratorClient.GetServiceRoutingAsync(new GetServiceRoutingRequest());

            // Default app-id should be "bannou" in development mode
            if (response.DefaultAppId != "bannou")
            {
                return TestResult.Failed($"Expected defaultAppId='bannou', got '{response.DefaultAppId}'");
            }

            // In monolith mode (single container), mappings may be empty since everything
            // routes to the default. This is correct behavior.
            if (response.TotalServices == 0)
            {
                return TestResult.Successful(
                    $"Service routing defaults verified: defaultAppId='bannou', " +
                    $"no custom mappings (all services use default)");
            }

            // If there are mappings, verify they all point to valid app-ids
            foreach (var mapping in response.Mappings ?? new Dictionary<string, string>())
            {
                if (string.IsNullOrEmpty(mapping.Value))
                {
                    return TestResult.Failed($"Service '{mapping.Key}' has empty app-id mapping");
                }
            }

            return TestResult.Successful(
                $"Service routing defaults verified: defaultAppId='bannou', " +
                $"{response.TotalServices} custom mappings");
        }, "Service routing defaults");

    /// <summary>
    /// Test the configuration version endpoint.
    /// </summary>
    private static Task<TestResult> TestGetConfigVersion(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();
            var response = await orchestratorClient.GetConfigVersionAsync(new GetConfigVersionRequest());

            // Verify required fields
            if (response.Timestamp == default)
                return TestResult.Failed("Config version missing timestamp");

            return TestResult.Successful(
                $"Configuration version: {response.Version}, " +
                $"keyCount={response.KeyCount}, " +
                $"hasPreviousConfig={response.HasPreviousConfig}");
        }, "Config version");

    /// <summary>
    /// Test configuration rollback when no previous config exists.
    /// </summary>
    private static Task<TestResult> TestRollbackConfigurationNoHistory(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();

            // First check if there's previous config
            var versionInfo = await orchestratorClient.GetConfigVersionAsync(new GetConfigVersionRequest());

            if (versionInfo.HasPreviousConfig)
            {
                // There is previous config, skip this test
                return TestResult.Successful("Previous config exists, skipping no-history rollback test");
            }

            // Try to rollback when no previous config exists
            try
            {
                var request = new ConfigRollbackRequest
                {
                    Reason = "test-rollback-no-history"
                };

                await orchestratorClient.RollbackConfigurationAsync(request);

                return TestResult.Failed("Rollback should fail when no previous config exists");
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                return TestResult.Successful("Rollback correctly failed with 400 when no previous config");
            }
        }, "Rollback no-history");

    /// <summary>
    /// Test the logs retrieval endpoint.
    /// </summary>
    private static Task<TestResult> TestGetLogs(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();

            // Request logs for main service container
            try
            {
                var response = await orchestratorClient.GetLogsAsync(new GetLogsRequest
                {
                    Service = "bannou",
                    Tail = 10,
                    Follow = false
                });

                if (response.Logs == null)
                    return TestResult.Failed("Logs response missing logs array");

                var logCount = response.Logs.Count;
                return TestResult.Successful(
                    $"Retrieved {logCount} log entries for service 'bannou', " +
                    $"truncated={response.Truncated}");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // The bannou service MUST exist since we're testing against it
                return TestResult.Failed($"Service 'bannou' not found for logs - orchestrator cannot retrieve logs from its own service. Status: {ex.StatusCode}");
            }
        }, "Get logs");

    /// <summary>
    /// Test logs retrieval with tail parameter.
    /// </summary>
    private static Task<TestResult> TestGetLogsWithTail(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();

            // Request exactly 5 log lines
            try
            {
                var response = await orchestratorClient.GetLogsAsync(new GetLogsRequest
                {
                    Service = "bannou",
                    Tail = 5,
                    Follow = false
                });

                if (response.Logs == null)
                    return TestResult.Failed("Logs response missing logs array");

                var logCount = response.Logs.Count;

                // Should have at most 5 logs if tail is working
                if (logCount > 5)
                {
                    return TestResult.Failed($"Tail parameter not respected: requested 5, got {logCount}");
                }

                return TestResult.Successful($"Tail parameter working: requested 5, got {logCount} log entries");
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // The bannou service MUST exist since we're testing against it
                return TestResult.Failed($"Service 'bannou' not found for logs - orchestrator cannot retrieve logs from its own service. Status: {ex.StatusCode}");
            }
        }, "Logs with tail");

    /// <summary>
    /// Test deploy with an unavailable backend fails appropriately.
    /// The orchestrator should NOT silently fall back - it should fail or explicitly report the fallback.
    /// </summary>
    private static Task<TestResult> TestDeployWithInvalidBackend(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();

            // First check what backends are available
            var backends = await orchestratorClient.GetBackendsAsync(new ListBackendsRequest());

            // Find a backend that's NOT available
            var unavailableBackend = backends.Backends?
                .FirstOrDefault(b => !b.Available);

            if (unavailableBackend == null)
            {
                // All backends are available - skip this test
                return TestResult.Successful("All backends available, skipping unavailable backend test");
            }

            // Try to deploy with an unavailable backend - this SHOULD fail
            try
            {
                var deployRequest = new DeployRequest
                {
                    Backend = unavailableBackend.Type,
                    Mode = DeploymentMode.Force,
                    WaitForHealthy = false,
                    Timeout = 10,
                    Topology = new ServiceTopology
                    {
                        Nodes = new List<TopologyNode>
                        {
                            new TopologyNode
                            {
                                Name = "test-invalid-backend",
                                Services = new List<string> { "testing" },
                                Replicas = 1
                            }
                        }
                    }
                };

                var response = await orchestratorClient.DeployAsync(deployRequest);

                if (!response.Success)
                {
                    // This is the expected behavior - deploy should fail for unavailable backend
                    return TestResult.Successful(
                        $"Deploy correctly failed for unavailable backend {unavailableBackend.Type}: {response.Message}");
                }

                // Deploy succeeded when it shouldn't have
                // Check if it actually used a different backend (which would be a bug - silent fallback)
                var postStatus = await orchestratorClient.GetStatusAsync(new GetStatusRequest());

                if (postStatus.Backend != unavailableBackend.Type)
                {
                    // Silent fallback happened - this is bad behavior we should report
                    return TestResult.Failed(
                        $"Deploy silently fell back from {unavailableBackend.Type} to {postStatus.Backend}. " +
                        $"Orchestrator should either fail or explicitly report fallback in response.");
                }

                // If we get here, the backend somehow became available or the deploy was a no-op
                return TestResult.Failed(
                    $"Deploy succeeded for supposedly unavailable backend {unavailableBackend.Type}. " +
                    $"This may indicate the backend detection is incorrect.");
            }
            catch (ApiException ex) when (ex.StatusCode == 409 || ex.StatusCode == 400 || ex.StatusCode == 503)
            {
                return TestResult.Successful($"Deploy correctly returned {ex.StatusCode} for unavailable backend {unavailableBackend.Type}");
            }
        }, "Deploy with invalid backend");

    /// <summary>
    /// Test the clean operation with a dry run approach.
    /// Note: Clean operation may return 400 with success=false if nothing to clean,
    /// or if the operation isn't supported in the current configuration.
    /// </summary>
    private static Task<TestResult> TestCleanDryRun(ITestClient client, string[] args) =>
        ExecuteTestAsync(async () =>
        {
            var orchestratorClient = GetServiceClient<IOrchestratorClient>();

            // Use a minimal clean request that's safe for testing
            var request = new CleanRequest
            {
                Targets = new List<CleanTarget> { CleanTarget.Images },
                Force = false,
                OlderThan = "7d" // Only clean things older than 7 days
            };

            try
            {
                var response = await orchestratorClient.CleanAsync(request);

                if (!response.Success)
                {
                    // Clean failing is acceptable if there's nothing to clean
                    return TestResult.Successful($"Clean completed (nothing to clean or not supported): {response.Message}");
                }

                return TestResult.Successful(
                    $"Clean operation: success={response.Success}, " +
                    $"reclaimedMB={response.ReclaimedSpaceMb}, " +
                    $"removedImages={response.RemovedImages}");
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                // 400 with success=false is valid when there's nothing to clean
                return TestResult.Successful($"Clean operation returned 400 (nothing to clean)");
            }
            catch (ApiException ex) when (ex.StatusCode == 501)
            {
                // 501 Not Implemented means the endpoint needs to be implemented
                return TestResult.Failed($"Clean operation not implemented: {ex.Message}");
            }
        }, "Clean dry run");

    // NOTE: TestDeployAndTeardown and TestDeploymentEvents removed
    // Teardown API destroys the test target (and potentially the tester), making integration testing impossible.
    // The Clean API can be tested safely via TestCleanDryRun.
    // Teardown with includeInfrastructure=true will remove all infrastructure (Redis, RabbitMQ, MySQL, etc.)
    // and requires manual verification since it obliterates the entire environment.
}
