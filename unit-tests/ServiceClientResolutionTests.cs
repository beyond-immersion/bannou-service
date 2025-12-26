using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http;
using Xunit;

namespace BeyondImmersion.BannouService.UnitTests;

[CollectionDefinition("ServiceAppMappingResolverCollection", DisableParallelization = true)]
public class ServiceAppMappingResolverCollection { }

/// <summary>
/// Unit tests for service client registration and resolution patterns.
/// Tests the critical architecture distinction between local service injection vs distributed client calls.
/// </summary>
[Collection("ServiceAppMappingResolverCollection")]
public class ServiceClientResolutionTests
{
    public ServiceClientResolutionTests()
    {
        ServiceAppMappingResolver.ClearAllMappingsForTests();
    }

    /// <summary>
    /// Tests that AddAllBannouServiceClients correctly registers service app mapping resolver.
    /// This is the foundation for distributed service routing.
    /// </summary>
    [Fact]
    public void AddAllBannouServiceClients_RegistersServiceAppMappingResolver()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act - Use ServiceClientsDependencyInjection which registers both resolver and clients
        ServiceClientsDependencyInjection.AddAllBannouServiceClients(services);
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var resolver = serviceProvider.GetService<IServiceAppMappingResolver>();
        Assert.NotNull(resolver);
        Assert.IsType<ServiceAppMappingResolver>(resolver);
    }

    /// <summary>
    /// Tests the service app mapping resolver's default routing behavior.
    /// In development, all services should route to "bannou" by default.
    /// </summary>
    [Fact]
    public void ServiceAppMappingResolver_DefaultsToOmnipotentBannouRouting()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Act & Assert
        Assert.Equal("bannou", resolver.GetAppIdForService("accounts"));
        Assert.Equal("bannou", resolver.GetAppIdForService("auth"));
        Assert.Equal("bannou", resolver.GetAppIdForService("behavior"));
        Assert.Equal("bannou", resolver.GetAppIdForService("nonexistent"));
    }

    /// <summary>
    /// Tests that UpdateServiceMapping correctly overrides the default app-id.
    /// This is the foundation for distributed multi-node deployments.
    /// </summary>
    [Fact]
    public void ServiceAppMappingResolver_UpdateServiceMapping_OverridesDefault()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Verify default first
        Assert.Equal("bannou", resolver.GetAppIdForService("accounts"));

        // Act - Update the mapping to point to a different app-id
        resolver.UpdateServiceMapping("accounts", "bannou-accounts-node");

        // Assert - Now it should return the new mapping
        Assert.Equal("bannou-accounts-node", resolver.GetAppIdForService("accounts"));

        // Other services should still use default
        Assert.Equal("bannou", resolver.GetAppIdForService("auth"));
        Assert.Equal("bannou", resolver.GetAppIdForService("behavior"));
    }

    /// <summary>
    /// Tests that multiple services can be mapped to different app-ids.
    /// This validates the split-service deployment scenario.
    /// </summary>
    [Fact]
    public void ServiceAppMappingResolver_UpdateServiceMapping_SupportsSplitDeployment()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Act - Map different services to different nodes
        resolver.UpdateServiceMapping("accounts", "bannou-auth-accounts-node");
        resolver.UpdateServiceMapping("auth", "bannou-auth-accounts-node");
        resolver.UpdateServiceMapping("behavior", "bannou-behavior-node");

        // Assert - Each service routes to its designated node
        Assert.Equal("bannou-auth-accounts-node", resolver.GetAppIdForService("accounts"));
        Assert.Equal("bannou-auth-accounts-node", resolver.GetAppIdForService("auth"));
        Assert.Equal("bannou-behavior-node", resolver.GetAppIdForService("behavior"));

        // Unmapped services still default to bannou
        Assert.Equal("bannou", resolver.GetAppIdForService("game-session"));
    }

    /// <summary>
    /// Tests that RemoveServiceMapping reverts to the default app-id.
    /// This validates dynamic service unregistration.
    /// </summary>
    [Fact]
    public void ServiceAppMappingResolver_RemoveServiceMapping_RevertsToDefault()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Set up a mapping first
        resolver.UpdateServiceMapping("accounts", "bannou-accounts-node");
        Assert.Equal("bannou-accounts-node", resolver.GetAppIdForService("accounts"));

        // Act - Remove the mapping
        resolver.RemoveServiceMapping("accounts");

        // Assert - Should revert to default
        Assert.Equal("bannou", resolver.GetAppIdForService("accounts"));
    }

    /// <summary>
    /// Tests that GetAllMappings returns the current state of all mappings.
    /// This is useful for debugging and monitoring.
    /// </summary>
    [Fact]
    public void ServiceAppMappingResolver_GetAllMappings_ReturnsCurrentState()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Initially should have no mappings
        var initialMappings = resolver.GetAllMappings();
        Assert.Empty(initialMappings);

        // Act - Add some mappings
        resolver.UpdateServiceMapping("accounts", "node-1");
        resolver.UpdateServiceMapping("auth", "node-2");

        var currentMappings = resolver.GetAllMappings();

        // Assert
        Assert.Equal(2, currentMappings.Count);
        Assert.Equal("node-1", currentMappings["accounts"]);
        Assert.Equal("node-2", currentMappings["auth"]);

        // Verify removal updates the mapping state
        resolver.RemoveServiceMapping("accounts");
        var afterRemoval = resolver.GetAllMappings();
        Assert.Single(afterRemoval);
        Assert.False(afterRemoval.ContainsKey("accounts"));
        Assert.True(afterRemoval.ContainsKey("auth"));
    }

    /// <summary>
    /// Tests that UpdateServiceMapping handles edge cases properly.
    /// </summary>
    [Fact]
    public void ServiceAppMappingResolver_UpdateServiceMapping_HandlesEdgeCases()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Act & Assert - Empty or null service names should not throw, but should not create mapping
        resolver.UpdateServiceMapping("", "some-app");
        resolver.UpdateServiceMapping("   ", "some-app");

        // These should not create mappings
        var mappings = resolver.GetAllMappings();
        Assert.Empty(mappings);

        // Empty app-id should not create mapping
        resolver.UpdateServiceMapping("valid-service", "");
        resolver.UpdateServiceMapping("valid-service", "   ");

        mappings = resolver.GetAllMappings();
        Assert.Empty(mappings);

        // Valid mapping should work
        resolver.UpdateServiceMapping("valid-service", "valid-app");
        mappings = resolver.GetAllMappings();
        Assert.Single(mappings);
        Assert.Equal("valid-app", mappings["valid-service"]);
    }

    /// <summary>
    /// Tests null service name handling in GetAppIdForService.
    /// </summary>
    [Fact]
    public void ServiceAppMappingResolver_GetAppIdForService_HandlesNullServiceName()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Act & Assert - null should return default
        Assert.Equal("bannou", resolver.GetAppIdForService(null));
        Assert.Equal("bannou", resolver.GetAppIdForService(""));
        Assert.Equal("bannou", resolver.GetAppIdForService("   "));
    }

    /// <summary>
    /// Tests that ServiceMappingEvent with Register action updates the resolver.
    /// This validates the event-driven mapping update pattern used by the orchestrator.
    /// </summary>
    [Fact]
    public void ServiceAppMappingResolver_ServiceMappingEvent_Register_UpdatesMapping()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Verify default first
        Assert.Equal("bannou", resolver.GetAppIdForService("auth"));
        Assert.Equal("bannou", resolver.GetAppIdForService("accounts"));

        // Simulate receiving ServiceMappingEvent with Register action
        // (This is what the ServiceMappingEventsController should do)
        var mappingEvent = new ServiceMappingEventTestData
        {
            ServiceName = "auth",
            AppId = "bannou-auth",
            Action = "Register"
        };

        // Act - Apply the mapping event (simulating controller behavior)
        resolver.UpdateServiceMapping(mappingEvent.ServiceName, mappingEvent.AppId);

        // Assert - Mapping should be updated
        Assert.Equal("bannou-auth", resolver.GetAppIdForService("auth"));
        // Other services should still use default
        Assert.Equal("bannou", resolver.GetAppIdForService("accounts"));
    }

    /// <summary>
    /// Tests that ServiceMappingEvent with Unregister action removes the mapping.
    /// This validates reverting to default when a split node goes offline.
    /// </summary>
    [Fact]
    public void ServiceAppMappingResolver_ServiceMappingEvent_Unregister_RemovesMapping()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Set up initial mapping (simulating previous Register event)
        resolver.UpdateServiceMapping("auth", "bannou-auth");
        Assert.Equal("bannou-auth", resolver.GetAppIdForService("auth"));

        // Simulate receiving ServiceMappingEvent with Unregister action
        var mappingEvent = new ServiceMappingEventTestData
        {
            ServiceName = "auth",
            AppId = "bannou-auth",
            Action = "Unregister"
        };

        // Act - Apply the unregister event
        resolver.RemoveServiceMapping(mappingEvent.ServiceName);

        // Assert - Should revert to default
        Assert.Equal("bannou", resolver.GetAppIdForService("auth"));
    }

    /// <summary>
    /// Tests that multiple ServiceMappingEvents correctly update a split deployment.
    /// This validates the full deployment scenario where orchestrator sends multiple events.
    /// </summary>
    [Fact]
    public void ServiceAppMappingResolver_MultipleServiceMappingEvents_SplitDeployment()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Verify all default initially
        Assert.Equal("bannou", resolver.GetAppIdForService("auth"));
        Assert.Equal("bannou", resolver.GetAppIdForService("accounts"));
        Assert.Equal("bannou", resolver.GetAppIdForService("game-session"));

        // Simulate orchestrator deploying split-auth-routing-test preset
        // This sends mapping events for auth and accounts to bannou-auth node
        var mappingEvents = new[]
        {
            new ServiceMappingEventTestData { ServiceName = "auth", AppId = "bannou-auth", Action = "Register" },
            new ServiceMappingEventTestData { ServiceName = "accounts", AppId = "bannou-auth", Action = "Register" },
        };

        // Act - Apply all mapping events
        foreach (var evt in mappingEvents)
        {
            resolver.UpdateServiceMapping(evt.ServiceName, evt.AppId);
        }

        // Assert - auth and accounts should route to bannou-auth
        Assert.Equal("bannou-auth", resolver.GetAppIdForService("auth"));
        Assert.Equal("bannou-auth", resolver.GetAppIdForService("accounts"));
        // game-session should still route to default bannou
        Assert.Equal("bannou", resolver.GetAppIdForService("game-session"));

        // Verify GetAllMappings returns correct state
        var mappings = resolver.GetAllMappings();
        Assert.Equal(2, mappings.Count);
        Assert.Equal("bannou-auth", mappings["auth"]);
        Assert.Equal("bannou-auth", mappings["accounts"]);
    }

    /// <summary>
    /// Test data class simulating ServiceMappingEvent from lib-orchestrator.
    /// Used in unit tests to avoid dependency on orchestrator library.
    /// </summary>
    public class ServiceMappingEventTestData
    {
        public string ServiceName { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
    }

    /// <summary>
    /// Demonstrates the CORRECT pattern for service-to-service calls.
    /// Services should use HttpClient + ServiceAppMappingResolver, NOT direct interface injection.
    /// </summary>
    [Fact]
    public void CorrectServiceToServiceCallPattern_UsesHttpClientWithResolver()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        // Use ServiceClientsDependencyInjection which registers both resolver and clients
        ServiceClientsDependencyInjection.AddAllBannouServiceClients(services);

        // Mock HTTP client factory
        var mockHttpClient = new HttpClient();
        var mockClientFactory = new Mock<IHttpClientFactory>();
        mockClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(mockHttpClient);
        services.AddSingleton(mockClientFactory.Object);

        var serviceProvider = services.BuildServiceProvider();

        // Act - Get the resolver that would be used in correct service implementation
        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        // Assert - This is how services SHOULD make calls
        Assert.NotNull(resolver);
        Assert.NotNull(httpClientFactory);

        // Verify routing behavior
        var accountsAppId = resolver.GetAppIdForService("accounts");
        Assert.Equal("bannou", accountsAppId); // Development default

        // This demonstrates the correct call pattern:
        // var httpClient = httpClientFactory.CreateClient("AuthService");
        // var baseUrl = $"http://localhost:3500/v1.0/invoke/{accountsAppId}/method";
        // var response = await httpClient.PostAsync($"{baseUrl}/api/accounts/create", content);
    }

    /// <summary>
    /// Tests that the service client registration can discover client types by naming convention.
    /// This validates the AddAllBannouServiceClients reflection-based discovery.
    /// </summary>
    [Fact]
    public void ServiceClientDiscovery_FindsClientTypesByNamingConvention()
    {
        // Arrange
        var assembly = typeof(ServiceClientExtensions).Assembly;

        // Act - Simulate the discovery logic from AddAllBannouServiceClients
        var clientTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Client"))
            .ToList();

        // Assert - Should find any existing client types
        // Currently we expect few/no client types since they should be generated
        Assert.NotNull(clientTypes);

        // Test the naming convention logic
        foreach (var clientType in clientTypes)
        {
            var serviceName = clientType.Name.Replace("Client", "").ToLowerInvariant();
            var interfaceName = $"I{clientType.Name}";

            Assert.NotEmpty(serviceName);
            Assert.NotEmpty(interfaceName);

            // Verify interface exists (if client exists)
            var interfaceType = assembly.GetType($"{clientType.Namespace}.{interfaceName}");
            if (interfaceType != null)
            {
                Assert.True(interfaceType.IsInterface);
            }
        }
    }
}

/// <summary>
/// Example of how a service client SHOULD be implemented for distributed calls.
/// This demonstrates the correct architecture pattern.
/// </summary>
public class ExampleAccountsClient : BannouServiceClientBase
{
    public ExampleAccountsClient(
        HttpClient httpClient,
        IServiceAppMappingResolver appMappingResolver,
        ILogger<ExampleAccountsClient> logger)
        : base(httpClient, appMappingResolver, logger, "accounts")
    {
    }

    /// <summary>
    /// Example of correct service-to-service call using Bannou routing.
    /// </summary>
    public async Task<CreateAccountResponse?> CreateAccountAsync(CreateAccountRequest request)
    {
        try
        {
            if (_httpClient == null)
                throw new InvalidOperationException("HttpClient is not available");

            // Use base class BaseUrl property which handles app-id resolution
            var jsonContent = new StringContent(
                BannouJson.Serialize(request),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync($"{BaseUrl}/api/accounts/create", jsonContent);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return BannouJson.Deserialize<CreateAccountResponse>(responseContent);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error calling accounts service");
            return null;
        }
    }
}

/// <summary>
/// Mock request/response models for testing
/// </summary>
public class CreateAccountRequest
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class CreateAccountResponse
{
    public string AccountId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
