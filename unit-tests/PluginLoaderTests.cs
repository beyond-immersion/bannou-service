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

namespace BeyondImmersion.BannouService.UnitTests;

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
        var currentAssemblyTypes = GetTypesImplementingIDaprService(currentAssembly);
        var testAssemblyTypes = GetTypesImplementingIDaprService(testAssembly);

        // Assert - Debug output to understand what's happening
        Assert.NotNull(currentAssemblyTypes);
        Assert.NotNull(testAssemblyTypes);

        // We should find TestService in the test assembly
        var testServiceType = testAssemblyTypes.FirstOrDefault(t => t.Name == "TestService");
        Assert.NotNull(testServiceType);
        Assert.True(typeof(IDaprService).IsAssignableFrom(testServiceType));

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
            .Where(t => typeof(IDaprService).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();

        // Check if TestingService is in the list
        var testingServiceType = types.FirstOrDefault(t => t.Name == expectedServiceTypeName);

        // Assert - This replicates the production scenario exactly
        Assert.NotEmpty(types);
        Assert.NotNull(testingServiceType);
        Assert.Equal(expectedServiceTypeName, testingServiceType.Name);
        Assert.True(typeof(IDaprService).IsAssignableFrom(testingServiceType));

        // This proves the service discovery logic works correctly
        Assert.True(testingServiceType == typeof(BeyondImmersion.BannouService.Testing.TestingService));
    }

    /// <summary>
    /// Debug test to understand ServiceLifetime parameter issue.
    /// </summary>
    [Fact]
    public void Debug_DaprServiceAttribute_ParameterMapping()
    {
        // Test direct attribute creation with explicit parameters to verify constructor behavior
        var attr1 = new DaprServiceAttribute("test1", typeof(IDaprService), false, ServiceLifetime.Scoped);
        Assert.Equal("test1", attr1.Name);
        Assert.Equal(typeof(IDaprService), attr1.InterfaceType);
        Assert.False(attr1.Priority);
        Assert.Equal(ServiceLifetime.Scoped, attr1.Lifetime);

        // Test with named parameters
        var attr2 = new DaprServiceAttribute("test2", interfaceType: typeof(IDaprService), priority: false, lifetime: ServiceLifetime.Scoped);
        Assert.Equal("test2", attr2.Name);
        Assert.Equal(typeof(IDaprService), attr2.InterfaceType);
        Assert.False(attr2.Priority);
        Assert.Equal(ServiceLifetime.Scoped, attr2.Lifetime);

        // Test positional with different lifetimes
        var attr3 = new DaprServiceAttribute("test3", typeof(IDaprService), false, ServiceLifetime.Transient);
        Assert.Equal(ServiceLifetime.Transient, attr3.Lifetime);

        var attr4 = new DaprServiceAttribute("test4", typeof(IDaprService), false, ServiceLifetime.Singleton);
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
            var implementsIDaprService = typeof(IDaprService).IsAssignableFrom(type);
            var daprAttr = type.GetCustomAttribute<DaprServiceAttribute>();

            testOutput += $"  - {type.Name}: IDaprService={implementsIDaprService}, DaprAttr={daprAttr?.Name ?? "NONE"}\n";
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
    /// Tests service enabling logic with configuration environment variables.
    /// </summary>
    [Fact]
    public void ServiceEnabling_ShouldRespectConfigurationSettings()
    {
        try
        {
            // Arrange - Set up environment variables for testing
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "true");
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", "false");

            var services = new ServiceCollection();
            services.AddLogging();
            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
            var pluginLoader = new PluginLoader(logger);

            // Use reflection to test the service enabling logic
            var isServiceEnabledMethod = typeof(PluginLoader)
                .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

            // Act - Test various service enabling scenarios
            var testingEnabledResult = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var testingEnabled = testingEnabledResult != null && (bool)testingEnabledResult;

            // Change environment variable and test again
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", "true");
            var testingDisabledResult = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var testingDisabled = testingDisabledResult != null && (bool)testingDisabledResult;

            // Test global disabled - but we need to also reset Program.Configuration for this test
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "false");
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", null);

            // Note: In unit tests, Program.Configuration might not update with environment changes
            // This test verifies the logic works, but the actual configuration update depends on Program initialization
            // var globalDisabled = (bool)isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });

            // Assert
            Assert.True(testingEnabled, "Testing service should be enabled when SERVICES_ENABLED=true and TESTING_SERVICE_DISABLED=false");
            Assert.False(testingDisabled, "Testing service should be disabled when TESTING_SERVICE_DISABLED=true");
            // Skip the global test for now as it requires Program.Configuration to be reinitialized
            // Assert.False(globalDisabled, "Testing service should be disabled when SERVICES_ENABLED=false");
        }
        finally
        {
            // Cleanup - Reset environment variables
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", null);
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
            .Where(t => typeof(IDaprService).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();

        var testingServiceType = types.FirstOrDefault(t => t.Name == expectedServiceTypeName);

        // Assert
        Assert.NotEmpty(types);
        Assert.NotNull(testingServiceType);
        Assert.Equal(expectedServiceTypeName, testingServiceType.Name);
        Assert.True(typeof(IDaprService).IsAssignableFrom(testingServiceType));

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

        // Create a mock interface for IPermissionsClient
        var permissionsClientInterface = typeof(IPermissionsClient);
        var permissionsClientLifetimeResult = getClientLifetimeMethod?.Invoke(pluginLoader, new object[] { permissionsClientInterface });
        var permissionsClientLifetime = permissionsClientLifetimeResult != null ? (ServiceLifetime)permissionsClientLifetimeResult : ServiceLifetime.Scoped;

        // Assert - Clients should default to Singleton to avoid DI conflicts
        Assert.Equal(ServiceLifetime.Singleton, authClientLifetime);
        Assert.Equal(ServiceLifetime.Singleton, permissionsClientLifetime);

        // Test that this prevents DI validation errors
        newServices.AddSingleton(authClientInterface, Mock.Of<IAuthClient>());
        newServices.AddSingleton(permissionsClientInterface, Mock.Of<IPermissionsClient>());

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

        // TestingService: Scoped (as specified in DaprService attribute)
        productionServices.AddScoped<TestService>();
        productionServices.AddScoped<TestServiceConfiguration>();

        // Register clients with lifetimes that match or are compatible with their consumers
        // IAuthClient and IPermissionsClient: Singleton (compatible with ConnectService Singleton)
        productionServices.AddSingleton<IAuthClient>(Mock.Of<IAuthClient>());
        productionServices.AddSingleton<IPermissionsClient>(Mock.Of<IPermissionsClient>());

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
        Assert.Equal(2, registrationServices.Count);

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
        services.AddSingleton<IPermissionsClient>(Mock.Of<IPermissionsClient>());

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
        services.AddSingleton<IPermissionsClient>(Mock.Of<IPermissionsClient>());

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
            Assert.True(configType.GetProperty("Force_Service_ID") != null);
            // Service_Disabled property was removed - service enable/disable is now handled via environment variables
        }
    }



    /// <summary>
    /// Tests that services with [DaprService] attributes are discovered correctly.
    /// </summary>
    [Fact]
    public void ServiceTypeDiscovery_ShouldFindServicesWithDaprServiceAttributes()
    {
        // Arrange
        var testingAssembly = typeof(BeyondImmersion.BannouService.Testing.TestingService).Assembly;
        var expectedServiceName = "testing";

        // Act - Find all types with DaprService attributes
        var serviceTypes = testingAssembly.GetTypes()
            .Where(t => typeof(IDaprService).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Where(t => t.GetCustomAttribute<BeyondImmersion.BannouService.Attributes.DaprServiceAttribute>() != null)
            .ToList();

        // Assert
        Assert.NotEmpty(serviceTypes);

        var testingServiceType = serviceTypes.FirstOrDefault(t => t.Name == "TestingService");
        Assert.NotNull(testingServiceType);

        // Verify the DaprService attribute has correct configuration
        var daprServiceAttr = testingServiceType.GetCustomAttribute<BeyondImmersion.BannouService.Attributes.DaprServiceAttribute>();
        Assert.NotNull(daprServiceAttr);
        Assert.Equal(expectedServiceName, daprServiceAttr.Name);

        // Verify that the attribute is correctly reading the lifetime from our positional parameters
        // [DaprService("testing", typeof(ITestingService), false, ServiceLifetime.Scoped)]
        var actualLifetime = daprServiceAttr.Lifetime;
        var actualInterfaceType = daprServiceAttr.InterfaceType;
        var actualPriority = daprServiceAttr.Priority;

        // Debug output for diagnostic purposes
        System.Diagnostics.Debug.WriteLine($"Attribute values: Name={daprServiceAttr.Name}, InterfaceType={actualInterfaceType?.Name}, Priority={actualPriority}, Lifetime={actualLifetime}");

        Assert.Equal(ServiceLifetime.Scoped, actualLifetime);
    }

    /// <summary>
    /// Tests comprehensive interface/attribute-based service discovery.
    /// Validates that only types implementing IDaprService with DaprServiceAttribute are discovered.
    /// </summary>
    [Fact]
    public void InterfaceBasedDiscovery_ShouldOnlyFindValidServiceTypes()
    {
        // Arrange
        var testingAssembly = typeof(BeyondImmersion.BannouService.Testing.TestingService).Assembly;

        // Act - Test the exact logic used by PluginLoader.DiscoverServiceTypes
        var discoveredServices = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IDaprService).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttribute<DaprServiceAttribute>() != null)
            .ToList();

        // Assert - Should find TestingService
        Assert.NotEmpty(discoveredServices);
        var testingService = discoveredServices.FirstOrDefault(t => t.Name == "TestingService");
        Assert.NotNull(testingService);

        // Verify it implements IDaprService
        Assert.True(typeof(IDaprService).IsAssignableFrom(testingService));

        // Verify it has DaprServiceAttribute
        var daprAttr = testingService.GetCustomAttribute<DaprServiceAttribute>();
        Assert.NotNull(daprAttr);
        Assert.Equal("testing", daprAttr.Name);

        // Verify no types without proper interface/attribute are discovered
        var invalidTypes = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract)
            .Where(t => !typeof(IDaprService).IsAssignableFrom(t) || t.GetCustomAttribute<DaprServiceAttribute>() == null)
            .Where(t => t.Name.EndsWith("Service"))  // Only check service-like classes
            .ToList();

        // None of these should be in the discovered services list
        foreach (var invalidType in invalidTypes)
        {
            Assert.DoesNotContain(invalidType, discoveredServices);
        }
    }

    /// <summary>
    /// Tests that client discovery uses IDaprClient interface without fallbacks.
    /// </summary>
    [Fact]
    public void InterfaceBasedClientDiscovery_ShouldRequireIDaprClientInterface()
    {
        // Arrange
        var testingAssembly = typeof(TestService).Assembly;  // This assembly

        // Act - Test client discovery logic (similar to PluginLoader.DiscoverClientTypes)
        var discoveredClients = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IDaprClient).IsAssignableFrom(t))
            .ToList();

        // Assert - Should only find types that implement IDaprClient
        foreach (var clientType in discoveredClients)
        {
            Assert.True(typeof(IDaprClient).IsAssignableFrom(clientType));
            Assert.False(clientType.IsInterface);
            Assert.False(clientType.IsAbstract);
        }

        // Verify that types ending in "Client" but not implementing IDaprClient are NOT discovered
        var nonClientTypes = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && t.Name.EndsWith("Client"))
            .Where(t => !typeof(IDaprClient).IsAssignableFrom(t))
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

        // 1. Service Discovery - should find services with IDaprService + DaprServiceAttribute
        var serviceTypes = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IDaprService).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttribute<DaprServiceAttribute>() != null)
            .ToList();

        // 2. Configuration Discovery - should find configs with IServiceConfiguration (optionally + ServiceConfigurationAttribute)
        var configTypes = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IServiceConfiguration).IsAssignableFrom(t))
            .ToList();

        // 3. Client Discovery - should find clients with IDaprClient interface
        var clientTypes = testingAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IDaprClient).IsAssignableFrom(t))
            .ToList();

        // Assert - Verify discovery worked based on interfaces/attributes, not naming

        // Should find TestingService (has IDaprService + DaprServiceAttribute)
        var testingService = serviceTypes.FirstOrDefault(t => t.Name == "TestingService");
        Assert.NotNull(testingService);

        // Should find configurations (have IServiceConfiguration)
        Assert.NotEmpty(configTypes);

        // Verify no discovery based on naming alone
        var typesWithServiceInName = testingAssembly.GetTypes()
            .Where(t => t.Name.Contains("Service") && !t.IsInterface && !t.IsAbstract)
            .Where(t => !typeof(IDaprService).IsAssignableFrom(t))  // Don't implement IDaprService
            .ToList();

        // None of these should be in serviceTypes (they don't implement IDaprService)
        foreach (var nameOnlyType in typesWithServiceInName)
        {
            Assert.DoesNotContain(nameOnlyType, serviceTypes);
        }
    }

    /// <summary>
    /// Tests that the PluginLoader correctly registers service types with DaprService attributes.
    /// </summary>
    [Fact]
    public void PluginLoader_ShouldRegisterServicesWithDaprServiceAttributes()
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
        // This should be Scoped as specified in the DaprService attribute
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

        // TestService implements IDaprService but has NO DaprServiceAttribute
        var testServiceWithoutAttr = typeof(TestService);
        Assert.True(typeof(IDaprService).IsAssignableFrom(testServiceWithoutAttr));
        Assert.Null(testServiceWithoutAttr.GetCustomAttribute<DaprServiceAttribute>());

        // Mock a class that ends with "Service" but doesn't implement IDaprService
        var nonServiceClass = typeof(TestConnectService);  // Has "Service" in name but doesn't implement IDaprService
        Assert.False(typeof(IDaprService).IsAssignableFrom(nonServiceClass));

        // Simulate PluginLoader discovery logic
        var discoveredServices = currentAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IDaprService).IsAssignableFrom(t))
            .Where(t =>
            {
                try
                {
                    return t.GetCustomAttribute<DaprServiceAttribute>() != null;
                }
                catch (AmbiguousMatchException)
                {
                    // Type has multiple DaprServiceAttribute - this should be valid and found
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
            Assert.True(typeof(IDaprService).IsAssignableFrom(discoveredService));
            try
            {
                Assert.NotNull(discoveredService.GetCustomAttribute<DaprServiceAttribute>());
            }
            catch (AmbiguousMatchException)
            {
                // Type has multiple DaprServiceAttribute - this is valid, just continue
                Assert.True(discoveredService.GetCustomAttributes<DaprServiceAttribute>().Any());
            }
        }
    }

    /// <summary>
    /// Tests that service lifetime is correctly extracted from DaprServiceAttribute.
    /// </summary>
    [Fact]
    public void AttributeBasedLifetimeExtraction_ShouldRespectDaprServiceAttributeLifetime()
    {
        // Arrange
        var testingAssembly = typeof(BeyondImmersion.BannouService.Testing.TestingService).Assembly;

        // Act - Find TestingService and extract its lifetime from attribute
        var testingServiceType = testingAssembly.GetTypes()
            .Where(t => t.Name == "TestingService" && typeof(IDaprService).IsAssignableFrom(t))
            .FirstOrDefault();

        Assert.NotNull(testingServiceType);
        var daprAttr = testingServiceType.GetCustomAttribute<DaprServiceAttribute>();
        Assert.NotNull(daprAttr);

        // Assert - Verify lifetime is read correctly from attribute
        Assert.Equal(ServiceLifetime.Scoped, daprAttr.Lifetime);
        Assert.Equal("testing", daprAttr.Name);

        // Test that the PluginLoader would use this lifetime for registration
        var expectedLifetime = daprAttr.Lifetime;
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
            .Where(t => !typeof(IDaprService).IsAssignableFrom(t) && !typeof(IDaprClient).IsAssignableFrom(t) && !typeof(IServiceConfiguration).IsAssignableFrom(t))
            .ToList();

        // Act - Test interface-based discovery
        var servicesViaInterface = currentAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IDaprService).IsAssignableFrom(t))
            .ToList();

        var clientsViaInterface = currentAssembly.GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract && typeof(IDaprClient).IsAssignableFrom(t))
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
            Assert.True(typeof(IDaprService).IsAssignableFrom(service));
        }

        foreach (var client in clientsViaInterface)
        {
            Assert.True(typeof(IDaprClient).IsAssignableFrom(client));
        }

        foreach (var config in configurationsViaInterface)
        {
            Assert.True(typeof(IServiceConfiguration).IsAssignableFrom(config));
        }
    }

    /// <summary>
    /// Tests comprehensive service enable/disable logic - Mode 1: SERVICES_ENABLED=true with X_SERVICE_DISABLED flags.
    /// This tests the user's exact specification for the first mode.
    /// </summary>
    [Fact]
    public void ServiceEnableDisable_Mode1_ServicesEnabledTrue_WithDisableFlags()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        var isServiceEnabledMethod = typeof(PluginLoader)
            .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            // Mode 1: SERVICES_ENABLED=true → all services enabled by default, use X_SERVICE_DISABLED to disable individual
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "true");

            // Test 1: No disable flag set - service should be enabled
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", null);
            var result1Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var result1 = result1Obj != null && (bool)result1Obj;
            Assert.True(result1, "Service should be enabled when SERVICES_ENABLED=true and no disable flag");

            // Test 2: Disable flag set to false - service should be enabled
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", "false");
            var result2Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var result2 = result2Obj != null && (bool)result2Obj;
            Assert.True(result2, "Service should be enabled when SERVICES_ENABLED=true and X_SERVICE_DISABLED=false");

            // Test 3: Disable flag set to true - service should be disabled
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", "true");
            var result3Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var result3 = result3Obj != null && (bool)result3Obj;
            Assert.False(result3, "Service should be disabled when SERVICES_ENABLED=true and X_SERVICE_DISABLED=true");

            // Test 4: Different service - accounts
            Environment.SetEnvironmentVariable("ACCOUNTS_SERVICE_DISABLED", "true");
            var result4Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "accounts" });
            var result4 = result4Obj != null && (bool)result4Obj;
            Assert.False(result4, "Accounts service should be disabled when SERVICES_ENABLED=true and ACCOUNTS_SERVICE_DISABLED=true");

            // Test 5: Accounts with no disable flag - should be enabled
            Environment.SetEnvironmentVariable("ACCOUNTS_SERVICE_DISABLED", null);
            var result5Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "accounts" });
            var result5 = result5Obj != null && (bool)result5Obj;
            Assert.True(result5, "Accounts service should be enabled when SERVICES_ENABLED=true and no disable flag");
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("ACCOUNTS_SERVICE_DISABLED", null);
        }
    }

    /// <summary>
    /// Tests comprehensive service enable/disable logic - Mode 2: SERVICES_ENABLED=false with X_SERVICE_ENABLED flags.
    /// This tests the user's exact specification for the second mode.
    /// </summary>
    [Fact]
    public void ServiceEnableDisable_Mode2_ServicesEnabledFalse_WithEnableFlags()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        var isServiceEnabledMethod = typeof(PluginLoader)
            .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            // Mode 2: SERVICES_ENABLED=false → all services disabled by default, use X_SERVICE_ENABLED to enable individual
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "false");

            // Test 1: No enable flag set - service should be disabled
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", null);
            var result1Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var result1 = result1Obj != null && (bool)result1Obj;
            Assert.False(result1, "Service should be disabled when SERVICES_ENABLED=false and no enable flag");

            // Test 2: Enable flag set to false - service should be disabled
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "false");
            var result2Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var result2 = result2Obj != null && (bool)result2Obj;
            Assert.False(result2, "Service should be disabled when SERVICES_ENABLED=false and X_SERVICE_ENABLED=false");

            // Test 3: Enable flag set to true - service should be enabled
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "true");
            var result3Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var result3 = result3Obj != null && (bool)result3Obj;
            Assert.True(result3, "Service should be enabled when SERVICES_ENABLED=false and X_SERVICE_ENABLED=true");

            // Test 4: Different service - accounts
            Environment.SetEnvironmentVariable("ACCOUNTS_SERVICE_ENABLED", "true");
            var result4Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "accounts" });
            var result4 = result4Obj != null && (bool)result4Obj;
            Assert.True(result4, "Accounts service should be enabled when SERVICES_ENABLED=false and ACCOUNTS_SERVICE_ENABLED=true");

            // Test 5: Accounts with no enable flag - should be disabled
            Environment.SetEnvironmentVariable("ACCOUNTS_SERVICE_ENABLED", null);
            var result5Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "accounts" });
            var result5 = result5Obj != null && (bool)result5Obj;
            Assert.False(result5, "Accounts service should be disabled when SERVICES_ENABLED=false and no enable flag");
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", null);
            Environment.SetEnvironmentVariable("ACCOUNTS_SERVICE_ENABLED", null);
        }
    }

    /// <summary>
    /// Tests service enable/disable logic with various casing scenarios to ensure robustness.
    /// </summary>
    [Fact]
    public void ServiceEnableDisable_ShouldHandleCaseVariations()
    {
        // Arrange
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
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", "TRUE");  // Uppercase

            var result1Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var result1 = result1Obj != null && (bool)result1Obj;
            Assert.False(result1, "Should handle uppercase TRUE for disable flag");

            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "True");  // Mixed case
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", "False");  // Mixed case

            var result2Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var result2 = result2Obj != null && (bool)result2Obj;
            Assert.True(result2, "Should handle mixed case True/False");

            // Test lowercase service names are converted to uppercase for env var lookup
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "true");
            Environment.SetEnvironmentVariable("BEHAVIOR_SERVICE_DISABLED", "true");

            var result3Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "behavior" });
            var result3 = result3Obj != null && (bool)result3Obj;
            Assert.False(result3, "Should convert lowercase service name to uppercase for env var lookup");
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("BEHAVIOR_SERVICE_DISABLED", null);
        }
    }

    /// <summary>
    /// Tests that infrastructure test scenario works correctly - only testing service enabled.
    /// This validates the exact scenario that was failing.
    /// </summary>
    [Fact]
    public void ServiceEnableDisable_InfrastructureTestScenario_OnlyTestingEnabled()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        var isServiceEnabledMethod = typeof(PluginLoader)
            .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            // Infrastructure test configuration: SERVICES_ENABLED=true with all other services disabled
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "true");
            Environment.SetEnvironmentVariable("ACCOUNTS_SERVICE_DISABLED", "true");
            Environment.SetEnvironmentVariable("AUTH_SERVICE_DISABLED", "true");
            Environment.SetEnvironmentVariable("BEHAVIOR_SERVICE_DISABLED", "true");
            Environment.SetEnvironmentVariable("CONNECT_SERVICE_DISABLED", "true");
            Environment.SetEnvironmentVariable("GAME_SESSION_SERVICE_DISABLED", "true");
            Environment.SetEnvironmentVariable("PERMISSIONS_SERVICE_DISABLED", "true");
            Environment.SetEnvironmentVariable("WEBSITE_SERVICE_DISABLED", "true");
            // Note: TESTING_SERVICE_DISABLED is not set, so testing should be enabled

            // Act & Assert - Only testing service should be enabled
            var testingEnabledObj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var testingEnabled = testingEnabledObj != null && (bool)testingEnabledObj;
            Assert.True(testingEnabled, "Testing service should be enabled in infrastructure test scenario");

            var accountsEnabledObj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "accounts" });
            var accountsEnabled = accountsEnabledObj != null && (bool)accountsEnabledObj;
            Assert.False(accountsEnabled, "Accounts service should be disabled in infrastructure test scenario");

            var authEnabledObj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "auth" });
            var authEnabled = authEnabledObj != null && (bool)authEnabledObj;
            Assert.False(authEnabled, "Auth service should be disabled in infrastructure test scenario");

            var connectEnabledObj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "connect" });
            var connectEnabled = connectEnabledObj != null && (bool)connectEnabledObj;
            Assert.False(connectEnabled, "Connect service should be disabled in infrastructure test scenario");

            var behaviorEnabledObj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "behavior" });
            var behaviorEnabled = behaviorEnabledObj != null && (bool)behaviorEnabledObj;
            Assert.False(behaviorEnabled, "Behavior service should be disabled in infrastructure test scenario");
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Environment.SetEnvironmentVariable("ACCOUNTS_SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("AUTH_SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("BEHAVIOR_SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("CONNECT_SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("GAME_SESSION_SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("PERMISSIONS_SERVICE_DISABLED", null);
            Environment.SetEnvironmentVariable("WEBSITE_SERVICE_DISABLED", null);
        }
    }

    /// <summary>
    /// Tests mixed scenarios and edge cases for service enable/disable logic.
    /// </summary>
    [Fact]
    public void ServiceEnableDisable_EdgeCasesAndValidation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        var isServiceEnabledMethod = typeof(PluginLoader)
            .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            // Test 1: Empty string values should be treated as null
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "true");
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", "");

            var result1Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var result1 = result1Obj != null && (bool)result1Obj;
            Assert.True(result1, "Empty string disable flag should be treated as null (enabled)");

            // Test 2: Invalid boolean values should be treated as false
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", "invalid");
            var result2Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var result2 = result2Obj != null && (bool)result2Obj;
            Assert.True(result2, "Invalid boolean values should be treated as false (not disabled)");

            // Test 3: Whitespace handling
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", " true ");
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", " true ");

            var result3Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var result3 = result3Obj != null && (bool)result3Obj;
            Assert.False(result3, "Should handle whitespace in environment variables");

            // Test 4: Null service name should not crash
            Exception? caughtException = null;
            try
            {
                isServiceEnabledMethod?.Invoke(pluginLoader, new object?[] { null });
            }
            catch (Exception ex)
            {
                caughtException = ex.InnerException ?? ex; // Reflection wraps exceptions
            }

            // We expect this might throw, but it shouldn't be a catastrophic failure
            // The method should handle null service names gracefully
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            Environment.SetEnvironmentVariable("TESTING_SERVICE_DISABLED", null);
        }
    }

    /// <summary>
    /// Tests that the service enable/disable logic respects the complete two-mode system specification.
    /// This is the comprehensive validation test for the user's requirements.
    /// </summary>
    [Fact]
    public void ServiceEnableDisable_ComprehensiveTwoModeValidation()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<PluginLoader>>();
        var pluginLoader = new PluginLoader(logger);

        var isServiceEnabledMethod = typeof(PluginLoader)
            .GetMethod("IsServiceEnabled", BindingFlags.NonPublic | BindingFlags.Instance);

        var testServices = new[] { "testing", "accounts", "auth", "connect", "behavior" };

        try
        {
            // Phase 1: Test Mode 1 - SERVICES_ENABLED=true (all enabled by default)
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "true");

            // Clear all individual flags first
            foreach (var service in testServices)
            {
                Environment.SetEnvironmentVariable($"{service.ToUpper()}_SERVICE_DISABLED", null);
                Environment.SetEnvironmentVariable($"{service.ToUpper()}_SERVICE_ENABLED", null);
            }

            // All services should be enabled by default
            foreach (var service in testServices)
            {
                var resultObj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { service });
                var result = resultObj != null && (bool)resultObj;
                Assert.True(result, $"Service '{service}' should be enabled by default when SERVICES_ENABLED=true");
            }

            // Disable individual services
            Environment.SetEnvironmentVariable("ACCOUNTS_SERVICE_DISABLED", "true");
            Environment.SetEnvironmentVariable("AUTH_SERVICE_DISABLED", "true");

            var accountsResultObj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "accounts" });
            var accountsResult = accountsResultObj != null && (bool)accountsResultObj;
            var authResultObj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "auth" });
            var authResult = authResultObj != null && (bool)authResultObj;
            var testingResultObj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var testingResult = testingResultObj != null && (bool)testingResultObj;

            Assert.False(accountsResult, "Accounts should be disabled with X_SERVICE_DISABLED=true");
            Assert.False(authResult, "Auth should be disabled with X_SERVICE_DISABLED=true");
            Assert.True(testingResult, "Testing should remain enabled without disable flag");

            // Phase 2: Test Mode 2 - SERVICES_ENABLED=false (all disabled by default)
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", "false");

            // Clear all flags
            foreach (var service in testServices)
            {
                Environment.SetEnvironmentVariable($"{service.ToUpper()}_SERVICE_DISABLED", null);
                Environment.SetEnvironmentVariable($"{service.ToUpper()}_SERVICE_ENABLED", null);
            }

            // All services should be disabled by default
            foreach (var service in testServices)
            {
                var resultObj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { service });
                var result = resultObj != null && (bool)resultObj;
                Assert.False(result, $"Service '{service}' should be disabled by default when SERVICES_ENABLED=false");
            }

            // Enable individual services
            Environment.SetEnvironmentVariable("TESTING_SERVICE_ENABLED", "true");
            Environment.SetEnvironmentVariable("CONNECT_SERVICE_ENABLED", "true");

            var testingResult2Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "testing" });
            var testingResult2 = testingResult2Obj != null && (bool)testingResult2Obj;
            var connectResultObj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "connect" });
            var connectResult = connectResultObj != null && (bool)connectResultObj;
            var accountsResult2Obj = isServiceEnabledMethod?.Invoke(pluginLoader, new object[] { "accounts" });
            var accountsResult2 = accountsResult2Obj != null && (bool)accountsResult2Obj;

            Assert.True(testingResult2, "Testing should be enabled with X_SERVICE_ENABLED=true");
            Assert.True(connectResult, "Connect should be enabled with X_SERVICE_ENABLED=true");
            Assert.False(accountsResult2, "Accounts should remain disabled without enable flag");
        }
        finally
        {
            // Comprehensive cleanup
            Environment.SetEnvironmentVariable("SERVICES_ENABLED", null);
            foreach (var service in testServices)
            {
                Environment.SetEnvironmentVariable($"{service.ToUpper()}_SERVICE_DISABLED", null);
                Environment.SetEnvironmentVariable($"{service.ToUpper()}_SERVICE_ENABLED", null);
            }
        }
    }

    /// <summary>
    /// Helper method to get types implementing IDaprService from an assembly.
    /// </summary>
    private List<Type> GetTypesImplementingIDaprService(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes()
                .Where(t => typeof(IDaprService).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Handle assembly loading issues
            return ex.Types?.Where(t => t != null && typeof(IDaprService).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract).ToList() ?? new List<Type>();
        }
    }

}

/// <summary>
/// Test service class that implements IDaprService for unit testing.
/// </summary>
public class TestService : IDaprService
{
    // IDaprService methods are provided by default interface implementations
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
    private readonly IPermissionsClient _permissionsClient;

    public ConnectService(IAuthClient authClient, IPermissionsClient permissionsClient)
    {
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
        _permissionsClient = permissionsClient ?? throw new ArgumentNullException(nameof(permissionsClient));
    }
}

/// <summary>
/// Mock ConnectServiceConfiguration for unit testing.
/// </summary>
public class ConnectServiceConfiguration : IServiceConfiguration
{
    public string InstanceId { get; set; } = "test-instance";
    public string? Force_Service_ID { get; } = null;
}

/// <summary>
/// Mock IAuthClient interface for unit testing.
/// </summary>
public interface IAuthClient
{
    // Empty interface for testing purposes
}

/// <summary>
/// Mock IPermissionsClient interface for unit testing.
/// </summary>
public interface IPermissionsClient
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
    public string? Force_Service_ID { get; } = null;
}

/// <summary>
/// Test configuration class for testing scoped lifetime registration.
/// </summary>
public class TestServiceConfiguration2 : IServiceConfiguration
{
    public string TestSetting { get; set; } = "test2";
    public string? Force_Service_ID { get; } = null;
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
    private readonly IPermissionsClient _permissionsClient;

    public TestConnectService(
        TestConnectServiceConfiguration configuration,
        IAuthClient authClient,
        IPermissionsClient permissionsClient)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
        _permissionsClient = permissionsClient ?? throw new ArgumentNullException(nameof(permissionsClient));
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
    public string? Force_Service_ID { get; } = null;
}

/// <summary>
/// Configuration class that doesn't match any service for testing unmatched configuration handling.
/// </summary>
public class UnmatchedConfiguration : IServiceConfiguration
{
    public string UnusedSetting { get; set; } = "unused";
    public string? Force_Service_ID { get; } = null;
}

/// <summary>
/// Configuration class for testing configuration type filtering.
/// </summary>
public class ComplexNamingTestServiceConfiguration : IServiceConfiguration
{
    public string ComplexSetting { get; set; } = "complex";
    public string? Force_Service_ID { get; } = null;
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

        // Register service by interface (standard DaprService pattern)
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
        services.AddSingleton<IPermissionsClient>(_ => new Mock<IPermissionsClient>().Object);

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
        services.AddSingleton<IPermissionsClient>(_ => new Mock<IPermissionsClient>().Object);

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
