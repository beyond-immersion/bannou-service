using BeyondImmersion.Bannou.Character.ClientEvents;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Character;

/// <summary>
/// Implementation of the Character service for game worlds.
/// Characters are independent world assets (not owned by accounts).
/// Uses realm-based partitioning for scalability.
/// Note: Character relationships are managed by the separate Relationship service.
/// </summary>
[BannouService("character", typeof(ICharacterService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class CharacterService : ICharacterService
{
    /// <summary>State store for character data (keyed by realm:characterId).</summary>
    private readonly IStateStore<CharacterModel> _characterStore;

    /// <summary>State store for character archive data (compressed dead characters).</summary>
    private readonly IStateStore<CharacterArchiveModel> _archiveStore;

    /// <summary>State store for reference count tracking data.</summary>
    private readonly IStateStore<RefCountData> _refCountStore;

    /// <summary>State store for global character-to-realm index (string values).</summary>
    private readonly IStateStore<string> _globalIndexStore;

    /// <summary>State store for realm-partitioned character ID lists.</summary>
    private readonly IStateStore<List<string>> _realmIndexStore;

    /// <summary>JSON-queryable state store for server-side character queries.</summary>
    private readonly IJsonQueryableStateStore<CharacterModel> _characterJsonStore;

    private readonly IMessageBus _messageBus;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<CharacterService> _logger;
    private readonly CharacterServiceConfiguration _configuration;
    private readonly IRealmClient _realmClient;
    private readonly ISpeciesClient _speciesClient;
    private readonly IRelationshipClient _relationshipClient;
    private readonly IContractClient _contractClient;
    private readonly IResourceClient _resourceClient;
    private readonly IEntitySessionRegistry _entitySessionRegistry;
    private readonly ITelemetryProvider _telemetryProvider;

    // Key prefixes for realm-partitioned storage
    private const string CHARACTER_KEY_PREFIX = "character:";
    private const string REALM_INDEX_KEY_PREFIX = "realm-index:";
    private const string ARCHIVE_KEY_PREFIX = "archive:";
    private const string REF_COUNT_KEY_PREFIX = "refcount:";

    // Reference type constants for L2 services (Relationships, Contracts)
    // L4 references (Actor, Encounter) are tracked via lib-resource and queried dynamically
    private const string REFERENCE_TYPE_RELATIONSHIP = "RELATIONSHIP";
    private const string REFERENCE_TYPE_CONTRACT = "CONTRACT";

    // Grace period for cleanup eligibility - from configuration in days, converted to seconds at usage

    public CharacterService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IDistributedLockProvider lockProvider,
        ILogger<CharacterService> logger,
        CharacterServiceConfiguration configuration,
        IRealmClient realmClient,
        ISpeciesClient speciesClient,
        IRelationshipClient relationshipClient,
        IContractClient contractClient,
        IEventConsumer eventConsumer,
        IResourceClient resourceClient,
        IEntitySessionRegistry entitySessionRegistry,
        ITelemetryProvider telemetryProvider)
    {
        _characterStore = stateStoreFactory.GetStore<CharacterModel>(StateStoreDefinitions.Character);
        _archiveStore = stateStoreFactory.GetStore<CharacterArchiveModel>(StateStoreDefinitions.Character);
        _refCountStore = stateStoreFactory.GetStore<RefCountData>(StateStoreDefinitions.Character);
        _globalIndexStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.Character);
        _realmIndexStore = stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Character);
        _characterJsonStore = stateStoreFactory.GetJsonQueryableStore<CharacterModel>(StateStoreDefinitions.Character);
        _messageBus = messageBus;
        _lockProvider = lockProvider;
        _logger = logger;
        _configuration = configuration;
        _realmClient = realmClient;
        _speciesClient = speciesClient;
        _relationshipClient = relationshipClient;
        _contractClient = contractClient;
        _resourceClient = resourceClient ?? throw new ArgumentNullException(nameof(resourceClient));
        _entitySessionRegistry = entitySessionRegistry;
        _telemetryProvider = telemetryProvider;

        // Register event handlers via partial class (CharacterServiceEvents.cs)
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    #region Character CRUD Operations

    public async Task<(StatusCodes, CharacterResponse?)> CreateCharacterAsync(
        CreateCharacterRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating character: {Name} in realm: {RealmId}", body.Name, body.RealmId);

        // Validate realm exists and is active
        var (realmExists, realmIsActive) = await ValidateRealmAsync(body.RealmId, cancellationToken);
        if (!realmExists)
        {
            _logger.LogWarning("Cannot create character: realm not found: {RealmId}", body.RealmId);
            return (StatusCodes.BadRequest, null);
        }

        if (!realmIsActive)
        {
            _logger.LogWarning("Cannot create character: realm is deprecated: {RealmId}", body.RealmId);
            return (StatusCodes.BadRequest, null);
        }

        // Validate species exists
        var (speciesExists, speciesInRealm) = await ValidateSpeciesAsync(body.SpeciesId, body.RealmId, cancellationToken);
        if (!speciesExists)
        {
            _logger.LogWarning("Cannot create character: species not found: {SpeciesId}", body.SpeciesId);
            return (StatusCodes.BadRequest, null);
        }

        if (!speciesInRealm)
        {
            _logger.LogWarning("Cannot create character: species {SpeciesId} is not available in realm {RealmId}",
                body.SpeciesId, body.RealmId);
            return (StatusCodes.BadRequest, null);
        }

        var characterId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Create character model
        var character = new CharacterModel
        {
            CharacterId = characterId,
            Name = body.Name,
            RealmId = body.RealmId,
            SpeciesId = body.SpeciesId,
            BirthDate = body.BirthDate,
            Status = body.Status,
            PatronDeityCode = body.PatronDeityCode,
            // Auto-set DeathDate when created with Dead status (ensures compression eligibility)
            DeathDate = body.Status == CharacterStatus.Dead ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Build realm-partitioned key
        var characterKey = BuildCharacterKey(body.RealmId.ToString(), characterId.ToString());

        // Save character to state store
        await _characterStore.SaveAsync(characterKey, character, cancellationToken: cancellationToken);

        // Add to realm index for efficient listing
        await AddCharacterToRealmIndexAsync(body.RealmId.ToString(), characterId.ToString(), cancellationToken);

        _logger.LogInformation("Character created: {CharacterId} in realm: {RealmId}", characterId, body.RealmId);

        // Publish character created event
        await PublishCharacterCreatedEventAsync(character);

        // Publish realm joined event
        await PublishCharacterRealmJoinedEventAsync(characterId, body.RealmId, previousRealmId: null);

        var response = MapToCharacterResponse(character);
        return (StatusCodes.OK, response);
    }

    public async Task<(StatusCodes, CharacterResponse?)> GetCharacterAsync(
        GetCharacterRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting character: {CharacterId}", body.CharacterId);

        // We need to find the character - scan realm indexes if we don't know the realm
        var character = await FindCharacterByIdAsync(body.CharacterId.ToString(), cancellationToken);

        if (character == null)
        {
            _logger.LogWarning("Character not found: {CharacterId}", body.CharacterId);
            return (StatusCodes.NotFound, null);
        }

        var response = MapToCharacterResponse(character);
        return (StatusCodes.OK, response);
    }

    public async Task<(StatusCodes, CharacterResponse?)> UpdateCharacterAsync(
        UpdateCharacterRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating character: {CharacterId}", body.CharacterId);

        // Acquire distributed lock for character modification (per IMPLEMENTATION TENETS)
        var lockOwner = $"update-character-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.CharacterLock,
            body.CharacterId.ToString(),
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for character {CharacterId}", body.CharacterId);
            return (StatusCodes.Conflict, null);
        }

        // Find existing character
        var character = await FindCharacterByIdAsync(body.CharacterId.ToString(), cancellationToken);

        if (character == null)
        {
            _logger.LogWarning("Character not found for update: {CharacterId}", body.CharacterId);
            return (StatusCodes.NotFound, null);
        }

        // Track changes for event
        var changedFields = new List<string>();

        // Update fields if provided
        if (body.Name != null && body.Name != character.Name)
        {
            changedFields.Add("name");
            character.Name = body.Name;
        }

        if (body.Status.HasValue && body.Status.Value != character.Status)
        {
            changedFields.Add("status");
            character.Status = body.Status.Value;

            // Auto-set DeathDate when status transitions to Dead (mirrors DeathDate→Dead auto-set)
            // Skip if DeathDate is also provided in this request (handled below)
            if (body.Status.Value == CharacterStatus.Dead && !character.DeathDate.HasValue && !body.DeathDate.HasValue)
            {
                changedFields.Add("deathDate");
                character.DeathDate = DateTimeOffset.UtcNow;
            }
        }

        if (body.DeathDate.HasValue)
        {
            changedFields.Add("deathDate");
            character.DeathDate = body.DeathDate.Value;

            // If death date is set, also set status to dead
            if (character.Status != CharacterStatus.Dead)
            {
                changedFields.Add("status");
                character.Status = CharacterStatus.Dead;
            }
        }

        // Handle species migration (used for species merge operations)
        if (body.SpeciesId.HasValue && body.SpeciesId.Value != character.SpeciesId)
        {
            changedFields.Add("speciesId");
            character.SpeciesId = body.SpeciesId.Value;
        }

        // Handle patron deity changes (opaque string — null in request = not provided)
        if (body.PatronDeityCode != null && body.PatronDeityCode != character.PatronDeityCode)
        {
            changedFields.Add("patronDeityCode");
            character.PatronDeityCode = body.PatronDeityCode;
        }

        character.UpdatedAt = DateTimeOffset.UtcNow;

        // Save updated character
        var characterKey = BuildCharacterKey(character.RealmId.ToString(), character.CharacterId.ToString());
        await _characterStore.SaveAsync(characterKey, character, cancellationToken: cancellationToken);

        _logger.LogInformation("Character updated: {CharacterId}", body.CharacterId);

        // Publish update event if there were changes
        if (changedFields.Count > 0)
        {
            await PublishCharacterUpdatedEventAsync(character, changedFields);

            // Publish client event for real-time UI updates via Entity Session Registry
            await PublishCharacterUpdatedClientEventAsync(character, changedFields, cancellationToken);
        }

        var response = MapToCharacterResponse(character);
        return (StatusCodes.OK, response);
    }

    public async Task<StatusCodes> DeleteCharacterAsync(
        DeleteCharacterRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting character: {CharacterId}", body.CharacterId);

        // Find existing character
        var character = await FindCharacterByIdAsync(body.CharacterId.ToString(), cancellationToken);

        if (character == null)
        {
            _logger.LogWarning("Character not found for deletion: {CharacterId}", body.CharacterId);
            return StatusCodes.NotFound;
        }

        var realmId = character.RealmId;
        var characterKey = BuildCharacterKey(realmId.ToString(), character.CharacterId.ToString());

        // Check for L4 references and execute cleanup callbacks (per x-references contract)
        // This triggers cascade deletion in CharacterPersonality, CharacterHistory, etc.
        try
        {
            var resourceCheck = await _resourceClient.CheckReferencesAsync(
                new Resource.CheckReferencesRequest
                {
                    ResourceType = "character",
                    ResourceId = body.CharacterId
                }, cancellationToken);

            if (resourceCheck != null && resourceCheck.RefCount > 0)
            {
                var sourceTypes = resourceCheck.Sources != null
                    ? string.Join(", ", resourceCheck.Sources.Select(s => s.SourceType))
                    : "unknown";
                _logger.LogDebug(
                    "Character {CharacterId} has {RefCount} external references from: {SourceTypes}, executing cleanup",
                    body.CharacterId, resourceCheck.RefCount, sourceTypes);

                // Execute cleanup callbacks (CASCADE/DETACH) before proceeding
                var cleanupResult = await _resourceClient.ExecuteCleanupAsync(
                    new Resource.ExecuteCleanupRequest
                    {
                        ResourceType = "character",
                        ResourceId = body.CharacterId,
                        CleanupPolicy = Resource.CleanupPolicy.AllRequired
                    }, cancellationToken);

                if (!cleanupResult.Success)
                {
                    _logger.LogWarning(
                        "Cleanup blocked for character {CharacterId}: {Reason}",
                        body.CharacterId, cleanupResult.AbortReason ?? "cleanup failed");
                    return StatusCodes.Conflict;
                }

                _logger.LogDebug(
                    "Cleanup completed for character {CharacterId}: {CallbackCount} callback(s) executed",
                    body.CharacterId, cleanupResult.CallbackResults.Count);
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // No references registered - this is normal for characters without L4 data
            _logger.LogDebug("No lib-resource references found for character {CharacterId}", body.CharacterId);
        }
        catch (ApiException ex)
        {
            // lib-resource unavailable - fail closed to protect referential integrity
            _logger.LogError(ex,
                "lib-resource unavailable when checking references for character {CharacterId}, blocking deletion for safety",
                body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character", "DeleteCharacter", "resource_service_unavailable",
                $"lib-resource unavailable when checking references for character {body.CharacterId}",
                dependency: "resource", endpoint: "post:/character/delete",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return StatusCodes.ServiceUnavailable;
        }

        // Delete character from state store
        await _characterStore.DeleteAsync(characterKey, cancellationToken);

        // Remove from realm index
        await RemoveCharacterFromRealmIndexAsync(realmId.ToString(), character.CharacterId.ToString(), cancellationToken);

        _logger.LogInformation("Character deleted: {CharacterId} from realm: {RealmId}", body.CharacterId, realmId);

        // Publish realm left event (reason: deletion)
        await PublishCharacterRealmLeftEventAsync(
            body.CharacterId,
            realmId,
            CharacterRealmLeftReason.Deletion);

        // Publish character deleted event
        await PublishCharacterDeletedEventAsync(character);

        return StatusCodes.OK;
    }

    public async Task<(StatusCodes, CharacterListResponse?)> ListCharactersAsync(
        ListCharactersRequest body,
        CancellationToken cancellationToken = default)
    {
        var page = body.Page > 0 ? body.Page : 1;
        var pageSize = body.PageSize > 0 ? Math.Min(body.PageSize, _configuration.MaxPageSize) : _configuration.DefaultPageSize;

        _logger.LogDebug(
            "Listing characters - RealmId: {RealmId}, Page: {Page}, PageSize: {PageSize}",
            body.RealmId,
            page,
            pageSize);

        return await GetCharactersByRealmInternalAsync(
            body.RealmId.ToString(),
            body.Status,
            body.SpeciesId,
            page,
            pageSize,
            cancellationToken);
    }

    public async Task<(StatusCodes, CharacterListResponse?)> GetCharactersByRealmAsync(
        GetCharactersByRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        var page = body.Page > 0 ? body.Page : 1;
        var pageSize = body.PageSize > 0 ? Math.Min(body.PageSize, _configuration.MaxPageSize) : _configuration.DefaultPageSize;

        _logger.LogDebug("Getting characters by realm: {RealmId} - Page: {Page}, PageSize: {PageSize}",
            body.RealmId, page, pageSize);

        return await GetCharactersByRealmInternalAsync(
            body.RealmId.ToString(),
            body.Status,
            body.SpeciesId,
            page,
            pageSize,
            cancellationToken);
    }

    /// <summary>
    /// Transfers a character to a different realm.
    /// Updates all indexes and publishes realm transition events.
    /// </summary>
    public async Task<(StatusCodes, CharacterResponse?)> TransferCharacterToRealmAsync(
        TransferCharacterToRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Transferring character {CharacterId} to realm {TargetRealmId}",
            body.CharacterId, body.TargetRealmId);

        // Validate target realm exists and is active
        var (targetRealmExists, targetRealmIsActive) = await ValidateRealmAsync(body.TargetRealmId, cancellationToken);
        if (!targetRealmExists)
        {
            _logger.LogWarning("Cannot transfer character: target realm not found: {TargetRealmId}", body.TargetRealmId);
            return (StatusCodes.NotFound, null);
        }

        if (!targetRealmIsActive)
        {
            _logger.LogWarning("Cannot transfer character: target realm is deprecated: {TargetRealmId}", body.TargetRealmId);
            return (StatusCodes.BadRequest, null);
        }

        // Acquire distributed lock for character modification (per IMPLEMENTATION TENETS)
        var lockOwner = $"transfer-character-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.CharacterLock,
            body.CharacterId.ToString(),
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for character transfer {CharacterId}", body.CharacterId);
            return (StatusCodes.Conflict, null);
        }

        // Find existing character
        var character = await FindCharacterByIdAsync(body.CharacterId.ToString(), cancellationToken);

        if (character == null)
        {
            _logger.LogWarning("Character not found for transfer: {CharacterId}", body.CharacterId);
            return (StatusCodes.NotFound, null);
        }

        // Check if already in target realm
        if (character.RealmId == body.TargetRealmId)
        {
            _logger.LogWarning("Character {CharacterId} is already in realm {RealmId}", body.CharacterId, body.TargetRealmId);
            return (StatusCodes.BadRequest, null);
        }

        var previousRealmId = character.RealmId;

        // Delete old character data (keyed by old realm)
        var oldCharacterKey = BuildCharacterKey(previousRealmId.ToString(), character.CharacterId.ToString());
        await _characterStore.DeleteAsync(oldCharacterKey, cancellationToken);

        // Remove from old realm index
        await RemoveCharacterFromRealmIndexAsync(previousRealmId.ToString(), character.CharacterId.ToString(), cancellationToken);

        // Update character with new realm
        character.RealmId = body.TargetRealmId;
        character.UpdatedAt = DateTimeOffset.UtcNow;

        // Save character with new realm key
        var newCharacterKey = BuildCharacterKey(body.TargetRealmId.ToString(), character.CharacterId.ToString());
        await _characterStore.SaveAsync(newCharacterKey, character, cancellationToken: cancellationToken);

        // Add to new realm index (this also updates global index)
        await AddCharacterToRealmIndexAsync(body.TargetRealmId.ToString(), character.CharacterId.ToString(), cancellationToken);

        _logger.LogInformation("Character {CharacterId} transferred from realm {PreviousRealmId} to realm {TargetRealmId}",
            body.CharacterId, previousRealmId, body.TargetRealmId);

        // Publish realm left event for previous realm
        await PublishCharacterRealmLeftEventAsync(
            body.CharacterId,
            previousRealmId,
            CharacterRealmLeftReason.Transfer);

        // Publish realm joined event for new realm
        await PublishCharacterRealmJoinedEventAsync(body.CharacterId, body.TargetRealmId, previousRealmId);

        // Publish character updated event
        await PublishCharacterUpdatedEventAsync(character, new List<string> { "realmId" });

        // Publish realm transfer client event for UI context switch
        await PublishCharacterRealmTransferredClientEventAsync(
            body.CharacterId, previousRealmId, body.TargetRealmId, cancellationToken);

        var response = MapToCharacterResponse(character);
        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Migrates all characters from one realm to another. Called by lib-resource during realm merge.
    /// Iterates through all characters in the source realm and transfers each individually,
    /// reusing the existing transfer flow (index updates, events, client events).
    /// Per-item error isolation ensures one failure does not block other migrations.
    /// </summary>
    public async Task<(StatusCodes, MigrateByRealmResponse?)> MigrateByRealmAsync(
        MigrateByRealmRequest body,
        CancellationToken cancellationToken = default)
    {

        _logger.LogInformation("Starting realm migration from {SourceRealmId} to {TargetRealmId}",
            body.SourceRealmId, body.TargetRealmId);

        var migrated = 0;
        var failed = 0;

        // Load all character IDs from the source realm index
        var realmIndexKey = BuildRealmIndexKey(body.SourceRealmId.ToString());
        var characterIds = await _realmIndexStore.GetAsync(realmIndexKey, cancellationToken);

        if (characterIds == null || characterIds.Count == 0)
        {
            _logger.LogInformation("No characters found in source realm {SourceRealmId} for migration",
                body.SourceRealmId);
            return (StatusCodes.OK, new MigrateByRealmResponse { Migrated = 0, Failed = 0 });
        }

        _logger.LogInformation("Found {Count} character(s) in realm {SourceRealmId} for migration",
            characterIds.Count, body.SourceRealmId);

        // Snapshot the list to avoid mutation during iteration (transfers modify the index)
        var characterIdSnapshot = characterIds.ToList();

        foreach (var characterId in characterIdSnapshot)
        {
            try
            {
                if (!Guid.TryParse(characterId, out var parsedId))
                {
                    _logger.LogWarning(
                        "Skipping invalid character ID during realm migration: {CharacterId} in realm {SourceRealmId}",
                        characterId, body.SourceRealmId);
                    failed++;
                    continue;
                }

                var (status, _) = await TransferCharacterToRealmAsync(
                    new TransferCharacterToRealmRequest
                    {
                        CharacterId = parsedId,
                        TargetRealmId = body.TargetRealmId
                    },
                    cancellationToken);

                if (status == StatusCodes.OK)
                {
                    migrated++;
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to migrate character {CharacterId} from realm {SourceRealmId} to {TargetRealmId} (status: {StatusCode})",
                        characterId, body.SourceRealmId, body.TargetRealmId, status);
                    failed++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Exception migrating character {CharacterId} from realm {SourceRealmId} to {TargetRealmId}",
                    characterId, body.SourceRealmId, body.TargetRealmId);
                failed++;
            }
        }

        _logger.LogInformation(
            "Realm migration completed from {SourceRealmId} to {TargetRealmId}: {Migrated} migrated, {Failed} failed",
            body.SourceRealmId, body.TargetRealmId, migrated, failed);

        return (StatusCodes.OK, new MigrateByRealmResponse { Migrated = migrated, Failed = failed });
    }

    #endregion

    #region Enriched Character & Compression Operations

    /// <summary>
    /// Gets a character with optional enriched data from L2 services (family tree from Relationship).
    /// For L4 data (personality, backstory, combat preferences), callers should query those services directly.
    /// </summary>
    public async Task<(StatusCodes, EnrichedCharacterResponse?)> GetEnrichedCharacterAsync(
        GetEnrichedCharacterRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting enriched character: {CharacterId}", body.CharacterId);

        // Get base character data
        var character = await FindCharacterByIdAsync(body.CharacterId.ToString(), cancellationToken);

        if (character == null)
        {
            _logger.LogWarning("Character not found: {CharacterId}", body.CharacterId);
            return (StatusCodes.NotFound, null);
        }

        var response = new EnrichedCharacterResponse
        {
            CharacterId = character.CharacterId,
            Name = character.Name,
            RealmId = character.RealmId,
            SpeciesId = character.SpeciesId,
            BirthDate = character.BirthDate,
            DeathDate = character.DeathDate,
            Status = character.Status,
            PatronDeityCode = character.PatronDeityCode,
            CreatedAt = character.CreatedAt,
            UpdatedAt = character.UpdatedAt
        };

        // Fetch family tree if requested (uses Relationship service - L2, allowed)
        if (body.IncludeFamilyTree)
        {
            response.FamilyTree = await BuildFamilyTreeAsync(body.CharacterId, cancellationToken);
        }

        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Compresses a dead character to archive format for long-term storage.
    /// NOTE: Per SERVICE_HIERARCHY, Character (L2) cannot depend on L4 services like
    /// CharacterPersonality or CharacterHistory. Archives include only family summary
    /// (from Relationships, L2). Personality and history data should be handled by
    /// subscribing L4 services via the character.compressed event.
    /// </summary>
    public async Task<(StatusCodes, CharacterArchive?)> CompressCharacterAsync(
        CompressCharacterRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Compressing character: {CharacterId}", body.CharacterId);

        // Acquire distributed lock for character compression (per IMPLEMENTATION TENETS)
        var lockOwner = $"compress-character-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.CharacterLock,
            body.CharacterId.ToString(),
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for character compression {CharacterId}", body.CharacterId);
            return (StatusCodes.Conflict, null);
        }

        // Get character
        var character = await FindCharacterByIdAsync(body.CharacterId.ToString(), cancellationToken);

        if (character == null)
        {
            _logger.LogWarning("Character not found for compression: {CharacterId}", body.CharacterId);
            return (StatusCodes.NotFound, null);
        }

        // Must be dead to compress
        if (character.Status != CharacterStatus.Dead || !character.DeathDate.HasValue)
        {
            _logger.LogWarning("Cannot compress alive character: {CharacterId}", body.CharacterId);
            return (StatusCodes.BadRequest, null);
        }

        // NOTE: Per SERVICE_HIERARCHY, we cannot call CharacterPersonality or CharacterHistory (L4).
        // Personality summary and backstory/history summaries are NOT included.
        // Full hierarchical compression (including L4 data) is handled by the Resource service
        // via /resource/compress/execute. L4 services provide compression callbacks, not event subscriptions.
        string? familySummary = await GenerateFamilySummaryAsync(body.CharacterId, cancellationToken);

        var archive = new CharacterArchive
        {
            CharacterId = body.CharacterId,
            Name = character.Name,
            RealmId = character.RealmId,
            SpeciesId = character.SpeciesId,
            BirthDate = character.BirthDate,
            DeathDate = character.DeathDate.Value,
            CompressedAt = DateTimeOffset.UtcNow,
            PersonalitySummary = null, // L4 data not available per SERVICE_HIERARCHY
            KeyBackstoryPoints = null, // L4 data not available per SERVICE_HIERARCHY; null = not archived
            MajorLifeEvents = null, // L4 data not available per SERVICE_HIERARCHY; null = not archived
            FamilySummary = familySummary // From Relationships (L2), allowed
        };

        // Store archive
        var archiveKey = $"{ARCHIVE_KEY_PREFIX}{body.CharacterId}";
        await _archiveStore.SaveAsync(archiveKey, MapToArchiveModel(archive), cancellationToken: cancellationToken);

        // NOTE: deleteSourceData flag cannot delete L4 service data per SERVICE_HIERARCHY.
        // Full hierarchical deletion uses /resource/cleanup/execute with cascade callbacks.
        if (body.DeleteSourceData)
        {
            _logger.LogDebug(
                "DeleteSourceData=true for character {CharacterId}, but this legacy endpoint cannot " +
                "delete L4 service data per SERVICE_HIERARCHY. Use /resource/compress/execute for " +
                "full hierarchical compression with L4 cleanup callbacks.",
                body.CharacterId);
        }

        // Publish compression event - L4 services can subscribe to clean up their data
        await _messageBus.PublishCharacterCompressedAsync(new CharacterCompressedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = body.CharacterId,
            DeletedSourceData = body.DeleteSourceData
        });

        _logger.LogInformation("Character compressed: {CharacterId}", body.CharacterId);
        return (StatusCodes.OK, archive);
    }

    /// <summary>
    /// Gets compressed archive data for a character.
    /// </summary>
    public async Task<(StatusCodes, CharacterArchive?)> GetCharacterArchiveAsync(
        GetCharacterArchiveRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting archive for character: {CharacterId}", body.CharacterId);

        var archiveKey = $"{ARCHIVE_KEY_PREFIX}{body.CharacterId}";
        var archiveModel = await _archiveStore.GetAsync(archiveKey, cancellationToken);

        if (archiveModel == null)
        {
            _logger.LogDebug("No archive found for character {CharacterId}", body.CharacterId);
            return (StatusCodes.NotFound, null);
        }

        var archive = MapFromArchiveModel(archiveModel);
        return (StatusCodes.OK, archive);
    }

    /// <summary>
    /// Gets character base data for centralized compression via lib-resource.
    /// Called by Resource service during compression to gather L2 character data.
    /// Returns BadRequest if character is alive - only dead characters can be compressed.
    /// NOTE: Per SERVICE_HIERARCHY, this only returns L2 data (base character info + family summary).
    /// L4 data (personality, history, encounters) is gathered by Resource from L4 services.
    /// </summary>
    public async Task<(StatusCodes, CharacterBaseArchive?)> GetCompressDataAsync(
        GetCompressDataRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting compress data for character: {CharacterId}", body.CharacterId);

        // Find character
        var character = await FindCharacterByIdAsync(body.CharacterId.ToString(), cancellationToken);

        if (character == null)
        {
            _logger.LogWarning("Character not found for compression: {CharacterId}", body.CharacterId);
            return (StatusCodes.NotFound, null);
        }

        // Only dead characters can be compressed
        if (character.Status != CharacterStatus.Dead || !character.DeathDate.HasValue)
        {
            _logger.LogWarning("Cannot get compress data for alive character: {CharacterId}", body.CharacterId);
            return (StatusCodes.BadRequest, null);
        }

        // Generate family summary (uses Relationship service - L2, allowed)
        var familySummary = await GenerateFamilySummaryAsync(body.CharacterId, cancellationToken);

        var compressData = new CharacterBaseArchive
        {
            // ResourceArchiveBase fields
            ResourceId = character.CharacterId,
            ResourceType = "character",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            // Service-specific fields
            CharacterId = character.CharacterId,
            Name = character.Name,
            RealmId = character.RealmId,
            SpeciesId = character.SpeciesId,
            BirthDate = character.BirthDate,
            DeathDate = character.DeathDate.Value,
            Status = character.Status,
            FamilySummary = familySummary,
            CreatedAt = character.CreatedAt,
            UpdatedAt = character.UpdatedAt
        };

        _logger.LogDebug("Generated compress data for character: {CharacterId}", body.CharacterId);
        return (StatusCodes.OK, compressData);
    }

    /// <summary>
    /// Checks reference count for cleanup eligibility.
    /// Queries both same-layer/lower services (Relationships at L2, Contracts at L1) and
    /// lib-resource (L1) for L4 references registered via the event-driven pattern.
    /// L4 services (Actor, CharacterEncounter, etc.) publish to lib-resource's
    /// character.reference.registered/unregistered topics, and this method queries
    /// lib-resource to include those references in the count.
    /// </summary>
    public async Task<(StatusCodes, CharacterRefCount?)> CheckCharacterReferencesAsync(
        CheckReferencesRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Checking references for character: {CharacterId}", body.CharacterId);

        // Check if character exists
        var character = await FindCharacterByIdAsync(body.CharacterId.ToString(), cancellationToken);
        if (character == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Check if compressed
        var archiveKey = $"{ARCHIVE_KEY_PREFIX}{body.CharacterId}";
        var archiveModel = await _archiveStore.GetAsync(archiveKey, cancellationToken);
        var isCompressed = archiveModel != null;

        // Count references from same-layer or lower services only (per SERVICE_HIERARCHY)
        var referenceTypes = new List<string>();
        var referenceCount = 0;

        // Check relationships (L2 - allowed)
        try
        {
            // Query for relationships where this character is entity1 or entity2
            var relResult = await _relationshipClient.ListRelationshipsByEntityAsync(
                new ListRelationshipsByEntityRequest
                {
                    EntityId = body.CharacterId,
                    EntityType = EntityType.Character
                },
                cancellationToken);

            if (relResult != null && relResult.Relationships.Count > 0)
            {
                referenceCount += relResult.TotalCount;

                // Track that relationships exist (detailed categorization would require
                // additional calls to RelationshipType service for type codes)
                if (!referenceTypes.Contains(REFERENCE_TYPE_RELATIONSHIP))
                    referenceTypes.Add(REFERENCE_TYPE_RELATIONSHIP);
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("No relationships found for character {CharacterId}", body.CharacterId);
        }

        // Check L4 references via lib-resource (L1 - allowed)
        // L4 services (Actor, CharacterEncounter, etc.) register their references with lib-resource
        // via event-driven registration. We query lib-resource to get those reference counts.
        try
        {
            var resourceCheck = await _resourceClient.CheckReferencesAsync(
                new Resource.CheckReferencesRequest
                {
                    ResourceType = "character",
                    ResourceId = body.CharacterId
                }, cancellationToken);

            if (resourceCheck != null && resourceCheck.RefCount > 0)
            {
                referenceCount += resourceCheck.RefCount;

                // Add source types from lib-resource response to reference types list
                if (resourceCheck.Sources != null)
                {
                    foreach (var source in resourceCheck.Sources)
                    {
                        // Normalize source type to uppercase for consistency with L2 constants
                        var normalizedType = source.SourceType.ToUpperInvariant();
                        if (!referenceTypes.Contains(normalizedType))
                        {
                            referenceTypes.Add(normalizedType);
                        }
                    }
                }
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // No references registered in lib-resource - this is normal
            _logger.LogDebug("No lib-resource references found for character {CharacterId}", body.CharacterId);
        }
        catch (ApiException ex)
        {
            // Graceful degradation: lib-resource unavailable means L4 references may be missing,
            // but we still return L2 reference info (relationships, contracts). No error event
            // emitted because this is a read-only advisory check, not a fail-closed mutation --
            // contrast with the delete flow which MUST fail if lib-resource is unavailable.
            _logger.LogWarning(ex, "lib-resource unavailable when checking references for character {CharacterId}, L4 references may be missing", body.CharacterId);
        }

        // Check contracts where character is a party (L1 - allowed)
        try
        {
            var contractResult = await _contractClient.QueryContractInstancesAsync(
                new QueryContractInstancesRequest
                {
                    PartyEntityId = body.CharacterId,
                    PartyEntityType = EntityType.Character,
                    Cursor = null,
                    PageSize = 1 // Only need to know if any exist
                },
                cancellationToken);

            if (contractResult != null && contractResult.Contracts.Count > 0)
            {
                // With cursor-based pagination, we don't have total count.
                // Indicate at least one reference exists; exact count requires full enumeration.
                referenceCount += contractResult.HasMore ? 2 : contractResult.Contracts.Count;
                if (!referenceTypes.Contains(REFERENCE_TYPE_CONTRACT))
                    referenceTypes.Add(REFERENCE_TYPE_CONTRACT);
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("No contracts found for character {CharacterId}", body.CharacterId);
        }

        // Get/update reference tracking data with optimistic concurrency
        var refCountKey = $"{REF_COUNT_KEY_PREFIX}{body.CharacterId}";
        var (initialData, initialEtag) = await _refCountStore.GetWithETagAsync(refCountKey, cancellationToken);
        var refData = initialData ?? new RefCountData { CharacterId = body.CharacterId };

        var maxRetries = _configuration.RefCountUpdateMaxRetries;
        for (var retry = 0; retry < maxRetries; retry++)
        {
            if (retry > 0)
            {
                // Re-fetch on retry
                var (storedData, newEtag) = await _refCountStore.GetWithETagAsync(refCountKey, cancellationToken);
                refData = storedData ?? new RefCountData { CharacterId = body.CharacterId };
                initialEtag = newEtag;
            }

            // Track when refCount first hit zero
            bool needsSave = false;
            if (referenceCount == 0 && refData.ZeroRefSinceUnix == null)
            {
                refData.ZeroRefSinceUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                needsSave = true;
            }
            else if (referenceCount > 0 && refData.ZeroRefSinceUnix != null)
            {
                refData.ZeroRefSinceUnix = null;
                needsSave = true;
            }

            if (!needsSave)
            {
                break; // No changes needed
            }

            // initialEtag is null on first save (no prior value); empty string signals
            // "create new" to TrySaveAsync (will never execute when etag exists)
            var savedEtag = await _refCountStore.TrySaveAsync(refCountKey, refData, initialEtag ?? string.Empty, cancellationToken: cancellationToken);
            if (savedEtag != null)
            {
                break; // Successfully saved
            }

            if (retry == maxRetries - 1)
            {
                _logger.LogWarning("RefCount update retry exhausted for character {CharacterId}", body.CharacterId);
            }
        }

        // Determine cleanup eligibility - grace period from configuration in days, converted to seconds
        var cleanupGracePeriodSeconds = _configuration.CleanupGracePeriodDays * 24 * 60 * 60;
        var isEligibleForCleanup = isCompressed && referenceCount == 0 && refData.ZeroRefSinceUnix != null &&
            (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - refData.ZeroRefSinceUnix.Value) >= cleanupGracePeriodSeconds;

        var response = new CharacterRefCount
        {
            CharacterId = body.CharacterId,
            ReferenceCount = referenceCount,
            ReferenceTypes = referenceTypes,
            IsCompressed = isCompressed,
            IsEligibleForCleanup = isEligibleForCleanup,
            ZeroRefSince = refData.ZeroRefSinceUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(refData.ZeroRefSinceUnix.Value)
                : null
        };

        return (StatusCodes.OK, response);
    }

    #endregion

    #region Permission Registration

    #endregion
}
