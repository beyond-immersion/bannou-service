using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State.Services;
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

    // State store names
    private const string CHARACTER_STATE_STORE = "character-statestore";

    // Key prefixes for realm-partitioned storage
    private const string CHARACTER_KEY_PREFIX = "character:";
    private const string REALM_INDEX_KEY_PREFIX = "realm-index:";

    // Event topics
    private const string CHARACTER_CREATED_TOPIC = "character.created";
    private const string CHARACTER_UPDATED_TOPIC = "character.updated";
    private const string CHARACTER_DELETED_TOPIC = "character.deleted";
    private const string CHARACTER_REALM_JOINED_TOPIC = "character.realm.joined";
    private const string CHARACTER_REALM_LEFT_TOPIC = "character.realm.left";

    public CharacterService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<CharacterService> logger,
        CharacterServiceConfiguration configuration,
        IRealmClient realmClient,
        ISpeciesClient speciesClient,
        IEventConsumer eventConsumer)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _realmClient = realmClient;
        _speciesClient = speciesClient;

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
            _logger.LogInformation("Creating character: {Name} in realm: {RealmId}", body.Name, body.RealmId);

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
                CharacterId = characterId.ToString(),
                Name = body.Name,
                RealmId = body.RealmId.ToString(),
                SpeciesId = body.SpeciesId.ToString(),
                BirthDate = body.BirthDate,
                Status = body.Status,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Build realm-partitioned key
            var characterKey = BuildCharacterKey(body.RealmId.ToString(), characterId.ToString());

            // Save character to state store
            await _stateStoreFactory.GetStore<CharacterModel>(CHARACTER_STATE_STORE)
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
            _logger.LogInformation("Getting character: {CharacterId}", body.CharacterId);

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
            _logger.LogInformation("Updating character: {CharacterId}", body.CharacterId);

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
            if (body.SpeciesId.HasValue && body.SpeciesId.Value != Guid.Parse(character.SpeciesId))
            {
                changedFields.Add("speciesId");
                character.SpeciesId = body.SpeciesId.Value.ToString();
            }

            character.UpdatedAt = DateTimeOffset.UtcNow;

            // Save updated character
            var characterKey = BuildCharacterKey(character.RealmId, character.CharacterId);
            await _stateStoreFactory.GetStore<CharacterModel>(CHARACTER_STATE_STORE)
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
            _logger.LogInformation("Deleting character: {CharacterId}", body.CharacterId);

            // Find existing character
            var character = await FindCharacterByIdAsync(body.CharacterId.ToString(), cancellationToken);

            if (character == null)
            {
                _logger.LogWarning("Character not found for deletion: {CharacterId}", body.CharacterId);
                return StatusCodes.NotFound;
            }

            var realmId = character.RealmId;
            var characterKey = BuildCharacterKey(realmId, character.CharacterId);

            // Delete character from state store
            await _stateStoreFactory.GetStore<CharacterModel>(CHARACTER_STATE_STORE)
                .DeleteAsync(characterKey, cancellationToken);

            // Remove from realm index
            await RemoveCharacterFromRealmIndexAsync(realmId, character.CharacterId, cancellationToken);

            _logger.LogInformation("Character deleted: {CharacterId} from realm: {RealmId}", body.CharacterId, realmId);

            // Publish realm left event (reason: deletion)
            await PublishCharacterRealmLeftEventAsync(
                body.CharacterId,
                Guid.Parse(realmId),
                "deletion");

            // Publish character deleted event
            await PublishCharacterDeletedEventAsync(character);

            return StatusCodes.OK;
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

            _logger.LogInformation("Listing characters - Page: {Page}, PageSize: {PageSize}", page, pageSize);

            // If realm filter is provided, use realm-specific query
            if (body.RealmId.HasValue)
            {
                return await GetCharactersByRealmInternalAsync(
                    body.RealmId.Value.ToString(),
                    body.Status,
                    body.SpeciesId,
                    page,
                    pageSize,
                    cancellationToken);
            }

            // Without realm filter, we need to scan all realms (less efficient)
            // For now, return empty - in production you'd want a global index
            _logger.LogWarning("ListCharacters called without realmId filter - returning empty for efficiency");

            var response = new CharacterListResponse
            {
                Characters = new List<CharacterResponse>(),
                TotalCount = 0,
                Page = page,
                PageSize = pageSize,
                HasNextPage = false,
                HasPreviousPage = false
            };

            return (StatusCodes.OK, response);
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

            _logger.LogInformation("Getting characters by realm: {RealmId} - Page: {Page}, PageSize: {PageSize}",
                body.RealmId, page, pageSize);

            return await GetCharactersByRealmInternalAsync(
                body.RealmId.ToString(),
                body.Status,
                body.SpeciesId,
                page,
                pageSize,
                cancellationToken);
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
        var realmId = await _stateStoreFactory.GetStore<string>(CHARACTER_STATE_STORE)
            .GetAsync(globalIndexKey, cancellationToken);

        if (string.IsNullOrEmpty(realmId))
        {
            _logger.LogDebug("Character {CharacterId} not found in global index", characterId);
            return null;
        }

        var characterKey = BuildCharacterKey(realmId, characterId);
        return await _stateStoreFactory.GetStore<CharacterModel>(CHARACTER_STATE_STORE)
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
        var characterIds = await _stateStoreFactory.GetStore<List<string>>(CHARACTER_STATE_STORE)
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
        var bulkResults = await _stateStoreFactory.GetStore<CharacterModel>(CHARACTER_STATE_STORE)
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
            if (speciesFilter.HasValue && character.SpeciesId != speciesFilter.Value.ToString())
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
        var store = _stateStoreFactory.GetStore<List<string>>(CHARACTER_STATE_STORE);

        // Retry loop for optimistic concurrency
        const int maxRetries = 3;
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
                if (await store.TrySaveAsync(realmIndexKey, characterIds, etag, cancellationToken))
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
        await _stateStoreFactory.GetStore<string>(CHARACTER_STATE_STORE)
            .SaveAsync(globalIndexKey, realmId, cancellationToken: cancellationToken);
    }

    private async Task RemoveCharacterFromRealmIndexAsync(string realmId, string characterId, CancellationToken cancellationToken)
    {
        var realmIndexKey = BuildRealmIndexKey(realmId);
        var store = _stateStoreFactory.GetStore<List<string>>(CHARACTER_STATE_STORE);

        // Retry loop for optimistic concurrency
        const int maxRetries = 3;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            var (characterIds, etag) = await store.GetWithETagAsync(realmIndexKey, cancellationToken);

            // If list doesn't exist, nothing to remove
            if (characterIds == null || etag == null)
                break;

            if (characterIds.Remove(characterId))
            {
                if (await store.TrySaveAsync(realmIndexKey, characterIds, etag, cancellationToken))
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
        await _stateStoreFactory.GetStore<string>(CHARACTER_STATE_STORE)
            .DeleteAsync(globalIndexKey, cancellationToken);
    }

    private static CharacterResponse MapToCharacterResponse(CharacterModel model)
    {
        return new CharacterResponse
        {
            CharacterId = Guid.Parse(model.CharacterId),
            Name = model.Name,
            RealmId = Guid.Parse(model.RealmId),
            SpeciesId = Guid.Parse(model.SpeciesId),
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
            CharacterId = Guid.Parse(character.CharacterId),
            Name = character.Name,
            RealmId = Guid.Parse(character.RealmId),
            SpeciesId = Guid.Parse(character.SpeciesId),
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
            CharacterId = Guid.Parse(character.CharacterId),
            Name = character.Name,
            RealmId = Guid.Parse(character.RealmId),
            SpeciesId = Guid.Parse(character.SpeciesId),
            BirthDate = character.BirthDate,
            Status = character.Status.ToString(),
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
            CharacterId = Guid.Parse(character.CharacterId),
            Name = character.Name,
            RealmId = Guid.Parse(character.RealmId),
            SpeciesId = Guid.Parse(character.SpeciesId),
            BirthDate = character.BirthDate,
            Status = character.Status.ToString(),
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
    /// Registers this service's API permissions with the Permissions service on startup.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Character service permissions...");
        try
        {
            await CharacterPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
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
/// </summary>
internal class CharacterModel
{
    public string CharacterId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RealmId { get; set; } = string.Empty;
    public string SpeciesId { get; set; } = string.Empty;
    public CharacterStatus Status { get; set; } = CharacterStatus.Alive;

    // Store as DateTimeOffset directly - lib-state handles serialization
    public DateTimeOffset BirthDate { get; set; }
    public DateTimeOffset? DeathDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

#endregion
