using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.CharacterEncounter.Caching;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

// Note: InternalsVisibleTo attribute is in AssemblyInfo.cs

namespace BeyondImmersion.BannouService.CharacterEncounter;

/// <summary>
/// Implementation of the CharacterEncounter service.
/// Tracks memorable interactions between characters to enable dialogue awareness,
/// grudges/alliances, quest hooks, and NPC memory.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// </remarks>
[BannouService("character-encounter", typeof(ICharacterEncounterService), lifetime: ServiceLifetime.Scoped)]
public partial class CharacterEncounterService : ICharacterEncounterService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<CharacterEncounterService> _logger;
    private readonly CharacterEncounterServiceConfiguration _configuration;
    private readonly ICharacterClient _characterClient;
    private readonly IEncounterDataCache _encounterDataCache;
    private readonly IResourceClient _resourceClient;
    private readonly ITelemetryProvider _telemetryProvider;

    // Key prefixes for different data types
    private const string ENCOUNTER_KEY_PREFIX = "enc-";
    private const string PERSPECTIVE_KEY_PREFIX = "pers-";
    private const string TYPE_KEY_PREFIX = "type-";
    private const string CHAR_INDEX_PREFIX = "char-idx-";
    private const string PAIR_INDEX_PREFIX = "pair-idx-";
    private const string LOCATION_INDEX_PREFIX = "loc-idx-";
    private const string GLOBAL_CHAR_INDEX_KEY = "global-char-idx";
    private const string CUSTOM_TYPE_INDEX_KEY = "custom-type-idx";
    private const string TYPE_ENCOUNTER_INDEX_PREFIX = "type-enc-idx-";
    private const string ENCOUNTER_PERSPECTIVE_INDEX_PREFIX = "enc-pers-idx-";

    // Event topics
    private const string ENCOUNTER_RECORDED_TOPIC = "encounter.recorded";
    private const string ENCOUNTER_MEMORY_FADED_TOPIC = "encounter.memory.faded";
    private const string ENCOUNTER_MEMORY_REFRESHED_TOPIC = "encounter.memory.refreshed";
    private const string ENCOUNTER_PERSPECTIVE_UPDATED_TOPIC = "encounter.perspective.updated";
    private const string ENCOUNTER_DELETED_TOPIC = "encounter.deleted";

    // Built-in encounter types
    private static readonly List<BuiltInEncounterType> BuiltInTypes = new()
    {
        new("COMBAT", "Combat", "Physical confrontation between characters", EmotionalImpact.ANGER, 10),
        new("DIALOGUE", "Dialogue", "Conversation or negotiation between characters", EmotionalImpact.INDIFFERENCE, 20),
        new("TRADE", "Trade", "Economic exchange between characters", EmotionalImpact.GRATITUDE, 30),
        new("QUEST", "Quest", "Shared objective completion", EmotionalImpact.RESPECT, 40),
        new("SOCIAL", "Social", "Casual social interaction", EmotionalImpact.AFFECTION, 50),
        new("CEREMONY", "Ceremony", "Formal event participation", EmotionalImpact.PRIDE, 60)
    };

    /// <summary>
    /// Initializes the CharacterEncounter service with required dependencies.
    /// </summary>
    public CharacterEncounterService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<CharacterEncounterService> logger,
        CharacterEncounterServiceConfiguration configuration,
        ICharacterClient characterClient,
        IEncounterDataCache encounterDataCache,
        IResourceClient resourceClient,
        ITelemetryProvider telemetryProvider)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        _characterClient = characterClient;
        _encounterDataCache = encounterDataCache;
        _resourceClient = resourceClient;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    // ============================================================================
    // Encounter Type Management
    // ============================================================================

    /// <summary>
    /// Creates a new custom encounter type.
    /// </summary>
    public async Task<(StatusCodes, EncounterTypeResponse?)> CreateEncounterTypeAsync(CreateEncounterTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating encounter type with code {Code}", body.Code);

        // Check if code is reserved (built-in)
        if (BuiltInTypes.Any(t => t.Code.Equals(body.Code, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Cannot create encounter type with reserved code {Code}", body.Code);
            return (StatusCodes.BadRequest, null);
        }

        var store = _stateStoreFactory.GetStore<EncounterTypeData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{TYPE_KEY_PREFIX}{body.Code.ToUpperInvariant()}";

        // Check if already exists
        var existing = await store.GetAsync(key, cancellationToken);
        if (existing != null)
        {
            _logger.LogWarning("Encounter type with code {Code} already exists", body.Code);
            return (StatusCodes.Conflict, null);
        }

        var typeId = Guid.NewGuid();
        var data = new EncounterTypeData
        {
            TypeId = typeId,
            Code = body.Code.ToUpperInvariant(),
            Name = body.Name,
            Description = body.Description,
            IsBuiltIn = false,
            DefaultEmotionalImpact = body.DefaultEmotionalImpact,
            SortOrder = body.SortOrder,
            IsActive = true,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await store.SaveAsync(key, data, cancellationToken: cancellationToken);

        // Add to custom type index for enumeration
        await AddToCustomTypeIndexAsync(body.Code.ToUpperInvariant(), cancellationToken);

        _logger.LogInformation("Created encounter type {Code} with ID {TypeId}", body.Code, typeId);
        return (StatusCodes.OK, MapToEncounterTypeResponse(data));
    }

    /// <summary>
    /// Retrieves an encounter type by its code.
    /// </summary>
    public async Task<(StatusCodes, EncounterTypeResponse?)> GetEncounterTypeAsync(GetEncounterTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting encounter type {Code}", body.Code);

        var store = _stateStoreFactory.GetStore<EncounterTypeData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{TYPE_KEY_PREFIX}{body.Code.ToUpperInvariant()}";
        var data = await store.GetAsync(key, cancellationToken);

        if (data == null)
        {
            // Check if it's a built-in type that needs seeding
            var builtIn = BuiltInTypes.FirstOrDefault(t => t.Code.Equals(body.Code, StringComparison.OrdinalIgnoreCase));
            if (builtIn != null)
            {
                // Auto-seed this built-in type
                data = await SeedBuiltInTypeAsync(store, builtIn, cancellationToken);
            }
        }

        if (data == null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, MapToEncounterTypeResponse(data));
    }

    /// <summary>
    /// Lists all encounter types with optional filtering.
    /// </summary>
    public async Task<(StatusCodes, EncounterTypeListResponse?)> ListEncounterTypesAsync(ListEncounterTypesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing encounter types");

        // Ensure built-in types are seeded
        await EnsureBuiltInTypesSeededAsync(cancellationToken);

        var store = _stateStoreFactory.GetStore<EncounterTypeData>(StateStoreDefinitions.CharacterEncounter);
        var types = new List<EncounterTypeResponse>();

        // Query all types with TYPE_KEY_PREFIX
        var allKeys = await GetAllTypeKeysAsync(store, cancellationToken);
        foreach (var key in allKeys)
        {
            var data = await store.GetAsync(key, cancellationToken);
            if (data == null) continue;

            // Apply filters
            if (!body.IncludeInactive && !data.IsActive) continue;
            if (body.BuiltInOnly && !data.IsBuiltIn) continue;
            if (body.CustomOnly && data.IsBuiltIn) continue;

            types.Add(MapToEncounterTypeResponse(data));
        }

        // Sort by sort order
        types = types.OrderBy(t => t.SortOrder).ThenBy(t => t.Code).ToList();

        return (StatusCodes.OK, new EncounterTypeListResponse
        {
            Types = types,
            TotalCount = types.Count
        });
    }

    /// <summary>
    /// Updates an existing encounter type.
    /// </summary>
    public async Task<(StatusCodes, EncounterTypeResponse?)> UpdateEncounterTypeAsync(UpdateEncounterTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating encounter type {Code}", body.Code);

        var store = _stateStoreFactory.GetStore<EncounterTypeData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{TYPE_KEY_PREFIX}{body.Code.ToUpperInvariant()}";
        var data = await store.GetAsync(key, cancellationToken);

        if (data == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Built-in types can only have description and defaultEmotionalImpact updated
        if (data.IsBuiltIn)
        {
            if (body.Name != null || body.SortOrder != null)
            {
                _logger.LogWarning("Cannot update name or sortOrder for built-in type {Code}", body.Code);
                return (StatusCodes.BadRequest, null);
            }
        }

        // Apply updates
        if (body.Name != null) data.Name = body.Name;
        if (body.Description != null) data.Description = body.Description;
        if (body.DefaultEmotionalImpact != null) data.DefaultEmotionalImpact = body.DefaultEmotionalImpact.Value;
        if (body.SortOrder != null) data.SortOrder = body.SortOrder.Value;

        await store.SaveAsync(key, data, cancellationToken: cancellationToken);

        _logger.LogInformation("Updated encounter type {Code}", body.Code);
        return (StatusCodes.OK, MapToEncounterTypeResponse(data));
    }

    /// <summary>
    /// Deletes a custom encounter type.
    /// </summary>
    public async Task<StatusCodes> DeleteEncounterTypeAsync(DeleteEncounterTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting encounter type {Code}", body.Code);

        var store = _stateStoreFactory.GetStore<EncounterTypeData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{TYPE_KEY_PREFIX}{body.Code.ToUpperInvariant()}";
        var data = await store.GetAsync(key, cancellationToken);

        if (data == null)
        {
            return StatusCodes.NotFound;
        }

        if (data.IsBuiltIn)
        {
            _logger.LogWarning("Cannot delete built-in type {Code}", body.Code);
            return StatusCodes.BadRequest;
        }

        // Check if type is in use by any encounters
        var encounterCount = await GetTypeEncounterCountAsync(body.Code.ToUpperInvariant(), cancellationToken);
        if (encounterCount > 0)
        {
            _logger.LogWarning("Cannot delete encounter type {Code}: {Count} encounters using it", body.Code, encounterCount);
            return StatusCodes.Conflict;
        }

        // Soft-delete by marking inactive
        data.IsActive = false;
        await store.SaveAsync(key, data, cancellationToken: cancellationToken);

        // Remove from custom type index
        await RemoveFromCustomTypeIndexAsync(body.Code.ToUpperInvariant(), cancellationToken);

        _logger.LogInformation("Deleted (deactivated) encounter type {Code}", body.Code);
        return StatusCodes.OK;
    }

    /// <summary>
    /// Seeds built-in encounter types.
    /// </summary>
    public async Task<(StatusCodes, SeedEncounterTypesResponse?)> SeedEncounterTypesAsync(SeedEncounterTypesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding encounter types, forceReset={ForceReset}", body.ForceReset);

        var store = _stateStoreFactory.GetStore<EncounterTypeData>(StateStoreDefinitions.CharacterEncounter);
        var created = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var builtIn in BuiltInTypes)
        {
            var key = $"{TYPE_KEY_PREFIX}{builtIn.Code}";
            var existing = await store.GetAsync(key, cancellationToken);

            if (existing == null)
            {
                await SeedBuiltInTypeAsync(store, builtIn, cancellationToken);
                created++;
            }
            else if (body.ForceReset)
            {
                // Reset to defaults
                existing.Name = builtIn.Name;
                existing.Description = builtIn.Description;
                existing.DefaultEmotionalImpact = builtIn.DefaultEmotionalImpact;
                existing.SortOrder = builtIn.SortOrder;
                existing.IsActive = true;
                await store.SaveAsync(key, existing, cancellationToken: cancellationToken);
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        _logger.LogInformation("Seed complete: created={Created}, updated={Updated}, skipped={Skipped}",
            created, updated, skipped);

        return (StatusCodes.OK, new SeedEncounterTypesResponse
        {
            Created = created,
            Updated = updated,
            Skipped = skipped
        });
    }

    // ============================================================================
    // Recording
    // ============================================================================

    /// <summary>
    /// Records a new encounter between characters.
    /// </summary>
    public async Task<(StatusCodes, EncounterResponse?)> RecordEncounterAsync(RecordEncounterRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Recording encounter of type {Type} between {Count} participants",
            body.EncounterTypeCode, body.ParticipantIds.Count);

        // Validate participant count bounds
        if (body.ParticipantIds.Count < 2)
        {
            _logger.LogWarning("Encounter requires at least 2 participants");
            return (StatusCodes.BadRequest, null);
        }

        if (body.ParticipantIds.Count > _configuration.MaxParticipantsPerEncounter)
        {
            _logger.LogWarning("Encounter has {Count} participants, exceeding limit of {Max}",
                body.ParticipantIds.Count, _configuration.MaxParticipantsPerEncounter);
            return (StatusCodes.BadRequest, null);
        }

        // Validate encounter type exists
        var typeStore = _stateStoreFactory.GetStore<EncounterTypeData>(StateStoreDefinitions.CharacterEncounter);
        var typeKey = $"{TYPE_KEY_PREFIX}{body.EncounterTypeCode.ToUpperInvariant()}";
        var typeData = await typeStore.GetAsync(typeKey, cancellationToken);

        if (typeData == null)
        {
            // Try to auto-seed if it's a built-in type
            var builtIn = BuiltInTypes.FirstOrDefault(t => t.Code.Equals(body.EncounterTypeCode, StringComparison.OrdinalIgnoreCase));
            if (builtIn != null)
            {
                typeData = await SeedBuiltInTypeAsync(typeStore, builtIn, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Encounter type {Type} not found", body.EncounterTypeCode);
                return (StatusCodes.BadRequest, null);
            }
        }

        if (!typeData.IsActive)
        {
            _logger.LogWarning("Encounter type {Type} is inactive", body.EncounterTypeCode);
            return (StatusCodes.BadRequest, null);
        }

        var participantIds = body.ParticipantIds.Distinct().ToList();

        // Check for duplicate encounter (same participants, type, timestamp within tolerance)
        if (await IsDuplicateEncounterAsync(participantIds, body.EncounterTypeCode.ToUpperInvariant(), body.Timestamp, cancellationToken))
        {
            _logger.LogWarning("Duplicate encounter detected for type {Type} with {Count} participants",
                body.EncounterTypeCode, participantIds.Count);
            return (StatusCodes.Conflict, null);
        }

        // Validate all participant characters exist
        if (!await ValidateCharactersExistAsync(participantIds, cancellationToken))
        {
            return (StatusCodes.NotFound, null);
        }

        var encounterId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Create encounter record
        var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);
        var encounterData = new EncounterData
        {
            EncounterId = encounterId,
            Timestamp = body.Timestamp.ToUnixTimeSeconds(),
            RealmId = body.RealmId,
            LocationId = body.LocationId,
            EncounterTypeCode = body.EncounterTypeCode.ToUpperInvariant(),
            Context = body.Context,
            Outcome = body.Outcome,
            ParticipantIds = participantIds,
            Metadata = body.Metadata,
            CreatedAtUnix = now.ToUnixTimeSeconds()
        };

        await encounterStore.SaveAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", encounterData, cancellationToken: cancellationToken);

        // Add to type-encounter index for type-in-use validation
        await AddToTypeEncounterIndexAsync(body.EncounterTypeCode.ToUpperInvariant(), encounterId, cancellationToken);

        // Create perspectives for each participant
        var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
        var perspectives = new List<EncounterPerspectiveModel>();

        var providedPerspectives = body.Perspectives?.ToDictionary(p => p.CharacterId) ?? new Dictionary<Guid, PerspectiveInput>();
        var defaultEmotionalImpact = typeData.DefaultEmotionalImpact
            ?? GetDefaultEmotionalImpactForOutcome(body.Outcome);

        foreach (var participantId in participantIds)
        {
            var perspectiveId = Guid.NewGuid();
            var hasProvidedPerspective = providedPerspectives.TryGetValue(participantId, out var provided);

            var perspectiveData = new PerspectiveData
            {
                PerspectiveId = perspectiveId,
                EncounterId = encounterId,
                CharacterId = participantId,
                EmotionalImpact = hasProvidedPerspective && provided != null
                    ? provided.EmotionalImpact
                    : defaultEmotionalImpact,
                SentimentShift = hasProvidedPerspective ? provided?.SentimentShift : GetDefaultSentimentShiftForOutcome(body.Outcome),
                MemoryStrength = (float)(hasProvidedPerspective ? provided?.MemoryStrength ?? _configuration.DefaultMemoryStrength : _configuration.DefaultMemoryStrength),
                RememberedAs = hasProvidedPerspective ? provided?.RememberedAs : null,
                LastDecayedAtUnix = null,
                CreatedAtUnix = now.ToUnixTimeSeconds(),
                UpdatedAtUnix = null
            };

            await perspectiveStore.SaveAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", perspectiveData, cancellationToken: cancellationToken);

            // Update character index
            await AddToCharacterIndexAsync(participantId, perspectiveId, cancellationToken);

            // Update encounter-perspective index for O(1) perspective lookup
            await AddToEncounterPerspectiveIndexAsync(encounterId, perspectiveId, cancellationToken);

            perspectives.Add(MapToPerspectiveModel(perspectiveData));
        }

        // Update pair indexes
        await UpdatePairIndexesAsync(participantIds, encounterId, cancellationToken);

        // Update location index if location provided
        if (body.LocationId.HasValue)
        {
            await AddToLocationIndexAsync(body.LocationId.Value, encounterId, cancellationToken);
        }

        // Enforce encounter limits by pruning oldest encounters
        foreach (var participantId in participantIds)
        {
            await PruneCharacterEncountersIfNeededAsync(participantId, cancellationToken);
        }
        await PrunePairEncountersIfNeededAsync(participantIds, cancellationToken);

        // Publish event - convert enum to string at API boundary for event schema
        await _messageBus.TryPublishAsync(ENCOUNTER_RECORDED_TOPIC, new EncounterRecordedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            EncounterId = encounterId,
            EncounterTypeCode = body.EncounterTypeCode.ToUpperInvariant(),
            Outcome = body.Outcome.ToString(),
            RealmId = body.RealmId,
            LocationId = body.LocationId,
            ParticipantIds = participantIds,
            Context = body.Context,
            EncounterTimestamp = body.Timestamp
        }, cancellationToken: cancellationToken);

        // Register character references with lib-resource for cleanup coordination
        foreach (var participantId in participantIds)
        {
            await RegisterCharacterReferenceAsync(encounterId.ToString(), participantId, cancellationToken);
        }

        // Invalidate encounter cache for all participants so Actor sees fresh data
        foreach (var participantId in participantIds)
        {
            _encounterDataCache.Invalidate(participantId);
        }

        _logger.LogInformation("Recorded encounter {EncounterId} with {Count} perspectives",
            encounterId, perspectives.Count);

        return (StatusCodes.OK, new EncounterResponse
        {
            Encounter = MapToEncounterModel(encounterData),
            Perspectives = perspectives
        });
    }

    // ============================================================================
    // Queries
    // ============================================================================

    /// <summary>
    /// Queries encounters for a specific character.
    /// </summary>
    public async Task<(StatusCodes, EncounterListResponse?)> QueryByCharacterAsync(QueryByCharacterRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying encounters for character {CharacterId}", body.CharacterId);

        {
            var perspectiveIds = await GetCharacterPerspectiveIdsAsync(body.CharacterId, cancellationToken);
            var pageSize = Math.Min(body.PageSize > 0 ? body.PageSize : _configuration.DefaultPageSize, _configuration.MaxPageSize);

            if (perspectiveIds.Count == 0)
            {
                return (StatusCodes.OK, new EncounterListResponse
                {
                    Encounters = new List<EncounterResponse>(),
                    TotalCount = 0,
                    Page = body.Page,
                    PageSize = pageSize,
                    HasNextPage = false,
                    HasPreviousPage = body.Page > 1
                });
            }

            // Bulk load all perspectives with parallel decay
            var allPerspectives = await BulkLoadPerspectivesWithDecayAsync(perspectiveIds, cancellationToken);

            // Filter by minimum memory strength
            var filteredPerspectives = body.MinimumMemoryStrength.HasValue
                ? allPerspectives.Where(p => p.MemoryStrength >= body.MinimumMemoryStrength.Value).ToList()
                : allPerspectives;

            if (filteredPerspectives.Count == 0)
            {
                return (StatusCodes.OK, new EncounterListResponse
                {
                    Encounters = new List<EncounterResponse>(),
                    TotalCount = 0,
                    Page = body.Page,
                    PageSize = pageSize,
                    HasNextPage = false,
                    HasPreviousPage = body.Page > 1
                });
            }

            // Collect unique encounter IDs
            var encounterIds = filteredPerspectives.Select(p => p.EncounterId).Distinct().ToList();

            // Bulk load all encounters
            var encountersDict = await BulkLoadEncountersAsync(encounterIds, cancellationToken);

            // Apply encounter-level filters
            var filteredEncounterIds = new List<Guid>();

            foreach (var encounterId in encounterIds)
            {
                if (!encountersDict.TryGetValue(encounterId, out var encounter)) continue;

                // Apply filters
                if (!string.IsNullOrEmpty(body.EncounterTypeCode) &&
                    !encounter.EncounterTypeCode.Equals(body.EncounterTypeCode, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (body.Outcome.HasValue && encounter.Outcome != body.Outcome.Value)
                    continue;

                var encounterTimestamp = DateTimeOffset.FromUnixTimeSeconds(encounter.Timestamp);
                if (body.FromTimestamp.HasValue && encounterTimestamp < body.FromTimestamp.Value)
                    continue;
                if (body.ToTimestamp.HasValue && encounterTimestamp > body.ToTimestamp.Value)
                    continue;

                filteredEncounterIds.Add(encounterId);
            }

            // Bulk load all perspectives for filtered encounters (eliminates N+1 pattern)
            var allPerspectivesDict = await BulkLoadAllEncounterPerspectivesAsync(filteredEncounterIds, cancellationToken);

            // Build response
            var encounters = new List<EncounterResponse>();
            foreach (var encounterId in filteredEncounterIds)
            {
                if (!encountersDict.TryGetValue(encounterId, out var encounter)) continue;

                var encounterPerspectives = allPerspectivesDict.TryGetValue(encounterId, out var perspectives)
                    ? perspectives
                    : new List<EncounterPerspectiveModel>();

                encounters.Add(new EncounterResponse
                {
                    Encounter = MapToEncounterModel(encounter),
                    Perspectives = encounterPerspectives
                });
            }

            // Sort by timestamp descending
            encounters = encounters.OrderByDescending(e => e.Encounter.Timestamp).ToList();

            // Paginate
            var page = body.Page;
            var totalCount = encounters.Count;
            var paged = encounters.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return (StatusCodes.OK, new EncounterListResponse
            {
                Encounters = paged,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasNextPage = page * pageSize < totalCount,
                HasPreviousPage = page > 1
            });
        }
    }

    /// <summary>
    /// Queries encounters between two specific characters.
    /// </summary>
    public async Task<(StatusCodes, EncounterListResponse?)> QueryBetweenAsync(QueryBetweenRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying encounters between {CharA} and {CharB}", body.CharacterIdA, body.CharacterIdB);

        var encounterIds = await GetPairEncounterIdsAsync(body.CharacterIdA, body.CharacterIdB, cancellationToken);
        var pageSize = Math.Min(body.PageSize > 0 ? body.PageSize : _configuration.DefaultPageSize, _configuration.MaxPageSize);

        if (encounterIds.Count == 0)
        {
            return (StatusCodes.OK, new EncounterListResponse
            {
                Encounters = new List<EncounterResponse>(),
                TotalCount = 0,
                Page = body.Page,
                PageSize = pageSize,
                HasNextPage = false,
                HasPreviousPage = body.Page > 1
            });
        }

        // Bulk load all encounters
        var encountersDict = await BulkLoadEncountersAsync(encounterIds, cancellationToken);

        // Apply type filter first (can do before loading perspectives)
        var typeFilteredEncounterIds = new List<Guid>();
        foreach (var encounterId in encounterIds)
        {
            if (!encountersDict.TryGetValue(encounterId, out var encounter)) continue;

            // Apply type filter
            if (!string.IsNullOrEmpty(body.EncounterTypeCode) &&
                !encounter.EncounterTypeCode.Equals(body.EncounterTypeCode, StringComparison.OrdinalIgnoreCase))
                continue;

            typeFilteredEncounterIds.Add(encounterId);
        }

        // Bulk load all perspectives for type-filtered encounters (eliminates N+1 pattern)
        var allPerspectivesDict = await BulkLoadAllEncounterPerspectivesAsync(typeFilteredEncounterIds, cancellationToken);

        // Build response with memory strength filter applied
        var encounters = new List<EncounterResponse>();
        foreach (var encounterId in typeFilteredEncounterIds)
        {
            if (!encountersDict.TryGetValue(encounterId, out var encounter)) continue;

            var perspectives = allPerspectivesDict.TryGetValue(encounterId, out var perspectivesList)
                ? perspectivesList
                : new List<EncounterPerspectiveModel>();

            // Apply memory strength filter
            if (body.MinimumMemoryStrength.HasValue)
            {
                var hasStrongMemory = perspectives.Any(p =>
                    (p.CharacterId == body.CharacterIdA || p.CharacterId == body.CharacterIdB) &&
                    p.MemoryStrength >= body.MinimumMemoryStrength.Value);
                if (!hasStrongMemory) continue;
            }

            encounters.Add(new EncounterResponse
            {
                Encounter = MapToEncounterModel(encounter),
                Perspectives = perspectives
            });
        }

        // Sort by timestamp descending
        encounters = encounters.OrderByDescending(e => e.Encounter.Timestamp).ToList();

        // Paginate
        var page = body.Page;
        var totalCount = encounters.Count;
        var paged = encounters.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return (StatusCodes.OK, new EncounterListResponse
        {
            Encounters = paged,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasNextPage = page * pageSize < totalCount,
            HasPreviousPage = page > 1
        });
    }

    /// <summary>
    /// Queries recent encounters at a location.
    /// </summary>
    public async Task<(StatusCodes, EncounterListResponse?)> QueryByLocationAsync(QueryByLocationRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying encounters at location {LocationId}", body.LocationId);

        var encounterIds = await GetLocationEncounterIdsAsync(body.LocationId, cancellationToken);
        var pageSize = Math.Min(body.PageSize > 0 ? body.PageSize : _configuration.DefaultPageSize, _configuration.MaxPageSize);

        if (encounterIds.Count == 0)
        {
            return (StatusCodes.OK, new EncounterListResponse
            {
                Encounters = new List<EncounterResponse>(),
                TotalCount = 0,
                Page = body.Page,
                PageSize = pageSize,
                HasNextPage = false,
                HasPreviousPage = body.Page > 1
            });
        }

        // Bulk load all encounters
        var encountersDict = await BulkLoadEncountersAsync(encounterIds, cancellationToken);

        // Apply encounter-level filters first
        var filteredEncounterIds = new List<Guid>();
        foreach (var encounterId in encounterIds)
        {
            if (!encountersDict.TryGetValue(encounterId, out var encounter)) continue;

            // Apply filters
            if (!string.IsNullOrEmpty(body.EncounterTypeCode) &&
                !encounter.EncounterTypeCode.Equals(body.EncounterTypeCode, StringComparison.OrdinalIgnoreCase))
                continue;

            var encounterTimestamp = DateTimeOffset.FromUnixTimeSeconds(encounter.Timestamp);
            if (body.FromTimestamp.HasValue && encounterTimestamp < body.FromTimestamp.Value)
                continue;

            filteredEncounterIds.Add(encounterId);
        }

        // Bulk load all perspectives for filtered encounters (eliminates N+1 pattern)
        var allPerspectivesDict = await BulkLoadAllEncounterPerspectivesAsync(filteredEncounterIds, cancellationToken);

        // Build response
        var encounters = new List<EncounterResponse>();
        foreach (var encounterId in filteredEncounterIds)
        {
            if (!encountersDict.TryGetValue(encounterId, out var encounter)) continue;

            var perspectives = allPerspectivesDict.TryGetValue(encounterId, out var perspectivesList)
                ? perspectivesList
                : new List<EncounterPerspectiveModel>();

            encounters.Add(new EncounterResponse
            {
                Encounter = MapToEncounterModel(encounter),
                Perspectives = perspectives
            });
        }

        // Sort by timestamp descending
        encounters = encounters.OrderByDescending(e => e.Encounter.Timestamp).ToList();

        // Paginate
        var page = body.Page;
        var totalCount = encounters.Count;
        var paged = encounters.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return (StatusCodes.OK, new EncounterListResponse
        {
            Encounters = paged,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            HasNextPage = page * pageSize < totalCount,
            HasPreviousPage = page > 1
        });
    }

    /// <summary>
    /// Checks if two characters have met.
    /// </summary>
    public async Task<(StatusCodes, HasMetResponse?)> HasMetAsync(HasMetRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking if {CharA} has met {CharB}", body.CharacterIdA, body.CharacterIdB);

        {
            var encounterIds = await GetPairEncounterIdsAsync(body.CharacterIdA, body.CharacterIdB, cancellationToken);

            return (StatusCodes.OK, new HasMetResponse
            {
                HasMet = encounterIds.Count > 0,
                EncounterCount = encounterIds.Count
            });
        }
    }

    /// <summary>
    /// Calculates aggregate sentiment toward another character.
    /// </summary>
    public async Task<(StatusCodes, SentimentResponse?)> GetSentimentAsync(GetSentimentRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting sentiment of {CharacterId} toward {TargetId}", body.CharacterId, body.TargetCharacterId);

        var encounterIds = await GetPairEncounterIdsAsync(body.CharacterId, body.TargetCharacterId, cancellationToken);

        if (encounterIds.Count == 0)
        {
            return (StatusCodes.OK, new SentimentResponse
            {
                CharacterId = body.CharacterId,
                TargetCharacterId = body.TargetCharacterId,
                Sentiment = 0,
                EncounterCount = 0,
                DominantEmotion = null
            });
        }

        // Collect all perspective IDs from all encounters (parallel index lookups)
        var indexTasks = encounterIds.Select(id => GetEncounterPerspectiveIdsAsync(id, cancellationToken));
        var perspectiveIdLists = await Task.WhenAll(indexTasks);
        var allPerspectiveIds = perspectiveIdLists.SelectMany(ids => ids).Distinct().ToList();

        // Bulk load all perspectives with parallel decay
        var allPerspectives = await BulkLoadPerspectivesWithDecayAsync(allPerspectiveIds, cancellationToken);

        // Filter to just the querying character's perspectives and aggregate
        var characterPerspectives = allPerspectives.Where(p => p.CharacterId == body.CharacterId).ToList();

        var totalSentiment = 0.0f;
        var totalWeight = 0.0f;
        var emotionCounts = new Dictionary<EmotionalImpact, int>();

        foreach (var perspective in characterPerspectives)
        {
            // Weight by memory strength
            var weight = perspective.MemoryStrength;
            var sentimentShift = perspective.SentimentShift ?? 0;

            totalSentiment += sentimentShift * weight;
            totalWeight += weight;

            // Track emotions
            if (!emotionCounts.ContainsKey(perspective.EmotionalImpact))
                emotionCounts[perspective.EmotionalImpact] = 0;
            emotionCounts[perspective.EmotionalImpact]++;
        }

        var aggregateSentiment = totalWeight > 0 ? totalSentiment / totalWeight : 0;
        var dominantEmotion = emotionCounts.Count > 0
            ? emotionCounts.OrderByDescending(kv => kv.Value).First().Key
            : (EmotionalImpact?)null;

        return (StatusCodes.OK, new SentimentResponse
        {
            CharacterId = body.CharacterId,
            TargetCharacterId = body.TargetCharacterId,
            Sentiment = Math.Clamp(aggregateSentiment, -1.0f, 1.0f),
            EncounterCount = encounterIds.Count,
            DominantEmotion = dominantEmotion
        });
    }

    /// <summary>
    /// Batch retrieves sentiment toward multiple targets.
    /// </summary>
    public async Task<(StatusCodes, BatchSentimentResponse?)> BatchGetSentimentAsync(BatchGetSentimentRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Batch getting sentiment for {CharacterId} toward {Count} targets",
            body.CharacterId, body.TargetCharacterIds.Count);

        if (body.TargetCharacterIds.Count > _configuration.MaxBatchSize)
        {
            _logger.LogWarning("Batch size {Size} exceeds maximum {Max}",
                body.TargetCharacterIds.Count, _configuration.MaxBatchSize);
            return (StatusCodes.BadRequest, null);
        }

        // Run sentiment calculations in parallel
        var sentimentTasks = body.TargetCharacterIds.Select(async targetId =>
        {
            var (status, sentiment) = await GetSentimentAsync(new GetSentimentRequest
            {
                CharacterId = body.CharacterId,
                TargetCharacterId = targetId
            }, cancellationToken);

            return (status, sentiment);
        });

        var results = await Task.WhenAll(sentimentTasks);

        var sentiments = results
            .Where(r => r.status == StatusCodes.OK && r.sentiment != null)
            .Select(r => r.sentiment!)
            .ToList();

        return (StatusCodes.OK, new BatchSentimentResponse
        {
            CharacterId = body.CharacterId,
            Sentiments = sentiments
        });
    }

    // ============================================================================
    // Perspectives
    // ============================================================================

    /// <summary>
    /// Gets a character's perspective on an encounter.
    /// </summary>
    public async Task<(StatusCodes, PerspectiveResponse?)> GetPerspectiveAsync(GetPerspectiveRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting perspective of {CharacterId} on encounter {EncounterId}",
            body.CharacterId, body.EncounterId);

        var perspective = await FindPerspectiveAsync(body.EncounterId, body.CharacterId, cancellationToken);
        if (perspective == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Apply lazy decay
        var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
        perspective = await ApplyLazyDecayAsync(perspectiveStore, perspective, cancellationToken);

        return (StatusCodes.OK, new PerspectiveResponse
        {
            Perspective = MapToPerspectiveModel(perspective)
        });
    }

    /// <summary>
    /// Updates a character's perspective on an encounter.
    /// </summary>
    public async Task<(StatusCodes, PerspectiveResponse?)> UpdatePerspectiveAsync(UpdatePerspectiveRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating perspective of {CharacterId} on encounter {EncounterId}",
            body.CharacterId, body.EncounterId);

        var found = await FindPerspectiveAsync(body.EncounterId, body.CharacterId, cancellationToken);
        if (found == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Re-load with ETag for concurrency safety
        var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
        var perspectiveKey = $"{PERSPECTIVE_KEY_PREFIX}{found.PerspectiveId}";
        var (perspective, etag) = await perspectiveStore.GetWithETagAsync(perspectiveKey, cancellationToken);
        if (perspective == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var previousEmotion = perspective.EmotionalImpact;
        var previousSentiment = perspective.SentimentShift;

        // Apply updates
        if (body.EmotionalImpact.HasValue) perspective.EmotionalImpact = body.EmotionalImpact.Value;
        if (body.SentimentShift.HasValue) perspective.SentimentShift = body.SentimentShift.Value;
        if (body.RememberedAs != null) perspective.RememberedAs = body.RememberedAs;
        perspective.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // GetWithETagAsync returns non-null etag for existing records;
        // coalesce satisfies compiler's nullable analysis (will never execute)
        var newEtag = await perspectiveStore.TrySaveAsync(perspectiveKey, perspective, etag ?? string.Empty, cancellationToken);
        if (newEtag == null)
        {
            _logger.LogWarning("Concurrent modification detected for perspective {PerspectiveId}", found.PerspectiveId);
            return (StatusCodes.Conflict, null);
        }

        // Publish event - convert enum to string at API boundary for event schema
        await _messageBus.TryPublishAsync(ENCOUNTER_PERSPECTIVE_UPDATED_TOPIC, new EncounterPerspectiveUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EncounterId = body.EncounterId,
            CharacterId = body.CharacterId,
            PerspectiveId = perspective.PerspectiveId,
            PreviousEmotionalImpact = previousEmotion,
            NewEmotionalImpact = body.EmotionalImpact,
            PreviousSentimentShift = previousSentiment,
            NewSentimentShift = body.SentimentShift
        }, cancellationToken: cancellationToken);

        // Invalidate encounter cache for the affected character
        _encounterDataCache.Invalidate(body.CharacterId);

        _logger.LogInformation("Updated perspective {PerspectiveId}", perspective.PerspectiveId);

        return (StatusCodes.OK, new PerspectiveResponse
        {
            Perspective = MapToPerspectiveModel(perspective)
        });
    }

    /// <summary>
    /// Refreshes memory strength for an encounter.
    /// </summary>
    public async Task<(StatusCodes, PerspectiveResponse?)> RefreshMemoryAsync(RefreshMemoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Refreshing memory of {CharacterId} for encounter {EncounterId}",
            body.CharacterId, body.EncounterId);

        var found = await FindPerspectiveAsync(body.EncounterId, body.CharacterId, cancellationToken);
        if (found == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Re-load with ETag for concurrency safety
        var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
        var perspectiveKey = $"{PERSPECTIVE_KEY_PREFIX}{found.PerspectiveId}";
        var (perspective, etag) = await perspectiveStore.GetWithETagAsync(perspectiveKey, cancellationToken);
        if (perspective == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var previousStrength = perspective.MemoryStrength;
        var boost = body.StrengthBoost ?? (float)_configuration.MemoryRefreshBoost;
        perspective.MemoryStrength = Math.Clamp(perspective.MemoryStrength + boost, 0f, 1f);
        perspective.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // GetWithETagAsync returns non-null etag for existing records;
        // coalesce satisfies compiler's nullable analysis (will never execute)
        var newEtag = await perspectiveStore.TrySaveAsync(perspectiveKey, perspective, etag ?? string.Empty, cancellationToken);
        if (newEtag == null)
        {
            _logger.LogWarning("Concurrent modification detected for perspective {PerspectiveId}", found.PerspectiveId);
            return (StatusCodes.Conflict, null);
        }

        // Publish event
        await _messageBus.TryPublishAsync(ENCOUNTER_MEMORY_REFRESHED_TOPIC, new EncounterMemoryRefreshedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EncounterId = body.EncounterId,
            CharacterId = body.CharacterId,
            PerspectiveId = perspective.PerspectiveId,
            PreviousStrength = previousStrength,
            NewStrength = perspective.MemoryStrength
        }, cancellationToken: cancellationToken);

        // Invalidate encounter cache for the affected character
        _encounterDataCache.Invalidate(body.CharacterId);

        _logger.LogDebug("Refreshed memory {PerspectiveId}: {OldStrength} -> {NewStrength}",
            perspective.PerspectiveId, previousStrength, perspective.MemoryStrength);

        return (StatusCodes.OK, new PerspectiveResponse
        {
            Perspective = MapToPerspectiveModel(perspective)
        });
    }

    // ============================================================================
    // Admin
    // ============================================================================

    /// <summary>
    /// Deletes an encounter and all its perspectives.
    /// </summary>
    public async Task<(StatusCodes, DeleteEncounterResponse?)> DeleteEncounterAsync(DeleteEncounterRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting encounter {EncounterId}", body.EncounterId);

        var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);
        var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{body.EncounterId}", cancellationToken);

        if (encounter == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var participantIds = encounter.ParticipantIds;

        // Delete all perspectives
        var perspectivesDeleted = await DeleteEncounterPerspectivesAsync(body.EncounterId, cancellationToken);

        // Delete encounter
        await encounterStore.DeleteAsync($"{ENCOUNTER_KEY_PREFIX}{body.EncounterId}", cancellationToken);

        // Update pair indexes
        await RemoveFromPairIndexesAsync(participantIds, body.EncounterId, cancellationToken);

        // Update location index
        if (encounter.LocationId.HasValue)
        {
            await RemoveFromLocationIndexAsync(encounter.LocationId.Value, body.EncounterId, cancellationToken);
        }

        // Remove from type-encounter index
        await RemoveFromTypeEncounterIndexAsync(encounter.EncounterTypeCode, body.EncounterId, cancellationToken);

        // Publish event
        await _messageBus.TryPublishAsync(ENCOUNTER_DELETED_TOPIC, new EncounterDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EncounterId = body.EncounterId,
            ParticipantIds = participantIds,
            PerspectivesDeleted = perspectivesDeleted,
            DeletedByCharacterCleanup = false,
            CleanupCharacterId = null
        }, cancellationToken: cancellationToken);

        // Unregister character references with lib-resource
        foreach (var participantId in participantIds)
        {
            await UnregisterCharacterReferenceAsync(body.EncounterId.ToString(), participantId, cancellationToken);
        }

        // Invalidate encounter cache for all participants
        foreach (var participantId in participantIds)
        {
            _encounterDataCache.Invalidate(participantId);
        }

        _logger.LogInformation("Deleted encounter {EncounterId} with {Count} perspectives",
            body.EncounterId, perspectivesDeleted);

        return (StatusCodes.OK, new DeleteEncounterResponse
        {
            EncounterId = body.EncounterId,
            PerspectivesDeleted = perspectivesDeleted
        });
    }

    /// <summary>
    /// Deletes all encounters involving a character.
    /// </summary>
    public async Task<(StatusCodes, DeleteByCharacterResponse?)> DeleteByCharacterAsync(DeleteByCharacterRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting all encounters for character {CharacterId}", body.CharacterId);

        {
            var perspectiveIds = await GetCharacterPerspectiveIdsAsync(body.CharacterId, cancellationToken);
            var encountersDeleted = 0;
            var perspectivesDeleted = 0;

            var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
            var processedEncounterIds = new HashSet<Guid>();

            foreach (var perspectiveId in perspectiveIds)
            {
                var perspective = await perspectiveStore.GetAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", cancellationToken);
                if (perspective == null) continue;

                // Delete this perspective
                await perspectiveStore.DeleteAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", cancellationToken);
                perspectivesDeleted++;

                // Remove from character index
                await RemoveFromCharacterIndexAsync(body.CharacterId, perspectiveId, cancellationToken);

                // Track encounter IDs to delete
                processedEncounterIds.Add(perspective.EncounterId);
            }

            // Delete encounters that this character was part of
            var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);
            var affectedCharacterIds = new HashSet<Guid> { body.CharacterId };
            foreach (var encounterId in processedEncounterIds)
            {
                var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
                if (encounter == null) continue;

                // Delete all other perspectives for this encounter
                var otherPerspectivesDeleted = await DeleteEncounterPerspectivesAsync(encounterId, cancellationToken);
                perspectivesDeleted += otherPerspectivesDeleted;

                // Delete the encounter
                await encounterStore.DeleteAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
                encountersDeleted++;

                // Update pair indexes
                var participantIds = encounter.ParticipantIds;
                foreach (var pid in participantIds) affectedCharacterIds.Add(pid);
                await RemoveFromPairIndexesAsync(participantIds, encounterId, cancellationToken);

                // Update location index
                if (encounter.LocationId.HasValue)
                {
                    await RemoveFromLocationIndexAsync(encounter.LocationId.Value, encounterId, cancellationToken);
                }

                // Remove from type-encounter index
                await RemoveFromTypeEncounterIndexAsync(encounter.EncounterTypeCode, encounterId, cancellationToken);

                // Publish event (+1 for target character's perspective deleted in first loop)
                await _messageBus.TryPublishAsync(ENCOUNTER_DELETED_TOPIC, new EncounterDeletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    EncounterId = encounterId,
                    ParticipantIds = participantIds,
                    PerspectivesDeleted = otherPerspectivesDeleted + 1,
                    DeletedByCharacterCleanup = true,
                    CleanupCharacterId = body.CharacterId
                }, cancellationToken: cancellationToken);
            }

            // Invalidate encounter cache for all affected characters
            foreach (var affectedId in affectedCharacterIds)
            {
                _encounterDataCache.Invalidate(affectedId);
            }

            _logger.LogInformation("Deleted {Encounters} encounters and {Perspectives} perspectives for character {CharacterId}",
                encountersDeleted, perspectivesDeleted, body.CharacterId);

            return (StatusCodes.OK, new DeleteByCharacterResponse
            {
                CharacterId = body.CharacterId,
                EncountersDeleted = encountersDeleted,
                PerspectivesDeleted = perspectivesDeleted
            });
        }
    }

    /// <summary>
    /// Triggers memory decay processing.
    /// </summary>
    public async Task<(StatusCodes, DecayMemoriesResponse?)> DecayMemoriesAsync(DecayMemoriesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing memory decay, characterId={CharacterId}, dryRun={DryRun}",
            body.CharacterId, body.DryRun);

        if (!_configuration.MemoryDecayEnabled)
        {
            _logger.LogInformation("Memory decay is disabled");
            return (StatusCodes.OK, new DecayMemoriesResponse
            {
                PerspectivesProcessed = 0,
                MemoriesFaded = 0,
                DryRun = body.DryRun
            });
        }

        var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
        var perspectivesProcessed = 0;
        var memoriesFaded = 0;
        var dryRun = body.DryRun;

        // Collect character IDs to process: either a single character or all via global index.
        // Per-character iteration keeps memory bounded to one character's perspectives at a time,
        // avoiding the previous pattern of loading ALL perspective IDs into a single list.
        IReadOnlyList<Guid> characterIds;
        if (body.CharacterId.HasValue)
        {
            characterIds = [body.CharacterId.Value];
        }
        else
        {
            var globalIndexStore = _stateStoreFactory.GetStore<GlobalCharacterIndexData>(StateStoreDefinitions.CharacterEncounter);
            var globalIndex = await globalIndexStore.GetAsync(GLOBAL_CHAR_INDEX_KEY, cancellationToken);
            characterIds = globalIndex?.CharacterIds ?? [];
        }

        foreach (var characterId in characterIds)
        {
            var perspectiveIds = await GetCharacterPerspectiveIdsAsync(characterId, cancellationToken);

            foreach (var perspectiveId in perspectiveIds)
            {
                var perspectiveKey = $"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}";
                var (perspective, pEtag) = await perspectiveStore.GetWithETagAsync(perspectiveKey, cancellationToken);
                if (perspective == null) continue;

                var (decayed, faded) = CalculateDecay(perspective);

                if (decayed)
                {
                    perspectivesProcessed++;
                    var decayAmount = GetDecayAmount(perspective);
                    var previousStrength = perspective.MemoryStrength;

                    if (!dryRun)
                    {
                        perspective.MemoryStrength = Math.Max(0, previousStrength - decayAmount);
                        perspective.LastDecayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var decayResult = await perspectiveStore.TrySaveAsync(perspectiveKey, perspective, pEtag ?? string.Empty, cancellationToken);
                        if (decayResult == null)
                        {
                            _logger.LogWarning("Concurrent modification during decay for perspective {PerspectiveId}, skipping", perspectiveId);
                            continue;
                        }
                    }

                    if (faded)
                    {
                        memoriesFaded++;

                        if (!dryRun)
                        {
                            await _messageBus.TryPublishAsync(ENCOUNTER_MEMORY_FADED_TOPIC, new EncounterMemoryFadedEvent
                            {
                                EventId = Guid.NewGuid(),
                                Timestamp = DateTimeOffset.UtcNow,
                                EncounterId = perspective.EncounterId,
                                CharacterId = perspective.CharacterId,
                                PerspectiveId = perspective.PerspectiveId,
                                PreviousStrength = previousStrength,
                                NewStrength = perspective.MemoryStrength,
                                FadeThreshold = (float)_configuration.MemoryFadeThreshold
                            }, cancellationToken: cancellationToken);
                        }
                    }
                }
            }
        }

        _logger.LogInformation("Decay complete: processed={Processed}, faded={Faded}, dryRun={DryRun}",
            perspectivesProcessed, memoriesFaded, dryRun);

        return (StatusCodes.OK, new DecayMemoriesResponse
        {
            PerspectivesProcessed = perspectivesProcessed,
            MemoriesFaded = memoriesFaded,
            DryRun = dryRun
        });
    }

    // ============================================================================
    // Compression Methods
    // ============================================================================

    /// <summary>
    /// Gets encounter data for compression during character archival.
    /// Called by Resource service during character compression via compression callback.
    /// </summary>
    public async Task<(StatusCodes, CharacterEncounterArchive?)> GetCompressDataAsync(
        GetCompressDataRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting compress data for character {CharacterId}", body.CharacterId);

        {
            // Get all perspective IDs for this character
            var perspectiveIds = await GetCharacterPerspectiveIdsAsync(body.CharacterId, cancellationToken);

            if (perspectiveIds.Count == 0)
            {
                _logger.LogDebug("No encounter data found for character {CharacterId}", body.CharacterId);
                return (StatusCodes.NotFound, null);
            }

            var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);
            var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
            var encounters = new List<EncounterResponse>();
            var processedEncounterIds = new HashSet<Guid>();

            // Collect unique encounters this character participated in
            foreach (var perspectiveId in perspectiveIds)
            {
                var perspective = await perspectiveStore.GetAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", cancellationToken);
                if (perspective == null) continue;

                // Skip if we already processed this encounter
                if (!processedEncounterIds.Add(perspective.EncounterId)) continue;

                var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{perspective.EncounterId}", cancellationToken);
                if (encounter == null) continue;

                // Get all perspectives for this encounter (all participants)
                var allPerspectives = await GetEncounterPerspectivesAsync(encounter.EncounterId, cancellationToken);

                encounters.Add(new EncounterResponse
                {
                    Encounter = MapToEncounterModel(encounter),
                    Perspectives = allPerspectives
                });
            }

            // Sort by timestamp descending
            encounters = encounters.OrderByDescending(e => e.Encounter.Timestamp).ToList();

            // Compute aggregate sentiment towards other characters
            var aggregateSentiment = new Dictionary<string, float>();
            foreach (var perspectiveId in perspectiveIds)
            {
                var perspective = await perspectiveStore.GetAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", cancellationToken);
                if (perspective?.SentimentShift == null) continue;

                var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{perspective.EncounterId}", cancellationToken);
                if (encounter == null) continue;

                // Aggregate sentiment towards other participants
                foreach (var participantId in encounter.ParticipantIds)
                {
                    if (participantId == body.CharacterId) continue;

                    var key = participantId.ToString();
                    if (!aggregateSentiment.TryGetValue(key, out var current))
                    {
                        current = 0f;
                    }
                    aggregateSentiment[key] = current + perspective.SentimentShift.Value;
                }
            }

            var response = new CharacterEncounterArchive
            {
                // ResourceArchiveBase fields
                ResourceId = body.CharacterId,
                ResourceType = "character-encounter",
                ArchivedAt = DateTimeOffset.UtcNow,
                SchemaVersion = 1,
                // Service-specific fields
                CharacterId = body.CharacterId,
                HasEncounters = encounters.Count > 0,
                EncounterCount = encounters.Count,
                Encounters = encounters,
                AggregateSentiment = aggregateSentiment.Count > 0 ? aggregateSentiment : null
            };

            _logger.LogInformation(
                "Compress data retrieved for character {CharacterId}: encounters={EncounterCount}",
                body.CharacterId, encounters.Count);

            return (StatusCodes.OK, response);
        }
    }

    /// <summary>
    /// Restores encounter data from a compressed archive.
    /// Called by Resource service during character decompression via decompression callback.
    /// </summary>
    public async Task<(StatusCodes, RestoreFromArchiveResponse?)> RestoreFromArchiveAsync(
        RestoreFromArchiveRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Restoring encounter data from archive for character {CharacterId}", body.CharacterId);

        var encountersRestored = 0;
        var perspectivesRestored = 0;

        // Decompress the archive data
        CharacterEncounterArchive archiveData;
        try
        {
            var compressedBytes = Convert.FromBase64String(body.Data);
            var jsonData = DecompressJsonData(compressedBytes);
            archiveData = BannouJson.Deserialize<CharacterEncounterArchive>(jsonData)
                ?? throw new InvalidOperationException("Deserialized archive data is null");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decompress archive data for character {CharacterId}", body.CharacterId);
            return (StatusCodes.BadRequest, new RestoreFromArchiveResponse
            {
                CharacterId = body.CharacterId,
                EncountersRestored = 0,
                PerspectivesRestored = 0,
                Success = false,
                ErrorMessage = $"Invalid archive data: {ex.Message}"
            });
        }

        // Restore encounters and perspectives
        if (archiveData.HasEncounters && archiveData.Encounters.Count > 0)
        {
            var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);
            var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);

            foreach (var encounterResponse in archiveData.Encounters)
            {
                var encounterModel = encounterResponse.Encounter;

                // Check if encounter already exists
                var existingEncounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterModel.EncounterId}", cancellationToken);
                if (existingEncounter == null)
                {
                    // Create the encounter
                    var encounterData = new EncounterData
                    {
                        EncounterId = encounterModel.EncounterId,
                        Timestamp = encounterModel.Timestamp.ToUnixTimeSeconds(),
                        RealmId = encounterModel.RealmId,
                        LocationId = encounterModel.LocationId,
                        EncounterTypeCode = encounterModel.EncounterTypeCode,
                        Context = encounterModel.Context,
                        Outcome = encounterModel.Outcome,
                        ParticipantIds = encounterModel.ParticipantIds.ToList(),
                        Metadata = encounterModel.Metadata,
                        CreatedAtUnix = encounterModel.CreatedAt.ToUnixTimeSeconds()
                    };

                    await encounterStore.SaveAsync($"{ENCOUNTER_KEY_PREFIX}{encounterModel.EncounterId}", encounterData, cancellationToken: cancellationToken);

                    // Update pair indexes for new encounter
                    await UpdatePairIndexesAsync(encounterData.ParticipantIds, encounterModel.EncounterId, cancellationToken);

                    // Update location index if location provided
                    if (encounterModel.LocationId.HasValue)
                    {
                        await AddToLocationIndexAsync(encounterModel.LocationId.Value, encounterModel.EncounterId, cancellationToken);
                    }

                    // Update type-encounter index
                    await AddToTypeEncounterIndexAsync(encounterModel.EncounterTypeCode, encounterModel.EncounterId, cancellationToken);

                    encountersRestored++;
                }

                // Restore perspectives for this encounter (only for the archived character)
                foreach (var perspectiveModel in encounterResponse.Perspectives)
                {
                    // Only restore perspectives for the character being restored
                    if (perspectiveModel.CharacterId != body.CharacterId) continue;

                    // Check if perspective already exists
                    var existingPerspective = await perspectiveStore.GetAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveModel.PerspectiveId}", cancellationToken);
                    if (existingPerspective != null) continue;

                    var perspectiveData = new PerspectiveData
                    {
                        PerspectiveId = perspectiveModel.PerspectiveId,
                        EncounterId = perspectiveModel.EncounterId,
                        CharacterId = perspectiveModel.CharacterId,
                        EmotionalImpact = perspectiveModel.EmotionalImpact,
                        SentimentShift = perspectiveModel.SentimentShift,
                        MemoryStrength = perspectiveModel.MemoryStrength,
                        RememberedAs = perspectiveModel.RememberedAs,
                        LastDecayedAtUnix = perspectiveModel.LastDecayedAt?.ToUnixTimeSeconds(),
                        CreatedAtUnix = perspectiveModel.CreatedAt.ToUnixTimeSeconds(),
                        UpdatedAtUnix = perspectiveModel.UpdatedAt?.ToUnixTimeSeconds()
                    };

                    await perspectiveStore.SaveAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveModel.PerspectiveId}", perspectiveData, cancellationToken: cancellationToken);

                    // Update character index
                    await AddToCharacterIndexAsync(body.CharacterId, perspectiveModel.PerspectiveId, cancellationToken);

                    // Update encounter-perspective index for O(1) perspective lookup by encounter
                    await AddToEncounterPerspectiveIndexAsync(perspectiveModel.EncounterId, perspectiveModel.PerspectiveId, cancellationToken);

                    perspectivesRestored++;
                }
            }

            _logger.LogInformation(
                "Restored {EncounterCount} encounters and {PerspectiveCount} perspectives for character {CharacterId}",
                encountersRestored, perspectivesRestored, body.CharacterId);
        }

        return (StatusCodes.OK, new RestoreFromArchiveResponse
        {
            CharacterId = body.CharacterId,
            EncountersRestored = encountersRestored,
            PerspectivesRestored = perspectivesRestored,
            Success = true
        });
    }

    /// <summary>
    /// Decompresses gzipped JSON data.
    /// </summary>
    private static string DecompressJsonData(byte[] compressedData)
    {
        using var input = new System.IO.MemoryStream(compressedData);
        using var gzip = new System.IO.Compression.GZipStream(
            input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new System.IO.MemoryStream();
        gzip.CopyTo(output);
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    // ============================================================================
    // Permission Registration
    // ============================================================================

    // ============================================================================
    // Helper Methods
    // ============================================================================

    private async Task<EncounterTypeData> SeedBuiltInTypeAsync(IStateStore<EncounterTypeData> store, BuiltInEncounterType builtIn, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.SeedBuiltInTypeAsync");
        var data = new EncounterTypeData
        {
            TypeId = Guid.NewGuid(),
            Code = builtIn.Code,
            Name = builtIn.Name,
            Description = builtIn.Description,
            IsBuiltIn = true,
            DefaultEmotionalImpact = builtIn.DefaultEmotionalImpact,
            SortOrder = builtIn.SortOrder,
            IsActive = true,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await store.SaveAsync($"{TYPE_KEY_PREFIX}{builtIn.Code}", data, cancellationToken: cancellationToken);
        return data;
    }

    private async Task EnsureBuiltInTypesSeededAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.EnsureBuiltInTypesSeededAsync");
        if (!_configuration.SeedBuiltInTypesOnStartup) return;

        var store = _stateStoreFactory.GetStore<EncounterTypeData>(StateStoreDefinitions.CharacterEncounter);
        foreach (var builtIn in BuiltInTypes)
        {
            var key = $"{TYPE_KEY_PREFIX}{builtIn.Code}";
            var existing = await store.GetAsync(key, cancellationToken);
            if (existing == null)
            {
                await SeedBuiltInTypeAsync(store, builtIn, cancellationToken);
            }
        }
    }

    private async Task<List<string>> GetAllTypeKeysAsync(IStateStore<EncounterTypeData> store, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetAllTypeKeysAsync");
        var keys = new List<string>();

        // Add built-in type keys
        foreach (var builtIn in BuiltInTypes)
        {
            keys.Add($"{TYPE_KEY_PREFIX}{builtIn.Code}");
        }

        // Add custom type keys from the index
        var customTypeIndexStore = _stateStoreFactory.GetStore<CustomTypeIndexData>(StateStoreDefinitions.CharacterEncounter);
        var customTypeIndex = await customTypeIndexStore.GetAsync(CUSTOM_TYPE_INDEX_KEY, cancellationToken);

        if (customTypeIndex != null)
        {
            foreach (var typeCode in customTypeIndex.TypeCodes)
            {
                keys.Add($"{TYPE_KEY_PREFIX}{typeCode}");
            }
        }

        return keys;
    }

    private async Task<List<Guid>> GetCharacterPerspectiveIdsAsync(Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetCharacterPerspectiveIdsAsync");
        var indexStore = _stateStoreFactory.GetStore<CharacterIndexData>(StateStoreDefinitions.CharacterEncounter);
        var index = await indexStore.GetAsync($"{CHAR_INDEX_PREFIX}{characterId}", cancellationToken);
        return index?.PerspectiveIds.ToList() ?? new List<Guid>();
    }

    private async Task<List<Guid>> GetPairEncounterIdsAsync(Guid charA, Guid charB, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetPairEncounterIdsAsync");
        var pairKey = GetPairKey(charA, charB);
        var indexStore = _stateStoreFactory.GetStore<PairIndexData>(StateStoreDefinitions.CharacterEncounter);
        var index = await indexStore.GetAsync($"{PAIR_INDEX_PREFIX}{pairKey}", cancellationToken);
        return index?.EncounterIds.ToList() ?? new List<Guid>();
    }

    private async Task<List<Guid>> GetLocationEncounterIdsAsync(Guid locationId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetLocationEncounterIdsAsync");
        var indexStore = _stateStoreFactory.GetStore<LocationIndexData>(StateStoreDefinitions.CharacterEncounter);
        var index = await indexStore.GetAsync($"{LOCATION_INDEX_PREFIX}{locationId}", cancellationToken);
        return index?.EncounterIds.ToList() ?? new List<Guid>();
    }

    /// <summary>
    /// Checks if a duplicate encounter already exists with the same participants, type, and timestamp within tolerance.
    /// </summary>
    private async Task<bool> IsDuplicateEncounterAsync(
        List<Guid> participantIds,
        string encounterTypeCode,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.IsDuplicateEncounterAsync");
        var toleranceMinutes = _configuration.DuplicateTimestampToleranceMinutes;
        var sortedParticipants = participantIds.OrderBy(id => id).ToList();
        var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);

        // Collect candidate encounter IDs from pair indexes (any pair will do for duplicate check)
        var candidateEncounterIds = new HashSet<Guid>();

        // Check first pair only for efficiency - if a duplicate exists, it will be in this pair's index
        if (sortedParticipants.Count >= 2)
        {
            var pairEncounterIds = await GetPairEncounterIdsAsync(sortedParticipants[0], sortedParticipants[1], cancellationToken);
            foreach (var encounterId in pairEncounterIds)
            {
                candidateEncounterIds.Add(encounterId);
            }
        }

        // Check each candidate for duplicate match
        foreach (var encounterId in candidateEncounterIds)
        {
            var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
            if (encounter == null)
            {
                continue;
            }

            // Check type match
            if (!encounter.EncounterTypeCode.Equals(encounterTypeCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Check exact participant match (sorted comparison)
            var existingParticipants = encounter.ParticipantIds.OrderBy(id => id).ToList();
            if (!sortedParticipants.SequenceEqual(existingParticipants))
            {
                continue;
            }

            // Check timestamp within tolerance
            var existingTimestamp = DateTimeOffset.FromUnixTimeSeconds(encounter.Timestamp);
            var timeDiff = Math.Abs((timestamp - existingTimestamp).TotalMinutes);
            if (timeDiff <= toleranceMinutes)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Validates that all character IDs exist via the Character service.
    /// </summary>
    private async Task<bool> ValidateCharactersExistAsync(List<Guid> characterIds, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.ValidateCharactersExistAsync");
        var validationTasks = characterIds.Select(async characterId =>
        {
            try
            {
                await _characterClient.GetCharacterAsync(new GetCharacterRequest { CharacterId = characterId }, cancellationToken);
                return true;
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                _logger.LogWarning("Character {CharacterId} not found during encounter recording", characterId);
                return false;
            }
        });

        var results = await Task.WhenAll(validationTasks);
        return results.All(exists => exists);
    }

    private async Task AddToCharacterIndexAsync(Guid characterId, Guid perspectiveId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.AddToCharacterIndexAsync");
        var indexStore = _stateStoreFactory.GetStore<CharacterIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{CHAR_INDEX_PREFIX}{characterId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            var isNewCharacter = index == null;

            index ??= new CharacterIndexData { CharacterId = characterId };

            if (!index.PerspectiveIds.Contains(perspectiveId))
            {
                index.PerspectiveIds.Add(perspectiveId);
                // etag is null when key doesn't exist yet; empty string signals
                // "create new" to TrySaveAsync (will never conflict on new entries)
                var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification on character index {CharacterId}, retrying (attempt {Attempt})",
                        characterId, attempt + 1);
                    continue;
                }
            }

            // Add to global character index if this is the character's first perspective
            if (isNewCharacter)
            {
                await AddToGlobalCharacterIndexAsync(characterId, cancellationToken);
            }

            return;
        }

        _logger.LogWarning("Failed to add perspective {PerspectiveId} to character index {CharacterId} after 3 attempts",
            perspectiveId, characterId);
    }

    private async Task AddToGlobalCharacterIndexAsync(Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.AddToGlobalCharacterIndexAsync");
        var globalIndexStore = _stateStoreFactory.GetStore<GlobalCharacterIndexData>(StateStoreDefinitions.CharacterEncounter);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (globalIndex, etag) = await globalIndexStore.GetWithETagAsync(GLOBAL_CHAR_INDEX_KEY, cancellationToken);
            globalIndex ??= new GlobalCharacterIndexData();

            if (!globalIndex.CharacterIds.Contains(characterId))
            {
                globalIndex.CharacterIds.Add(characterId);
                // etag is null when key doesn't exist yet; empty string signals
                // "create new" to TrySaveAsync (will never conflict on new entries)
                var saveResult = await globalIndexStore.TrySaveAsync(GLOBAL_CHAR_INDEX_KEY, globalIndex, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification on global character index, retrying (attempt {Attempt})", attempt + 1);
                    continue;
                }
            }

            return;
        }

        _logger.LogWarning("Failed to add character {CharacterId} to global character index after 3 attempts", characterId);
    }

    private async Task RemoveFromCharacterIndexAsync(Guid characterId, Guid perspectiveId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromCharacterIndexAsync");
        var indexStore = _stateStoreFactory.GetStore<CharacterIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{CHAR_INDEX_PREFIX}{characterId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            if (index == null)
            {
                return;
            }

            index.PerspectiveIds.Remove(perspectiveId);
            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification on character index {CharacterId} during remove, retrying (attempt {Attempt})",
                    characterId, attempt + 1);
                continue;
            }

            // Remove from global index if this was the character's last perspective
            if (index.PerspectiveIds.Count == 0)
            {
                await RemoveFromGlobalCharacterIndexAsync(characterId, cancellationToken);
            }

            return;
        }

        _logger.LogWarning("Failed to remove perspective {PerspectiveId} from character index {CharacterId} after 3 attempts",
            perspectiveId, characterId);
    }

    private async Task RemoveFromGlobalCharacterIndexAsync(Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromGlobalCharacterIndexAsync");
        var globalIndexStore = _stateStoreFactory.GetStore<GlobalCharacterIndexData>(StateStoreDefinitions.CharacterEncounter);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (globalIndex, etag) = await globalIndexStore.GetWithETagAsync(GLOBAL_CHAR_INDEX_KEY, cancellationToken);
            if (globalIndex == null)
            {
                return;
            }

            globalIndex.CharacterIds.Remove(characterId);
            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await globalIndexStore.TrySaveAsync(GLOBAL_CHAR_INDEX_KEY, globalIndex, etag ?? string.Empty, cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification on global character index during remove, retrying (attempt {Attempt})", attempt + 1);
                continue;
            }

            return;
        }

        _logger.LogWarning("Failed to remove character {CharacterId} from global character index after 3 attempts", characterId);
    }

    private async Task AddToCustomTypeIndexAsync(string typeCode, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.AddToCustomTypeIndexAsync");
        var indexStore = _stateStoreFactory.GetStore<CustomTypeIndexData>(StateStoreDefinitions.CharacterEncounter);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(CUSTOM_TYPE_INDEX_KEY, cancellationToken);
            index ??= new CustomTypeIndexData();

            if (!index.TypeCodes.Contains(typeCode))
            {
                index.TypeCodes.Add(typeCode);
                // etag is null when key doesn't exist yet; empty string signals
                // "create new" to TrySaveAsync (will never conflict on new entries)
                var saveResult = await indexStore.TrySaveAsync(CUSTOM_TYPE_INDEX_KEY, index, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification on custom type index, retrying (attempt {Attempt})", attempt + 1);
                    continue;
                }
            }

            return;
        }

        _logger.LogWarning("Failed to add type code {TypeCode} to custom type index after 3 attempts", typeCode);
    }

    private async Task RemoveFromCustomTypeIndexAsync(string typeCode, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromCustomTypeIndexAsync");
        var indexStore = _stateStoreFactory.GetStore<CustomTypeIndexData>(StateStoreDefinitions.CharacterEncounter);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(CUSTOM_TYPE_INDEX_KEY, cancellationToken);
            if (index == null)
            {
                return;
            }

            index.TypeCodes.Remove(typeCode);
            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await indexStore.TrySaveAsync(CUSTOM_TYPE_INDEX_KEY, index, etag ?? string.Empty, cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification on custom type index during remove, retrying (attempt {Attempt})", attempt + 1);
                continue;
            }

            return;
        }

        _logger.LogWarning("Failed to remove type code {TypeCode} from custom type index after 3 attempts", typeCode);
    }

    /// <summary>
    /// Adds an encounter ID to the type-encounter index for the given encounter type code.
    /// Uses optimistic concurrency with ETag-based retry pattern.
    /// </summary>
    private async Task AddToTypeEncounterIndexAsync(string typeCode, Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.AddToTypeEncounterIndexAsync");
        var indexStore = _stateStoreFactory.GetStore<TypeEncounterIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{TYPE_ENCOUNTER_INDEX_PREFIX}{typeCode}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            index ??= new TypeEncounterIndexData { TypeCode = typeCode };

            if (!index.EncounterIds.Contains(encounterId))
            {
                index.EncounterIds.Add(encounterId);
                // etag is null when key doesn't exist yet; empty string signals
                // "create new" to TrySaveAsync (will never conflict on new entries)
                var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification on type-encounter index {TypeCode}, retrying (attempt {Attempt})",
                        typeCode, attempt + 1);
                    continue;
                }
            }

            return;
        }

        _logger.LogWarning("Failed to add encounter {EncounterId} to type-encounter index {TypeCode} after 3 attempts",
            encounterId, typeCode);
    }

    /// <summary>
    /// Removes an encounter ID from the type-encounter index for the given encounter type code.
    /// Uses optimistic concurrency with ETag-based retry pattern.
    /// </summary>
    private async Task RemoveFromTypeEncounterIndexAsync(string typeCode, Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromTypeEncounterIndexAsync");
        var indexStore = _stateStoreFactory.GetStore<TypeEncounterIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{TYPE_ENCOUNTER_INDEX_PREFIX}{typeCode}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            if (index == null)
            {
                return;
            }

            index.EncounterIds.Remove(encounterId);
            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification on type-encounter index {TypeCode} during remove, retrying (attempt {Attempt})",
                    typeCode, attempt + 1);
                continue;
            }

            return;
        }

        _logger.LogWarning("Failed to remove encounter {EncounterId} from type-encounter index {TypeCode} after 3 attempts",
            encounterId, typeCode);
    }

    /// <summary>
    /// Gets the count of encounters using the given encounter type code.
    /// </summary>
    private async Task<int> GetTypeEncounterCountAsync(string typeCode, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetTypeEncounterCountAsync");
        var indexStore = _stateStoreFactory.GetStore<TypeEncounterIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{TYPE_ENCOUNTER_INDEX_PREFIX}{typeCode}";
        var index = await indexStore.GetAsync(key, cancellationToken);
        return index?.EncounterIds.Count ?? 0;
    }

    /// <summary>
    /// Prunes encounters for a character if they exceed MaxEncountersPerCharacter.
    /// Removes oldest encounters (by timestamp) first.
    /// </summary>
    private async Task PruneCharacterEncountersIfNeededAsync(Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.PruneCharacterEncountersIfNeededAsync");
        var perspectiveIds = await GetCharacterPerspectiveIdsAsync(characterId, cancellationToken);
        if (perspectiveIds.Count <= _configuration.MaxEncountersPerCharacter)
        {
            return;
        }

        var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
        var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);

        // Load all perspectives with their encounter timestamps
        var perspectivesWithTimestamp = new List<(Guid perspectiveId, Guid encounterId, long timestamp)>();
        foreach (var perspectiveId in perspectiveIds)
        {
            var perspective = await perspectiveStore.GetAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", cancellationToken);
            if (perspective == null) continue;

            var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{perspective.EncounterId}", cancellationToken);
            if (encounter == null) continue;

            perspectivesWithTimestamp.Add((perspectiveId, perspective.EncounterId, encounter.Timestamp));
        }

        // Sort by timestamp ascending (oldest first)
        var sorted = perspectivesWithTimestamp.OrderBy(p => p.timestamp).ToList();

        // Calculate how many to remove
        var toRemove = sorted.Count - _configuration.MaxEncountersPerCharacter;
        if (toRemove <= 0) return;

        _logger.LogInformation("Pruning {Count} oldest encounters for character {CharacterId}", toRemove, characterId);

        // Remove the oldest perspectives
        for (var i = 0; i < toRemove; i++)
        {
            var (perspectiveId, encounterId, _) = sorted[i];

            // Delete the perspective
            await perspectiveStore.DeleteAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", cancellationToken);
            await RemoveFromCharacterIndexAsync(characterId, perspectiveId, cancellationToken);

            // Check if this encounter has any remaining perspectives
            var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
            if (encounter != null)
            {
                var remainingPerspectives = await GetEncounterPerspectivesAsync(encounterId, cancellationToken);
                if (remainingPerspectives.Count == 0)
                {
                    // No perspectives left - delete the encounter
                    await encounterStore.DeleteAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);

                    // Clean up pair indexes
                    var participantIds = encounter.ParticipantIds;
                    await RemoveFromPairIndexesAsync(participantIds, encounterId, cancellationToken);

                    // Clean up location index
                    if (encounter.LocationId.HasValue)
                    {
                        await RemoveFromLocationIndexAsync(encounter.LocationId.Value, encounterId, cancellationToken);
                    }

                    // Clean up type-encounter index
                    await RemoveFromTypeEncounterIndexAsync(encounter.EncounterTypeCode, encounterId, cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Prunes encounters between character pairs if they exceed MaxEncountersPerPair.
    /// Removes oldest encounters (by timestamp) first.
    /// </summary>
    private async Task PrunePairEncountersIfNeededAsync(List<Guid> participantIds, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.PrunePairEncountersIfNeededAsync");
        var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);

        // Check each unique pair
        for (var i = 0; i < participantIds.Count; i++)
        {
            for (var j = i + 1; j < participantIds.Count; j++)
            {
                var charA = participantIds[i];
                var charB = participantIds[j];

                var encounterIds = await GetPairEncounterIdsAsync(charA, charB, cancellationToken);
                if (encounterIds.Count <= _configuration.MaxEncountersPerPair)
                {
                    continue;
                }

                // Load encounters with timestamps
                var encountersWithTimestamp = new List<(Guid encounterId, long timestamp)>();
                foreach (var encounterId in encounterIds)
                {
                    var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
                    if (encounter != null)
                    {
                        encountersWithTimestamp.Add((encounterId, encounter.Timestamp));
                    }
                }

                // Sort by timestamp ascending (oldest first)
                var sorted = encountersWithTimestamp.OrderBy(e => e.timestamp).ToList();

                // Calculate how many to remove
                var toRemove = sorted.Count - _configuration.MaxEncountersPerPair;
                if (toRemove <= 0) continue;

                _logger.LogInformation("Pruning {Count} oldest encounters between pair {CharA}/{CharB}",
                    toRemove, charA, charB);

                // Remove the oldest encounters
                for (var k = 0; k < toRemove; k++)
                {
                    var (encounterId, _) = sorted[k];

                    // Delete all perspectives for this encounter
                    await DeleteEncounterPerspectivesAsync(encounterId, cancellationToken);

                    // Delete the encounter
                    var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
                    if (encounter != null)
                    {
                        await encounterStore.DeleteAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);

                        // Clean up pair indexes (including this pair and any others)
                        var allParticipants = encounter.ParticipantIds;
                        await RemoveFromPairIndexesAsync(allParticipants, encounterId, cancellationToken);

                        // Clean up location index
                        if (encounter.LocationId.HasValue)
                        {
                            await RemoveFromLocationIndexAsync(encounter.LocationId.Value, encounterId, cancellationToken);
                        }

                        // Clean up type-encounter index
                        await RemoveFromTypeEncounterIndexAsync(encounter.EncounterTypeCode, encounterId, cancellationToken);
                    }
                }
            }
        }
    }

    private async Task UpdatePairIndexesAsync(List<Guid> participantIds, Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.UpdatePairIndexesAsync");
        var indexStore = _stateStoreFactory.GetStore<PairIndexData>(StateStoreDefinitions.CharacterEncounter);

        // Create pair indexes for each unique pair
        for (var i = 0; i < participantIds.Count; i++)
        {
            for (var j = i + 1; j < participantIds.Count; j++)
            {
                var pairKey = GetPairKey(participantIds[i], participantIds[j]);
                var key = $"{PAIR_INDEX_PREFIX}{pairKey}";
                var charA = participantIds[i] < participantIds[j] ? participantIds[i] : participantIds[j];
                var charB = participantIds[i] < participantIds[j] ? participantIds[j] : participantIds[i];

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
                    index ??= new PairIndexData { CharacterIdA = charA, CharacterIdB = charB };

                    if (!index.EncounterIds.Contains(encounterId))
                    {
                        index.EncounterIds.Add(encounterId);
                        // etag is null when key doesn't exist yet; empty string signals
                        // "create new" to TrySaveAsync (will never conflict on new entries)
                        var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken);
                        if (saveResult == null)
                        {
                            _logger.LogDebug("Concurrent modification on pair index {PairKey}, retrying (attempt {Attempt})",
                                pairKey, attempt + 1);
                            continue;
                        }
                    }

                    break;
                }
            }
        }
    }

    private async Task RemoveFromPairIndexesAsync(List<Guid> participantIds, Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromPairIndexesAsync");
        var indexStore = _stateStoreFactory.GetStore<PairIndexData>(StateStoreDefinitions.CharacterEncounter);

        for (var i = 0; i < participantIds.Count; i++)
        {
            for (var j = i + 1; j < participantIds.Count; j++)
            {
                var pairKey = GetPairKey(participantIds[i], participantIds[j]);
                var key = $"{PAIR_INDEX_PREFIX}{pairKey}";

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
                    if (index == null)
                    {
                        break;
                    }

                    index.EncounterIds.Remove(encounterId);
                    // GetWithETagAsync returns non-null etag for existing records;
                    // coalesce satisfies compiler's nullable analysis (will never execute)
                    var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken);
                    if (saveResult == null)
                    {
                        _logger.LogDebug("Concurrent modification on pair index {PairKey} during remove, retrying (attempt {Attempt})",
                            pairKey, attempt + 1);
                        continue;
                    }

                    break;
                }
            }
        }
    }

    private async Task AddToLocationIndexAsync(Guid locationId, Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.AddToLocationIndexAsync");
        var indexStore = _stateStoreFactory.GetStore<LocationIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{LOCATION_INDEX_PREFIX}{locationId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            index ??= new LocationIndexData { LocationId = locationId };

            if (!index.EncounterIds.Contains(encounterId))
            {
                index.EncounterIds.Add(encounterId);
                // etag is null when key doesn't exist yet; empty string signals
                // "create new" to TrySaveAsync (will never conflict on new entries)
                var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification on location index {LocationId}, retrying (attempt {Attempt})",
                        locationId, attempt + 1);
                    continue;
                }
            }

            return;
        }

        _logger.LogWarning("Failed to add encounter {EncounterId} to location index {LocationId} after 3 attempts",
            encounterId, locationId);
    }

    private async Task RemoveFromLocationIndexAsync(Guid locationId, Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromLocationIndexAsync");
        var indexStore = _stateStoreFactory.GetStore<LocationIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{LOCATION_INDEX_PREFIX}{locationId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            if (index == null)
            {
                return;
            }

            index.EncounterIds.Remove(encounterId);
            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification on location index {LocationId} during remove, retrying (attempt {Attempt})",
                    locationId, attempt + 1);
                continue;
            }

            return;
        }

        _logger.LogWarning("Failed to remove encounter {EncounterId} from location index {LocationId} after 3 attempts",
            encounterId, locationId);
    }

    // ============================================================================
    // Encounter-Perspective Index Management
    // ============================================================================

    private async Task AddToEncounterPerspectiveIndexAsync(Guid encounterId, Guid perspectiveId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.AddToEncounterPerspectiveIndexAsync");
        var indexStore = _stateStoreFactory.GetStore<EncounterPerspectiveIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{ENCOUNTER_PERSPECTIVE_INDEX_PREFIX}{encounterId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            index ??= new EncounterPerspectiveIndexData { EncounterId = encounterId };

            if (!index.PerspectiveIds.Contains(perspectiveId))
            {
                index.PerspectiveIds.Add(perspectiveId);
                // etag is null when key doesn't exist yet; empty string signals
                // "create new" to TrySaveAsync (will never conflict on new entries)
                var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification on encounter perspective index {EncounterId}, retrying (attempt {Attempt})",
                        encounterId, attempt + 1);
                    continue;
                }
            }
            return;
        }

        _logger.LogWarning("Failed to add perspective {PerspectiveId} to encounter perspective index {EncounterId} after 3 attempts",
            perspectiveId, encounterId);
    }

    private async Task RemoveFromEncounterPerspectiveIndexAsync(Guid encounterId, Guid perspectiveId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.RemoveFromEncounterPerspectiveIndexAsync");
        var indexStore = _stateStoreFactory.GetStore<EncounterPerspectiveIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{ENCOUNTER_PERSPECTIVE_INDEX_PREFIX}{encounterId}";

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (index, etag) = await indexStore.GetWithETagAsync(key, cancellationToken);
            if (index == null) return;

            index.PerspectiveIds.Remove(perspectiveId);

            if (index.PerspectiveIds.Count == 0)
            {
                // Delete empty index
                await indexStore.DeleteAsync(key, cancellationToken);
                return;
            }

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await indexStore.TrySaveAsync(key, index, etag ?? string.Empty, cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification on encounter perspective index {EncounterId}, retrying (attempt {Attempt})",
                    encounterId, attempt + 1);
                continue;
            }
            return;
        }

        _logger.LogWarning("Failed to remove perspective {PerspectiveId} from encounter perspective index {EncounterId} after 3 attempts",
            perspectiveId, encounterId);
    }

    private async Task DeleteEncounterPerspectiveIndexAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.DeleteEncounterPerspectiveIndexAsync");
        var indexStore = _stateStoreFactory.GetStore<EncounterPerspectiveIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{ENCOUNTER_PERSPECTIVE_INDEX_PREFIX}{encounterId}";
        await indexStore.DeleteAsync(key, cancellationToken);
    }

    private async Task<List<Guid>> GetEncounterPerspectiveIdsAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetEncounterPerspectiveIdsAsync");
        var indexStore = _stateStoreFactory.GetStore<EncounterPerspectiveIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{ENCOUNTER_PERSPECTIVE_INDEX_PREFIX}{encounterId}";
        var index = await indexStore.GetAsync(key, cancellationToken);
        return index?.PerspectiveIds ?? new List<Guid>();
    }

    // ============================================================================
    // Bulk Load Helpers
    // ============================================================================

    /// <summary>
    /// Bulk loads perspectives by IDs with parallel lazy decay.
    /// </summary>
    private async Task<List<PerspectiveData>> BulkLoadPerspectivesWithDecayAsync(
        IEnumerable<Guid> perspectiveIds,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.BulkLoadPerspectivesWithDecayAsync");
        var idList = perspectiveIds.ToList();
        if (idList.Count == 0) return new List<PerspectiveData>();

        var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
        var perspectiveKeys = idList.Select(id => $"{PERSPECTIVE_KEY_PREFIX}{id}").ToList();

        // Single bulk read
        var bulkResult = await perspectiveStore.GetBulkAsync(perspectiveKeys, cancellationToken);

        if (!_configuration.MemoryDecayEnabled || _configuration.MemoryDecayMode != MemoryDecayMode.Lazy)
        {
            // No decay needed - just return values
            return bulkResult.Values.Where(p => p != null).ToList()!;
        }

        // Separate perspectives needing decay from those that don't
        var needsDecay = new List<PerspectiveData>();
        var noDecayNeeded = new List<PerspectiveData>();

        foreach (var (_, perspective) in bulkResult)
        {
            if (perspective == null) continue;

            var (shouldDecay, _) = CalculateDecay(perspective);
            if (shouldDecay)
                needsDecay.Add(perspective);
            else
                noDecayNeeded.Add(perspective);
        }

        if (needsDecay.Count == 0)
        {
            return noDecayNeeded;
        }

        // Apply decay in parallel
        var decayTasks = needsDecay.Select(p => ApplyLazyDecayAsync(perspectiveStore, p, cancellationToken));
        var decayedPerspectives = await Task.WhenAll(decayTasks);

        // Combine results
        return noDecayNeeded.Concat(decayedPerspectives).ToList();
    }

    /// <summary>
    /// Bulk loads encounters by IDs.
    /// </summary>
    private async Task<Dictionary<Guid, EncounterData>> BulkLoadEncountersAsync(
        IEnumerable<Guid> encounterIds,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.BulkLoadEncountersAsync");
        var idList = encounterIds.ToList();
        if (idList.Count == 0) return new Dictionary<Guid, EncounterData>();

        var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);
        var encounterKeys = idList.Select(id => $"{ENCOUNTER_KEY_PREFIX}{id}").ToList();

        var bulkResult = await encounterStore.GetBulkAsync(encounterKeys, cancellationToken);

        // Map back to encounter IDs
        var result = new Dictionary<Guid, EncounterData>();
        foreach (var id in idList)
        {
            var key = $"{ENCOUNTER_KEY_PREFIX}{id}";
            if (bulkResult.TryGetValue(key, out var encounter) && encounter != null)
            {
                result[id] = encounter;
            }
        }
        return result;
    }

    /// <summary>
    /// Bulk loads all perspectives for multiple encounters at once.
    /// Uses parallel index lookups followed by a single bulk perspective load.
    /// </summary>
    private async Task<Dictionary<Guid, List<EncounterPerspectiveModel>>> BulkLoadAllEncounterPerspectivesAsync(
        IEnumerable<Guid> encounterIds,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.BulkLoadAllEncounterPerspectivesAsync");
        var idList = encounterIds.ToList();
        if (idList.Count == 0) return new Dictionary<Guid, List<EncounterPerspectiveModel>>();

        // Step 1: Parallel fetch perspective IDs for all encounters
        var indexTasks = idList.Select(async encounterId =>
        {
            var perspectiveIds = await GetEncounterPerspectiveIdsAsync(encounterId, cancellationToken);
            return (encounterId, perspectiveIds);
        });
        var indexResults = await Task.WhenAll(indexTasks);

        // Build mapping: perspectiveId -> encounterId and collect all perspective IDs
        var perspectiveToEncounter = new Dictionary<Guid, Guid>();
        var allPerspectiveIds = new List<Guid>();
        var encountersThatNeedLegacyFallback = new List<Guid>();

        foreach (var (encounterId, perspectiveIds) in indexResults)
        {
            if (perspectiveIds.Count > 0)
            {
                foreach (var perspectiveId in perspectiveIds)
                {
                    perspectiveToEncounter[perspectiveId] = encounterId;
                    allPerspectiveIds.Add(perspectiveId);
                }
            }
            else
            {
                // No index entry - will need legacy fallback
                encountersThatNeedLegacyFallback.Add(encounterId);
            }
        }

        // Step 2: Single bulk load of all perspectives with parallel decay
        var allPerspectives = await BulkLoadPerspectivesWithDecayAsync(allPerspectiveIds, cancellationToken);

        // Step 3: Group perspectives by encounter ID
        var result = new Dictionary<Guid, List<EncounterPerspectiveModel>>();
        foreach (var perspective in allPerspectives)
        {
            if (perspectiveToEncounter.TryGetValue(perspective.PerspectiveId, out var encounterId))
            {
                if (!result.ContainsKey(encounterId))
                {
                    result[encounterId] = new List<EncounterPerspectiveModel>();
                }
                result[encounterId].Add(MapToPerspectiveModel(perspective));
            }
        }

        // Step 4: Handle legacy fallback for encounters without index (pre-existing data)
        if (encountersThatNeedLegacyFallback.Count > 0)
        {
            foreach (var encounterId in encountersThatNeedLegacyFallback)
            {
                var legacyPerspectives = await GetEncounterPerspectivesAsync(encounterId, cancellationToken);
                if (legacyPerspectives.Count > 0)
                {
                    result[encounterId] = legacyPerspectives;
                }
            }
        }

        return result;
    }

    private static string GetPairKey(Guid charA, Guid charB)
    {
        // Always put the smaller GUID first for consistent keying
        return charA < charB ? $"{charA}:{charB}" : $"{charB}:{charA}";
    }

    private async Task<List<EncounterPerspectiveModel>> GetEncounterPerspectivesAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.GetEncounterPerspectivesAsync");
        // Try new index first for O(1) lookup
        var perspectiveIds = await GetEncounterPerspectiveIdsAsync(encounterId, cancellationToken);

        if (perspectiveIds.Count > 0)
        {
            // Bulk load with parallel decay
            var perspectives = await BulkLoadPerspectivesWithDecayAsync(perspectiveIds, cancellationToken);
            return perspectives.Select(MapToPerspectiveModel).ToList();
        }

        // Fallback for pre-existing encounters without index (legacy data)
        var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
        var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);
        var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
        if (encounter == null) return new List<EncounterPerspectiveModel>();

        var legacyPerspectives = new List<EncounterPerspectiveModel>();
        foreach (var participantId in encounter.ParticipantIds)
        {
            var perspective = await FindPerspectiveByEncounterAndCharacterAsync(encounterId, participantId, cancellationToken);
            if (perspective != null)
            {
                // Apply lazy decay
                perspective = await ApplyLazyDecayAsync(perspectiveStore, perspective, cancellationToken);
                legacyPerspectives.Add(MapToPerspectiveModel(perspective));
            }
        }
        return legacyPerspectives;
    }

    private async Task<PerspectiveData?> FindPerspectiveAsync(Guid encounterId, Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.FindPerspectiveAsync");
        return await FindPerspectiveByEncounterAndCharacterAsync(encounterId, characterId, cancellationToken);
    }

    private async Task<PerspectiveData?> FindPerspectiveByEncounterAndCharacterAsync(Guid encounterId, Guid characterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.FindPerspectiveByEncounterAndCharacterAsync");
        // Get character's perspective IDs and find the one for this encounter
        var perspectiveIds = await GetCharacterPerspectiveIdsAsync(characterId, cancellationToken);
        var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);

        foreach (var perspectiveId in perspectiveIds)
        {
            var perspective = await perspectiveStore.GetAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", cancellationToken);
            if (perspective != null && perspective.EncounterId == encounterId)
            {
                return perspective;
            }
        }
        return null;
    }

    private async Task<int> DeleteEncounterPerspectivesAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.DeleteEncounterPerspectivesAsync");
        var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);

        // Try the new index first for O(1) lookup
        var perspectiveIds = await GetEncounterPerspectiveIdsAsync(encounterId, cancellationToken);

        if (perspectiveIds.Count > 0)
        {
            // Bulk load perspectives using the index
            var perspectiveKeys = perspectiveIds.Select(id => $"{PERSPECTIVE_KEY_PREFIX}{id}").ToList();
            var perspectives = await perspectiveStore.GetBulkAsync(perspectiveKeys, cancellationToken);

            foreach (var (key, perspective) in perspectives)
            {
                if (perspective != null)
                {
                    await perspectiveStore.DeleteAsync(key, cancellationToken);
                    await RemoveFromCharacterIndexAsync(perspective.CharacterId, perspective.PerspectiveId, cancellationToken);
                }
            }

            // Delete the encounter-perspective index itself
            await DeleteEncounterPerspectiveIndexAsync(encounterId, cancellationToken);

            return perspectiveIds.Count;
        }

        // Fallback for pre-existing encounters without index (legacy data)
        var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);
        var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
        if (encounter == null) return 0;

        var deleted = 0;
        foreach (var participantId in encounter.ParticipantIds)
        {
            var perspective = await FindPerspectiveByEncounterAndCharacterAsync(encounterId, participantId, cancellationToken);
            if (perspective != null)
            {
                await perspectiveStore.DeleteAsync($"{PERSPECTIVE_KEY_PREFIX}{perspective.PerspectiveId}", cancellationToken);
                await RemoveFromCharacterIndexAsync(participantId, perspective.PerspectiveId, cancellationToken);
                deleted++;
            }
        }
        return deleted;
    }

    private async Task<PerspectiveData> ApplyLazyDecayAsync(IStateStore<PerspectiveData> store, PerspectiveData perspective, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.ApplyLazyDecayAsync");
        if (!_configuration.MemoryDecayEnabled || _configuration.MemoryDecayMode != MemoryDecayMode.Lazy)
            return perspective;

        var (needsDecay, _) = CalculateDecay(perspective);
        if (!needsDecay) return perspective;

        // Re-fetch with ETag for optimistic concurrency (prevents double-decay from concurrent reads)
        var perspectiveKey = $"{PERSPECTIVE_KEY_PREFIX}{perspective.PerspectiveId}";
        var (freshPerspective, etag) = await store.GetWithETagAsync(perspectiveKey, cancellationToken);
        if (freshPerspective == null) return perspective;

        // Recalculate on fresh data in case another instance already decayed
        var (stillNeedsDecay, _) = CalculateDecay(freshPerspective);
        if (!stillNeedsDecay) return freshPerspective;

        var decayAmount = GetDecayAmount(freshPerspective);
        var previousStrength = freshPerspective.MemoryStrength;
        freshPerspective.MemoryStrength = Math.Max(0, freshPerspective.MemoryStrength - decayAmount);
        freshPerspective.LastDecayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // GetWithETagAsync returns non-null etag for existing records;
        // coalesce satisfies compiler's nullable analysis (will never execute)
        var saveResult = await store.TrySaveAsync(perspectiveKey, freshPerspective, etag ?? string.Empty, cancellationToken);
        if (saveResult == null)
        {
            // Concurrent modification - another instance likely already applied decay
            _logger.LogDebug("Concurrent modification during lazy decay for perspective {PerspectiveId}, skipping", perspective.PerspectiveId);
            return freshPerspective;
        }

        // Check if faded below threshold
        if (previousStrength >= _configuration.MemoryFadeThreshold && freshPerspective.MemoryStrength < _configuration.MemoryFadeThreshold)
        {
            await _messageBus.TryPublishAsync(ENCOUNTER_MEMORY_FADED_TOPIC, new EncounterMemoryFadedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                EncounterId = freshPerspective.EncounterId,
                CharacterId = freshPerspective.CharacterId,
                PerspectiveId = freshPerspective.PerspectiveId,
                PreviousStrength = previousStrength,
                NewStrength = freshPerspective.MemoryStrength,
                FadeThreshold = (float)_configuration.MemoryFadeThreshold
            }, cancellationToken: cancellationToken);
        }

        return freshPerspective;
    }

    private (bool needsDecay, bool willFade) CalculateDecay(PerspectiveData perspective)
    {
        if (perspective.MemoryStrength <= 0) return (false, false);

        var lastDecayed = perspective.LastDecayedAtUnix.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(perspective.LastDecayedAtUnix.Value)
            : DateTimeOffset.FromUnixTimeSeconds(perspective.CreatedAtUnix);

        var hoursSinceLastDecay = (DateTimeOffset.UtcNow - lastDecayed).TotalHours;
        var intervalsElapsed = hoursSinceLastDecay / _configuration.MemoryDecayIntervalHours;

        if (intervalsElapsed < 1) return (false, false);

        var decayAmount = GetDecayAmount(perspective);
        var newStrength = perspective.MemoryStrength - decayAmount;
        var willFade = perspective.MemoryStrength >= _configuration.MemoryFadeThreshold &&
                        newStrength < _configuration.MemoryFadeThreshold;

        return (true, willFade);
    }

    private float GetDecayAmount(PerspectiveData perspective)
    {
        var lastDecayed = perspective.LastDecayedAtUnix.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(perspective.LastDecayedAtUnix.Value)
            : DateTimeOffset.FromUnixTimeSeconds(perspective.CreatedAtUnix);

        var hoursSinceLastDecay = (DateTimeOffset.UtcNow - lastDecayed).TotalHours;
        var intervalsElapsed = (int)(hoursSinceLastDecay / _configuration.MemoryDecayIntervalHours);

        return (float)(intervalsElapsed * _configuration.MemoryDecayRate);
    }

    private static EmotionalImpact GetDefaultEmotionalImpactForOutcome(EncounterOutcome outcome)
    {
        return outcome switch
        {
            EncounterOutcome.POSITIVE => EmotionalImpact.GRATITUDE,
            EncounterOutcome.NEGATIVE => EmotionalImpact.ANGER,
            EncounterOutcome.NEUTRAL => EmotionalImpact.INDIFFERENCE,
            EncounterOutcome.MEMORABLE => EmotionalImpact.RESPECT,
            EncounterOutcome.TRANSFORMATIVE => EmotionalImpact.PRIDE,
            _ => EmotionalImpact.INDIFFERENCE
        };
    }

    private float? GetDefaultSentimentShiftForOutcome(EncounterOutcome outcome)
    {
        return outcome switch
        {
            EncounterOutcome.POSITIVE => (float)_configuration.SentimentShiftPositive,
            EncounterOutcome.NEGATIVE => (float)_configuration.SentimentShiftNegative,
            EncounterOutcome.NEUTRAL => 0f,
            EncounterOutcome.MEMORABLE => (float)_configuration.SentimentShiftMemorable,
            EncounterOutcome.TRANSFORMATIVE => (float)_configuration.SentimentShiftTransformative,
            _ => 0f
        };
    }

    // ============================================================================
    // Mapping Helpers
    // ============================================================================

    private static EncounterTypeResponse MapToEncounterTypeResponse(EncounterTypeData data)
    {
        return new EncounterTypeResponse
        {
            TypeId = data.TypeId,
            Code = data.Code,
            Name = data.Name,
            Description = data.Description,
            IsBuiltIn = data.IsBuiltIn,
            DefaultEmotionalImpact = data.DefaultEmotionalImpact,
            SortOrder = data.SortOrder,
            IsActive = data.IsActive,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix)
        };
    }

    private static EncounterModel MapToEncounterModel(EncounterData data)
    {
        return new EncounterModel
        {
            EncounterId = data.EncounterId,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(data.Timestamp),
            RealmId = data.RealmId,
            LocationId = data.LocationId,
            EncounterTypeCode = data.EncounterTypeCode,
            Context = data.Context,
            Outcome = data.Outcome,
            ParticipantIds = data.ParticipantIds,
            Metadata = data.Metadata,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix)
        };
    }

    private static EncounterPerspectiveModel MapToPerspectiveModel(PerspectiveData data)
    {
        return new EncounterPerspectiveModel
        {
            PerspectiveId = data.PerspectiveId,
            EncounterId = data.EncounterId,
            CharacterId = data.CharacterId,
            EmotionalImpact = data.EmotionalImpact,
            SentimentShift = data.SentimentShift,
            MemoryStrength = data.MemoryStrength,
            RememberedAs = data.RememberedAs,
            LastDecayedAt = data.LastDecayedAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(data.LastDecayedAtUnix.Value) : null,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix),
            UpdatedAt = data.UpdatedAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(data.UpdatedAtUnix.Value) : null
        };
    }
}
