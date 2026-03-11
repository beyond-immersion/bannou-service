using BeyondImmersion.Bannou.BehaviorCompiler.Templates;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Generated.ResourceTemplates;
using BeyondImmersion.BannouService.Location.Caching;
using BeyondImmersion.BannouService.Location.Providers;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Location;

/// <summary>
/// Plugin wrapper for Location service enabling plugin-based discovery and lifecycle management.
/// Registers territory_constraint clause type with Contract service during startup.
/// Registers entity presence cleanup background worker.
/// </summary>
public class LocationServicePlugin : StandardServicePlugin<ILocationService>
{
    public override string PluginName => "location";
    public override string DisplayName => "Location Service";

    /// <summary>
    /// Registers background services, caches, and variable provider factories.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddHostedService<EntityPresenceCleanupWorker>();
    }

    /// <summary>
    /// Running phase - registers territory_constraint clause type with Contract service.
    /// This enables Contract to validate territory constraints without direct Location dependency,
    /// maintaining SERVICE_HIERARCHY compliance (L2 Location registers with L1 Contract, not vice versa).
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        await RegisterTerritoryClauseTypeAsync(CancellationToken.None);
        await RegisterCompressionCallbackAsync(CancellationToken.None);
    }

    /// <summary>
    /// Registers the location resource template and compression callback with lib-resource.
    /// IResourceTemplateRegistry is L0 infrastructure and IResourceClient is L1 — both guaranteed available.
    /// </summary>
    private async Task RegisterCompressionCallbackAsync(CancellationToken cancellationToken)
    {
        var serviceProvider = ServiceProvider ?? throw new InvalidOperationException(
            "ServiceProvider not available during OnRunningAsync");

        // Register resource template for ABML compile-time path validation.
        var templateRegistry = serviceProvider.GetRequiredService<IResourceTemplateRegistry>();
        templateRegistry.Register(new LocationBaseTemplate());
        Logger?.LogDebug("Registered location resource template with namespace 'location'");

        // Register compression callback with lib-resource.
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();
        if (await LocationCompressionCallbacks.RegisterAsync(resourceClient, cancellationToken))
        {
            Logger?.LogInformation("Registered location compression callback with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register location compression callback with lib-resource");
        }
    }

    /// <summary>
    /// Registers the territory_constraint clause type handler with Contract service.
    /// This enables Contract to validate territory constraints without direct Location dependency.
    /// IContractClient is L1 (AppFoundation) — guaranteed available when L2 runs per FOUNDATION TENETS.
    /// </summary>
    private async Task RegisterTerritoryClauseTypeAsync(CancellationToken cancellationToken)
    {
        var serviceProvider = ServiceProvider ?? throw new InvalidOperationException(
            "ServiceProvider not available during OnRunningAsync");

        using var scope = serviceProvider.CreateScope();

        // IContractClient is L1 — must be available (fail-fast per FOUNDATION TENETS)
        var contractClient = scope.ServiceProvider.GetRequiredService<IContractClient>();

        try
        {
            var response = await contractClient.RegisterClauseTypeAsync(new RegisterClauseTypeRequest
            {
                TypeCode = "territory_constraint",
                Description = "Validates entity location against territorial boundaries defined in contract",
                Category = ClauseCategory.Validation,
                ValidationHandler = new ClauseHandlerDefinition
                {
                    Service = "location",
                    Endpoint = "/location/validate-territory",
                    RequestMapping = new Dictionary<string, object>
                    {
                        ["locationId"] = "$.proposedAction.locationId",
                        ["territoryLocationIds"] = "$.customTerms.territoryLocationIds",
                        ["territoryMode"] = "$.customTerms.territoryMode"
                    },
                    ResponseMapping = new Dictionary<string, object>
                    {
                        ["is_valid"] = "$.isValid",
                        ["reason"] = "$.violationReason"
                    }
                }
            }, cancellationToken);

            Logger?.LogInformation("Territory clause type handler registered successfully with Contract service: {TypeCode}", response.TypeCode);
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            // Conflict = already exists — idempotent, expected on restart
            Logger?.LogDebug("Territory clause type already registered (409 Conflict)");
        }
    }
}
