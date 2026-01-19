using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http;
using Xunit;

namespace BeyondImmersion.BannouService.Tests;

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
    /// Helper to set up common test services including AppConfiguration.
    /// </summary>
    private static void AddTestServices(ServiceCollection services)
    {
        services.AddLogging();
        services.AddSingleton<AppConfiguration>();
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
        AddTestServices(services);

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
        AddTestServices(services);
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Act & Assert
        Assert.Equal("bannou", resolver.GetAppIdForService("account"));
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
        AddTestServices(services);
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Verify default first
        Assert.Equal("bannou", resolver.GetAppIdForService("account"));

        // Act - Update the mapping to point to a different app-id
        resolver.UpdateServiceMapping("account", "bannou-account-node");

        // Assert - Now it should return the new mapping
        Assert.Equal("bannou-account-node", resolver.GetAppIdForService("account"));

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
        AddTestServices(services);
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Act - Map different services to different nodes
        resolver.UpdateServiceMapping("account", "bannou-auth-account-node");
        resolver.UpdateServiceMapping("auth", "bannou-auth-account-node");
        resolver.UpdateServiceMapping("behavior", "bannou-behavior-node");

        // Assert - Each service routes to its designated node
        Assert.Equal("bannou-auth-account-node", resolver.GetAppIdForService("account"));
        Assert.Equal("bannou-auth-account-node", resolver.GetAppIdForService("auth"));
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
        AddTestServices(services);
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Set up a mapping first
        resolver.UpdateServiceMapping("account", "bannou-account-node");
        Assert.Equal("bannou-account-node", resolver.GetAppIdForService("account"));

        // Act - Remove the mapping
        resolver.RemoveServiceMapping("account");

        // Assert - Should revert to default
        Assert.Equal("bannou", resolver.GetAppIdForService("account"));
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
        AddTestServices(services);
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Initially should have no mappings
        var initialMappings = resolver.GetAllMappings();
        Assert.Empty(initialMappings);

        // Act - Add some mappings
        resolver.UpdateServiceMapping("account", "node-1");
        resolver.UpdateServiceMapping("auth", "node-2");

        var currentMappings = resolver.GetAllMappings();

        // Assert
        Assert.Equal(2, currentMappings.Count);
        Assert.Equal("node-1", currentMappings["account"]);
        Assert.Equal("node-2", currentMappings["auth"]);

        // Verify removal updates the mapping state
        resolver.RemoveServiceMapping("account");
        var afterRemoval = resolver.GetAllMappings();
        Assert.Single(afterRemoval);
        Assert.False(afterRemoval.ContainsKey("account"));
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
        AddTestServices(services);
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
        AddTestServices(services);
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
        AddTestServices(services);
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Verify default first
        Assert.Equal("bannou", resolver.GetAppIdForService("auth"));
        Assert.Equal("bannou", resolver.GetAppIdForService("account"));

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
        Assert.Equal("bannou", resolver.GetAppIdForService("account"));
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
        AddTestServices(services);
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
        AddTestServices(services);
        services.AddServiceAppMappingResolver();
        var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<IServiceAppMappingResolver>();

        // Verify all default initially
        Assert.Equal("bannou", resolver.GetAppIdForService("auth"));
        Assert.Equal("bannou", resolver.GetAppIdForService("account"));
        Assert.Equal("bannou", resolver.GetAppIdForService("game-session"));

        // Simulate orchestrator deploying split-auth-routing-test preset
        // This sends mapping events for auth and account to bannou-auth node
        var mappingEvents = new[]
        {
            new ServiceMappingEventTestData { ServiceName = "auth", AppId = "bannou-auth", Action = "Register" },
            new ServiceMappingEventTestData { ServiceName = "account", AppId = "bannou-auth", Action = "Register" },
        };

        // Act - Apply all mapping events
        foreach (var evt in mappingEvents)
        {
            resolver.UpdateServiceMapping(evt.ServiceName, evt.AppId);
        }

        // Assert - auth and account should route to bannou-auth
        Assert.Equal("bannou-auth", resolver.GetAppIdForService("auth"));
        Assert.Equal("bannou-auth", resolver.GetAppIdForService("account"));
        // game-session should still route to default bannou
        Assert.Equal("bannou", resolver.GetAppIdForService("game-session"));

        // Verify GetAllMappings returns correct state
        var mappings = resolver.GetAllMappings();
        Assert.Equal(2, mappings.Count);
        Assert.Equal("bannou-auth", mappings["auth"]);
        Assert.Equal("bannou-auth", mappings["account"]);
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
        AddTestServices(services);
        // Use ServiceClientsDependencyInjection which registers both resolver and clients
        ServiceClientsDependencyInjection.AddAllBannouServiceClients(services);

        // Mock HTTP client factory
        using var mockHttpClient = new HttpClient();
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
        var accountAppId = resolver.GetAppIdForService("account");
        Assert.Equal("bannou", accountAppId); // Development default
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
