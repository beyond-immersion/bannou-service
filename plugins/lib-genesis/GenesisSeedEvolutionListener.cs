// =============================================================================
// Genesis Seed Evolution Listener
// Drives the cognitive stage progression (Dormant → EventBrain → CharacterBrain)
// when seed phase transitions fire. Spawns actors, creates characters in system
// realms, binds actors to characters, and materializes deferred bond relationships.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Actor;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Genesis;

/// <summary>
/// Receives seed evolution notifications for genesis-owned seeds and drives the cognitive
/// progression: <c>Dormant → EventBrain → CharacterBrain</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the Genesis runtime pipeline's final stage.</b> When the growth flush worker
/// writes batched growth to a genesis-owned seed, Seed's internal dispatcher fires
/// <see cref="OnPhaseChangedAsync"/> on this listener if the growth crossed a phase threshold.
/// The listener then orchestrates the cognitive stage transition:
/// </para>
/// <list type="bullet">
///   <item><b>Dormant → EventBrain (Stirring):</b> acquire transition lock, look up the pre-resolved
///     actor template ID from the shared state map, call <see cref="IActorClient.SpawnActorAsync"/>,
///     store the actor ID on the entity record, publish <c>genesis.entity.phase-changed</c>.</item>
///   <item><b>EventBrain → CharacterBrain (Awakened):</b> acquire transition lock, re-validate the
///     template's system realm and species (defense-in-depth against post-registration deletion),
///     call <see cref="ICharacterClient.CreateCharacterAsync"/> in the system realm, call
///     <see cref="IActorClient.BindActorCharacterAsync"/>, materialize any deferred bond
///     <see cref="IRelationshipClient.CreateRelationshipAsync"/> if bond intent exists on the
///     entity, publish <c>genesis.entity.phase-changed</c>.</item>
/// </list>
/// <para>
/// <b>Failure handling:</b> Every transition step is wrapped in try-catch. On failure, the listener
/// publishes <c>genesis.entity.transition-failed</c> with a human-readable reason, and does not
/// save partial state. The entity remains in its previous cognitive stage — the next growth event
/// will retry the transition naturally.
/// </para>
/// <para>
/// <b>Distributed locking:</b> Multiple nodes may receive the same phase change notification
/// (Seed's dispatcher fires per-node). The <c>transition:{entityId}</c> distributed lock ensures
/// that exactly one node performs the transition. Losing the lock is logged at debug level and
/// treated as success — another node is already handling it.
/// </para>
/// <para>
/// <b>Lifetime:</b> Registered as Singleton via <see cref="BannouHelperServiceAttribute"/>. Must
/// be a separate class from the scoped <see cref="GenesisService"/> because Seed's listener
/// collection is resolved from the singleton root provider.
/// </para>
/// </remarks>
[BannouHelperService("genesis-seed-evolution", typeof(IGenesisService), typeof(ISeedEvolutionListener), lifetime: ServiceLifetime.Singleton)]
public class GenesisSeedEvolutionListener : ISeedEvolutionListener
{
    // Key prefix for the ActorTemplateMap composite key. The map is an in-memory dictionary,
    // not a state store, but the FOUNDATION TENETS key-builder pattern (const prefix + Build*Key
    // method) is enforced uniformly via StateStoreKeyValidator and makes the format discoverable.
    private const string ACTOR_TEMPLATE_KEY_PREFIX = "actor-tpl:";

    // State stores
    private readonly IStateStore<GenesisEntityModel> _entityStore;
    private readonly IQueryableStateStore<GenesisEntityModel> _entityQueryStore;
    private readonly IStateStore<GenesisTemplateModel> _templateStore;
    private readonly IStateStore<CachedGenesisEntity> _entityCacheStore;
    private readonly IStateStore<CachedCapabilityManifest> _capsCacheStore;

    // Infrastructure
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IMessageBus _messageBus;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<GenesisSeedEvolutionListener> _logger;
    private readonly GenesisServiceConfiguration _configuration;
    private readonly GenesisGrowthState _state;

    // Service clients
    private readonly IActorClient _actorClient;
    private readonly ICharacterClient _characterClient;
    private readonly IRelationshipClient _relationshipClient;
    private readonly IRealmClient _realmClient;
    private readonly ISpeciesClient _speciesClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenesisSeedEvolutionListener"/> class.
    /// </summary>
    public GenesisSeedEvolutionListener(
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        IMessageBus messageBus,
        ITelemetryProvider telemetryProvider,
        ILogger<GenesisSeedEvolutionListener> logger,
        GenesisServiceConfiguration configuration,
        GenesisGrowthState state,
        IActorClient actorClient,
        ICharacterClient characterClient,
        IRelationshipClient relationshipClient,
        IRealmClient realmClient,
        ISpeciesClient speciesClient)
    {
        _entityStore = stateStoreFactory.GetStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities);
        _entityQueryStore = stateStoreFactory.GetQueryableStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities);
        _templateStore = stateStoreFactory.GetStore<GenesisTemplateModel>(StateStoreDefinitions.GenesisTemplates);
        _entityCacheStore = stateStoreFactory.GetStore<CachedGenesisEntity>(StateStoreDefinitions.GenesisEntityCache);
        _capsCacheStore = stateStoreFactory.GetStore<CachedCapabilityManifest>(StateStoreDefinitions.GenesisEntityCache);

        _lockProvider = lockProvider;
        _messageBus = messageBus;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
        _configuration = configuration;
        _state = state;
        _actorClient = actorClient;
        _characterClient = characterClient;
        _relationshipClient = relationshipClient;
        _realmClient = realmClient;
        _speciesClient = speciesClient;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Empty set = wildcard. Genesis filters by owner type (<see cref="EntityType.Other"/>) rather than
    /// seed type code because there is no fixed set of genesis seed types — every template registers
    /// its own seed type. Filtering by owner type in-method is cheaper than maintaining a dynamic
    /// type set in the listener.
    /// </remarks>
    public IReadOnlySet<string> InterestedSeedTypes { get; } = new HashSet<string>();

    /// <inheritdoc />
    /// <remarks>
    /// Annotated with <see cref="RequiresDistributedLockAttribute"/> because both the
    /// cognitive-transition branch AND the phase-only branch perform state store writes
    /// on genesis entities, and two nodes may receive the same phase-change notification
    /// concurrently. The structural test
    /// <c>LockRequired_ImplementationsAcquireLockBeforeWrites</c> enforces that every
    /// write path is covered by the <c>transition:{entityId}</c> distributed lock.
    /// </remarks>
    [RequiresDistributedLock("transition:{entityId}")]
    public async Task OnPhaseChangedAsync(SeedPhaseNotification notification, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisSeedEvolutionListener.OnPhaseChanged");

        // Fast filter: genesis-owned seeds always have OwnerType=Other
        if (notification.OwnerType != EntityType.Other)
            return;

        // Look up the genesis entity by seedId via the queryable store
        var entity = await FindEntityBySeedIdAsync(notification.SeedId, ct);
        if (entity == null)
        {
            _logger.LogDebug(
                "Phase change for seed {SeedId} has no matching genesis entity, ignoring",
                notification.SeedId);
            return;
        }

        var template = await _templateStore.GetAsync(
            GenesisService.BuildTemplateKey(entity.TemplateCode), ct);
        if (template == null)
        {
            _logger.LogWarning(
                "Phase change for entity {EntityId} cannot proceed: template {TemplateCode} missing",
                entity.EntityId, entity.TemplateCode);
            await PublishTransitionFailedAsync(entity, notification.NewPhase, entity.CognitiveStage,
                $"Template {entity.TemplateCode} not found", ct);
            return;
        }

        var targetPhase = template.Seed.Phases.FirstOrDefault(p => p.PhaseName == notification.NewPhase);
        if (targetPhase == null)
        {
            _logger.LogWarning(
                "Phase change for entity {EntityId}: phase {PhaseName} not in template {TemplateCode}",
                entity.EntityId, notification.NewPhase, entity.TemplateCode);
            return;
        }

        // Acquire distributed lock before ANY state-store writes. Both the phase-only
        // branch and the cognitive-transition branch write to the genesis entity store,
        // so both must execute under the transition lock. This matches the method's
        // [RequiresDistributedLock("transition:{entityId}")] annotation and prevents
        // concurrent writes from multiple nodes observing the same phase-change dispatch.
        var lockOwner = $"transition-{Guid.NewGuid():N}";
        await using var lockHandle = await _lockProvider.LockAsync(
            StateStoreDefinitions.GenesisLock,
            $"transition:{entity.EntityId}",
            lockOwner,
            _configuration.TransitionLockTimeoutSeconds,
            ct);

        if (!lockHandle.Success)
        {
            _logger.LogDebug(
                "Could not acquire transition lock for entity {EntityId} — another node is handling the transition",
                entity.EntityId);
            return;
        }

        // Re-read entity under lock (defense against stale state)
        entity = await _entityStore.GetAsync(GenesisService.BuildEntityKey(entity.EntityId), ct);
        if (entity == null)
        {
            _logger.LogDebug(
                "Entity destroyed between phase change dispatch and lock acquisition, skipping");
            return;
        }

        // No cognitive transition required — phase changed but stage stayed the same.
        // Also handles the "another node already transitioned" case when this code runs
        // after a peer completed the cognitive transition: the re-read entity now has the
        // new cognitive stage, so this branch executes the phase-only update.
        if (targetPhase.CognitiveStage == entity.CognitiveStage)
        {
            _logger.LogDebug(
                "Phase change for entity {EntityId} to {PhaseName} keeps cognitive stage {Stage}, updating phase only",
                entity.EntityId, notification.NewPhase, targetPhase.CognitiveStage);
            await UpdateEntityPhaseOnlyAsync(entity, notification.NewPhase, ct);
            return;
        }

        try
        {
            if (targetPhase.CognitiveStage == CognitiveStage.EventBrain &&
                entity.CognitiveStage == CognitiveStage.Dormant)
            {
                await TransitionToEventBrainAsync(entity, template, notification.NewPhase, ct);
            }
            else if (targetPhase.CognitiveStage == CognitiveStage.CharacterBrain &&
                     entity.CognitiveStage == CognitiveStage.EventBrain)
            {
                await TransitionToCharacterBrainAsync(entity, template, notification.NewPhase, ct);
            }
            else
            {
                _logger.LogWarning(
                    "Entity {EntityId}: unsupported cognitive transition {Current} → {Target}",
                    entity.EntityId, entity.CognitiveStage, targetPhase.CognitiveStage);
                await PublishTransitionFailedAsync(entity, notification.NewPhase, targetPhase.CognitiveStage,
                    $"Unsupported cognitive transition {entity.CognitiveStage} → {targetPhase.CognitiveStage}", ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Cognitive transition failed for entity {EntityId}: {Current} → {Target}",
                entity.EntityId, entity.CognitiveStage, targetPhase.CognitiveStage);
            await PublishTransitionFailedAsync(entity, notification.NewPhase, targetPhase.CognitiveStage,
                $"Transition exception: {ex.GetType().Name}: {ex.Message}", ct);
        }
    }

    /// <inheritdoc />
    public async Task OnGrowthRecordedAsync(SeedGrowthNotification notification, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisSeedEvolutionListener.OnGrowthRecorded");

        if (notification.OwnerType != EntityType.Other)
            return;

        // Growth may unlock new capabilities; invalidate the capability cache for the genesis entity
        var entity = await FindEntityBySeedIdAsync(notification.SeedId, ct);
        if (entity != null)
            await _capsCacheStore.DeleteAsync(GenesisService.BuildCapsCacheKey(entity.EntityId), ct);
    }

    /// <inheritdoc />
    public async Task OnCapabilitiesChangedAsync(SeedCapabilityNotification notification, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisSeedEvolutionListener.OnCapabilitiesChanged");

        if (notification.OwnerType != EntityType.Other)
            return;

        var entity = await FindEntityBySeedIdAsync(notification.SeedId, ct);
        if (entity != null)
            await _capsCacheStore.DeleteAsync(GenesisService.BuildCapsCacheKey(entity.EntityId), ct);
    }

    /// <summary>
    /// Transitions a Dormant entity to EventBrain: spawns its actor and stores the actorId.
    /// </summary>
    private async Task TransitionToEventBrainAsync(
        GenesisEntityModel entity, GenesisTemplateModel template, string newPhase, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisSeedEvolutionListener.TransitionToEventBrain");

        // Resolve the pre-registered actor template for this phase
        var actorTemplateKey = BuildActorTemplateKey(entity.TemplateCode, newPhase);
        if (!_state.TryGetActorTemplate(actorTemplateKey, out var actorTemplateId))
        {
            _logger.LogWarning(
                "Entity {EntityId}: no actor template pre-registered for phase {Phase} (key {Key})",
                entity.EntityId, newPhase, actorTemplateKey);
            await PublishTransitionFailedAsync(entity, newPhase, CognitiveStage.EventBrain,
                $"Actor template not pre-registered for phase {newPhase}", ct);
            return;
        }

        // Spawn the actor. Actor API uses a string actorId; we pass the entity GUID formatted as string
        // so the actor is unambiguously tied to this genesis entity. Since we generate the actorId
        // from entity.EntityId, we store entity.EntityId directly rather than parsing the response —
        // Actor echoes the request actorId verbatim, so parsing would round-trip the same GUID we sent.
        try
        {
            await _actorClient.SpawnActorAsync(
                new SpawnActorRequest
                {
                    TemplateId = actorTemplateId,
                    ActorId = entity.EntityId.ToString(),
                    CharacterId = null,
                    RealmId = entity.RealmId,
                }, ct);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Actor spawn failed for entity {EntityId} (template {ActorTemplateId})",
                entity.EntityId, actorTemplateId);
            await PublishTransitionFailedAsync(entity, newPhase, CognitiveStage.EventBrain,
                $"Actor spawn failed: status {ex.StatusCode}", ct);
            return;
        }

        // Update entity and persist. ActorId tracks the entity's GUID — Actor's string actorId
        // is the same value round-trip (see SpawnActorRequest.ActorId above).
        entity.ActorId = entity.EntityId;
        entity.CognitiveStage = CognitiveStage.EventBrain;
        entity.CurrentPhase = newPhase;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _entityStore.SaveAsync(GenesisService.BuildEntityKey(entity.EntityId), entity, cancellationToken: ct);
        await InvalidateEntityCachesAsync(entity.EntityId, ct);

        await _messageBus.PublishGenesisEntityPhaseChangedAsync(new GenesisEntityPhaseChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EntityId = entity.EntityId,
            TemplateCode = entity.TemplateCode,
            PhaseName = newPhase,
            CognitiveStage = CognitiveStage.EventBrain,
            ActorId = entity.ActorId,
            CharacterId = null,
        }, ct);

        _logger.LogInformation(
            "Entity {EntityId} transitioned to EventBrain (phase {Phase}, actor {ActorId})",
            entity.EntityId, newPhase, actorGuid);
    }

    /// <summary>
    /// Transitions an EventBrain entity to CharacterBrain: creates a character in the system realm,
    /// binds the actor, and materializes any deferred bond relationship.
    /// </summary>
    private async Task TransitionToCharacterBrainAsync(
        GenesisEntityModel entity, GenesisTemplateModel template, string newPhase, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisSeedEvolutionListener.TransitionToCharacterBrain");

        if (entity.ActorId == null)
        {
            _logger.LogError(
                "Entity {EntityId} is EventBrain but has no ActorId — cannot transition to CharacterBrain",
                entity.EntityId);
            await PublishTransitionFailedAsync(entity, newPhase, CognitiveStage.CharacterBrain,
                "EventBrain entity missing ActorId", ct);
            return;
        }

        // Re-validate system realm (defense-in-depth against post-registration deletion)
        RealmResponse realm;
        try
        {
            realm = await _realmClient.GetRealmByCodeAsync(
                new GetRealmByCodeRequest { Code = template.Awakening.SystemRealmCode }, ct);
            if (!realm.IsSystemType)
            {
                await PublishTransitionFailedAsync(entity, newPhase, CognitiveStage.CharacterBrain,
                    $"Realm {template.Awakening.SystemRealmCode} is no longer a system realm", ct);
                return;
            }
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "System realm {RealmCode} unavailable during awakening for entity {EntityId}",
                template.Awakening.SystemRealmCode, entity.EntityId);
            await PublishTransitionFailedAsync(entity, newPhase, CognitiveStage.CharacterBrain,
                $"System realm {template.Awakening.SystemRealmCode} unavailable (status {ex.StatusCode})", ct);
            return;
        }

        // Re-validate species
        SpeciesResponse species;
        try
        {
            species = await _speciesClient.GetSpeciesByCodeAsync(
                new GetSpeciesByCodeRequest { Code = template.Awakening.CharacterSpeciesCode }, ct);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Species {SpeciesCode} unavailable during awakening for entity {EntityId}",
                template.Awakening.CharacterSpeciesCode, entity.EntityId);
            await PublishTransitionFailedAsync(entity, newPhase, CognitiveStage.CharacterBrain,
                $"Species {template.Awakening.CharacterSpeciesCode} unavailable (status {ex.StatusCode})", ct);
            return;
        }

        // Create the character in the system realm
        CharacterResponse character;
        try
        {
            character = await _characterClient.CreateCharacterAsync(
                new CreateCharacterRequest
                {
                    Name = entity.DisplayName ?? entity.TemplateCode,
                    RealmId = realm.RealmId,
                    SpeciesId = species.SpeciesId,
                    BirthDate = DateTimeOffset.UtcNow,
                    Status = CharacterStatus.Alive,
                }, ct);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Character creation failed for entity {EntityId}",
                entity.EntityId);
            await PublishTransitionFailedAsync(entity, newPhase, CognitiveStage.CharacterBrain,
                $"Character creation failed: status {ex.StatusCode}", ct);
            return;
        }

        // Personality seeding is currently a no-op: character-personality lives at L4 and Genesis
        // (L2) cannot hold a hard dependency on it per the service hierarchy rules. The initial
        // traits are preserved on the template and will be applied by a future lib-character-personality
        // subscription to genesis.entity.phase-changed (see ACTOR-BOUND-ENTITIES.md and the Genesis
        // deep dive's "Stubs" section). Logging a warning when traits are configured but skipped.
        if (template.Awakening.InitialPersonalityTraits != null &&
            template.Awakening.InitialPersonalityTraits.Count > 0)
        {
            _logger.LogWarning(
                "Entity {EntityId} template {TemplateCode} specifies {TraitCount} initial personality traits " +
                "but Genesis cannot seed them directly (L2 → L4 hierarchy violation). Traits will be applied " +
                "via future lib-character-personality subscription to genesis.entity.phase-changed.",
                entity.EntityId, entity.TemplateCode, template.Awakening.InitialPersonalityTraits.Count);
        }

        // Bind the actor to the character
        try
        {
            await _actorClient.BindActorCharacterAsync(
                new BindActorCharacterRequest
                {
                    ActorId = entity.ActorId.Value.ToString(),
                    CharacterId = character.CharacterId,
                }, ct);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Actor binding failed for entity {EntityId} (actor {ActorId}, character {CharacterId})",
                entity.EntityId, entity.ActorId, character.CharacterId);
            await PublishTransitionFailedAsync(entity, newPhase, CognitiveStage.CharacterBrain,
                $"Actor binding failed: status {ex.StatusCode}", ct);
            return;
        }

        // Materialize deferred bond Relationship if bond intent exists on the entity
        Guid? bondId = null;
        if (entity.BondTargetEntityId != null && entity.BondTargetEntityType != null && entity.BondId == null)
        {
            bondId = await TryMaterializeDeferredBondAsync(entity, template, character.CharacterId, ct);
        }

        // Persist entity state
        entity.CharacterId = character.CharacterId;
        entity.CognitiveStage = CognitiveStage.CharacterBrain;
        entity.CurrentPhase = newPhase;
        if (bondId != null) entity.BondId = bondId;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await _entityStore.SaveAsync(GenesisService.BuildEntityKey(entity.EntityId), entity, cancellationToken: ct);
        await InvalidateEntityCachesAsync(entity.EntityId, ct);

        await _messageBus.PublishGenesisEntityPhaseChangedAsync(new GenesisEntityPhaseChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EntityId = entity.EntityId,
            TemplateCode = entity.TemplateCode,
            PhaseName = newPhase,
            CognitiveStage = CognitiveStage.CharacterBrain,
            ActorId = entity.ActorId,
            CharacterId = entity.CharacterId,
        }, ct);

        _logger.LogInformation(
            "Entity {EntityId} transitioned to CharacterBrain (phase {Phase}, character {CharacterId})",
            entity.EntityId, newPhase, character.CharacterId);
    }

    /// <summary>
    /// Updates the entity's current phase without changing cognitive stage. Used when a phase change
    /// keeps the entity in the same cognitive stage (e.g., Awakened → Ancient both CharacterBrain).
    /// </summary>
    private async Task UpdateEntityPhaseOnlyAsync(
        GenesisEntityModel entity, string newPhase, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisSeedEvolutionListener.UpdateEntityPhaseOnly");

        entity.CurrentPhase = newPhase;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _entityStore.SaveAsync(GenesisService.BuildEntityKey(entity.EntityId), entity, cancellationToken: ct);
        await InvalidateEntityCachesAsync(entity.EntityId, ct);

        await _messageBus.PublishGenesisEntityPhaseChangedAsync(new GenesisEntityPhaseChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EntityId = entity.EntityId,
            TemplateCode = entity.TemplateCode,
            PhaseName = newPhase,
            CognitiveStage = entity.CognitiveStage,
            ActorId = entity.ActorId,
            CharacterId = entity.CharacterId,
        }, ct);
    }

    /// <summary>
    /// Attempts to create a Relationship for a deferred bond at awakening time. Returns the new
    /// bondId on success, or null if bond materialization failed — the entity awakens normally
    /// either way (the bond will materialize on the next explicit CreateBond call).
    /// </summary>
    private async Task<Guid?> TryMaterializeDeferredBondAsync(
        GenesisEntityModel entity, GenesisTemplateModel template, Guid characterId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisSeedEvolutionListener.TryMaterializeDeferredBond");

        if (string.IsNullOrEmpty(template.Bond.RelationshipTypeCode))
        {
            _logger.LogWarning(
                "Entity {EntityId} has bond intent but template has no RelationshipTypeCode — skipping bond materialization",
                entity.EntityId);
            return null;
        }

        try
        {
            var relType = await _relationshipClient.GetRelationshipTypeByCodeAsync(
                new GetRelationshipTypeByCodeRequest { Code = template.Bond.RelationshipTypeCode }, ct);
            var relResponse = await _relationshipClient.CreateRelationshipAsync(
                new CreateRelationshipRequest
                {
                    Entity1Id = characterId,
                    Entity1Type = EntityType.Character,
                    Entity2Id = entity.BondTargetEntityId!.Value,
                    Entity2Type = entity.BondTargetEntityType!.Value,
                    RelationshipTypeId = relType.RelationshipTypeId,
                    StartedAt = DateTimeOffset.UtcNow,
                }, ct);
            _logger.LogInformation(
                "Materialized deferred bond for entity {EntityId}: relationship {RelationshipId}",
                entity.EntityId, relResponse.RelationshipId);
            return relResponse.RelationshipId;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Deferred bond materialization failed for entity {EntityId} at awakening",
                entity.EntityId);
            return null;
        }
    }

    /// <summary>
    /// Looks up a genesis entity by its seedId via the queryable store.
    /// </summary>
    private async Task<GenesisEntityModel?> FindEntityBySeedIdAsync(Guid seedId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisSeedEvolutionListener.FindEntityBySeedId");

        var results = await _entityQueryStore.QueryAsync(e => e.SeedId == seedId, ct);
        return results.FirstOrDefault();
    }

    /// <summary>
    /// Invalidates both the entity cache and capability cache entries for an entity.
    /// </summary>
    private async Task InvalidateEntityCachesAsync(Guid entityId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisSeedEvolutionListener.InvalidateEntityCaches");

        await _entityCacheStore.DeleteAsync(GenesisService.BuildEntityCacheKey(entityId), ct);
        await _capsCacheStore.DeleteAsync(GenesisService.BuildCapsCacheKey(entityId), ct);
    }

    /// <summary>
    /// Publishes <c>genesis.entity.transition-failed</c> with a human-readable failure reason.
    /// </summary>
    private async Task PublishTransitionFailedAsync(
        GenesisEntityModel entity, string targetPhase, CognitiveStage targetCognitiveStage,
        string failureReason, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisSeedEvolutionListener.PublishTransitionFailed");

        await _messageBus.PublishGenesisEntityTransitionFailedAsync(new GenesisEntityTransitionFailedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EntityId = entity.EntityId,
            TargetPhase = targetPhase,
            TargetCognitiveStage = targetCognitiveStage,
            FailureReason = failureReason,
        }, ct);
    }

    /// <summary>
    /// Builds the composite key for the actor template map:
    /// <c>{ACTOR_TEMPLATE_KEY_PREFIX}{templateCode}:{phaseName}</c>.
    /// </summary>
    internal static string BuildActorTemplateKey(string templateCode, string phaseName)
        => $"{ACTOR_TEMPLATE_KEY_PREFIX}{templateCode}:{phaseName}";
}
