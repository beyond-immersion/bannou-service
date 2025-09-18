using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Plugin wrapper for Testing service enabling plugin-based discovery and lifecycle management.
/// This plugin is specifically designed to test the centralized service resolution system.
/// </summary>
public class TestingServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "testing";
    public override string DisplayName => "Testing Service";

    private TestingService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection - registers TestingService and its dependencies.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogInformation("🔧 Configuring Testing service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [DaprService] attributes
        // No need to register ITestingService and TestingService here

        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // No need to register TestingServiceConfiguration here

        Logger?.LogInformation("✅ Testing service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("🔧 Configuring Testing service application pipeline");

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("✅ Testing service application pipeline configured");
    }

    /// <summary>
    /// Start the service - uses centralized service resolution from PluginLoader.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("▶️  Starting Testing service");

        try
        {
            // Get centrally resolved service from PluginLoader
            var pluginLoader = BeyondImmersion.BannouService.Program.PluginLoader;
            if (pluginLoader != null)
            {
                var resolvedService = pluginLoader.GetResolvedService("testing");
                if (resolvedService != null)
                {
                    _service = resolvedService as TestingService;
                    Logger?.LogInformation("✅ Using centrally resolved TestingService");

                    // Test the service is working
                    if (_service != null)
                    {
                        var testResult = await _service.RunTestAsync("plugin-startup-test");
                        if (testResult.Item1 == StatusCodes.OK)
                        {
                            Logger?.LogInformation("✅ Testing service startup test passed");
                        }
                        else
                        {
                            Logger?.LogWarning("⚠️  Testing service startup test failed");
                        }
                    }

                    Logger?.LogInformation("✅ Testing service started successfully (centrally managed)");
                    return true;
                }
                else
                {
                    Logger?.LogWarning("⚠️  No centrally resolved service found for 'testing' plugin");
                }
            }
            else
            {
                Logger?.LogWarning("⚠️  PluginLoader not available for central service resolution");
            }

            // Fallback to manual service resolution
            Logger?.LogInformation("🔄 Falling back to manual service resolution");

            if (_serviceProvider == null)
            {
                Logger?.LogError("❌ Service provider is null - ConfigureApplication may not have been called");
                return false;
            }

            // Get service instance from DI container
            _service = _serviceProvider?.GetService<TestingService>();

            if (_service == null)
            {
                Logger?.LogError("❌ Failed to resolve TestingService from DI container");
                return false;
            }

            Logger?.LogInformation("✅ TestingService resolved successfully (fallback)");

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnStartAsync for Testing service");
                await daprService.OnStartAsync(CancellationToken.None);
            }

            // Test the service is working
            var fallbackTestResult = await _service.RunTestAsync("plugin-fallback-startup-test");
            if (fallbackTestResult.Item1 == StatusCodes.OK)
            {
                Logger?.LogInformation("✅ Testing service fallback startup test passed");
            }

            Logger?.LogInformation("✅ Testing service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "❌ Failed to start Testing service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("🏃 Testing service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnRunningAsync for Testing service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }

            // Run a periodic test
            var runningTestResult = await _service.RunTestAsync("plugin-running-test");
            if (runningTestResult.Item1 == StatusCodes.OK)
            {
                Logger?.LogDebug("✅ Testing service running test passed");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "⚠️  Exception during Testing service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("🛑 Shutting down Testing service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("🔄 Calling IDaprService.OnShutdownAsync for Testing service");
                await daprService.OnShutdownAsync();
            }

            Logger?.LogInformation("✅ Testing service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "⚠️  Exception during Testing service shutdown");
        }
    }
}
