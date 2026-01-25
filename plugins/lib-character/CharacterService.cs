using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.CharacterHistory;
using BeyondImmersion.BannouService.CharacterPersonality;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.RelationshipType;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Character;

/// <summary>
/// Implementation of the Character service for Arcadia game world.
/// Characters are independent world assets (not owned by accounts).
/// Uses realm-based partitioning for scalability.
/// Note: Character relationships are managed by the separate Relationship service.
/// </summary>
[BannouService("character", typeof(ICharacterService), lifetime: ServiceLifetime.Scoped)]
public partial class CharacterService : ICharacterService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<CharacterService> _logger;
    private readonly CharacterServiceConfiguration _configuration;
    private readonly IRealmClient _realmClient;
    private readonly ISpeciesClient _speciesClient;
    private readonly ICharacterPersonalityClient _personalityClient;
    private readonly ICharacterHistoryClient _historyClient;
    private readonly IRelationshipClient _relationshipClient;
    private readonly IRelationshipTypeClient _relationshipTypeClient;

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

    // Reference type constants
    private const string REFERENCE_TYPE_RELATIONSHIP = "RELATIONSHIP";

    // Grace period for cleanup eligibility - from configuration in days, converted to seconds at usage

    public CharacterService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<CharacterService> logger,
        CharacterServiceConfiguration configuration,
        IRealmClient realmClient,
        ISpeciesClient speciesClient,
        ICharacterPersonalityClient personalityClient,
        ICharacterHistoryClient historyClient,
        IRelationshipClient relationshipClient,
        IRelationshipTypeClient relationshipTypeClient,
        IEventConsumer eventConsumer)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _realmClient = realmClient;
        _speciesClient = speciesClient;
        _personalityClient = personalityClient;
        _historyClient = historyClient;
        _relationshipClient = relationshipClient;
        _relationshipTypeClient = relationshipTypeClient;

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

    #endregion

    #region Enriched Character & Compression Operations

    /// <summary>
    /// Gets a character with optional enriched data (personality, backstory, family tree).
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

            // Fetch personality if requested
            if (body.IncludePersonality)
            {
                try
                {
                    var personalityResult = await _personalityClient.GetPersonalityAsync(
                        new CharacterPersonality.GetPersonalityRequest { CharacterId = body.CharacterId },
                        cancellationToken);

                    if (personalityResult != null)
                    {
                        response.Personality = new PersonalitySnapshot
                        {
                            Traits = personalityResult.Traits.ToDictionary(t => t.Axis.ToString(), t => t.Value),
                            Version = personalityResult.Version
                        };
                    }
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    _logger.LogDebug("No personality found for character {CharacterId}", body.CharacterId);
                }
            }

            // Fetch combat preferences if requested
            if (body.IncludeCombatPreferences)
            {
                try
                {
                    var combatResult = await _personalityClient.GetCombatPreferencesAsync(
                        new CharacterPersonality.GetCombatPreferencesRequest { CharacterId = body.CharacterId },
                        cancellationToken);

                    if (combatResult != null)
                    {
                        response.CombatPreferences = new CombatPreferencesSnapshot
                        {
                            Style = combatResult.Preferences.Style.ToString(),
                            PreferredRange = combatResult.Preferences.PreferredRange.ToString(),
                            GroupRole = combatResult.Preferences.GroupRole.ToString(),
                            RiskTolerance = combatResult.Preferences.RiskTolerance,
                            RetreatThreshold = combatResult.Preferences.RetreatThreshold,
                            ProtectAllies = combatResult.Preferences.ProtectAllies
                        };
                    }
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    _logger.LogDebug("No combat preferences found for character {CharacterId}", body.CharacterId);
                }
            }

            // Fetch backstory if requested
            if (body.IncludeBackstory)
            {
                try
                {
                    var backstoryResult = await _historyClient.GetBackstoryAsync(
                        new CharacterHistory.GetBackstoryRequest { CharacterId = body.CharacterId },
                        cancellationToken);

                    if (backstoryResult != null)
                    {
                        response.Backstory = new BackstorySnapshot
                        {
                            Elements = backstoryResult.Elements.Select(e => new BackstoryElementSnapshot
                            {
                                ElementType = e.ElementType.ToString(),
                                Key = e.Key,
                                Value = e.Value,
                                Strength = e.Strength
                            }).ToList()
                        };
                    }
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    _logger.LogDebug("No backstory found for character {CharacterId}", body.CharacterId);
                }
            }

            // Fetch family tree if requested
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
    /// </summary>
    public async Task<(StatusCodes, CharacterArchive?)> CompressCharacterAsync(
        CompressCharacterRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Compressing character: {CharacterId}", body.CharacterId);

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

            // Generate text summaries
            string? personalitySummary = null;
            var keyBackstoryPoints = new List<string>();
            var majorLifeEvents = new List<string>();
            string? familySummary = null;

            // Get personality summary
            try
            {
                var personalityResult = await _personalityClient.GetPersonalityAsync(
                    new CharacterPersonality.GetPersonalityRequest { CharacterId = body.CharacterId },
                    cancellationToken);

                if (personalityResult != null)
                {
                    personalitySummary = GeneratePersonalitySummary(personalityResult.Traits);
                }
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogDebug("No personality to summarize for character {CharacterId}", body.CharacterId);
            }

            // Get backstory/history summaries
            try
            {
                var historyResult = await _historyClient.SummarizeHistoryAsync(
                    new SummarizeHistoryRequest
                    {
                        CharacterId = body.CharacterId,
                        MaxBackstoryPoints = _configuration.CompressionMaxBackstoryPoints,
                        MaxLifeEvents = _configuration.CompressionMaxLifeEvents
                    },
                    cancellationToken);

                if (historyResult != null)
                {
                    keyBackstoryPoints = historyResult.KeyBackstoryPoints.ToList();
                    majorLifeEvents = historyResult.MajorLifeEvents.ToList();
                }
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogDebug("No history to summarize for character {CharacterId}", body.CharacterId);
            }

            // Get family summary
            familySummary = await GenerateFamilySummaryAsync(body.CharacterId, cancellationToken);

            var archive = new CharacterArchive
            {
                CharacterId = body.CharacterId,
                Name = character.Name,
                RealmId = character.RealmId,
                SpeciesId = character.SpeciesId,
                BirthDate = character.BirthDate,
                DeathDate = character.DeathDate.Value,
                CompressedAt = DateTimeOffset.UtcNow,
                PersonalitySummary = personalitySummary,
                KeyBackstoryPoints = keyBackstoryPoints,
                MajorLifeEvents = majorLifeEvents,
                FamilySummary = familySummary
            };

            // Store archive
            var archiveKey = $"{ARCHIVE_KEY_PREFIX}{body.CharacterId}";
            await _stateStoreFactory.GetStore<CharacterArchiveModel>(StateStoreDefinitions.Character)
                .SaveAsync(archiveKey, MapToArchiveModel(archive), cancellationToken: cancellationToken);

            // Optionally delete source data
            if (body.DeleteSourceData)
            {
                try
                {
                    await _personalityClient.DeletePersonalityAsync(
                        new CharacterPersonality.DeletePersonalityRequest { CharacterId = body.CharacterId },
                        cancellationToken);
                }
                catch (ApiException ex) when (ex.StatusCode == 404) { /* Ignore if not found */ }

                try
                {
                    await _historyClient.DeleteAllHistoryAsync(
                        new DeleteAllHistoryRequest { CharacterId = body.CharacterId },
                        cancellationToken);
                }
                catch (ApiException ex) when (ex.StatusCode == 404) { /* Ignore if not found */ }
            }

            // Publish compression event
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
    /// Checks reference count for cleanup eligibility.
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

            // Count references from relationship service
            var referenceTypes = new List<string>();
            var referenceCount = 0;

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
                    referenceCount = relResult.TotalCount;

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

            // Build type code lookup from relationship type IDs
            var uniqueTypeIds = result.Relationships
                .Select(r => r.RelationshipTypeId)
                .Distinct()
                .ToList();

            var typeCodeLookup = new Dictionary<Guid, string>();
            foreach (var typeId in uniqueTypeIds)
            {
                try
                {
                    var typeResponse = await _relationshipTypeClient.GetRelationshipTypeAsync(
                        new GetRelationshipTypeRequest { RelationshipTypeId = typeId },
                        cancellationToken);
                    if (typeResponse != null)
                    {
                        typeCodeLookup[typeId] = typeResponse.Code;
                    }
                }
                catch (ApiException)
                {
                    // If we can't look up the type, skip it
                    _logger.LogWarning("Could not look up relationship type {TypeId}", typeId);
                }
            }

            var familyTree = new FamilyTreeResponse
            {
                Parents = new List<FamilyMember>(),
                Children = new List<FamilyMember>(),
                Siblings = new List<FamilyMember>(),
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

                // Get related character info
                var relatedCharacter = await FindCharacterByIdAsync(relatedId.ToString(), cancellationToken);
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
                    familyTree.Spouse = new FamilyMember
                    {
                        CharacterId = relatedId,
                        Name = name,
                        RelationshipType = typeCode,
                        IsAlive = isAlive
                    };
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
    /// Generates a text summary of personality traits.
    /// </summary>
    private string GeneratePersonalitySummary(ICollection<CharacterPersonality.TraitValue> traits)
    {
        var descriptions = new List<string>();
        var threshold = (float)_configuration.PersonalityTraitThreshold;
        var negThreshold = -threshold;

        foreach (var trait in traits)
        {
            var desc = trait.Axis.ToString() switch
            {
                "OPENNESS" => trait.Value > threshold ? "creative" : trait.Value < negThreshold ? "traditional" : null,
                "CONSCIENTIOUSNESS" => trait.Value > threshold ? "organized" : trait.Value < negThreshold ? "spontaneous" : null,
                "EXTRAVERSION" => trait.Value > threshold ? "outgoing" : trait.Value < negThreshold ? "reserved" : null,
                "AGREEABLENESS" => trait.Value > threshold ? "cooperative" : trait.Value < negThreshold ? "competitive" : null,
                "NEUROTICISM" => trait.Value > threshold ? "anxious" : trait.Value < negThreshold ? "calm" : null,
                "HONESTY" => trait.Value > threshold ? "sincere" : trait.Value < negThreshold ? "deceptive" : null,
                "AGGRESSION" => trait.Value > threshold ? "confrontational" : trait.Value < negThreshold ? "pacifist" : null,
                "LOYALTY" => trait.Value > threshold ? "devoted" : trait.Value < negThreshold ? "self-serving" : null,
                _ => null
            };

            if (desc != null)
                descriptions.Add(desc);
        }

        if (descriptions.Count == 0)
            return "balanced personality";

        return string.Join(", ", descriptions);
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

        if (familyTree.Spouse != null)
            parts.Add($"married to {familyTree.Spouse.Name ?? "unknown"}");

        if (familyTree.Children.Count > 0)
            parts.Add($"parent of {familyTree.Children.Count}");

        if (familyTree.Parents.Count == 0)
            parts.Add("orphaned");
        else if (familyTree.Parents.Count == 1)
            parts.Add("single parent household");

        if (familyTree.PastLives.Count > 0)
            parts.Add($"reincarnated from {familyTree.PastLives.Count} past life(s)");

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not validate realm {RealmId} - failing operation (fail closed)", realmId);
            // If RealmService is unavailable, fail the operation - don't assume realm is valid
            throw new InvalidOperationException($"Cannot validate realm {realmId}: RealmService unavailable", ex);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not validate species {SpeciesId} - failing operation (fail closed)", speciesId);
            // If SpeciesService is unavailable, fail the operation - don't assume species is valid
            throw new InvalidOperationException($"Cannot validate species {speciesId}: SpeciesService unavailable", ex);
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
        // Step 1: Get character IDs from realm index (single query)
        var realmIndexKey = BuildRealmIndexKey(realmId);
        var characterIds = await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Character)
            .GetAsync(realmIndexKey, cancellationToken) ?? new List<string>();

        if (characterIds.Count == 0)
        {
            return (StatusCodes.OK, new CharacterListResponse
            {
                Characters = new List<CharacterResponse>(),
                TotalCount = 0,
                Page = page,
                PageSize = pageSize,
                HasNextPage = false,
                HasPreviousPage = false
            });
        }

        // Step 2: Build keys for bulk retrieval
        var keys = characterIds
            .Select(id => BuildCharacterKey(realmId, id))
            .ToList();

        // Step 3: Bulk load all characters (single query instead of N queries)
        var bulkResults = await _stateStoreFactory.GetStore<CharacterModel>(StateStoreDefinitions.Character)
            .GetBulkAsync(keys, cancellationToken);

        // Step 4: Filter results
        var filteredCharacters = new List<CharacterModel>();
        foreach (var (_, character) in bulkResults)
        {
            if (character == null)
                continue;

            // Apply filters
            if (statusFilter.HasValue && character.Status != statusFilter.Value)
                continue;
            if (speciesFilter.HasValue && character.SpeciesId != speciesFilter.Value)
                continue;

            filteredCharacters.Add(character);
        }

        // Step 5: Paginate
        var totalCount = filteredCharacters.Count;
        var skip = (page - 1) * pageSize;
        var pagedCharacters = filteredCharacters
            .Skip(skip)
            .Take(pageSize)
            .Select(MapToCharacterResponse)
            .ToList();

        var response = new CharacterListResponse
        {
            Characters = pagedCharacters,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasNextPage = skip + pageSize < totalCount,
            HasPreviousPage = page > 1
        };

        return (StatusCodes.OK, response);
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
            BirthDate = character.BirthDate
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

#region Internal Models

/// <summary>
/// Character data model for lib-state storage.
/// Uses Guid types for type-safe ID handling per IMPLEMENTATION TENETS.
/// </summary>
internal class CharacterModel
{
    public Guid CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid RealmId { get; set; }
    public Guid SpeciesId { get; set; }
    public CharacterStatus Status { get; set; } = CharacterStatus.Alive;

    // Store as DateTimeOffset directly - lib-state handles serialization
    public DateTimeOffset BirthDate { get; set; }
    public DateTimeOffset? DeathDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Archive data model for compressed characters.
/// Uses Guid types for type-safe ID handling per IMPLEMENTATION TENETS.
/// </summary>
internal class CharacterArchiveModel
{
    public Guid CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid RealmId { get; set; }
    public Guid SpeciesId { get; set; }
    public long BirthDateUnix { get; set; }
    public long DeathDateUnix { get; set; }
    public long CompressedAtUnix { get; set; }
    public string? PersonalitySummary { get; set; }
    public List<string> KeyBackstoryPoints { get; set; } = new();
    public List<string> MajorLifeEvents { get; set; } = new();
    public string? FamilySummary { get; set; }
}

/// <summary>
/// Reference count tracking data for cleanup eligibility.
/// Uses Guid type for type-safe ID handling per IMPLEMENTATION TENETS.
/// </summary>
internal class RefCountData
{
    public Guid CharacterId { get; set; }
    public long? ZeroRefSinceUnix { get; set; }
}

#endregion
