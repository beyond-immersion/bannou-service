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
        await base.OnRunningAsync();

        await RegisterTerritoryClauseTypeAsync(CancellationToken.None);
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

            if (response.Registered)
            {
                Logger?.LogInformation("Territory clause type handler registered successfully with Contract service");
            }
            else
            {
                Logger?.LogDebug("Territory clause type already registered with Contract service");
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            // Conflict = already exists — idempotent, expected on restart
            Logger?.LogDebug("Territory clause type already registered (409 Conflict)");
        }
    }
}
