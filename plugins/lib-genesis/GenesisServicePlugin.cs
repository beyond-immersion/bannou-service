using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Genesis;

/// <summary>
/// Plugin wrapper for Genesis service enabling plugin-based discovery and lifecycle management.
/// </summary>
/// <remarks>
/// <para>
/// Overrides the standard plugin lifecycle to:
/// <list type="bullet">
///   <item><b>OnStartAsync:</b> populate the in-memory wallet map from MySQL by iterating all active
///     genesis entities. This is the fast-path cache that <see cref="GenesisCurrencyTransactionListener"/>
///     uses to decide whether a currency mutation belongs to a genesis entity.</item>
///   <item><b>OnRunningAsync:</b> re-register seed types with Seed and actor templates with Actor for
///     all pre-existing genesis templates. New templates register these at creation time; startup
///     re-registration handles templates created before this deployment's code was updated. Also
///     delegates to the base class for resource cleanup and compression callback registration.</item>
/// </list>
/// </para>
/// </remarks>
public class GenesisServicePlugin : StandardServicePlugin<IGenesisService>
{
    public override string PluginName => "genesis";
    public override string DisplayName => "Genesis Service";

    /// <inheritdoc/>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // GenesisGrowthState is the shared substrate between the currency transaction listener,
        // the growth flush worker, the seed evolution listener, and the self-subscription event
        // handlers. Singleton because it holds the in-memory wallet map and growth accumulator,
        // and because the listener + worker are both Singletons.
        services.AddSingleton<GenesisGrowthState>();
    }

    /// <inheritdoc/>
    protected override async Task<bool> OnStartAsync()
    {
        var started = await base.OnStartAsync();
        if (!started) return false;

        using var scope = ServiceProvider!.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var state = scope.ServiceProvider.GetRequiredService<GenesisGrowthState>();

        var entityQueryStore = stateStoreFactory.GetQueryableStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities);
        var templateStore = stateStoreFactory.GetStore<GenesisTemplateModel>(StateStoreDefinitions.GenesisTemplates);

        Logger?.LogInformation("Populating wallet map from MySQL for currency transaction fast-path");

        try
        {
            var activeEntities = await entityQueryStore.QueryAsync(
                e => e.Status == GenesisEntityStatus.Active, CancellationToken.None);

            var entityCount = 0;
            var walletCount = 0;
            foreach (var entity in activeEntities)
            {
                try
                {
                    var template = await templateStore.GetAsync(
                        GenesisService.BuildTemplateKey(entity.TemplateCode), CancellationToken.None);
                    if (template == null)
                    {
                        Logger?.LogWarning(
                            "Skipping wallet map entry for entity {EntityId}: template {TemplateCode} missing",
                            entity.EntityId, entity.TemplateCode);
                        continue;
                    }

                    foreach (var (walletCode, walletId) in entity.WalletIds)
                    {
                        state.WalletMap[walletId] = new GenesisWalletMapping(
                            EntityId: entity.EntityId,
                            TemplateCode: entity.TemplateCode,
                            WalletCode: walletCode,
                            GrowthMappings: template.Economy.GrowthMappings.ToList());
                        walletCount++;
                    }
                    entityCount++;
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex,
                        "Failed to populate wallet map entry for entity {EntityId}", entity.EntityId);
                }
            }

            Logger?.LogInformation(
                "Wallet map populated: {EntityCount} entities, {WalletCount} wallet mappings",
                entityCount, walletCount);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to populate wallet map during OnStartAsync");
            // Do not fail plugin startup — the wallet map will be populated incrementally as entities
            // are created (via self-subscription to genesis.entity.created events). Currency mutations
            // for pre-existing entities until the next template update will simply miss the listener.
        }

        return true;
    }

    /// <inheritdoc/>
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        using var scope = ServiceProvider!.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var seedClient = scope.ServiceProvider.GetRequiredService<ISeedClient>();
        var actorClient = scope.ServiceProvider.GetRequiredService<IActorClient>();
        var state = scope.ServiceProvider.GetRequiredService<GenesisGrowthState>();

        // Resource cleanup and compression callback registration
        if (await GenesisService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None))
            Logger?.LogInformation("Registered resource cleanup callbacks with lib-resource");
        else
            Logger?.LogWarning("Failed to register some resource cleanup callbacks");

        if (await GenesisCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
            Logger?.LogInformation("Registered compression callback with lib-resource");
        else
            Logger?.LogWarning("Failed to register compression callback");

        // Re-register seed types and actor templates for pre-existing genesis templates.
        // New templates register these at RegisterTemplate time; this loop handles templates
        // that predate the current deployment's code (e.g., registered under an older version
        // that didn't create actor templates).
        var templateQueryStore = stateStoreFactory.GetQueryableStore<GenesisTemplateModel>(StateStoreDefinitions.GenesisTemplates);
        try
        {
            var allTemplates = await templateQueryStore.QueryAsync(_ => true, CancellationToken.None);
            var seedTypeCount = 0;
            var actorTemplateCount = 0;

            foreach (var template in allTemplates)
            {
                try
                {
                    await EnsureSeedTypeAsync(template, seedClient);
                    seedTypeCount++;
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex,
                        "Failed to re-register seed type for template {TemplateCode}", template.TemplateCode);
                }

                try
                {
                    var registered = await EnsureActorTemplatesAsync(template, actorClient, state);
                    actorTemplateCount += registered;
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex,
                        "Failed to re-register actor templates for template {TemplateCode}", template.TemplateCode);
                }
            }

            Logger?.LogInformation(
                "Pre-existing template registration complete: {SeedTypes} seed types, {ActorTemplates} actor templates",
                seedTypeCount, actorTemplateCount);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to re-register templates during OnRunningAsync");
        }
    }

    /// <summary>
    /// Ensures a seed type definition exists with Seed for the given genesis template.
    /// Re-registration is idempotent via Seed's RegisterSeedType conflict handling (returns the
    /// existing definition if the type code is already registered).
    /// </summary>
    private static async Task EnsureSeedTypeAsync(GenesisTemplateModel template, ISeedClient seedClient)
    {
        try
        {
            await seedClient.RegisterSeedTypeAsync(
                new RegisterSeedTypeRequest
                {
                    SeedTypeCode = template.Seed.SeedTypeCode,
                    GameServiceId = template.GameServiceId,
                    DisplayName = template.DisplayName,
                    Description = template.Description,
                    MaxPerOwner = 1,
                    AllowedOwnerTypes = new List<EntityType> { EntityType.Other },
                    GrowthPhases = template.Seed.Phases.Select(p => new GrowthPhaseDefinition
                    {
                        PhaseCode = p.PhaseName,
                        DisplayName = p.PhaseName,
                        MinTotalGrowth = (float)p.Threshold,
                    }).ToList(),
                    BondCardinality = 0,
                    BondPermanent = false,
                    CapabilityRules = template.Seed.CapabilityRules?.Select(c => new CapabilityRule
                    {
                        CapabilityCode = c.CapabilityCode,
                        Domain = c.Domain,
                        UnlockThreshold = (float)c.Threshold,
                        FidelityFormula = "linear",
                    }).ToList(),
                }, CancellationToken.None);
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            // Already registered — that's fine, nothing to update
        }
    }

    /// <summary>
    /// Ensures actor templates exist with Actor for each genesis phase that declares a behaviorRef
    /// and a cognitive stage requiring an actor. Populates <see cref="GenesisGrowthState.ActorTemplateMap"/>
    /// with the resolved template IDs for <see cref="GenesisSeedEvolutionListener"/> to consume.
    /// </summary>
    /// <returns>The number of actor templates successfully ensured for this genesis template.</returns>
    private static async Task<int> EnsureActorTemplatesAsync(
        GenesisTemplateModel template, IActorClient actorClient, GenesisGrowthState state)
    {
        var count = 0;
        foreach (var phase in template.Seed.Phases)
        {
            // Only phases that run an actor need a template. Dormant phases have no actor.
            if (phase.CognitiveStage == CognitiveStage.Dormant) continue;
            if (string.IsNullOrWhiteSpace(phase.BehaviorRef)) continue;

            var mapKey = GenesisSeedEvolutionListener.BuildActorTemplateKey(template.TemplateCode, phase.PhaseName);
            if (state.ActorTemplateMap.ContainsKey(mapKey))
            {
                count++;
                continue;
            }

            var category = $"genesis:{template.TemplateCode}:{phase.PhaseName}";
            Guid actorTemplateId;
            try
            {
                var response = await actorClient.CreateActorTemplateAsync(
                    new CreateActorTemplateRequest
                    {
                        Category = category,
                        BehaviorRef = phase.BehaviorRef!,
                    }, CancellationToken.None);
                actorTemplateId = response.TemplateId;
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                // Already exists — look it up by category to get the templateId
                var existing = await actorClient.GetActorTemplateAsync(
                    new GetActorTemplateRequest { Category = category }, CancellationToken.None);
                actorTemplateId = existing.TemplateId;
            }

            state.ActorTemplateMap[mapKey] = actorTemplateId;
            count++;
        }
        return count;
    }
}
