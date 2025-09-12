using BeyondImmersion.BannouService.ServiceClients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.Http;
using Xunit;

namespace BeyondImmersion.BannouService.UnitTests;

/// <summary>
/// Unit tests for service client registration and resolution patterns.
/// Tests the critical architecture distinction between local service injection vs distributed client calls.
/// </summary>
public class ServiceClientResolutionTests
{
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

        // Act
        services.AddAllBannouServiceClients();
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
    /// Demonstrates the CORRECT pattern for service-to-service calls.
    /// Services should use HttpClient + ServiceAppMappingResolver, NOT direct interface injection.
    /// </summary>
    [Fact]
    public void CorrectServiceToServiceCallPattern_UsesHttpClientWithResolver()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAllBannouServiceClients();

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
    /// This validates the AddAllDaprServiceClients reflection-based discovery.
    /// </summary>
    [Fact]
    public void ServiceClientDiscovery_FindsClientTypesByNamingConvention()
    {
        // Arrange
        var assembly = typeof(ServiceClientExtensions).Assembly;

        // Act - Simulate the discovery logic from AddAllDaprServiceClients
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
public class ExampleAccountsClient : DaprServiceClientBase
{
    public ExampleAccountsClient(
        HttpClient httpClient,
        IServiceAppMappingResolver appMappingResolver,
        ILogger<ExampleAccountsClient> logger)
        : base(httpClient, appMappingResolver, logger, "accounts")
    {
    }

    /// <summary>
    /// Example of correct service-to-service call using Dapr routing.
    /// </summary>
    public async Task<CreateAccountResponse?> CreateAccountAsync(CreateAccountRequest request)
    {
        try
        {
            if (_httpClient == null)
                throw new InvalidOperationException("HttpClient is not available");

            // Use base class BaseUrl property which handles app-id resolution
            var jsonContent = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(request),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync($"{BaseUrl}/api/accounts/create", jsonContent);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return System.Text.Json.JsonSerializer.Deserialize<CreateAccountResponse>(responseContent);
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
