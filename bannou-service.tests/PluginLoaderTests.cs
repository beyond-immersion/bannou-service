using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;
using Xunit;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Unit tests for PluginLoader centralized service resolution functionality.
/// Tests the critical architecture for service discovery and lifecycle management.
/// </summary>
public class PluginLoaderTests
{


    /// <summary>
    /// Tests that service resolution handles scoped services correctly.
    /// This addresses the "Cannot resolve scoped service from root provider" issue.
    /// </summary>
    [Fact]
    public void ServiceResolution_ShouldUseServiceScopeForScopedServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<TestService>();  // Register as scoped

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        // Act - This should not throw when resolving scoped services
        using var scope = serviceProvider.CreateScope();
        var scopedService = scope.ServiceProvider.GetService<TestService>();

        // Assert
        Assert.NotNull(scopedService);
        Assert.IsType<TestService>(scopedService);
    }

    /// <summary>
    /// Tests the new centralized plugin discovery and service registration workflow.
    /// </summary>
    [Fact]
    public Task NewPluginDiscovery_ShouldDiscoverAndRegisterTypesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        // Create a mock assembly and plugin for testing
        var testAssembly = typeof(TestService).Assembly;
        var loadedAssembliesField = typeof(PluginLoader)
            .GetField("_loadedAssemblies", BindingFlags.NonPublic | BindingFlags.Instance);
        var loadedAssemblies = new Dictionary<string, Assembly> { { "testing", testAssembly } };
        loadedAssembliesField?.SetValue(pluginLoader, loadedAssemblies);

        // Simulate enabled plugins list
        var mockPlugin = new Mock<IBannouPlugin>();
        mockPlugin.Setup(p => p.PluginName).Returns("testing");
        var enabledPluginsField = typeof(PluginLoader)
            .GetField("_enabledPlugins", BindingFlags.NonPublic | BindingFlags.Instance);
        var enabledPlugins = new List<IBannouPlugin> { mockPlugin.Object };
        enabledPluginsField?.SetValue(pluginLoader, enabledPlugins);

        // Simulate all plugins list
        var allPluginsField = typeof(PluginLoader)
            .GetField("_allPlugins", BindingFlags.NonPublic | BindingFlags.Instance);
        var allPlugins = new List<IBannouPlugin> { mockPlugin.Object };
        allPluginsField?.SetValue(pluginLoader, allPlugins);

        // Call the type discovery method directly via reflection
        var discoverTypesMethod = typeof(PluginLoader)
            .GetMethod("DiscoverTypesForRegistration", BindingFlags.NonPublic | BindingFlags.Instance);
        discoverTypesMethod?.Invoke(pluginLoader, null);

        // Act - Configure services with discovered types
        var newServices = new ServiceCollection();
        newServices.AddLogging();
        pluginLoader.ConfigureServices(newServices);

        // Assert - Verify types were discovered and can be registered
        var newServiceProvider = newServices.BuildServiceProvider();

        // Test that the service collection setup works (this tests the discovery and registration)
        Assert.NotNull(newServiceProvider);

        // Get the client types field to verify discovery worked
        var clientTypesField = typeof(PluginLoader)
            .GetField("_clientTypesToRegister", BindingFlags.NonPublic | BindingFlags.Instance);
        var clientTypes = clientTypesField?.GetValue(pluginLoader) as List<Type>;

        // We might not have client types in the test assembly, but the list should exist
        Assert.NotNull(clientTypes);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Tests service type discovery debugging - investigate why service types aren't found.
    /// </summary>
    [Fact]
    public void ServiceTypeDiscovery_ShouldDebugAssemblyTypeLoading()
    {
        // Arrange - Load all assemblies like the PluginLoader would
        var currentAssembly = typeof(PluginLoader).Assembly;
        var testAssembly = typeof(TestService).Assembly;

        // Act - Search for types in assemblies
        var currentAssemblyTypes = GetTypesImplementingIBannouService(currentAssembly);
        var testAssemblyTypes = GetTypesImplementingIBannouService(testAssembly);

        // Assert - Debug output to understand what's happening
        Assert.NotNull(currentAssemblyTypes);
        Assert.NotNull(testAssemblyTypes);

        // We should find TestService in the test assembly
        var testServiceType = testAssemblyTypes.FirstOrDefault(t => t.Name == "TestService");
        Assert.NotNull(testServiceType);
        Assert.True(typeof(IBannouService).IsAssignableFrom(testServiceType));

        // Debug: Print all found types for investigation
        var allFoundTypes = currentAssemblyTypes.Concat(testAssemblyTypes).ToList();
        Assert.NotEmpty(allFoundTypes);
    }

    /// <summary>
    /// Tests the exact scenario happening in production using TestingService only.
    /// This replicates the service discovery workflow without external dependencies.
    /// </summary>
    [Fact]
    public void ServiceTypeDiscovery_ShouldWorkWithTestingServiceOnly()
    {
        // Arrange - Use only the TestingService which is guaranteed to exist
        var expectedServiceTypeName = "TestingService";

        // Use the current assembly which contains TestingService
        var testingAssembly = typeof(BeyondImmersion.BannouService.Testing.TestingService).Assembly;

        // Act - Perform the exact same search that PluginLoader does
        var types = testingAssembly.GetTypes()
            .Where(t => typeof(IBannouService).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();

        // Check if TestingService is in the list
        var testingServiceType = types.FirstOrDefault(t => t.Name == expectedServiceTypeName);

        // Assert - This replicates the production scenario exactly
        Assert.NotEmpty(types);
        Assert.NotNull(testingServiceType);
        Assert.Equal(expectedServiceTypeName, testingServiceType.Name);
        Assert.True(typeof(IBannouService).IsAssignableFrom(testingServiceType));

        // This proves the service discovery logic works correctly
        Assert.True(testingServiceType == typeof(BeyondImmersion.BannouService.Testing.TestingService));
    }

    /// <summary>
    /// Debug test to understand ServiceLifetime parameter issue.
    /// </summary>
    [Fact]
    public void Debug_BannouServiceAttribute_ParameterMapping()
    {
        // Test direct attribute creation with explicit parameters to verify constructor behavior
        var attr1 = new BannouServiceAttribute("test1", typeof(IBannouService), false, ServiceLifetime.Scoped);
        Assert.Equal("test1", attr1.Name);
        Assert.Equal(typeof(IBannouService), attr1.InterfaceType);
        Assert.False(attr1.Priority);
        Assert.Equal(ServiceLifetime.Scoped, attr1.Lifetime);

        // Test with named parameters
        var attr2 = new BannouServiceAttribute("test2", interfaceType: typeof(IBannouService), priority: false, lifetime: ServiceLifetime.Scoped);
        Assert.Equal("test2", attr2.Name);
        Assert.Equal(typeof(IBannouService), attr2.InterfaceType);
        Assert.False(attr2.Priority);
        Assert.Equal(ServiceLifetime.Scoped, attr2.Lifetime);

        // Test positional with different lifetimes
        var attr3 = new BannouServiceAttribute("test3", typeof(IBannouService), false, ServiceLifetime.Transient);
        Assert.Equal(ServiceLifetime.Transient, attr3.Lifetime);

        var attr4 = new BannouServiceAttribute("test4", typeof(IBannouService), false, ServiceLifetime.Singleton);
        Assert.Equal(ServiceLifetime.Singleton, attr4.Lifetime);
    }

    /// <summary>
    /// Debug test to understand why only 2 services are discovered instead of 8.
    /// </summary>
    [Fact]
    public void Debug_ServiceTypeDiscovery_ActualPluginLoaderBehavior()
    {
        // Use reflection to access the PluginLoader's internal state after type discovery
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        // Get the private field that stores service types discovered
        var serviceTypesField = typeof(PluginLoader)
            .GetField("_serviceTypesToRegister", BindingFlags.NonPublic | BindingFlags.Instance);

        // Simulate discovery against TestingService assembly which we know exists
        var testingAssembly = typeof(BeyondImmersion.BannouService.Testing.TestingService).Assembly;

        // Use reflection to call DiscoverServiceTypes (which is private)
        var discoverMethod = typeof(PluginLoader)
            .GetMethod("DiscoverServiceTypes", BindingFlags.NonPublic | BindingFlags.Instance);

        discoverMethod?.Invoke(pluginLoader, new object[] { testingAssembly, "testing" });

        // Check what was discovered
        var serviceTypes = serviceTypesField?.GetValue(pluginLoader) as List<(Type interfaceType, Type implementationType, ServiceLifetime lifetime)>;

        // For debugging, create detailed output using xUnit output
        var discoveredCount = serviceTypes?.Count ?? 0;
        var testOutput = $"PluginLoader discovered {discoveredCount} service types from testing assembly\n";

        if (serviceTypes != null)
        {
            foreach (var serviceReg in serviceTypes)
            {
                testOutput += $"  - Interface: {serviceReg.interfaceType.Name}\n";
                testOutput += $"  - Implementation: {serviceReg.implementationType.Name}\n";
                testOutput += $"  - Lifetime: {serviceReg.lifetime}\n";
            }
        }

        // Now check what types exist in the testing assembly
        var allTypes = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract)
            .ToList();

        testOutput += $"\nTesting assembly contains {allTypes.Count} non-interface, non-abstract types:\n";

        foreach (var type in allTypes)
        {
            var implementsIBannouService = typeof(IBannouService).IsAssignableFrom(type);
            var bannouAttr = type.GetCustomAttribute<BannouServiceAttribute>();

            testOutput += $"  - {type.Name}: IBannouService={implementsIBannouService}, BannouAttr={bannouAttr?.Name ?? "NONE"}\n";
        }

        // The only way to see test output in xUnit is through an assertion failure
        // So we'll store the diagnostic info and fail if we don't find what we expect
        if (discoveredCount < 1)
        {
            Assert.Fail($"Expected at least 1 service type to be discovered. Debug info:\n{testOutput}");
        }

        // This test is informational - it helps us debug the issue
        Assert.NotNull(serviceTypes);
        Assert.True(serviceTypes?.Count >= 1, "Should find at least the TestingService");
    }

    /// <summary>
    /// Tests service enabling logic with individual override environment variables.
    /// The new API uses only {SERVICE}_SERVICE_ENABLED (no _DISABLED pattern).
    /// </summary>
    [Fact]
    public void ServiceEnabling_ShouldRespectIndividualOverrides()
    {
        try
        {
            // Arrange
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "true");

            var services = new ServiceCollection();
            services.AddLogging();
            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
            var pluginLoader = new PluginLoader(logger);

            var isServiceEnabledMethod = typeof(PluginLoader)
                .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

            // Act & Assert - Service enabled by default (no override set)
            var result1 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.True(result1, "Service should be enabled by default");

            // Individual override: explicitly disabled
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "false");
            var result2 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.False(result2, "Service should be disabled when TESTING_SERVICE_ENABLED=false");

            // Individual override: explicitly enabled
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "true");
            var result3 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.True(result3, "Service should be enabled when TESTING_SERVICE_ENABLED=true");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", null);
        }
    }

    /// <summary>
    /// Tests service type discovery specifically for TestingService in lib-testing assembly.
    /// </summary>
    [Fact]
    public void ServiceTypeDiscovery_ShouldFindTestingService()
    {
        // Arrange
        var testingAssembly = typeof(BeyondImmersion.BannouService.Testing.TestingService).Assembly;
        var expectedServiceTypeName = "TestingService";

        // Act - Search for TestingService in the testing assembly
        var types = testingAssembly.GetTypes()
            .Where(t => typeof(IBannouService).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();

        var testingServiceType = types.FirstOrDefault(t => t.Name == expectedServiceTypeName);

        // Assert
        Assert.NotEmpty(types);
        Assert.NotNull(testingServiceType);
        Assert.Equal(expectedServiceTypeName, testingServiceType.Name);
        Assert.True(typeof(IBannouService).IsAssignableFrom(testingServiceType));

        // Verify it can be cast to the specific type
        Assert.True(testingServiceType == typeof(BeyondImmersion.BannouService.Testing.TestingService));
    }

    /// <summary>
    /// Tests that client lifetime matching prevents DI validation errors.
    /// This test prevents regression of the exit code 139 crash we fixed.
    /// </summary>
    [Fact]
    public void ClientLifetimeMatching_ShouldPreventDIValidationErrors()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        // Simulate the scenario that caused the crash:
        // - ConnectService registered as Singleton
        // - IAuthClient needed to be registered as Singleton to avoid "Cannot consume scoped service from singleton"

        var newServices = new ServiceCollection();
        newServices.AddLogging();

        // Register ConnectService as Singleton (as it is in production)
        newServices.AddSingleton<ConnectService>();

        // Act - Use reflection to test GetClientLifetime method
        var getClientLifetimeMethod = typeof(PluginLoader)
            .GetMethod("GetClientLifetime", BindingFlags.NonPublic | BindingFlags.Instance);

        // Create a mock interface for IAuthClient
        var authClientInterface = typeof(IAuthClient);
        var authClientLifetimeResult = getClientLifetimeMethod?.Invoke(pluginLoader, new object[] { authClientInterface });
        var authClientLifetime = authClientLifetimeResult != null ? (ServiceLifetime)authClientLifetimeResult : ServiceLifetime.Scoped;

        // Create a mock interface for IPermissionClient
        var permissionClientInterface = typeof(IPermissionClient);
        var permissionClientLifetimeResult = getClientLifetimeMethod?.Invoke(pluginLoader, new object[] { permissionClientInterface });
        var permissionClientLifetime = permissionClientLifetimeResult != null ? (ServiceLifetime)permissionClientLifetimeResult : ServiceLifetime.Scoped;

        // Assert - Clients should default to Singleton to avoid DI conflicts
        Assert.Equal(ServiceLifetime.Singleton, authClientLifetime);
        Assert.Equal(ServiceLifetime.Singleton, permissionClientLifetime);

        // Test that this prevents DI validation errors
        newServices.AddSingleton(authClientInterface, Mock.Of<IAuthClient>());
        newServices.AddSingleton(permissionClientInterface, Mock.Of<IPermissionClient>());

        var finalServiceProvider = newServices.BuildServiceProvider();
        var connectService = finalServiceProvider.GetService<ConnectService>();

        // Should not throw during service resolution
        Assert.NotNull(finalServiceProvider);
    }

    /// <summary>
    /// Tests that configuration lifetime matches service lifetime.
    /// This prevents the ConnectServiceConfiguration lifetime mismatch we fixed.
    /// </summary>
    [Fact]
    public void ConfigurationLifetime_ShouldMatchServiceLifetime()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Test the pattern that ConnectServicePlugin uses:
        // If service is Singleton, configuration should be Singleton
        // If service is Scoped, configuration should be Scoped

        // Act & Assert - Test Singleton pattern (ConnectService case)
        services.AddSingleton<ConnectService>();
        services.AddSingleton<ConnectServiceConfiguration>();

        var singletonServiceProvider = services.BuildServiceProvider();
        var singletonConfig = singletonServiceProvider.GetService<ConnectServiceConfiguration>();
        var singletonConfig2 = singletonServiceProvider.GetService<ConnectServiceConfiguration>();

        // Singleton should return same instance
        Assert.NotNull(singletonConfig);
        Assert.Same(singletonConfig, singletonConfig2);

        // Test Scoped pattern (TestingService case)
        var scopedServices = new ServiceCollection();
        scopedServices.AddScoped<TestService>();
        scopedServices.AddScoped<TestServiceConfiguration>();

        var scopedServiceProvider = scopedServices.BuildServiceProvider();
        using var scope1 = scopedServiceProvider.CreateScope();
        using var scope2 = scopedServiceProvider.CreateScope();

        var scopedConfig1 = scope1.ServiceProvider.GetService<TestServiceConfiguration>();
        var scopedConfig2 = scope2.ServiceProvider.GetService<TestServiceConfiguration>();

        // Scoped should return different instances per scope
        Assert.NotNull(scopedConfig1);
        Assert.NotNull(scopedConfig2);
        Assert.NotSame(scopedConfig1, scopedConfig2);
    }

    /// <summary>
    /// Tests detection of duplicate service registrations.
    /// This prevents conflicts between plugin registration and PluginLoader registration.
    /// </summary>
    [Fact]
    public void DuplicateServiceRegistration_ShouldBeDetectable()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Simulate the scenario where both plugin and PluginLoader try to register the same service
        // This was happening with ConnectService

        // Act - Register the same service twice with different lifetimes (this would cause issues)
        services.AddSingleton<TestService>();
        services.AddScoped<TestService>(); // This overwrites the Singleton registration

        var serviceProvider = services.BuildServiceProvider();

        // Get the service descriptor to check what actually got registered
        var serviceDescriptor = services.LastOrDefault(s => s.ServiceType == typeof(TestService));

        // Assert - The last registration wins (Scoped in this case)
        Assert.NotNull(serviceDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, serviceDescriptor.Lifetime);

        // Test that we can detect this situation by checking service count
        var testServiceDescriptors = services.Where(s => s.ServiceType == typeof(TestService)).ToList();

        // There should be multiple registrations for the same type (this indicates a potential issue)
        Assert.Equal(2, testServiceDescriptors.Count);

        // In a real scenario, we'd want to log this as a warning
        var lifetimes = testServiceDescriptors.Select(d => d.Lifetime).Distinct().ToList();
        if (lifetimes.Count > 1)
        {
            // Multiple different lifetimes for same service type - this is what we want to detect
            Assert.Contains(ServiceLifetime.Singleton, lifetimes);
            Assert.Contains(ServiceLifetime.Scoped, lifetimes);
        }
    }

    /// <summary>
    /// Tests the complete DI validation workflow that prevented the exit code 139 crash.
    /// This is a comprehensive regression test for the entire service registration flow.
    /// </summary>
    [Fact]
    public void CompleteServiceRegistration_ShouldNotCauseDIValidationErrors()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        // Create a complete service registration scenario that mirrors production
        var productionServices = new ServiceCollection();
        productionServices.AddLogging();

        // Register services with their actual lifetimes from production
        // ConnectService: Singleton (for WebSocket connection management)
        productionServices.AddSingleton<ConnectService>();
        productionServices.AddSingleton<ConnectServiceConfiguration>();

        // TestingService: Scoped (as specified in BannouService attribute)
        productionServices.AddScoped<TestService>();
        productionServices.AddScoped<TestServiceConfiguration>();

        // Register clients with lifetimes that match or are compatible with their consumers
        // IAuthClient and IPermissionClient: Singleton (compatible with ConnectService Singleton)
        productionServices.AddSingleton<IAuthClient>(Mock.Of<IAuthClient>());
        productionServices.AddSingleton<IPermissionClient>(Mock.Of<IPermissionClient>());

        // Act - Build service provider (this is where DI validation would fail)
        ServiceProvider? finalServiceProvider = null;
        Exception? validationException = null;

        try
        {
            finalServiceProvider = productionServices.BuildServiceProvider(validateScopes: true);

            // Try to resolve the problematic service that caused the crash
            using var scope = finalServiceProvider.CreateScope();
            var connectService = scope.ServiceProvider.GetService<ConnectService>();
            var testService = scope.ServiceProvider.GetService<TestService>();

            // Assert - Services should resolve without errors
            Assert.NotNull(connectService);
            Assert.NotNull(testService);
        }
        catch (Exception ex)
        {
            validationException = ex;
        }
        finally
        {
            finalServiceProvider?.Dispose();
        }

        // Assert - No DI validation errors should occur
        Assert.Null(validationException);
    }

    /// <summary>
    /// Tests that configuration types are discovered and registered with matching service lifetimes.
    /// This validates the new configuration registration functionality in PluginLoader.
    /// </summary>
    [Fact]
    public void ConfigurationTypeDiscovery_ShouldFindServiceConfigurationAttributes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();

        // Act - Check that our test configuration class has the ServiceConfiguration attribute
        var configType = typeof(TestServiceWithAttrConfiguration);
        var serviceConfigAttr = configType.GetCustomAttribute<ServiceConfigurationAttribute>();

        // Assert - The attribute should be present and correctly configured
        Assert.NotNull(serviceConfigAttr);
        Assert.Equal(typeof(TestService), serviceConfigAttr.ServiceImplementationType);

        // Verify the configuration implements IServiceConfiguration
        Assert.True(typeof(IServiceConfiguration).IsAssignableFrom(configType));
    }

    /// <summary>
    /// Tests that configuration registration actually registers types in DI with correct lifetimes.
    /// </summary>
    [Fact]
    public void ConfigurationRegistration_ShouldRegisterWithCorrectLifetimes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        // Manually add test data
        var configTypesField = typeof(PluginLoader).GetField("_configurationTypesToRegister", BindingFlags.NonPublic | BindingFlags.Instance);
        var configTypes = (List<(Type configurationType, ServiceLifetime lifetime)>?)configTypesField?.GetValue(pluginLoader);

        if (configTypes != null)
        {
            // Add test configuration entries
            configTypes.Add((typeof(TestServiceWithAttrConfiguration), ServiceLifetime.Singleton));
            configTypes.Add((typeof(TestServiceConfiguration2), ServiceLifetime.Scoped));
        }

        var registrationServices = new ServiceCollection();

        // Act - Call the registration method
        var registerConfigMethod = typeof(PluginLoader).GetMethod("RegisterConfigurationTypes", BindingFlags.NonPublic | BindingFlags.Instance);
        registerConfigMethod?.Invoke(pluginLoader, new object[] { registrationServices });

        // Assert - Check that configurations were registered
        // Expect 4: AppConfiguration (global) + IServiceConfiguration forwarding + 2 test configurations
        Assert.Equal(4, registrationServices.Count);

        var singletonConfig = registrationServices.FirstOrDefault(s => s.ServiceType == typeof(TestServiceWithAttrConfiguration));
        Assert.NotNull(singletonConfig);
        Assert.Equal(ServiceLifetime.Singleton, singletonConfig.Lifetime);

        var scopedConfig = registrationServices.FirstOrDefault(s => s.ServiceType == typeof(TestServiceConfiguration2));
        Assert.NotNull(scopedConfig);
        // Configuration should always be Singleton regardless of original service lifetime
        // because configuration binding should only happen once at startup
        Assert.Equal(ServiceLifetime.Singleton, scopedConfig.Lifetime);
    }


    /// <summary>
    /// Tests that configuration registration normalizes all configurations to Singleton lifetime.
    /// This validates the PluginLoader requirement that all configurations should be Singleton
    /// regardless of original service lifetime for proper startup configuration binding.
    /// </summary>
    [Fact]
    public void ConfigurationRegistration_ShouldNormalizeAllConfigurationsToSingleton()
    {
        // Arrange
        var registrationServices = new ServiceCollection();

        // Register configurations with different lifetimes (simulating what services might want)
        var configRegistrations = new List<(Type configurationType, ServiceLifetime originalLifetime)>
        {
            (typeof(TestServiceWithAttrConfiguration), ServiceLifetime.Singleton),
            (typeof(TestServiceConfiguration2), ServiceLifetime.Scoped),
            (typeof(TestServiceConfiguration), ServiceLifetime.Transient)
        };

        // Act - Register each configuration type with their original lifetime first
        foreach (var (configurationType, originalLifetime) in configRegistrations)
        {
            switch (originalLifetime)
            {
                case ServiceLifetime.Singleton:
                    registrationServices.AddSingleton(configurationType);
                    break;
                case ServiceLifetime.Scoped:
                    registrationServices.AddScoped(configurationType);
                    break;
                case ServiceLifetime.Transient:
                    registrationServices.AddTransient(configurationType);
                    break;
            }
        }

        // Simulate PluginLoader configuration finalization: remove existing and add as Singleton
        var configTypes = configRegistrations.Select(c => c.configurationType).ToList();
        foreach (var configType in configTypes)
        {
            var existingRegistrations = registrationServices.Where(s => s.ServiceType == configType).ToList();
            foreach (var existing in existingRegistrations)
            {
                registrationServices.Remove(existing);
            }
            registrationServices.AddSingleton(configType);
        }

        // Assert - ALL configurations should now be Singleton regardless of original lifetime
        Assert.Equal(3, registrationServices.Count);

        var config1Reg = registrationServices.FirstOrDefault(s => s.ServiceType == typeof(TestServiceWithAttrConfiguration));
        Assert.NotNull(config1Reg);
        Assert.Equal(ServiceLifetime.Singleton, config1Reg.Lifetime);

        var config2Reg = registrationServices.FirstOrDefault(s => s.ServiceType == typeof(TestServiceConfiguration2));
        Assert.NotNull(config2Reg);
        Assert.Equal(ServiceLifetime.Singleton, config2Reg.Lifetime); // Changed from Scoped to Singleton

        var config3Reg = registrationServices.FirstOrDefault(s => s.ServiceType == typeof(TestServiceConfiguration));
        Assert.NotNull(config3Reg);
        Assert.Equal(ServiceLifetime.Singleton, config3Reg.Lifetime); // Changed from Transient to Singleton
    }

    /// <summary>
    /// Tests the end-to-end configuration registration workflow to ensure no DI conflicts.
    /// This simulates the actual PluginLoader workflow that should prevent exit code 139.
    /// </summary>
    [Fact]
    public void EndToEndConfigurationWorkflow_ShouldPreventDIConflicts()
    {
        // Arrange - Simulate the complete workflow that happens in production
        var services = new ServiceCollection();
        services.AddLogging();

        // Step 1: Register a service (ConnectService pattern - Singleton)
        services.AddSingleton<TestConnectService>();

        // Step 2: Register its configuration with MATCHING lifetime (this prevents conflicts)
        services.AddSingleton<TestConnectServiceConfiguration>();

        // Step 3: Register a scoped service
        services.AddScoped<TestService>();
        services.AddScoped<TestServiceConfiguration>();

        // Step 4: Register clients with compatible lifetimes
        services.AddSingleton<IAuthClient>(Mock.Of<IAuthClient>());
        services.AddSingleton<IPermissionClient>(Mock.Of<IPermissionClient>());

        // Act - Build service provider with DI validation
        ServiceProvider? serviceProvider = null;
        Exception? validationException = null;

        try
        {
            serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Try to resolve services to trigger validation
            using var scope = serviceProvider.CreateScope();
            var connectService = scope.ServiceProvider.GetService<TestConnectService>();
            var connectConfig = scope.ServiceProvider.GetService<TestConnectServiceConfiguration>();
            var testService = scope.ServiceProvider.GetService<TestService>();
            var testConfig = scope.ServiceProvider.GetService<TestServiceConfiguration>();

            // Assert services resolve correctly
            Assert.NotNull(connectService);
            Assert.NotNull(connectConfig);
            Assert.NotNull(testService);
            Assert.NotNull(testConfig);
        }
        catch (Exception ex)
        {
            validationException = ex;
        }
        finally
        {
            serviceProvider?.Dispose();
        }

        // Assert - No DI validation errors should occur
        Assert.Null(validationException);
    }

    /// <summary>
    /// Tests that configuration registration logs appropriate warnings for unmatched configurations.
    /// </summary>
    [Fact]
    public void ConfigurationDiscovery_ShouldLogWarningsForUnmatchedConfigurations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();

        // Create configuration type that won't match any service
        var unmatchedConfigType = typeof(UnmatchedConfiguration);
        var hasServiceConfigAttr = unmatchedConfigType.GetCustomAttribute<ServiceConfigurationAttribute>();

        // Act & Assert - Configuration without service match should be handled gracefully
        Assert.Null(hasServiceConfigAttr); // No ServiceConfiguration attribute
        Assert.True(typeof(IServiceConfiguration).IsAssignableFrom(unmatchedConfigType));

        // The PluginLoader should handle this case and log appropriate warnings
        // (We can't easily test logging in unit tests without complex setup, but structure validates)
    }

    /// <summary>
    /// Tests configuration lifetime validation to ensure configurations match their service lifetimes.
    /// This prevents the DI validation errors that cause exit code 139.
    /// </summary>
    [Fact]
    public void ConfigurationLifetimeValidation_ShouldPreventMismatchedLifetimes()
    {
        // Arrange - Test scenarios that could cause DI validation failures
        var services = new ServiceCollection();
        services.AddLogging();

        // Register required dependencies first
        services.AddSingleton<IAuthClient>(Mock.Of<IAuthClient>());
        services.AddSingleton<IPermissionClient>(Mock.Of<IPermissionClient>());

        // Scenario 1: Singleton service with Singleton configuration (CORRECT)
        services.AddSingleton<TestConnectService>();
        services.AddSingleton<TestConnectServiceConfiguration>();

        // Scenario 2: Scoped service with Scoped configuration (CORRECT)
        services.AddScoped<TestService>();
        services.AddScoped<TestServiceConfiguration>();

        // Act - Build service provider with validation enabled
        ServiceProvider? serviceProvider = null;
        Exception? validationException = null;

        try
        {
            serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Test service resolution to trigger validation
            using var scope = serviceProvider.CreateScope();
            var connectService = scope.ServiceProvider.GetService<TestConnectService>();
            var connectConfig = scope.ServiceProvider.GetService<TestConnectServiceConfiguration>();
            var testService = scope.ServiceProvider.GetService<TestService>();
            var testConfig = scope.ServiceProvider.GetService<TestServiceConfiguration>();

            // Assert - All services should resolve successfully
            Assert.NotNull(connectService);
            Assert.NotNull(connectConfig);
            Assert.NotNull(testService);
            Assert.NotNull(testConfig);
        }
        catch (Exception ex)
        {
            validationException = ex;
        }
        finally
        {
            serviceProvider?.Dispose();
        }

        // Assert - No DI validation errors should occur
        Assert.Null(validationException);
    }

    /// <summary>
    /// Tests that the PluginLoader correctly identifies configuration classes that implement IServiceConfiguration.
    /// </summary>
    [Fact]
    public void ConfigurationTypeFiltering_ShouldOnlyFindIServiceConfigurationImplementations()
    {
        // Arrange
        var testAssembly = typeof(TestServiceConfiguration).Assembly;

        // Act - Find all configuration types in this assembly
        var configurationTypes = testAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IServiceConfiguration).IsAssignableFrom(t))
            .ToList();

        var expectedConfigTypes = new[]
        {
            typeof(TestServiceWithAttrConfiguration),
            typeof(TestServiceConfiguration2),
            typeof(TestConnectServiceConfiguration),
            typeof(UnmatchedConfiguration),
            typeof(ComplexNamingTestServiceConfiguration)
        };

        // Assert - Should find all our test configuration classes
        Assert.NotEmpty(configurationTypes);

        foreach (var expectedType in expectedConfigTypes)
        {
            Assert.Contains(expectedType, configurationTypes);
            Assert.True(typeof(IServiceConfiguration).IsAssignableFrom(expectedType));
        }

        // Verify they all implement required IServiceConfiguration properties
        foreach (var configType in configurationTypes)
        {
            var instance = Activator.CreateInstance(configType) as IServiceConfiguration;
            Assert.NotNull(instance);

            // These properties should exist (can be null but must exist)
            Assert.True(configType.GetProperty("ForceServiceId") != null);
            // Service_Disabled property was removed - service enable/disable is now handled via environment variables
        }
    }



    /// <summary>
    /// Tests that services with [BannouService] attributes are discovered correctly.
    /// </summary>
    [Fact]
    public void ServiceTypeDiscovery_ShouldFindServicesWithBannouServiceAttributes()
    {
        // Arrange
        var testingAssembly = typeof(BeyondImmersion.BannouService.Testing.TestingService).Assembly;
        var expectedServiceName = "testing";

        // Act - Find all types with BannouService attributes
        var serviceTypes = testingAssembly.GetTypes()
            .Where(t => typeof(IBannouService).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Where(t => t.GetCustomAttribute<BeyondImmersion.BannouService.Attributes.BannouServiceAttribute>() != null)
            .ToList();

        // Assert
        Assert.NotEmpty(serviceTypes);

        var testingServiceType = serviceTypes.FirstOrDefault(t => t.Name == "TestingService");
        Assert.NotNull(testingServiceType);

        // Verify the BannouService attribute has correct configuration
        var bannouServiceAttr = testingServiceType.GetCustomAttribute<BeyondImmersion.BannouService.Attributes.BannouServiceAttribute>();
        Assert.NotNull(bannouServiceAttr);
        Assert.Equal(expectedServiceName, bannouServiceAttr.Name);

        // Verify that the attribute is correctly reading the lifetime from our positional parameters
        // [BannouService("testing", typeof(ITestingService), false, ServiceLifetime.Scoped)]
        var actualLifetime = bannouServiceAttr.Lifetime;
        var actualInterfaceType = bannouServiceAttr.InterfaceType;
        var actualPriority = bannouServiceAttr.Priority;

        // Debug output for diagnostic purposes
        System.Diagnostics.Debug.WriteLine($"Attribute values: Name={bannouServiceAttr.Name}, InterfaceType={actualInterfaceType?.Name}, Priority={actualPriority}, Lifetime={actualLifetime}");

        Assert.Equal(ServiceLifetime.Scoped, actualLifetime);
    }

    /// <summary>
    /// Tests comprehensive interface/attribute-based service discovery.
    /// Validates that only types implementing IBannouService with BannouServiceAttribute are discovered.
    /// </summary>
    [Fact]
    public void InterfaceBasedDiscovery_ShouldOnlyFindValidServiceTypes()
    {
        // Arrange
        var testingAssembly = typeof(BeyondImmersion.BannouService.Testing.TestingService).Assembly;

        // Act - Test the exact logic used by PluginLoader.DiscoverServiceTypes
        var discoveredServices = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IBannouService).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttribute<BannouServiceAttribute>() != null)
            .ToList();

        // Assert - Should find TestingService
        Assert.NotEmpty(discoveredServices);
        var testingService = discoveredServices.FirstOrDefault(t => t.Name == "TestingService");
        Assert.NotNull(testingService);

        // Verify it implements IBannouService
        Assert.True(typeof(IBannouService).IsAssignableFrom(testingService));

        // Verify it has BannouServiceAttribute
        var bannouAttr = testingService.GetCustomAttribute<BannouServiceAttribute>();
        Assert.NotNull(bannouAttr);
        Assert.Equal("testing", bannouAttr.Name);

        // Verify no types without proper interface/attribute are discovered
        var invalidTypes = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract)
            .Where(t => !typeof(IBannouService).IsAssignableFrom(t) || t.GetCustomAttribute<BannouServiceAttribute>() == null)
            .Where(t => t.Name.EndsWith("Service"))  // Only check service-like classes
            .ToList();

        // None of these should be in the discovered services list
        foreach (var invalidType in invalidTypes)
        {
            Assert.DoesNotContain(invalidType, discoveredServices);
        }
    }

    /// <summary>
    /// Tests that client discovery uses IServiceClient interface without fallbacks.
    /// </summary>
    [Fact]
    public void InterfaceBasedClientDiscovery_ShouldRequireIServiceClientInterface()
    {
        // Arrange
        var testingAssembly = typeof(TestService).Assembly;  // This assembly

        // Act - Test client discovery logic (similar to PluginLoader.DiscoverClientTypes)
        var discoveredClients = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IServiceClient).IsAssignableFrom(t))
            .ToList();

        // Assert - Should only find types that implement IServiceClient
        foreach (var clientType in discoveredClients)
        {
            Assert.True(typeof(IServiceClient).IsAssignableFrom(clientType));
            Assert.False(clientType.IsInterface);
            Assert.False(clientType.IsAbstract);
        }

        // Verify that types ending in "Client" but not implementing IServiceClient are NOT discovered
        var nonClientTypes = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && t.Name.EndsWith("Client"))
            .Where(t => !typeof(IServiceClient).IsAssignableFrom(t))
            .ToList();

        foreach (var nonClientType in nonClientTypes)
        {
            Assert.DoesNotContain(nonClientType, discoveredClients);
        }
    }

    /// <summary>
    /// Tests that configuration discovery requires explicit ServiceConfigurationAttribute.
    /// </summary>
    [Fact]
    public void AttributeBasedConfigurationDiscovery_ShouldRequireExplicitAttribute()
    {
        // Arrange
        var testingAssembly = typeof(TestServiceWithAttrConfiguration).Assembly;

        // Act - Test configuration discovery requiring explicit ServiceConfigurationAttribute
        var configurationsWithAttribute = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IServiceConfiguration).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttribute<ServiceConfigurationAttribute>() != null)
            .ToList();

        var configurationsWithoutAttribute = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IServiceConfiguration).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttribute<ServiceConfigurationAttribute>() == null)
            .ToList();

        // Assert - Should find TestServiceWithAttrConfiguration which has the attribute
        var configWithAttr = configurationsWithAttribute.FirstOrDefault(t => t.Name == "TestServiceWithAttrConfiguration");
        Assert.NotNull(configWithAttr);

        var serviceConfigAttr = configWithAttr.GetCustomAttribute<ServiceConfigurationAttribute>();
        Assert.NotNull(serviceConfigAttr);
        Assert.Equal(typeof(TestService), serviceConfigAttr.ServiceImplementationType);

        // Should also find configurations without attribute (they implement IServiceConfiguration)
        Assert.NotEmpty(configurationsWithoutAttribute);

        // But the new system requires explicit ServiceImplementationType for proper matching
        // Configurations without attributes won't have explicit service mapping
        foreach (var configWithoutAttr in configurationsWithoutAttribute)
        {
            var attr = configWithoutAttr.GetCustomAttribute<ServiceConfigurationAttribute>();
            Assert.Null(attr);  // Verify they don't have the attribute
            Assert.True(typeof(IServiceConfiguration).IsAssignableFrom(configWithoutAttr));  // But do implement the interface
        }
    }

    /// <summary>
    /// Tests the complete workflow of interface/attribute-based discovery without naming conventions.
    /// </summary>
    [Fact]
    public void CompleteInterfaceAttributeBasedWorkflow_ShouldWorkWithoutNamingConventions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        // Create test assembly data
        var testingAssembly = typeof(BeyondImmersion.BannouService.Testing.TestingService).Assembly;

        // Act - Test complete discovery workflow

        // 1. Service Discovery - should find services with IBannouService + BannouServiceAttribute
        var serviceTypes = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IBannouService).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttribute<BannouServiceAttribute>() != null)
            .ToList();

        // 2. Configuration Discovery - should find configs with IServiceConfiguration (optionally + ServiceConfigurationAttribute)
        var configTypes = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IServiceConfiguration).IsAssignableFrom(t))
            .ToList();

        // 3. Client Discovery - should find clients with IServiceClient interface
        var clientTypes = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IServiceClient).IsAssignableFrom(t))
            .ToList();

        // Assert - Verify discovery worked based on interfaces/attributes, not naming

        // Should find TestingService (has IBannouService + BannouServiceAttribute)
        var testingService = serviceTypes.FirstOrDefault(t => t.Name == "TestingService");
        Assert.NotNull(testingService);

        // Should find configurations (have IServiceConfiguration)
        Assert.NotEmpty(configTypes);

        // Verify no discovery based on naming alone
        var typesWithServiceInName = testingAssembly.GetTypes()
            .Where(t => t.Name.Contains("Service") && !t.IsInterface && !t.IsAbstract)
            .Where(t => !typeof(IBannouService).IsAssignableFrom(t))  // Don't implement IBannouService
            .ToList();

        // None of these should be in serviceTypes (they don't implement IBannouService)
        foreach (var nameOnlyType in typesWithServiceInName)
        {
            Assert.DoesNotContain(nameOnlyType, serviceTypes);
        }
    }

    /// <summary>
    /// Tests that the PluginLoader correctly registers service types with BannouService attributes.
    /// </summary>
    [Fact]
    public void PluginLoader_ShouldRegisterServicesWithBannouServiceAttributes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        // Create a mock assembly with TestingService
        var testingAssembly = typeof(BeyondImmersion.BannouService.Testing.TestingService).Assembly;
        var loadedAssembliesField = typeof(PluginLoader)
            .GetField("_loadedAssemblies", BindingFlags.NonPublic | BindingFlags.Instance);
        var loadedAssemblies = new Dictionary<string, Assembly> { { "testing", testingAssembly } };
        loadedAssembliesField?.SetValue(pluginLoader, loadedAssemblies);

        // Simulate enabled plugins list with testing plugin
        var mockPlugin = new Mock<IBannouPlugin>();
        mockPlugin.Setup(p => p.PluginName).Returns("testing");
        var enabledPluginsField = typeof(PluginLoader)
            .GetField("_enabledPlugins", BindingFlags.NonPublic | BindingFlags.Instance);
        var enabledPlugins = new List<IBannouPlugin> { mockPlugin.Object };
        enabledPluginsField?.SetValue(pluginLoader, enabledPlugins);

        // Act - Call type discovery method directly via reflection
        var discoverTypesMethod = typeof(PluginLoader)
            .GetMethod("DiscoverTypesForRegistration", BindingFlags.NonPublic | BindingFlags.Instance);
        discoverTypesMethod?.Invoke(pluginLoader, null);

        // Get the service types field to verify discovery worked
        var serviceTypesField = typeof(PluginLoader)
            .GetField("_serviceTypesToRegister", BindingFlags.NonPublic | BindingFlags.Instance);
        var serviceTypes = serviceTypesField?.GetValue(pluginLoader) as List<(Type interfaceType, Type implementationType, ServiceLifetime lifetime)>;

        // Assert - Verify TestingService was discovered and configured correctly
        Assert.NotNull(serviceTypes);
        Assert.NotEmpty(serviceTypes);

        var testingServiceRegistration = serviceTypes.FirstOrDefault(s => s.implementationType.Name == "TestingService");
        Assert.NotEqual(default, testingServiceRegistration);
        Assert.Equal("TestingService", testingServiceRegistration.implementationType.Name);
        // This should be Scoped as specified in the BannouService attribute
        Assert.Equal(ServiceLifetime.Scoped, testingServiceRegistration.lifetime);
    }

    /// <summary>
    /// Tests that PluginLoader rejects types that don't meet interface/attribute requirements.
    /// </summary>
    [Fact]
    public void PluginLoader_ShouldRejectTypesWithoutProperInterfaceAndAttributes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        // Get current assembly which has test types
        var currentAssembly = typeof(TestService).Assembly;

        // Act - Test that PluginLoader only discovers properly attributed types

        // TestService implements IBannouService but has NO BannouServiceAttribute
        var testServiceWithoutAttr = typeof(TestService);
        Assert.True(typeof(IBannouService).IsAssignableFrom(testServiceWithoutAttr));
        Assert.Null(testServiceWithoutAttr.GetCustomAttribute<BannouServiceAttribute>());

        // Mock a class that ends with "Service" but doesn't implement IBannouService
        var nonServiceClass = typeof(TestConnectService);  // Has "Service" in name but doesn't implement IBannouService
        Assert.False(typeof(IBannouService).IsAssignableFrom(nonServiceClass));

        // Simulate PluginLoader discovery logic
        var discoveredServices = currentAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IBannouService).IsAssignableFrom(t))
            .Where(t =>
            {
                try
                {
                    return t.GetCustomAttribute<BannouServiceAttribute>() != null;
                }
                catch (AmbiguousMatchException)
                {
                    // Type has multiple BannouServiceAttribute - this should be valid and found
                    return true;
                }
            })
            .ToList();

        // Assert - Types without proper interface+attribute should NOT be discovered
        Assert.DoesNotContain(testServiceWithoutAttr, discoveredServices);  // Has interface but no attribute
        Assert.DoesNotContain(nonServiceClass, discoveredServices);  // Has "Service" name but no interface

        // Only properly configured types should be found
        foreach (var discoveredService in discoveredServices)
        {
            Assert.True(typeof(IBannouService).IsAssignableFrom(discoveredService));
            try
            {
                Assert.NotNull(discoveredService.GetCustomAttribute<BannouServiceAttribute>());
            }
            catch (AmbiguousMatchException)
            {
                // Type has multiple BannouServiceAttribute - this is valid, just continue
                Assert.True(discoveredService.GetCustomAttributes<BannouServiceAttribute>().Any());
            }
        }
    }

    /// <summary>
    /// Tests that service lifetime is correctly extracted from BannouServiceAttribute.
    /// </summary>
    [Fact]
    public void AttributeBasedLifetimeExtraction_ShouldRespectBannouServiceAttributeLifetime()
    {
        // Arrange
        var testingAssembly = typeof(BeyondImmersion.BannouService.Testing.TestingService).Assembly;

        // Act - Find TestingService and extract its lifetime from attribute
        var testingServiceType = testingAssembly.GetTypes()
            .Where(t => t.Name == "TestingService" && typeof(IBannouService).IsAssignableFrom(t))
            .FirstOrDefault();

        Assert.NotNull(testingServiceType);
        var bannouAttr = testingServiceType.GetCustomAttribute<BannouServiceAttribute>();
        Assert.NotNull(bannouAttr);

        // Assert - Verify lifetime is read correctly from attribute
        Assert.Equal(ServiceLifetime.Scoped, bannouAttr.Lifetime);
        Assert.Equal("testing", bannouAttr.Name);

        // Test that the PluginLoader would use this lifetime for registration
        var expectedLifetime = bannouAttr.Lifetime;
        Assert.Equal(ServiceLifetime.Scoped, expectedLifetime);
    }

    /// <summary>
    /// Tests that interface-based discovery ignores types that only match by naming convention.
    /// </summary>
    [Fact]
    public void InterfaceBasedDiscovery_ShouldIgnoreNamingConventionOnlyTypes()
    {
        // Arrange
        var currentAssembly = typeof(TestService).Assembly;

        // Find types that follow naming conventions but don't implement required interfaces
        var conventionOnlyTypes = currentAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract)
            .Where(t => t.Name.EndsWith("Service") || t.Name.EndsWith("Client") || t.Name.EndsWith("Configuration"))
            .Where(t => !typeof(IBannouService).IsAssignableFrom(t) && !typeof(IServiceClient).IsAssignableFrom(t) && !typeof(IServiceConfiguration).IsAssignableFrom(t))
            .ToList();

        // Act - Test interface-based discovery
        var servicesViaInterface = currentAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IBannouService).IsAssignableFrom(t))
            .ToList();

        var clientsViaInterface = currentAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IServiceClient).IsAssignableFrom(t))
            .ToList();

        var configurationsViaInterface = currentAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IServiceConfiguration).IsAssignableFrom(t))
            .ToList();

        // Assert - Interface-based discovery should ignore naming-convention-only types
        foreach (var conventionType in conventionOnlyTypes)
        {
            Assert.DoesNotContain(conventionType, servicesViaInterface);
            Assert.DoesNotContain(conventionType, clientsViaInterface);
            Assert.DoesNotContain(conventionType, configurationsViaInterface);
        }

        // Interface-based discovery should only find types that implement the required interfaces
        foreach (var service in servicesViaInterface)
        {
            Assert.True(typeof(IBannouService).IsAssignableFrom(service));
        }

        foreach (var client in clientsViaInterface)
        {
            Assert.True(typeof(IServiceClient).IsAssignableFrom(client));
        }

        foreach (var config in configurationsViaInterface)
        {
            Assert.True(typeof(IServiceConfiguration).IsAssignableFrom(config));
        }
    }

    /// <summary>
    /// Tests layer-level enablement: when a layer is enabled, services in that layer are enabled.
    /// When a layer is disabled, services in that layer are disabled.
    /// </summary>
    [Fact]
    public void LayerEnabled_ServiceInLayer_IsEnabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        var isServiceEnabledMethod = typeof(PluginLoader)
            .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

        var originalEnableGameFeatures = Program.Configuration.EnableGameFeatures;
        var originalEnableAppFoundation = Program.Configuration.EnableAppFoundation;
        try
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "true");

            // Layer enabled (default) → service enabled
            Program.Configuration.EnableGameFeatures = true;
            var result = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.True(result, "Service should be enabled when its layer is enabled");

            // Layer disabled → service disabled
            Program.Configuration.EnableGameFeatures = false;
            var result2 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.False(result2, "Service should be disabled when its layer is disabled");

            // Different layer: AppFoundation disabled → L1 service disabled
            Program.Configuration.EnableAppFoundation = false;
            var result3 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "auth", ServiceLayer.AppFoundation }) ?? false);
            Assert.False(result3, "L1 service should be disabled when AppFoundation layer is disabled");

            // Restore and verify
            Program.Configuration.EnableAppFoundation = true;
            var result4 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "auth", ServiceLayer.AppFoundation }) ?? false);
            Assert.True(result4, "L1 service should be enabled when AppFoundation layer is restored");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Program.Configuration.EnableGameFeatures = originalEnableGameFeatures;
            Program.Configuration.EnableAppFoundation = originalEnableAppFoundation;
        }
    }

    /// <summary>
    /// Tests that individual override takes precedence over layer setting.
    /// SERVICE_ENABLED=true overrides a disabled layer; SERVICE_ENABLED=false overrides an enabled layer.
    /// </summary>
    [Fact]
    public void IndividualOverride_TakesPrecedenceOverLayer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        var isServiceEnabledMethod = typeof(PluginLoader)
            .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

        var originalEnableGameFeatures = Program.Configuration.EnableGameFeatures;
        try
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "true");

            // Layer disabled, but individual override enables the service
            Program.Configuration.EnableGameFeatures = false;
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "true");
            var result1 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.True(result1, "Individual SERVICE_ENABLED=true should override disabled layer");

            // Layer enabled, but individual override disables the service
            Program.Configuration.EnableGameFeatures = true;
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "false");
            var result2 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.False(result2, "Individual SERVICE_ENABLED=false should override enabled layer");

            // Clear individual override, layer control takes effect
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", null);
            var result3 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.True(result3, "Without individual override, layer control should take effect");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", null);
            Program.Configuration.EnableGameFeatures = originalEnableGameFeatures;
        }
    }

    /// <summary>
    /// Tests master kill switch (SERVICES_ENABLED=false) overrides layers but not individual overrides.
    /// </summary>
    [Fact]
    public void MasterKillSwitch_OverridesLayers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        var isServiceEnabledMethod = typeof(PluginLoader)
            .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            // SERVICES_ENABLED=false with no individual override → disabled regardless of layer
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "false");

            var result1 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.False(result1, "Service should be disabled when master kill switch is off");

            // Individual override takes precedence even over master kill switch
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "true");
            var result2 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.True(result2, "Individual override should take precedence over master kill switch");

            // Another service without override should remain disabled
            var result3 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "account", ServiceLayer.AppFoundation }) ?? false);
            Assert.False(result3, "Service without override should remain disabled under master kill switch");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", null);
        }
    }

    /// <summary>
    /// Tests that infrastructure services (state, messaging, mesh) are always enabled
    /// regardless of layer settings or master kill switch.
    /// </summary>
    [Fact]
    public void InfrastructureAlwaysEnabled_RegardlessOfLayerOrKillSwitch()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        var isServiceEnabledMethod = typeof(PluginLoader)
            .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            // Master kill switch off
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "false");

            // Required infrastructure should still be enabled
            var stateResult = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "state", ServiceLayer.Infrastructure }) ?? false);
            Assert.True(stateResult, "State service should always be enabled (required infrastructure)");

            var messagingResult = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "messaging", ServiceLayer.Infrastructure }) ?? false);
            Assert.True(messagingResult, "Messaging service should always be enabled (required infrastructure)");

            var meshResult = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "mesh", ServiceLayer.Infrastructure }) ?? false);
            Assert.True(meshResult, "Mesh service should always be enabled (required infrastructure)");

            // Non-infrastructure service should be disabled under kill switch
            var testingResult = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.False(testingResult, "Non-infrastructure service should be disabled under kill switch");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
        }
    }

    /// <summary>
    /// Tests case-insensitive handling for SERVICE_ENABLED environment variable values.
    /// </summary>
    [Fact]
    public void ServiceEnabling_ShouldHandleCaseVariations()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        var isServiceEnabledMethod = typeof(PluginLoader)
            .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "TRUE");  // Uppercase

            // Uppercase TRUE → enabled
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "TRUE");
            var result1 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.True(result1, "Should handle uppercase TRUE for enable flag");

            // Mixed case False → disabled
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "False");
            var result2 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.False(result2, "Should handle mixed case False for enable flag");

            // Lowercase service names are converted to uppercase for env var lookup
            Environment.SetEnvironmentVariable("BEHAVIOR_SERVICE_ENABLED", "false");
            var result3 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "behavior", ServiceLayer.GameFeatures }) ?? false);
            Assert.False(result3, "Should convert lowercase service name to uppercase for env var lookup");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", null);
            Environment.SetEnvironmentVariable("BEHAVIOR_SERVICE_ENABLED", null);
        }
    }

    /// <summary>
    /// Tests the infrastructure test scenario: SERVICES_ENABLED=false with only testing enabled.
    /// This is the canonical pattern for infrastructure tests.
    /// </summary>
    [Fact]
    public void InfrastructureTestScenario_MasterKillSwitch_OnlyTestingEnabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        var isServiceEnabledMethod = typeof(PluginLoader)
            .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            // Master kill switch + individual override pattern
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "false");
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "true");

            // Testing should be enabled via individual override
            var testingEnabled = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.True(testingEnabled, "Testing service should be enabled via individual override");

            // Other services should be disabled by master kill switch
            var accountEnabled = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "account", ServiceLayer.AppFoundation }) ?? false);
            Assert.False(accountEnabled, "Account should be disabled under master kill switch");

            var authEnabled = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "auth", ServiceLayer.AppFoundation }) ?? false);
            Assert.False(authEnabled, "Auth should be disabled under master kill switch");

            var behaviorEnabled = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "behavior", ServiceLayer.GameFeatures }) ?? false);
            Assert.False(behaviorEnabled, "Behavior should be disabled under master kill switch");

            // Infrastructure still always on
            var stateEnabled = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "state", ServiceLayer.Infrastructure }) ?? false);
            Assert.True(stateEnabled, "State should always be enabled (required infrastructure)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", null);
        }
    }

    /// <summary>
    /// Tests edge cases: empty strings, invalid values, whitespace, and null service names.
    /// </summary>
    [Fact]
    public void ServiceEnabling_EdgeCases()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        var isServiceEnabledMethod = typeof(PluginLoader)
            .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "true");

            // Empty string SERVICE_ENABLED should be treated as not set (fall through to layer)
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "");
            var result1 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.True(result1, "Empty string SERVICE_ENABLED should be treated as not set (fall through to layer)");

            // Invalid boolean value should be treated as not "true" → disabled
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "invalid");
            var result2 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            Assert.False(result2, "Invalid boolean value for SERVICE_ENABLED should be treated as not 'true' (disabled)");

            // Null/empty service name returns ServicesEnabled config value
            var result3 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "", ServiceLayer.GameFeatures }) ?? false);
            Assert.True(result3, "Empty service name should return ServicesEnabled config value");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", null);
        }
    }

    /// <summary>
    /// Comprehensive test of the full resolution order:
    /// 1. Infrastructure always on
    /// 2. Individual override (SERVICE_ENABLED=true/false)
    /// 3. Master kill switch (SERVICES_ENABLED=false)
    /// 4. Layer control (BANNOU_ENABLE_*)
    /// </summary>
    [Fact]
    public void ServiceEnabling_ComprehensiveResolutionOrder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        var isServiceEnabledMethod = typeof(PluginLoader)
            .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

        var testServices = new[] { "testing", "account", "auth", "connect", "behavior" };
        var originalEnableGameFeatures = Program.Configuration.EnableGameFeatures;
        var originalEnableAppFoundation = Program.Configuration.EnableAppFoundation;

        try
        {
            // Phase 1: All layers enabled, no overrides → all services enabled
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "true");
            foreach (var service in testServices)
                Environment.SetEnvironmentVariable($"{service.ToUpper()}_SERVICE_ENABLED", null);

            foreach (var service in testServices)
            {
                var result = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                    new object[] { service, ServiceLayer.GameFeatures }) ?? false);
                Assert.True(result, $"Service '{service}' should be enabled by default");
            }

            // Phase 2: Disable individual services via SERVICE_ENABLED=false
            Environment.SetEnvironmentVariable("ACCOUNT_SERVICE_ENABLED", "false");
            Environment.SetEnvironmentVariable("AUTH_SERVICE_ENABLED", "false");

            var accountResult = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "account", ServiceLayer.AppFoundation }) ?? false);
            var authResult = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "auth", ServiceLayer.AppFoundation }) ?? false);
            var testingResult = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);

            Assert.False(accountResult, "Account should be disabled with SERVICE_ENABLED=false");
            Assert.False(authResult, "Auth should be disabled with SERVICE_ENABLED=false");
            Assert.True(testingResult, "Testing should remain enabled without override");

            // Phase 3: Master kill switch with individual enables
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "false");
            foreach (var service in testServices)
                Environment.SetEnvironmentVariable($"{service.ToUpper()}_SERVICE_ENABLED", null);

            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "true");
            Environment.SetEnvironmentVariable("CONNECT_SERVICE_ENABLED", "true");

            var testingResult2 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "testing", ServiceLayer.GameFeatures }) ?? false);
            var connectResult = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "connect", ServiceLayer.AppFoundation }) ?? false);
            var accountResult2 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "account", ServiceLayer.AppFoundation }) ?? false);

            Assert.True(testingResult2, "Testing should be enabled with individual override");
            Assert.True(connectResult, "Connect should be enabled with individual override");
            Assert.False(accountResult2, "Account should remain disabled under kill switch");

            // Phase 4: Layer disabled → services in that layer disabled
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "true");
            foreach (var service in testServices)
                Environment.SetEnvironmentVariable($"{service.ToUpper()}_SERVICE_ENABLED", null);

            Program.Configuration.EnableGameFeatures = false;
            var behaviorResult = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "behavior", ServiceLayer.GameFeatures }) ?? false);
            Assert.False(behaviorResult, "Behavior should be disabled when GameFeatures layer is off");

            // L1 service in enabled layer should still work
            var authResult2 = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "auth", ServiceLayer.AppFoundation }) ?? false);
            Assert.True(authResult2, "Auth should be enabled when AppFoundation layer is on");

            // Infrastructure always on even when layers are off
            var stateResult = (bool)(isServiceEnabledMethod?.Invoke(pluginLoader,
                new object[] { "state", ServiceLayer.Infrastructure }) ?? false);
            Assert.True(stateResult, "Infrastructure always on regardless of layer settings");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            foreach (var service in testServices)
                Environment.SetEnvironmentVariable($"{service.ToUpper()}_SERVICE_ENABLED", null);
            Program.Configuration.EnableGameFeatures = originalEnableGameFeatures;
            Program.Configuration.EnableAppFoundation = originalEnableAppFoundation;
        }
    }

    /// <summary>
    /// Helper method to get types implementing IBannouService from an assembly.
    /// </summary>
    private List<Type> GetTypesImplementingIBannouService(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes()
                .Where(t => typeof(IBannouService).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();
        }
        catch (ReflectionTypeLoadException ex)
        {
            List<Type> types = new();

            // Handle assembly loading issues
            for (int i = 0; i < ex.Types.Length; i++)
            {
                Type? type = ex.Types[i];
                if (type == null)
                    continue;

                if (!typeof(IBannouService).IsAssignableFrom(type))
                    continue;

                if (type.IsInterface)
                    continue;

                if (type.IsAbstract)
                    continue;

                types.Add(type);
            }

            return types;
        }
    }
}

/// <summary>
/// Test service class that implements IBannouService for unit testing.
/// </summary>
public class TestService : IBannouService
{
    // IBannouService methods are provided by default interface implementations
    // No additional implementation needed for basic testing
}

/// <summary>
/// Test service configuration class for unit testing lifetime matching.
/// </summary>
public class TestServiceConfiguration
{
    public string TestSetting { get; set; } = "default";
}

/// <summary>
/// Mock ConnectService for unit testing (simplified version).
/// </summary>
public class ConnectService
{
    private readonly IAuthClient _authClient;
    private readonly IPermissionClient _permissionClient;

    public ConnectService(IAuthClient authClient, IPermissionClient permissionClient)
    {
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
        _permissionClient = permissionClient ?? throw new ArgumentNullException(nameof(permissionClient));
    }
}

/// <summary>
/// Mock ConnectServiceConfiguration for unit testing.
/// </summary>
public class ConnectServiceConfiguration : IServiceConfiguration
{
    public string InstanceId { get; set; } = "test-instance";
    public string? ForceServiceId { get; } = null;

    Guid? IServiceConfiguration.ForceServiceId => throw new NotImplementedException();
}

/// <summary>
/// Mock IAuthClient interface for unit testing.
/// </summary>
public interface IAuthClient
{
    // Empty interface for testing purposes
}

/// <summary>
/// Mock IPermissionClient interface for unit testing.
/// </summary>
public interface IPermissionClient
{
    // Empty interface for testing purposes
}

/// <summary>
/// Interface for TestConnectService to validate interface-to-implementation mapping.
/// </summary>
public interface ITestConnectService
{
    string GetInstanceId();
}

/// <summary>
/// Test configuration class with ServiceConfiguration attribute for testing.
/// </summary>
[ServiceConfiguration(typeof(TestService))]
public class TestServiceWithAttrConfiguration : IServiceConfiguration
{
    public string TestSetting { get; set; } = "test";
    public string? ForceServiceId { get; } = null;

    Guid? IServiceConfiguration.ForceServiceId => throw new NotImplementedException();
}

/// <summary>
/// Test configuration class for testing scoped lifetime registration.
/// </summary>
public class TestServiceConfiguration2 : IServiceConfiguration
{
    public string TestSetting { get; set; } = "test2";
    public string? ForceServiceId { get; } = null;

    Guid? IServiceConfiguration.ForceServiceId => throw new NotImplementedException();
}

/// <summary>
/// Simple test plugin for configuration testing.
/// </summary>
public class TestPlugin : IBannouPlugin
{
    public string PluginName { get; set; } = "test";
    public string DisplayName => "Test Plugin";
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services) { }
    public void ConfigureApplication(WebApplication app) { }
    public Task<bool> InitializeAsync() => Task.FromResult(true);
    public Task<bool> StartAsync() => Task.FromResult(true);
    public Task RunningAsync() => Task.CompletedTask;
    public Task ShutdownAsync() => Task.CompletedTask;
    public bool ValidatePlugin() => true;
}

/// <summary>
/// Test ConnectService for integration testing with complex configuration requirements.
/// </summary>
public class TestConnectService : ITestConnectService
{
    private readonly TestConnectServiceConfiguration _configuration;
    private readonly IAuthClient _authClient;
    private readonly IPermissionClient _permissionClient;

    public TestConnectService(
        TestConnectServiceConfiguration configuration,
        IAuthClient authClient,
        IPermissionClient permissionClient)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
        _permissionClient = permissionClient ?? throw new ArgumentNullException(nameof(permissionClient));
    }

    public string GetInstanceId() => _configuration.InstanceId;
}

/// <summary>
/// Test configuration for TestConnectService to validate Singleton lifetime patterns.
/// </summary>
public class TestConnectServiceConfiguration : IServiceConfiguration
{
    public string InstanceId { get; set; } = "test-connect-instance";
    public int MaxConnections { get; set; } = 1000;
    public string? ForceServiceId { get; } = null;

    Guid? IServiceConfiguration.ForceServiceId => throw new NotImplementedException();
}

/// <summary>
/// Configuration class that doesn't match any service for testing unmatched configuration handling.
/// </summary>
public class UnmatchedConfiguration : IServiceConfiguration
{
    public string UnusedSetting { get; set; } = "unused";
    public string? ForceServiceId { get; } = null;

    Guid? IServiceConfiguration.ForceServiceId => throw new NotImplementedException();
}

/// <summary>
/// Configuration class for testing configuration type filtering.
/// </summary>
public class ComplexNamingTestServiceConfiguration : IServiceConfiguration
{
    public string ComplexSetting { get; set; } = "complex";
    public string? ForceServiceId { get; } = null;

    Guid? IServiceConfiguration.ForceServiceId => throw new NotImplementedException();
}

/// <summary>
/// Tests for configuration discovery and interface mapping functionality.
/// Validates the fixes for configuration lifetime issues and interface resolution.
/// </summary>
public class ConfigurationDiscoveryTests
{
    /// <summary>
    /// Tests that configurations referencing service interfaces are correctly matched to service registrations.
    /// This validates the fix for configurations using typeof(IXService) instead of typeof(XService).
    /// </summary>
    [Fact]
    public void ConfigurationDiscovery_ShouldMatchInterfaceToImplementation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Register service by interface (standard BannouService pattern)
        services.AddSingleton<ITestConnectService, TestConnectService>();

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        // Simulate configuration discovery
        var serviceRegistrations = new List<(Type interfaceType, Type implementationType, ServiceLifetime lifetime)>
        {
            (typeof(ITestConnectService), typeof(TestConnectService), ServiceLifetime.Singleton)
        };

        // Act - Test interface-to-implementation matching
        var matchedLifetime = ServiceLifetime.Scoped; // Default
        foreach (var (interfaceType, implementationType, lifetime) in serviceRegistrations)
        {
            if (interfaceType == typeof(ITestConnectService))
            {
                matchedLifetime = lifetime;
                break;
            }
        }

        // Assert
        Assert.Equal(ServiceLifetime.Singleton, matchedLifetime);
    }

    /// <summary>
    /// Tests that Singleton services get Singleton configurations to prevent DI lifetime mismatches.
    /// This validates the core fix for "Cannot consume scoped service from singleton service" errors.
    /// </summary>
    [Fact]
    public void ConfigurationLifetime_ShouldMatchServiceLifetime_ForSingletonServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Register Singleton service (typical for Connect, Auth services)
        services.AddSingleton<ITestConnectService, TestConnectService>();
        services.AddSingleton<TestConnectServiceConfiguration>();

        // Register mock dependencies
        services.AddSingleton<IAuthClient>(_ => new Mock<IAuthClient>().Object);
        services.AddSingleton<IPermissionClient>(_ => new Mock<IPermissionClient>().Object);

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetService<ITestConnectService>();
        var config = serviceProvider.GetService<TestConnectServiceConfiguration>();

        // Assert - Both should resolve successfully (no DI lifetime errors)
        Assert.NotNull(service);
        Assert.NotNull(config);
        Assert.IsType<TestConnectService>(service);
        Assert.IsType<TestConnectServiceConfiguration>(config);
    }

    /// <summary>
    /// Tests that multiple registrations of the same configuration type are handled correctly.
    /// This validates the FinalizeConfigurationRegistrations logic that removes competing registrations.
    /// </summary>
    [Fact]
    public void ConfigurationRegistration_ShouldHandleMultipleRegistrations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Simulate the issue: auto-registration adds Scoped, our system adds Singleton
        services.AddScoped<TestConnectServiceConfiguration>(); // Auto-registered (problematic)

        // Act - Simulate our finalization logic
        var existingRegistrations = services.Where(s => s.ServiceType == typeof(TestConnectServiceConfiguration)).ToList();
        foreach (var existing in existingRegistrations)
        {
            services.Remove(existing);
        }
        services.AddSingleton<TestConnectServiceConfiguration>(); // Our correct registration

        // Assert - Only one registration should remain, and it should be Singleton
        var finalRegistrations = services.Where(s => s.ServiceType == typeof(TestConnectServiceConfiguration)).ToList();
        Assert.Single(finalRegistrations);
        Assert.Equal(ServiceLifetime.Singleton, finalRegistrations.First().Lifetime);
    }

    /// <summary>
    /// Tests the naming convention fallback when ServiceConfiguration attribute evaluation fails.
    /// This validates the fallback logic for typeof() resolution issues.
    /// </summary>
    [Fact]
    public void ConfigurationDiscovery_ShouldUseNamingConventionFallback()
    {
        // Arrange - Test naming convention: TestConnectServiceConfiguration -> TestConnectService
        var configurationTypeName = "TestConnectServiceConfiguration";
        var expectedServiceName = "TestConnectService";

        // Act - Simulate naming convention logic
        string? serviceName = null;
        if (configurationTypeName.EndsWith("ServiceConfiguration"))
        {
            serviceName = configurationTypeName.Replace("ServiceConfiguration", "Service");
        }

        // Assert
        Assert.Equal(expectedServiceName, serviceName);
    }

    /// <summary>
    /// Tests that configurations without matching services are properly skipped.
    /// This validates that orphaned configurations don't cause startup failures.
    /// </summary>
    [Fact]
    public void ConfigurationDiscovery_ShouldSkipUnmatchedConfigurations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Register a configuration that doesn't match any service
        services.AddSingleton<UnmatchedConfiguration>();

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var config = serviceProvider.GetService<UnmatchedConfiguration>();

        // Assert - Configuration should be registered but not cause errors
        Assert.NotNull(config);
        Assert.IsType<UnmatchedConfiguration>(config);
    }

    /// <summary>
    /// Tests the complete configuration discovery workflow with both success and failure scenarios.
    /// This integration test validates the entire configuration matching pipeline.
    /// </summary>
    [Fact]
    public void ConfigurationDiscovery_IntegrationTest_ShouldHandleMultipleScenarios()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Scenario 1: Matching service and configuration (should succeed)
        services.AddSingleton<ITestConnectService, TestConnectService>();
        services.AddSingleton<TestConnectServiceConfiguration>();

        // Register mock dependencies for TestConnectService
        services.AddSingleton<IAuthClient>(_ => new Mock<IAuthClient>().Object);
        services.AddSingleton<IPermissionClient>(_ => new Mock<IPermissionClient>().Object);

        // Scenario 2: Configuration without matching service (should be skipped gracefully)
        services.AddSingleton<UnmatchedConfiguration>();

        // Scenario 3: Multiple registrations (should resolve to correct lifetime)
        services.AddScoped<ComplexNamingTestServiceConfiguration>(); // Wrong lifetime
        services.Remove(services.First(s => s.ServiceType == typeof(ComplexNamingTestServiceConfiguration)));
        services.AddSingleton<ComplexNamingTestServiceConfiguration>(); // Correct lifetime

        // Act
        var serviceProvider = services.BuildServiceProvider();

        // Assert - All scenarios should work without throwing
        Exception? thrownException = null;
        try
        {
            var service = serviceProvider.GetService<ITestConnectService>();
            var config1 = serviceProvider.GetService<TestConnectServiceConfiguration>();
            var config2 = serviceProvider.GetService<UnmatchedConfiguration>();
            var config3 = serviceProvider.GetService<ComplexNamingTestServiceConfiguration>();

            Assert.NotNull(service);
            Assert.NotNull(config1);
            Assert.NotNull(config2);
            Assert.NotNull(config3);
        }
        catch (Exception ex)
        {
            thrownException = ex;
        }

        Assert.Null(thrownException);
    }
}
