using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Testing;

/// <summary>
/// Plugin wrapper for Testing service enabling plugin-based discovery and lifecycle management.
/// This plugin is specifically designed to test the centralized service resolution system.
/// </summary>
public class TestingServicePlugin : StandardServicePlugin<ITestingService>
{
    public override string PluginName => "testing";
    public override string DisplayName => "Testing Service";

    /// <summary>
    /// Start the service - uses centralized service resolution from PluginLoader to verify the system works.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting service");

        try
        {
            // Test centralized service resolution from PluginLoader
            var pluginLoader = BeyondImmersion.BannouService.Program.PluginLoader;
            if (pluginLoader != null)
            {
                var resolvedService = pluginLoader.GetResolvedService("testing");
                if (resolvedService != null)
                {
                    var testingService = resolvedService as TestingService;
                    Logger?.LogInformation("Using centrally resolved TestingService");

                    // Test the service is working
                    if (testingService != null)
                    {
                        var testResult = await testingService.RunTestAsync("plugin-startup-test");
                        if (testResult.Item1 == StatusCodes.OK)
                        {
                            Logger?.LogInformation("Testing service startup test passed");
                        }
                        else
                        {
                            Logger?.LogWarning("Testing service startup test failed");
                        }
                    }

                    Logger?.LogInformation("Service started (centrally managed)");
                    return true;
                }
                else
                {
                    Logger?.LogWarning("No centrally resolved service found for 'testing' plugin");
                }
            }
            else
            {
                Logger?.LogWarning("PluginLoader not available for central service resolution");
            }

            // Fallback to standard base class behavior
            Logger?.LogInformation("Falling back to standard service resolution");
            return await base.OnStartAsync();
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - runs periodic tests to verify service health.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (Service == null) return;

        Logger?.LogDebug("Service running");

        try
        {
            // Call base for standard IBannouService lifecycle
            await base.OnRunningAsync();

            // Run a periodic test
            var runningTestResult = await Service.RunTestAsync("plugin-running-test");
            if (runningTestResult.Item1 == StatusCodes.OK)
            {
                Logger?.LogDebug("Testing service running test passed");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during running phase");
        }
    }
}
