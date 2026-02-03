using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Location;

/// <summary>
/// Plugin wrapper for Location service enabling plugin-based discovery and lifecycle management.
/// Registers territory_constraint clause type with Contract service during startup.
/// </summary>
public class LocationServicePlugin : StandardServicePlugin<ILocationService>
{
    public override string PluginName => "location";
    public override string DisplayName => "Location Service";

    /// <summary>
    /// Running phase - registers territory_constraint clause type with Contract service.
    /// This enables Contract to validate territory constraints without direct Location dependency,
    /// maintaining SERVICE_HIERARCHY compliance (L2 Location registers with L1 Contract, not vice versa).
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        // Call base to handle IBannouService.OnRunningAsync if implemented
        await base.OnRunningAsync();

        // Register territory clause type with Contract service
        await RegisterTerritoryClauseTypeAsync(CancellationToken.None);
    }

    /// <summary>
    /// Registers the territory_constraint clause type handler with Contract service.
    /// This enables Contract to validate territory constraints without direct Location dependency.
    /// </summary>
    private async Task RegisterTerritoryClauseTypeAsync(CancellationToken cancellationToken)
    {
        if (ServiceProvider == null)
        {
            Logger?.LogWarning("ServiceProvider not available, skipping territory clause type registration");
            return;
        }

        try
        {
            using var scope = ServiceProvider.CreateScope();
            var contractClient = scope.ServiceProvider.GetService<IContractClient>();

            if (contractClient == null)
            {
                Logger?.LogDebug("IContractClient not available, skipping territory clause type registration");
                return;
            }

            // Register territory_constraint clause type with validation handler
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

            if (response.Registered)
            {
                Logger?.LogInformation("Territory clause type handler registered successfully with Contract service");
            }
            else
            {
                // Already registered (idempotent)
                Logger?.LogDebug("Territory clause type already registered with Contract service");
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            // Conflict = already exists - this is expected and fine (idempotent)
            Logger?.LogDebug("Territory clause type already registered (409 Conflict)");
        }
        catch (ApiException ex)
        {
            // Contract service unavailable or other API error - log as warning, don't crash
            Logger?.LogWarning(ex, "Contract service unavailable for clause type registration (status: {StatusCode})", ex.StatusCode);
        }
        catch (Exception ex)
        {
            // Unexpected error - log as error but don't crash plugin startup
            Logger?.LogError(ex, "Unexpected error registering territory clause type with Contract service");
        }
    }
}
