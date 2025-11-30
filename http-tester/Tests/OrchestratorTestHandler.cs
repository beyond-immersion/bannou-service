using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Testing;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

// Alias to resolve naming conflict with Orchestrator.TestResult
using TestResult = BeyondImmersion.BannouService.Testing.TestResult;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for orchestrator service API endpoints.
/// Tests the orchestrator service APIs directly via NSwag-generated OrchestratorClient.
///
/// Note: The orchestrator service must be running with a Dapr sidecar for these tests to work.
/// Use: docker compose -f provisioning/docker-compose.yml -f provisioning/docker-compose.services.yml -f provisioning/docker-compose.orchestrator.yml up
/// </summary>
public class OrchestratorTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return new[]
        {
            new ServiceTest(TestInfrastructureHealth, "InfrastructureHealth", "Orchestrator", "Test infrastructure health check endpoint"),
            new ServiceTest(TestServicesHealth, "ServicesHealth", "Orchestrator", "Test services health report endpoint"),
            new ServiceTest(TestGetBackends, "GetBackends", "Orchestrator", "Test backend detection endpoint"),
            new ServiceTest(TestGetStatus, "GetStatus", "Orchestrator", "Test environment status endpoint"),
            new ServiceTest(TestGetPresets, "GetPresets", "Orchestrator", "Test deployment presets endpoint"),
            new ServiceTest(TestDeployAndTeardown, "DeployAndTeardown", "Orchestrator", "Test deploy and teardown lifecycle"),
            new ServiceTest(TestDeploymentEvents, "DeploymentEvents", "Orchestrator", "Test deployment event publishing via RabbitMQ"),
        };
    }

    /// <summary>
    /// Test the infrastructure health check endpoint.
    /// Validates connectivity to Redis, RabbitMQ, and Dapr.
    /// </summary>
    private static async Task<TestResult> TestInfrastructureHealth(ITestClient client, string[] args)
    {
        try
        {
            var orchestratorClient = new OrchestratorClient();

            var response = await orchestratorClient.GetInfrastructureHealthAsync();

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
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Infrastructure health check failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Test the services health report endpoint.
    /// Returns health status of all services via Redis heartbeat monitoring.
    /// </summary>
    private static async Task<TestResult> TestServicesHealth(ITestClient client, string[] args)
    {
        try
        {
            var orchestratorClient = new OrchestratorClient();

            var response = await orchestratorClient.GetServicesHealthAsync();

            return TestResult.Successful(
                $"Services health report: {response.TotalServices} services, " +
                $"{response.HealthPercentage:F1}% healthy, " +
                $"{response.HealthyServices?.Count ?? 0} healthy, " +
                $"{response.UnhealthyServices?.Count ?? 0} unhealthy");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Services health check failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Test the backend detection endpoint.
    /// Returns available container orchestration backends (Compose, Docker, Kubernetes, etc.).
    /// </summary>
    private static async Task<TestResult> TestGetBackends(ITestClient client, string[] args)
    {
        try
        {
            var orchestratorClient = new OrchestratorClient();

            var response = await orchestratorClient.GetBackendsAsync();

            if (response.Backends == null || response.Backends.Count == 0)
                return TestResult.Failed("No backends detected");

            var availableBackends = response.Backends
                .Where(b => b.Available)
                .Select(b => b.Type.ToString());

            return TestResult.Successful(
                $"Backend detection: {response.Backends.Count} backends checked, " +
                $"recommended: {response.Recommended}, " +
                $"available: [{string.Join(", ", availableBackends)}]");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Backend detection failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Test the environment status endpoint.
    /// Returns current deployment status including containers, services, and topology.
    /// </summary>
    private static async Task<TestResult> TestGetStatus(ITestClient client, string[] args)
    {
        try
        {
            var orchestratorClient = new OrchestratorClient();

            var response = await orchestratorClient.GetStatusAsync();

            var serviceCount = response.Services?.Count ?? 0;
            var backendType = response.Backend;

            return TestResult.Successful(
                $"Environment status: deployed={response.Deployed}, " +
                $"backend={backendType}, " +
                $"services={serviceCount}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Environment status failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Test the deployment presets endpoint.
    /// Returns available deployment presets for different use cases.
    /// </summary>
    private static async Task<TestResult> TestGetPresets(ITestClient client, string[] args)
    {
        try
        {
            var orchestratorClient = new OrchestratorClient();

            var response = await orchestratorClient.GetPresetsAsync();

            var presetCount = response.Presets?.Count ?? 0;
            var presetNames = response.Presets?.Select(p => p.Name) ?? Enumerable.Empty<string>();

            return TestResult.Successful(
                $"Deployment presets: {presetCount} presets available " +
                $"[{string.Join(", ", presetNames)}]");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Get presets failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Test the full deploy and teardown lifecycle.
    /// This is the critical test for orchestrator functionality.
    /// Tests actual container deployment and teardown via the orchestrator service.
    /// </summary>
    private static async Task<TestResult> TestDeployAndTeardown(ITestClient client, string[] args)
    {
        try
        {
            var orchestratorClient = new OrchestratorClient();

            // Step 1: Get initial status
            var initialStatus = await orchestratorClient.GetStatusAsync();
            var initialServiceCount = initialStatus.Services?.Count ?? 0;

            // Step 2: Create a deploy request for a test container
            var deployRequest = new DeployRequest
            {
                Backend = BackendType.Compose,
                Mode = DeploymentMode.Force,
                WaitForHealthy = false,
                Timeout = 30,
                Topology = new ServiceTopology
                {
                    Nodes = new List<TopologyNode>
                    {
                        new TopologyNode
                        {
                            Name = "bannou-test-node",
                            Services = new List<string> { "testing" },
                            Replicas = 1,
                            DaprEnabled = true
                        }
                    }
                }
            };

            // Step 3: Execute deploy
            var deployResponse = await orchestratorClient.DeployAsync(deployRequest);

            if (!deployResponse.Success)
            {
                return TestResult.Failed($"Deploy failed: {deployResponse.Message}");
            }

            // Step 4: Verify deployment by checking status
            var postDeployStatus = await orchestratorClient.GetStatusAsync();

            // Step 5: Teardown
            var teardownRequest = new TeardownRequest
            {
                Mode = TeardownMode.Graceful,
                RemoveVolumes = false,
                RemoveNetworks = false,
                Timeout = 30
            };

            var teardownResponse = await orchestratorClient.TeardownAsync(teardownRequest);

            if (!teardownResponse.Success)
            {
                return TestResult.Failed($"Teardown failed: {teardownResponse.Message}");
            }

            return TestResult.Successful(
                $"Deploy/Teardown lifecycle completed: " +
                $"initial={initialServiceCount} services, " +
                $"deployed={postDeployStatus.Services?.Count ?? 0} services, " +
                $"teardown success={teardownResponse.Success}");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Deploy/Teardown failed: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Test that deployment events are published to RabbitMQ during deploy/teardown operations.
    /// This test subscribes directly to the deployment events exchange to verify event publishing.
    /// </summary>
    private static async Task<TestResult> TestDeploymentEvents(ITestClient client, string[] args)
    {
        const string DEPLOYMENT_EXCHANGE = "bannou-deployment-events";
        var connectionString = Environment.GetEnvironmentVariable("BANNOU_RabbitMqConnectionString")
            ?? Environment.GetEnvironmentVariable("RabbitMqConnectionString")
            ?? "amqp://guest:guest@rabbitmq:5672";

        IConnection? connection = null;
        IChannel? channel = null;

        try
        {
            // Step 1: Connect to RabbitMQ and create a consumer for deployment events
            var factory = new ConnectionFactory
            {
                Uri = new Uri(connectionString),
                AutomaticRecoveryEnabled = true
            };

            connection = await factory.CreateConnectionAsync();
            channel = await connection.CreateChannelAsync();

            // Ensure the exchange exists (same config as orchestrator)
            await channel.ExchangeDeclareAsync(DEPLOYMENT_EXCHANGE, ExchangeType.Fanout, durable: true);

            // Create exclusive queue that auto-deletes when consumer disconnects
            var queueDeclareResult = await channel.QueueDeclareAsync(
                queue: "",  // Let RabbitMQ generate a unique name
                durable: false,
                exclusive: true,
                autoDelete: true);
            var queueName = queueDeclareResult.QueueName;

            // Bind queue to the deployment events exchange
            await channel.QueueBindAsync(queueName, DEPLOYMENT_EXCHANGE, "");

            // Set up event collection
            var receivedEvents = new ConcurrentBag<DeploymentEvent>();
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (sender, eventArgs) =>
            {
                try
                {
                    var body = eventArgs.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    var deploymentEvent = JsonSerializer.Deserialize<DeploymentEvent>(message);
                    if (deploymentEvent != null)
                    {
                        receivedEvents.Add(deploymentEvent);
                        Console.WriteLine($"  📬 Received deployment event: {deploymentEvent.Action} - {deploymentEvent.DeploymentId}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️ Failed to deserialize event: {ex.Message}");
                }
                return Task.CompletedTask;
            };

            await channel.BasicConsumeAsync(queueName, autoAck: true, consumer: consumer);
            Console.WriteLine("  🎧 Subscribed to deployment events exchange");

            // Step 2: Execute a deploy operation
            var orchestratorClient = new OrchestratorClient();
            var deployRequest = new DeployRequest
            {
                Backend = BackendType.Compose,
                Mode = DeploymentMode.Force,
                WaitForHealthy = false,
                Timeout = 30,
                Topology = new ServiceTopology
                {
                    Nodes = new List<TopologyNode>
                    {
                        new TopologyNode
                        {
                            Name = "bannou-event-test-node",
                            Services = new List<string> { "testing" },
                            Replicas = 1,
                            DaprEnabled = true
                        }
                    }
                }
            };

            Console.WriteLine("  🚀 Triggering deploy operation...");
            var deployResponse = await orchestratorClient.DeployAsync(deployRequest);

            // Give some time for events to be processed
            await Task.Delay(1000);

            // Step 3: Execute teardown
            var teardownRequest = new TeardownRequest
            {
                Mode = TeardownMode.Graceful,
                RemoveVolumes = false,
                RemoveNetworks = false,
                Timeout = 30
            };

            Console.WriteLine("  🛑 Triggering teardown operation...");
            await orchestratorClient.TeardownAsync(teardownRequest);

            // Give some time for events to be processed
            await Task.Delay(1000);

            // Step 4: Verify events were received
            var eventsList = receivedEvents.ToList();
            var startedEvents = eventsList.Count(e => e.Action == DeploymentEventAction.Started);
            var completedEvents = eventsList.Count(e => e.Action == DeploymentEventAction.Completed);
            var topologyChangedEvents = eventsList.Count(e => e.Action == DeploymentEventAction.TopologyChanged);
            var failedEvents = eventsList.Count(e => e.Action == DeploymentEventAction.Failed);

            Console.WriteLine($"  📊 Events received: Started={startedEvents}, Completed={completedEvents}, TopologyChanged={topologyChangedEvents}, Failed={failedEvents}");

            // Verify we received the expected events
            // Deploy: Started + Completed (or Failed)
            // Teardown: TopologyChanged + Completed (or Failed)
            if (eventsList.Count == 0)
            {
                return TestResult.Failed("No deployment events received - RabbitMQ connection may be failing");
            }

            if (startedEvents == 0)
            {
                return TestResult.Failed($"No 'started' deployment events received. Total events: {eventsList.Count}");
            }

            if (completedEvents + failedEvents == 0)
            {
                return TestResult.Failed($"No 'completed' or 'failed' deployment events received. Total events: {eventsList.Count}");
            }

            return TestResult.Successful(
                $"Deployment events verified: {eventsList.Count} events received " +
                $"(started={startedEvents}, completed={completedEvents}, topology_changed={topologyChangedEvents}, failed={failedEvents})");
        }
        catch (RabbitMQ.Client.Exceptions.BrokerUnreachableException ex)
        {
            return TestResult.Failed($"RabbitMQ not reachable: {ex.Message}. Ensure RabbitMQ is running and accessible.");
        }
        catch (ApiException ex)
        {
            return TestResult.Failed($"Orchestrator API error: {ex.StatusCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test exception: {ex.Message}", ex);
        }
        finally
        {
            // Clean up RabbitMQ resources
            if (channel != null)
            {
                await channel.CloseAsync();
                await channel.DisposeAsync();
            }
            if (connection != null)
            {
                await connection.CloseAsync();
                await connection.DisposeAsync();
            }
        }
    }
}
