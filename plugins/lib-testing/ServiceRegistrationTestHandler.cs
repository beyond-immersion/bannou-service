using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Test handler for service registration pattern infrastructure.
/// Tests the automatic permission registration mechanism in IBannouService.
/// </summary>
public class ServiceRegistrationTestHandler : IServiceTestHandler
{
    public ServiceTest[] GetServiceTests()
    {
        return
        [
            new ServiceTest(TestServiceRegistrationPattern, "Service Registration Pattern", "Infrastructure", "Tests automatic service permission registration"),
            new ServiceTest(TestCommonEventModels, "Common Event Models", "Infrastructure", "Tests common event model generation and access"),
            new ServiceTest(TestRegistrationMethodSignature, "Registration Method Signature", "Infrastructure", "Tests IBannouService.RegisterServicePermissionsAsync signature"),
            new ServiceTest(TestEventSerialization, "Event Serialization", "Infrastructure", "Tests ServiceRegistrationEvent serialization")
        ];
    }

    /// <summary>
    /// Tests the service registration pattern infrastructure.
    /// </summary>
    private static async Task<TestResult> TestServiceRegistrationPattern(ITestClient testClient, string[] args)
    {
        await Task.CompletedTask;
        try
        {
            Console.WriteLine("Testing service registration pattern...");

            // Test that IBannouService has RegisterServicePermissionsAsync method
            var bannouServiceInterface = typeof(IBannouService);
            var registrationMethod = bannouServiceInterface.GetMethods()
                .FirstOrDefault(m => m.Name == "RegisterServicePermissionsAsync");

            if (registrationMethod == null)
                return new TestResult(false, "IBannouService.RegisterServicePermissionsAsync method not found");

            // Verify method signature
            if (registrationMethod.ReturnType != typeof(Task))
                return new TestResult(false, "RegisterServicePermissionsAsync should return Task");

            if (registrationMethod.GetParameters().Length != 0)
                return new TestResult(false, "RegisterServicePermissionsAsync should have no parameters");

            Console.WriteLine("✓ IBannouService.RegisterServicePermissionsAsync method signature correct");

            // Test that method is virtual
            if (!registrationMethod.IsVirtual)
                return new TestResult(false, "RegisterServicePermissionsAsync should be virtual for override");

            Console.WriteLine("✓ RegisterServicePermissionsAsync is properly virtual");

            return new TestResult(true, "Service registration pattern infrastructure verified");
        }
        catch (Exception ex)
        {
            return new TestResult(false, $"Test failed with exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Tests that common event models are accessible from bannou-service.
    /// </summary>
    private static async Task<TestResult> TestCommonEventModels(ITestClient testClient, string[] args)
    {
        await Task.CompletedTask;
        try
        {
            Console.WriteLine("Testing common event models access...");

            // Test ServiceRegistrationEvent accessibility
            var serviceRegistrationEventType = typeof(ServiceRegistrationEvent);
            if (serviceRegistrationEventType == null)
                return new TestResult(false, "ServiceRegistrationEvent type not found");

            Console.WriteLine("✓ ServiceRegistrationEvent type accessible");

            // Test ServiceEndpoint accessibility
            var serviceEndpointType = typeof(ServiceEndpoint);
            if (serviceEndpointType == null)
                return new TestResult(false, "ServiceEndpoint type not found");

            Console.WriteLine("✓ ServiceEndpoint type accessible");

            // Test PermissionRequirement accessibility
            var permissionRequirementType = typeof(PermissionRequirement);
            if (permissionRequirementType == null)
                return new TestResult(false, "PermissionRequirement type not found");

            Console.WriteLine("✓ PermissionRequirement type accessible");

            // Test ServiceEndpointMethod enum accessibility
            var serviceEndpointMethodType = typeof(ServiceEndpointMethod);
            if (serviceEndpointMethodType == null)
                return new TestResult(false, "ServiceEndpointMethod enum not found");

            if (!serviceEndpointMethodType.IsEnum)
                return new TestResult(false, "ServiceEndpointMethod should be an enum");

            Console.WriteLine("✓ ServiceEndpointMethod enum accessible");

            return new TestResult(true, "Common event models accessible from bannou-service");
        }
        catch (Exception ex)
        {
            return new TestResult(false, $"Test failed with exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Tests the registration method signature matches expected pattern.
    /// </summary>
    private static async Task<TestResult> TestRegistrationMethodSignature(ITestClient testClient, string[] args)
    {
        await Task.CompletedTask;
        try
        {
            Console.WriteLine("Testing registration method signature...");

            // Verify IBannouService interface exists and has correct method
            var bannouServiceType = typeof(IBannouService);
            var methods = bannouServiceType.GetMethods().Where(m => m.Name.Contains("RegisterService")).ToArray();

            if (methods.Length == 0)
                return new TestResult(false, "No RegisterService methods found in IBannouService");

            var registrationMethod = methods.FirstOrDefault(m => m.Name == "RegisterServicePermissionsAsync");
            if (registrationMethod == null)
                return new TestResult(false, "RegisterServicePermissionsAsync method not found");

            // Verify it's a default implementation (has method body)
            if (registrationMethod.IsAbstract)
                return new TestResult(false, "RegisterServicePermissionsAsync should have default implementation");

            Console.WriteLine("✓ Registration method signature correct with default implementation");

            return new TestResult(true, "Registration method signature verified");
        }
        catch (Exception ex)
        {
            return new TestResult(false, $"Test failed with exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Tests ServiceRegistrationEvent serialization for RabbitMQ compatibility.
    /// </summary>
    private static async Task<TestResult> TestEventSerialization(ITestClient testClient, string[] args)
    {
        await Task.CompletedTask;
        try
        {
            Console.WriteLine("Testing event serialization...");

            // Create a test ServiceRegistrationEvent
            var testEvent = new ServiceRegistrationEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ServiceId = Guid.NewGuid(),
                ServiceName = "test-service",
                Version = "1.0.0",
                AppId = "bannou",
                Endpoints = new List<ServiceEndpoint>
                {
                    new ServiceEndpoint
                    {
                        Path = "/test/endpoint",
                        Method = ServiceEndpointMethod.GET,
                        Permissions = new List<PermissionRequirement>
                        {
                            new PermissionRequirement
                            {
                                Role = "user",
                                RequiredStates = new Dictionary<string, string>
                                {
                                    { "game-session", "in_game" }
                                }
                            }
                        },
                        Description = "Test endpoint",
                        Category = "test"
                    }
                },
                Metadata = new Dictionary<string, object>
                {
                    { "generatedFrom", "x-permissions" },
                    { "testMode", true }
                }
            };

            // Test JSON serialization
            var json = BannouJson.Serialize(testEvent);
            if (string.IsNullOrEmpty(json))
                return new TestResult(false, "ServiceRegistrationEvent serialization failed");

            Console.WriteLine("✓ ServiceRegistrationEvent serializes to JSON");

            // Test JSON deserialization
            var deserializedEvent = BannouJson.Deserialize<ServiceRegistrationEvent>(json);
            if (deserializedEvent == null)
                return new TestResult(false, "ServiceRegistrationEvent deserialization failed");

            if (deserializedEvent.ServiceId != testEvent.ServiceId)
                return new TestResult(false, "ServiceId mismatch after deserialization");

            if (deserializedEvent.Endpoints?.Count != 1)
                return new TestResult(false, "Endpoints count mismatch after deserialization");

            Console.WriteLine("✓ ServiceRegistrationEvent deserializes correctly");

            return new TestResult(true, "Event serialization verified");
        }
        catch (Exception ex)
        {
            return new TestResult(false, $"Test failed with exception: {ex.Message}");
        }
    }
}
