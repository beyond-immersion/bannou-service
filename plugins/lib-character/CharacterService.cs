using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
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
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<CharacterService> _logger;
    private readonly CharacterServiceConfiguration _configuration;
    private readonly IRealmClient _realmClient;
    private readonly ISpeciesClient _speciesClient;
    private readonly IRelationshipClient _relationshipClient;
    private readonly IContractClient _contractClient;
    private readonly IResourceClient _resourceClient;

    // Key prefixes for realm-partitioned storage
    private const string CHARACTER_KEY_PREFIX = "character:";
    private const string REALM_INDEX_KEY_PREFIX = "realm-index:";
    private const string ARCHIVE_KEY_PREFIX = "archive:";
    private const string REF_COUNT_KEY_PREFIX = "refcount:";

    // Event topics
    private const string CHARACTER_CREATED_TOPIC = "character.created";
    private const string CHARACTER_UPDATED_TOPIC = "character.updated";
    private const string CHARACTER_DELETED_TOPIC = "character.deleted";
    private const string CHARACTER_REALM_JOINED_TOPIC = "character.realm.joined";
    private const string CHARACTER_REALM_LEFT_TOPIC = "character.realm.left";
    private const string CHARACTER_COMPRESSED_TOPIC = "character.compressed";

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
        IResourceClient resourceClient)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _lockProvider = lockProvider;
        _logger = logger;
        _configuration = configuration;
        _realmClient = realmClient;
        _speciesClient = speciesClient;
        _relationshipClient = relationshipClient;
        _contractClient = contractClient;
        _resourceClient = resourceClient ?? throw new ArgumentNullException(nameof(resourceClient));

        // Register event handlers via partial class (CharacterServiceEvents.cs)
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    #region Character CRUD Operations

    public async Task<(StatusCodes, CharacterResponse?)> CreateCharacterAsync(
        CreateCharacterRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
                // Auto-set DeathDate when created with Dead status (ensures compression eligibility)
                DeathDate = body.Status == CharacterStatus.Dead ? now : null,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Build realm-partitioned key
            var characterKey = BuildCharacterKey(body.RealmId.ToString(), characterId.ToString());

            // Save character to state store
            await _stateStoreFactory.GetStore<CharacterModel>(StateStoreDefinitions.Character)
                .SaveAsync(characterKey, character, cancellationToken: cancellationToken);

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
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error creating character: {Name}", body.Name);
            return (StatusCodes.ServiceUnavailable, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating character: {Name}", body.Name);
            await _messageBus.TryPublishErrorAsync(
                "character",
                "CreateCharacter",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/character/create",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, CharacterResponse?)> GetCharacterAsync(
        GetCharacterRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error getting character: {CharacterId}", body.CharacterId);
            return (StatusCodes.ServiceUnavailable, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting character: {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character",
                "GetCharacter",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/character/get",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, CharacterResponse?)> UpdateCharacterAsync(
        UpdateCharacterRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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

            character.UpdatedAt = DateTimeOffset.UtcNow;

            // Save updated character
            var characterKey = BuildCharacterKey(character.RealmId.ToString(), character.CharacterId.ToString());
            await _stateStoreFactory.GetStore<CharacterModel>(StateStoreDefinitions.Character)
                .SaveAsync(characterKey, character, cancellationToken: cancellationToken);

            _logger.LogInformation("Character updated: {CharacterId}", body.CharacterId);

            // Publish update event if there were changes
            if (changedFields.Count > 0)
            {
                await PublishCharacterUpdatedEventAsync(character, changedFields);
            }

            var response = MapToCharacterResponse(character);
            return (StatusCodes.OK, response);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error updating character: {CharacterId}", body.CharacterId);
            return (StatusCodes.ServiceUnavailable, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating character: {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character",
                "UpdateCharacter",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/character/update",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<StatusCodes> DeleteCharacterAsync(
        DeleteCharacterRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
                            CleanupPolicy = Resource.CleanupPolicy.ALL_REQUIRED
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
            await _stateStoreFactory.GetStore<CharacterModel>(StateStoreDefinitions.Character)
                .DeleteAsync(characterKey, cancellationToken);

            // Remove from realm index
            await RemoveCharacterFromRealmIndexAsync(realmId.ToString(), character.CharacterId.ToString(), cancellationToken);

            _logger.LogInformation("Character deleted: {CharacterId} from realm: {RealmId}", body.CharacterId, realmId);

            // Publish realm left event (reason: deletion)
            await PublishCharacterRealmLeftEventAsync(
                body.CharacterId,
                realmId,
                "deletion");

            // Publish character deleted event
            await PublishCharacterDeletedEventAsync(character);

            return StatusCodes.OK;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error deleting character: {CharacterId}", body.CharacterId);
            return StatusCodes.ServiceUnavailable;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting character: {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character",
                "DeleteCharacter",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/character/delete",
                details: null,
                stack: ex.StackTrace);
            return StatusCodes.InternalServerError;
        }
    }

    public async Task<(StatusCodes, CharacterListResponse?)> ListCharactersAsync(
        ListCharactersRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error listing characters");
            return (StatusCodes.ServiceUnavailable, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing characters");
            await _messageBus.TryPublishErrorAsync(
                "character",
                "ListCharacters",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/character/list",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, CharacterListResponse?)> GetCharactersByRealmAsync(
        GetCharactersByRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error getting characters by realm: {RealmId}", body.RealmId);
            return (StatusCodes.ServiceUnavailable, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting characters by realm: {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "character",
                "GetCharactersByRealm",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/character/by-realm",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Transfers a character to a different realm.
    /// Updates all indexes and publishes realm transition events.
    /// </summary>
    public async Task<(StatusCodes, CharacterResponse?)> TransferCharacterToRealmAsync(
        TransferCharacterToRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
            await _stateStoreFactory.GetStore<CharacterModel>(StateStoreDefinitions.Character)
                .DeleteAsync(oldCharacterKey, cancellationToken);

            // Remove from old realm index
            await RemoveCharacterFromRealmIndexAsync(previousRealmId.ToString(), character.CharacterId.ToString(), cancellationToken);

            // Update character with new realm
            character.RealmId = body.TargetRealmId;
            character.UpdatedAt = DateTimeOffset.UtcNow;

            // Save character with new realm key
            var newCharacterKey = BuildCharacterKey(body.TargetRealmId.ToString(), character.CharacterId.ToString());
            await _stateStoreFactory.GetStore<CharacterModel>(StateStoreDefinitions.Character)
                .SaveAsync(newCharacterKey, character, cancellationToken: cancellationToken);

            // Add to new realm index (this also updates global index)
            await AddCharacterToRealmIndexAsync(body.TargetRealmId.ToString(), character.CharacterId.ToString(), cancellationToken);

            _logger.LogInformation("Character {CharacterId} transferred from realm {PreviousRealmId} to realm {TargetRealmId}",
                body.CharacterId, previousRealmId, body.TargetRealmId);

            // Publish realm left event for previous realm
            await PublishCharacterRealmLeftEventAsync(
                body.CharacterId,
                previousRealmId,
                "transfer");

            // Publish realm joined event for new realm
            await PublishCharacterRealmJoinedEventAsync(body.CharacterId, body.TargetRealmId, previousRealmId);

            // Publish character updated event
            await PublishCharacterUpdatedEventAsync(character, new List<string> { "realmId" });

            var response = MapToCharacterResponse(character);
            return (StatusCodes.OK, response);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error transferring character: {CharacterId}", body.CharacterId);
            return (StatusCodes.ServiceUnavailable, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transferring character: {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character",
                "TransferCharacterToRealm",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/character/transfer-realm",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Enriched Character & Compression Operations

    /// <summary>
    /// Gets a character with optional enriched data (family tree).
    /// NOTE: Per SERVICE_HIERARCHY, Character (L2) cannot depend on L4 services like
    /// CharacterPersonality or CharacterHistory. Personality, backstory, and combat
    /// preferences are not included in this response. For fully enriched character data,
    /// callers should aggregate from L4 services directly or use a future L4 aggregator service.
    /// </summary>
    public async Task<(StatusCodes, EnrichedCharacterResponse?)> GetEnrichedCharacterAsync(
        GetEnrichedCharacterRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
                CreatedAt = character.CreatedAt,
                UpdatedAt = character.UpdatedAt
            };

            // NOTE: Personality, CombatPreferences, and Backstory are NOT included.
            // Per SERVICE_HIERARCHY, Character (L2) cannot depend on CharacterPersonality or
            // CharacterHistory (L4). Callers needing this data should call those services directly.
            if (body.IncludePersonality || body.IncludeCombatPreferences || body.IncludeBackstory)
            {
                _logger.LogDebug(
                    "Enrichment flags for personality/combat/backstory were set for character {CharacterId}, " +
                    "but these are not included per SERVICE_HIERARCHY (L2 cannot depend on L4). " +
                    "Callers should aggregate from L4 services directly.",
                    body.CharacterId);
            }

            // Fetch family tree if requested (uses Relationship service - L2, allowed)
            if (body.IncludeFamilyTree)
            {
                response.FamilyTree = await BuildFamilyTreeAsync(body.CharacterId, cancellationToken);
            }

            return (StatusCodes.OK, response);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error getting enriched character: {CharacterId}", body.CharacterId);
            return (StatusCodes.ServiceUnavailable, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting enriched character: {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character",
                "GetEnrichedCharacter",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/character/get-enriched",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
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
        try
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
            // L4 services should subscribe to character.compressed event to handle their own cleanup.
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
                KeyBackstoryPoints = new List<string>(), // L4 data not available per SERVICE_HIERARCHY
                MajorLifeEvents = new List<string>(), // L4 data not available per SERVICE_HIERARCHY
                FamilySummary = familySummary // From Relationships (L2), allowed
            };

            // Store archive
            var archiveKey = $"{ARCHIVE_KEY_PREFIX}{body.CharacterId}";
            await _stateStoreFactory.GetStore<CharacterArchiveModel>(StateStoreDefinitions.Character)
                .SaveAsync(archiveKey, MapToArchiveModel(archive), cancellationToken: cancellationToken);

            // NOTE: deleteSourceData flag cannot delete L4 service data per SERVICE_HIERARCHY.
            // L4 services should subscribe to character.compressed event to handle their own cleanup.
            if (body.DeleteSourceData)
            {
                _logger.LogDebug(
                    "DeleteSourceData=true for character {CharacterId}, but Character (L2) cannot call " +
                    "CharacterPersonality or CharacterHistory (L4) to delete their data per SERVICE_HIERARCHY. " +
                    "L4 services should subscribe to character.compressed event.",
                    body.CharacterId);
            }

            // Publish compression event - L4 services can subscribe to clean up their data
            await _messageBus.TryPublishAsync(CHARACTER_COMPRESSED_TOPIC, new CharacterCompressedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                CharacterId = body.CharacterId,
                DeletedSourceData = body.DeleteSourceData
            });

            _logger.LogInformation("Character compressed: {CharacterId}", body.CharacterId);
            return (StatusCodes.OK, archive);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error compressing character: {CharacterId}", body.CharacterId);
            return (StatusCodes.ServiceUnavailable, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compressing character: {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character",
                "CompressCharacter",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/character/compress",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets compressed archive data for a character.
    /// </summary>
    public async Task<(StatusCodes, CharacterArchive?)> GetCharacterArchiveAsync(
        GetCharacterArchiveRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting archive for character: {CharacterId}", body.CharacterId);

            var archiveKey = $"{ARCHIVE_KEY_PREFIX}{body.CharacterId}";
            var archiveModel = await _stateStoreFactory.GetStore<CharacterArchiveModel>(StateStoreDefinitions.Character)
                .GetAsync(archiveKey, cancellationToken);

            if (archiveModel == null)
            {
                _logger.LogDebug("No archive found for character {CharacterId}", body.CharacterId);
                return (StatusCodes.NotFound, null);
            }

            var archive = MapFromArchiveModel(archiveModel);
            return (StatusCodes.OK, archive);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error getting archive for character: {CharacterId}", body.CharacterId);
            return (StatusCodes.ServiceUnavailable, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting archive for character: {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character",
                "GetCharacterArchive",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/character/get-archive",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
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
        try
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
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error getting compress data for character: {CharacterId}", body.CharacterId);
            return (StatusCodes.ServiceUnavailable, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting compress data for character: {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character",
                "GetCompressData",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/character/get-compress-data",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
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
        try
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
            var archiveModel = await _stateStoreFactory.GetStore<CharacterArchiveModel>(StateStoreDefinitions.Character)
                .GetAsync(archiveKey, cancellationToken);
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
            var refCountStore = _stateStoreFactory.GetStore<RefCountData>(StateStoreDefinitions.Character);
            var (initialData, initialEtag) = await refCountStore.GetWithETagAsync(refCountKey, cancellationToken);
            var refData = initialData ?? new RefCountData { CharacterId = body.CharacterId };

            const int maxRetries = 3;
            for (var retry = 0; retry < maxRetries; retry++)
            {
                if (retry > 0)
                {
                    // Re-fetch on retry
                    var (storedData, newEtag) = await refCountStore.GetWithETagAsync(refCountKey, cancellationToken);
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

                var savedEtag = await refCountStore.TrySaveAsync(refCountKey, refData, initialEtag ?? string.Empty, cancellationToken);
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
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Dependency error checking references for character: {CharacterId}", body.CharacterId);
            return (StatusCodes.ServiceUnavailable, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking references for character: {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character",
                "CheckCharacterReferences",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/character/check-references",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Enrichment/Compression Helper Methods

    /// <summary>
    /// Builds family tree from relationships.
    /// Uses parallel lookups for relationship types and bulk loading for related characters.
    /// </summary>
    private async Task<FamilyTreeResponse?> BuildFamilyTreeAsync(Guid characterId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _relationshipClient.ListRelationshipsByEntityAsync(
                new ListRelationshipsByEntityRequest
                {
                    EntityId = characterId,
                    EntityType = EntityType.Character
                },
                cancellationToken);

            if (result == null || result.Relationships.Count == 0)
            {
                return new FamilyTreeResponse();
            }

            // Build type code lookup from relationship type IDs - PARALLEL
            var uniqueTypeIds = result.Relationships
                .Select(r => r.RelationshipTypeId)
                .Distinct()
                .ToList();

            var typeCodeLookup = await BuildTypeCodeLookupAsync(uniqueTypeIds, cancellationToken);

            // Collect all related character IDs for bulk loading
            var relatedCharacterIds = result.Relationships
                .Select(r => r.Entity1Id == characterId ? r.Entity2Id : r.Entity1Id)
                .Distinct()
                .ToList();

            // Bulk load all related characters in one call
            var characterLookup = await BulkLoadCharactersAsync(relatedCharacterIds, cancellationToken);

            var familyTree = new FamilyTreeResponse
            {
                Parents = new List<FamilyMember>(),
                Children = new List<FamilyMember>(),
                Siblings = new List<FamilyMember>(),
                Spouses = new List<FamilyMember>(),
                PastLives = new List<PastLifeReference>()
            };

            foreach (var rel in result.Relationships)
            {
                var isEntity1 = rel.Entity1Id == characterId;
                var relatedId = isEntity1 ? rel.Entity2Id : rel.Entity1Id;

                // Look up type code
                if (!typeCodeLookup.TryGetValue(rel.RelationshipTypeId, out var typeCode))
                {
                    continue; // Skip relationships with unknown types
                }

                // Get related character info from pre-loaded lookup
                characterLookup.TryGetValue(relatedId, out var relatedCharacter);
                var name = relatedCharacter?.Name;
                var isAlive = relatedCharacter?.Status == CharacterStatus.Alive;

                // Parent relationship
                if (typeCode == "PARENT" || typeCode == "MOTHER" || typeCode == "FATHER" || typeCode == "STEP_PARENT")
                {
                    if (isEntity1)
                    {
                        // Entity1 is PARENT of Entity2, so this character IS the parent
                        familyTree.Children.Add(new FamilyMember
                        {
                            CharacterId = relatedId,
                            Name = name,
                            RelationshipType = typeCode,
                            IsAlive = isAlive
                        });
                    }
                    else
                    {
                        // Entity2 is the child, this character is the child, rel points to parent
                        familyTree.Parents.Add(new FamilyMember
                        {
                            CharacterId = relatedId,
                            Name = name,
                            RelationshipType = typeCode,
                            IsAlive = isAlive
                        });
                    }
                }
                // Child relationship
                else if (typeCode == "CHILD" || typeCode == "SON" || typeCode == "DAUGHTER" || typeCode == "STEP_CHILD")
                {
                    if (isEntity1)
                    {
                        // Entity1 is CHILD of Entity2
                        familyTree.Parents.Add(new FamilyMember
                        {
                            CharacterId = relatedId,
                            Name = name,
                            RelationshipType = typeCode,
                            IsAlive = isAlive
                        });
                    }
                    else
                    {
                        familyTree.Children.Add(new FamilyMember
                        {
                            CharacterId = relatedId,
                            Name = name,
                            RelationshipType = typeCode,
                            IsAlive = isAlive
                        });
                    }
                }
                // Sibling relationship
                else if (typeCode == "SIBLING" || typeCode == "BROTHER" || typeCode == "SISTER" || typeCode == "HALF_SIBLING")
                {
                    familyTree.Siblings.Add(new FamilyMember
                    {
                        CharacterId = relatedId,
                        Name = name,
                        RelationshipType = typeCode,
                        IsAlive = isAlive
                    });
                }
                // Spouse relationship
                else if (typeCode == "SPOUSE" || typeCode == "HUSBAND" || typeCode == "WIFE")
                {
                    familyTree.Spouses.Add(new FamilyMember
                    {
                        CharacterId = relatedId,
                        Name = name,
                        RelationshipType = typeCode,
                        IsAlive = isAlive
                    });
                }
                // Past life (reincarnation)
                else if (typeCode == "INCARNATION")
                {
                    // This character IS the INCARNATION of another (reincarnated FROM)
                    if (!isEntity1)
                    {
                        familyTree.PastLives.Add(new PastLifeReference
                        {
                            CharacterId = relatedId,
                            Name = name,
                            DeathDate = relatedCharacter?.DeathDate
                        });
                    }
                }
            }

            return familyTree;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return new FamilyTreeResponse();
        }
    }

    /// <summary>
    /// Builds relationship type code lookup using parallel API calls.
    /// </summary>
    private async Task<Dictionary<Guid, string>> BuildTypeCodeLookupAsync(
        List<Guid> typeIds,
        CancellationToken cancellationToken)
    {
        var typeCodeLookup = new Dictionary<Guid, string>();

        if (typeIds.Count == 0)
            return typeCodeLookup;

        // Launch all lookups in parallel
        var lookupTasks = typeIds.Select(async typeId =>
        {
            try
            {
                var typeResponse = await _relationshipClient.GetRelationshipTypeAsync(
                    new GetRelationshipTypeRequest { RelationshipTypeId = typeId },
                    cancellationToken);
                return (typeId, code: typeResponse?.Code);
            }
            catch (ApiException)
            {
                _logger.LogWarning("Could not look up relationship type {TypeId}", typeId);
                return (typeId, code: (string?)null);
            }
        }).ToList();

        var results = await Task.WhenAll(lookupTasks);

        foreach (var (typeId, code) in results)
        {
            if (code != null)
            {
                typeCodeLookup[typeId] = code;
            }
        }

        return typeCodeLookup;
    }

    /// <summary>
    /// Bulk loads characters by ID using the global index for realm resolution.
    /// Returns a dictionary for O(1) lookup during family tree construction.
    /// </summary>
    private async Task<Dictionary<Guid, CharacterModel>> BulkLoadCharactersAsync(
        List<Guid> characterIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, CharacterModel>();

        if (characterIds.Count == 0)
            return result;

        var store = _stateStoreFactory.GetStore<CharacterModel>(StateStoreDefinitions.Character);
        var globalIndexStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Character);

        // Step 1: Bulk load global index entries to get realm IDs
        var globalIndexKeys = characterIds
            .Select(id => $"character-global-index:{id}")
            .ToList();

        var globalIndexResults = await globalIndexStore.GetBulkAsync(globalIndexKeys, cancellationToken);

        // Step 2: Build character keys from realm mappings
        var characterKeys = new List<string>();
        var keyToIdMap = new Dictionary<string, Guid>();

        foreach (var (globalIndexKey, realmId) in globalIndexResults)
        {
            if (string.IsNullOrEmpty(realmId))
                continue;

            // Extract character ID from global index key (format: "character-global-index:{id}")
            var characterIdStr = globalIndexKey.Replace("character-global-index:", "");
            if (Guid.TryParse(characterIdStr, out var characterId))
            {
                var characterKey = BuildCharacterKey(realmId, characterIdStr);
                characterKeys.Add(characterKey);
                keyToIdMap[characterKey] = characterId;
            }
        }

        if (characterKeys.Count == 0)
            return result;

        // Step 3: Bulk load all characters
        var characterResults = await store.GetBulkAsync(characterKeys, cancellationToken);

        foreach (var (key, character) in characterResults)
        {
            if (character != null && keyToIdMap.TryGetValue(key, out var characterId))
            {
                result[characterId] = character;
            }
        }

        return result;
    }

    /// <summary>
    /// Generates a text summary of family relationships.
    /// </summary>
    private async Task<string?> GenerateFamilySummaryAsync(Guid characterId, CancellationToken cancellationToken)
    {
        var familyTree = await BuildFamilyTreeAsync(characterId, cancellationToken);
        if (familyTree == null)
            return null;

        var parts = new List<string>();

        if (familyTree.Spouses?.Count > 0)
        {
            var spouseNames = familyTree.Spouses.Select(s => s.Name ?? "unknown");
            parts.Add($"married to {string.Join(" and ", spouseNames)}");
        }

        var childCount = familyTree.Children?.Count ?? 0;
        if (childCount > 0)
            parts.Add($"parent of {childCount}");

        var parentCount = familyTree.Parents?.Count ?? 0;
        if (parentCount == 0)
            parts.Add("orphaned");
        else if (parentCount == 1)
            parts.Add("single parent household");

        var pastLivesCount = familyTree.PastLives?.Count ?? 0;
        if (pastLivesCount > 0)
            parts.Add($"reincarnated from {pastLivesCount} past life(s)");

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private static CharacterArchiveModel MapToArchiveModel(CharacterArchive archive)
    {
        return new CharacterArchiveModel
        {
            CharacterId = archive.CharacterId,
            Name = archive.Name,
            RealmId = archive.RealmId,
            SpeciesId = archive.SpeciesId,
            BirthDateUnix = archive.BirthDate.ToUnixTimeSeconds(),
            DeathDateUnix = archive.DeathDate.ToUnixTimeSeconds(),
            CompressedAtUnix = archive.CompressedAt.ToUnixTimeSeconds(),
            PersonalitySummary = archive.PersonalitySummary,
            KeyBackstoryPoints = archive.KeyBackstoryPoints.ToList(),
            MajorLifeEvents = archive.MajorLifeEvents.ToList(),
            FamilySummary = archive.FamilySummary
        };
    }

    private static CharacterArchive MapFromArchiveModel(CharacterArchiveModel model)
    {
        return new CharacterArchive
        {
            CharacterId = model.CharacterId,
            Name = model.Name,
            RealmId = model.RealmId,
            SpeciesId = model.SpeciesId,
            BirthDate = DateTimeOffset.FromUnixTimeSeconds(model.BirthDateUnix),
            DeathDate = DateTimeOffset.FromUnixTimeSeconds(model.DeathDateUnix),
            CompressedAt = DateTimeOffset.FromUnixTimeSeconds(model.CompressedAtUnix),
            PersonalitySummary = model.PersonalitySummary,
            KeyBackstoryPoints = model.KeyBackstoryPoints,
            MajorLifeEvents = model.MajorLifeEvents,
            FamilySummary = model.FamilySummary
        };
    }

    #endregion

    #region Helper Methods

    private static string BuildCharacterKey(string realmId, string characterId)
        => $"{CHARACTER_KEY_PREFIX}{realmId}:{characterId}";

    private static string BuildRealmIndexKey(string realmId)
        => $"{REALM_INDEX_KEY_PREFIX}{realmId}";

    #region Validation Helpers

    /// <summary>
    /// Validates that a realm exists and is active (not deprecated).
    /// </summary>
    private async Task<(bool exists, bool isActive)> ValidateRealmAsync(Guid realmId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _realmClient.RealmExistsAsync(
                new RealmExistsRequest { RealmId = realmId },
                cancellationToken);
            return (response.Exists, response.IsActive);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return (false, false);
        }
        // Let ApiException propagate naturally so callers classify it as ServiceUnavailable (IMPLEMENTATION TENETS)
        catch (Exception ex) when (ex is not ApiException)
        {
            _logger.LogError(ex, "Could not validate realm {RealmId} - failing operation (fail closed)", realmId);
            throw;
        }
    }

    /// <summary>
    /// Validates that a species exists and is available in the specified realm.
    /// </summary>
    private async Task<(bool exists, bool isInRealm)> ValidateSpeciesAsync(Guid speciesId, Guid realmId, CancellationToken cancellationToken)
    {
        try
        {
            var speciesResponse = await _speciesClient.GetSpeciesAsync(
                new GetSpeciesRequest { SpeciesId = speciesId },
                cancellationToken);

            if (speciesResponse == null)
            {
                return (false, false);
            }

            // Check if species is available in the specified realm
            var isInRealm = speciesResponse.RealmIds?.Contains(realmId) ?? false;
            return (true, isInRealm);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return (false, false);
        }
        // Let ApiException propagate naturally so callers classify it as ServiceUnavailable (IMPLEMENTATION TENETS)
        catch (Exception ex) when (ex is not ApiException)
        {
            _logger.LogError(ex, "Could not validate species {SpeciesId} - failing operation (fail closed)", speciesId);
            throw;
        }
    }

    #endregion

    private async Task<CharacterModel?> FindCharacterByIdAsync(string characterId, CancellationToken cancellationToken)
    {
        // Use global character index to find realm for character ID lookup
        // Global index is maintained by AddCharacterToRealmIndexAsync/RemoveCharacterFromRealmIndexAsync
        var globalIndexKey = $"character-global-index:{characterId}";
        var realmId = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Character)
            .GetAsync(globalIndexKey, cancellationToken);

        if (string.IsNullOrEmpty(realmId))
        {
            _logger.LogDebug("Character {CharacterId} not found in global index", characterId);
            return null;
        }

        var characterKey = BuildCharacterKey(realmId, characterId);
        return await _stateStoreFactory.GetStore<CharacterModel>(StateStoreDefinitions.Character)
            .GetAsync(characterKey, cancellationToken);
    }

    private async Task<(StatusCodes, CharacterListResponse?)> GetCharactersByRealmInternalAsync(
        string realmId,
        CharacterStatus? statusFilter,
        Guid? speciesFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var jsonStore = _stateStoreFactory.GetJsonQueryableStore<CharacterModel>(StateStoreDefinitions.Character);
        var offset = (page - 1) * pageSize;

        var conditions = BuildCharacterQueryConditions(realmId, statusFilter, speciesFilter);

        var sortSpec = new JsonSortSpec
        {
            Path = "$.Name",
            Descending = false
        };

        var result = await jsonStore.JsonQueryPagedAsync(
            conditions,
            offset,
            pageSize,
            sortSpec,
            cancellationToken);

        var pagedCharacters = result.Items
            .Select(item => MapToCharacterResponse(item.Value))
            .ToList();

        var response = new CharacterListResponse
        {
            Characters = pagedCharacters,
            TotalCount = (int)result.TotalCount,
            Page = page,
            PageSize = pageSize,
            HasNextPage = result.HasMore,
            HasPreviousPage = page > 1
        };

        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Builds MySQL JSON query conditions for character listing.
    /// Uses server-side filtering to avoid loading all characters into memory.
    /// </summary>
    private static List<QueryCondition> BuildCharacterQueryConditions(
        string realmId,
        CharacterStatus? statusFilter,
        Guid? speciesFilter)
    {
        var conditions = new List<QueryCondition>
        {
            // Type discriminator: only CharacterModel records have CharacterId
            new QueryCondition { Path = "$.CharacterId", Operator = QueryOperator.Exists, Value = true },
            // Realm filter: server-side partition by realm
            new QueryCondition { Path = "$.RealmId", Operator = QueryOperator.Equals, Value = realmId }
        };

        if (statusFilter.HasValue)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.Status",
                Operator = QueryOperator.Equals,
                Value = statusFilter.Value.ToString()
            });
        }

        if (speciesFilter.HasValue)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.SpeciesId",
                Operator = QueryOperator.Equals,
                Value = speciesFilter.Value.ToString()
            });
        }

        return conditions;
    }

    private async Task AddCharacterToRealmIndexAsync(string realmId, string characterId, CancellationToken cancellationToken)
    {
        var realmIndexKey = BuildRealmIndexKey(realmId);
        var store = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Character);

        // Retry loop for optimistic concurrency
        var maxRetries = _configuration.RealmIndexUpdateMaxRetries;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            var (characterIds, etag) = await store.GetWithETagAsync(realmIndexKey, cancellationToken);
            characterIds ??= new List<string>();

            if (!characterIds.Contains(characterId))
            {
                characterIds.Add(characterId);

                // If no prior value (null etag), just save directly
                if (etag == null)
                {
                    await store.SaveAsync(realmIndexKey, characterIds, cancellationToken: cancellationToken);
                    break;
                }

                // Otherwise use optimistic concurrency
                if (await store.TrySaveAsync(realmIndexKey, characterIds, etag, cancellationToken) != null)
                    break;

                // Retry on conflict
                if (retry < maxRetries - 1)
                {
                    _logger.LogDebug("Realm index update conflict, retrying ({Retry}/{MaxRetries})", retry + 1, maxRetries);
                    continue;
                }
                throw new InvalidOperationException($"Failed to update realm index after {maxRetries} retries");
            }
            else
            {
                // Already in the list, no update needed
                break;
            }
        }

        // Also add to global index for ID-based lookups
        var globalIndexKey = $"character-global-index:{characterId}";
        await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Character)
            .SaveAsync(globalIndexKey, realmId, cancellationToken: cancellationToken);
    }

    private async Task RemoveCharacterFromRealmIndexAsync(string realmId, string characterId, CancellationToken cancellationToken)
    {
        var realmIndexKey = BuildRealmIndexKey(realmId);
        var store = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Character);

        // Retry loop for optimistic concurrency
        var maxRetries = _configuration.RealmIndexUpdateMaxRetries;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            var (characterIds, etag) = await store.GetWithETagAsync(realmIndexKey, cancellationToken);

            // If list doesn't exist, nothing to remove
            if (characterIds == null || etag == null)
                break;

            if (characterIds.Remove(characterId))
            {
                if (await store.TrySaveAsync(realmIndexKey, characterIds, etag, cancellationToken) != null)
                    break;

                // Retry on conflict
                if (retry < maxRetries - 1)
                {
                    _logger.LogDebug("Realm index update conflict, retrying ({Retry}/{MaxRetries})", retry + 1, maxRetries);
                    continue;
                }
                throw new InvalidOperationException($"Failed to update realm index after {maxRetries} retries");
            }
            else
            {
                // Not in the list, no update needed
                break;
            }
        }

        // Remove from global index
        var globalIndexKey = $"character-global-index:{characterId}";
        await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Character)
            .DeleteAsync(globalIndexKey, cancellationToken);
    }

    private static CharacterResponse MapToCharacterResponse(CharacterModel model)
    {
        return new CharacterResponse
        {
            CharacterId = model.CharacterId,
            Name = model.Name,
            RealmId = model.RealmId,
            SpeciesId = model.SpeciesId,
            BirthDate = model.BirthDate,
            DeathDate = model.DeathDate,
            Status = model.Status,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    #endregion

    #region Event Publishing

    /// <summary>
    /// Publishes character created event. TryPublishAsync handles buffering, retry, and error logging.
    /// </summary>
    private async Task PublishCharacterCreatedEventAsync(CharacterModel character)
    {
        var eventModel = new CharacterCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = character.CharacterId,
            Name = character.Name,
            RealmId = character.RealmId,
            SpeciesId = character.SpeciesId,
            BirthDate = character.BirthDate,
            DeathDate = character.DeathDate
        };

        await _messageBus.TryPublishAsync(CHARACTER_CREATED_TOPIC, eventModel);
        _logger.LogDebug("Published CharacterCreatedEvent for character: {CharacterId}", character.CharacterId);
    }

    /// <summary>
    /// Publishes character updated event. TryPublishAsync handles buffering, retry, and error logging.
    /// </summary>
    private async Task PublishCharacterUpdatedEventAsync(CharacterModel character, IEnumerable<string> changedFields)
    {
        var eventModel = new CharacterUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = character.CharacterId,
            Name = character.Name,
            RealmId = character.RealmId,
            SpeciesId = character.SpeciesId,
            BirthDate = character.BirthDate,
            DeathDate = character.DeathDate,
            Status = character.Status,
            CreatedAt = character.CreatedAt,
            UpdatedAt = character.UpdatedAt,
            ChangedFields = changedFields.ToList()
        };

        await _messageBus.TryPublishAsync(CHARACTER_UPDATED_TOPIC, eventModel);
        _logger.LogDebug("Published CharacterUpdatedEvent for character: {CharacterId}", character.CharacterId);
    }

    /// <summary>
    /// Publishes character deleted event. TryPublishAsync handles buffering, retry, and error logging.
    /// </summary>
    private async Task PublishCharacterDeletedEventAsync(CharacterModel character, string? deletedReason = null)
    {
        var eventModel = new CharacterDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = character.CharacterId,
            Name = character.Name,
            RealmId = character.RealmId,
            SpeciesId = character.SpeciesId,
            BirthDate = character.BirthDate,
            DeathDate = character.DeathDate,
            Status = character.Status,
            CreatedAt = character.CreatedAt,
            UpdatedAt = character.UpdatedAt,
            DeletedReason = deletedReason
        };

        await _messageBus.TryPublishAsync(CHARACTER_DELETED_TOPIC, eventModel);
        _logger.LogDebug("Published CharacterDeletedEvent for character: {CharacterId}", character.CharacterId);
    }

    /// <summary>
    /// Publishes character realm joined event. TryPublishAsync handles buffering, retry, and error logging.
    /// </summary>
    private async Task PublishCharacterRealmJoinedEventAsync(Guid characterId, Guid realmId, Guid? previousRealmId)
    {
        var eventModel = new CharacterRealmJoinedEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = characterId,
            RealmId = realmId,
            PreviousRealmId = previousRealmId
        };

        await _messageBus.TryPublishAsync(CHARACTER_REALM_JOINED_TOPIC, eventModel);
        _logger.LogDebug("Published CharacterRealmJoinedEvent for character: {CharacterId}", characterId);
    }

    /// <summary>
    /// Publishes character realm left event. TryPublishAsync handles buffering, retry, and error logging.
    /// </summary>
    private async Task PublishCharacterRealmLeftEventAsync(Guid characterId, Guid realmId, string reason)
    {
        var eventModel = new CharacterRealmLeftEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow,
            CharacterId = characterId,
            RealmId = realmId,
            Reason = reason
        };

        await _messageBus.TryPublishAsync(CHARACTER_REALM_LEFT_TOPIC, eventModel);
        _logger.LogDebug("Published CharacterRealmLeftEvent for character: {CharacterId}", characterId);
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogInformation("Registering Character service permissions...");
        try
        {
            await CharacterPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
            _logger.LogInformation("Character service permissions registered via event");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Character service permissions");
            throw;
        }
    }

    #endregion
}
