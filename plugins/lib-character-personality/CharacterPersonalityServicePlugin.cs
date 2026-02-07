using BeyondImmersion.Bannou.BehaviorCompiler.Templates;
using BeyondImmersion.BannouService.CharacterPersonality.Caching;
using BeyondImmersion.BannouService.CharacterPersonality.Providers;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.LibCharacterPersonality.Templates;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.CharacterPersonality;

/// <summary>
/// Plugin wrapper for CharacterPersonality service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IBannouService implementation with the new Plugin system.
/// </summary>
public class CharacterPersonalityServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "character-personality";
    public override string DisplayName => "CharacterPersonality Service";

    private ICharacterPersonalityService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection - mimics existing [BannouService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [BannouService] attributes
        // No need to register ICharacterPersonalityService and CharacterPersonalityService here

        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // No need to register CharacterPersonalityServiceConfiguration here

        // Register personality data cache (singleton for cross-request caching)
        services.AddSingleton<IPersonalityDataCache, PersonalityDataCache>();

        // Register variable provider factories for Actor to discover via DI
        // These enable dependency inversion: Actor (L2) consumes providers without knowing about CharacterPersonality (L3)
        services.AddSingleton<IVariableProviderFactory, PersonalityProviderFactory>();
        services.AddSingleton<IVariableProviderFactory, CombatPreferencesProviderFactory>();

        Logger?.LogDebug("Service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring CharacterPersonality service application pipeline");

        // The generated CharacterPersonalityController should already be discovered via standard ASP.NET Core controller discovery

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("CharacterPersonality service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting CharacterPersonality service");

        try
        {
            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<ICharacterPersonalityService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve ICharacterPersonalityService from DI container");
                return false;
            }

            // Call existing IBannouService.OnStartAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnStartAsync for CharacterPersonality service");
                await bannouService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("CharacterPersonality service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start CharacterPersonality service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IBannouService lifecycle if present.
    /// Also registers cleanup callbacks with lib-resource (must happen after all plugins are started).
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("CharacterPersonality service running");

        var serviceProvider = _serviceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");

        try
        {
            // Call existing IBannouService.OnRunningAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnRunningAsync for CharacterPersonality service");
                await bannouService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during CharacterPersonality service running phase");
        }

        // Register resource template for ABML compile-time path validation.
        // This enables SemanticAnalyzer to validate expressions like ${candidate.personality.archetypeHint}.
        try
        {
            var templateRegistry = serviceProvider.GetService<IResourceTemplateRegistry>();
            if (templateRegistry != null)
            {
                templateRegistry.Register(new CharacterPersonalityTemplate());
                Logger?.LogDebug("Registered character-personality resource template with namespace 'personality'");
            }
            else
            {
                Logger?.LogDebug("IResourceTemplateRegistry not available - resource template not registered");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to register character-personality resource template");
        }

        // Register cleanup callbacks with lib-resource for character reference tracking.
        // This MUST happen in OnRunningAsync (not OnStartAsync) because OnRunningAsync runs
        // AFTER all plugins have completed StartAsync, ensuring lib-resource is available.
        // Registering during OnStartAsync would be unsafe because plugin load order isn't guaranteed.
        try
        {
            using var scope = serviceProvider.CreateScope();
            var resourceClient = scope.ServiceProvider.GetService<IResourceClient>();
            if (resourceClient != null)
            {
                var success = await CharacterPersonalityService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
                if (success)
                {
                    Logger?.LogInformation("Registered character cleanup callbacks with lib-resource");
                }
                else
                {
                    Logger?.LogWarning("Failed to register some cleanup callbacks with lib-resource");
                }

                // Register compression callback (generated from x-compression-callback)
                if (await CharacterPersonalityCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
                {
                    Logger?.LogInformation("Registered character-personality compression callback with lib-resource");
                }
                else
                {
                    Logger?.LogWarning("Failed to register character-personality compression callback with lib-resource");
                }
            }
            else
            {
                Logger?.LogDebug("IResourceClient not available - cleanup callbacks not registered (lib-resource may not be enabled)");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to register cleanup callbacks with lib-resource");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("Shutting down CharacterPersonality service");

        try
        {
            // Call existing IBannouService.OnShutdownAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnShutdownAsync for CharacterPersonality service");
                await bannouService.OnShutdownAsync();
            }

            Logger?.LogInformation("CharacterPersonality service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during CharacterPersonality service shutdown");
        }
    }
}
