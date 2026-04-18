using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.CharacterEncounter.Caching;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.History;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Worldstate;
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
public partial class CharacterEncounterService : ICharacterEncounterService, ICleanDeprecatedEntity
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<CharacterEncounterService> _logger;
    private readonly CharacterEncounterServiceConfiguration _configuration;
    private readonly ICharacterClient _characterClient;
    private readonly IEncounterDataCache _encounterDataCache;
    private readonly IResourceClient _resourceClient;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IWorldstateClient _worldstateClient;

    /// <summary>Store for encounter type definitions (built-in and custom).</summary>
    private readonly IStateStore<EncounterTypeData> _encounterTypeStore;

    /// <summary>Store for encounter records.</summary>
    private readonly IStateStore<EncounterData> _encounterStore;

    /// <summary>Store for per-participant encounter perspectives.</summary>
    private readonly IStateStore<PerspectiveData> _perspectiveStore;

    /// <summary>Store for character-to-perspective index (maps character ID to their perspective IDs).</summary>
    private readonly IStateStore<CharacterIndexData> _characterIndexStore;

    /// <summary>Store for character pair-to-encounter index (maps pair key to shared encounter IDs).</summary>
    private readonly IStateStore<PairIndexData> _pairIndexStore;

    /// <summary>Store for location-to-encounter index (maps location ID to encounter IDs).</summary>
    private readonly IStateStore<LocationIndexData> _locationIndexStore;

    /// <summary>Store for global character index (tracks all characters with encounters).</summary>
    private readonly IStateStore<GlobalCharacterIndexData> _globalCharacterIndexStore;

    /// <summary>Store for custom encounter type index (tracks custom type codes).</summary>
    private readonly IStateStore<CustomTypeIndexData> _customTypeIndexStore;

    /// <summary>Store for type-to-encounter index (maps encounter type code to encounter IDs).</summary>
    private readonly IStateStore<TypeEncounterIndexData> _typeEncounterIndexStore;

    /// <summary>Store for encounter-to-perspective index (maps encounter ID to perspective IDs).</summary>
    private readonly IStateStore<EncounterPerspectiveIndexData> _encounterPerspectiveIndexStore;

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
        new("COMBAT", "Combat", "Physical confrontation between characters", EmotionalImpact.Anger, 10),
        new("DIALOGUE", "Dialogue", "Conversation or negotiation between characters", EmotionalImpact.Indifference, 20),
        new("TRADE", "Trade", "Economic exchange between characters", EmotionalImpact.Gratitude, 30),
        new("QUEST", "Quest", "Shared objective completion", EmotionalImpact.Respect, 40),
        new("SOCIAL", "Social", "Casual social interaction", EmotionalImpact.Affection, 50),
        new("CEREMONY", "Ceremony", "Formal event participation", EmotionalImpact.Pride, 60)
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
        ITelemetryProvider telemetryProvider,
        IWorldstateClient worldstateClient)
    {
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _characterClient = characterClient;
        _encounterDataCache = encounterDataCache;
        _resourceClient = resourceClient;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
        _worldstateClient = worldstateClient;

        // Constructor-cache all state store references per FOUNDATION TENETS
        _encounterTypeStore = stateStoreFactory.GetStore<EncounterTypeData>(StateStoreDefinitions.CharacterEncounter);
        _encounterStore = stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);
        _perspectiveStore = stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
        _characterIndexStore = stateStoreFactory.GetStore<CharacterIndexData>(StateStoreDefinitions.CharacterEncounter);
        _pairIndexStore = stateStoreFactory.GetStore<PairIndexData>(StateStoreDefinitions.CharacterEncounter);
        _locationIndexStore = stateStoreFactory.GetStore<LocationIndexData>(StateStoreDefinitions.CharacterEncounter);
        _globalCharacterIndexStore = stateStoreFactory.GetStore<GlobalCharacterIndexData>(StateStoreDefinitions.CharacterEncounter);
        _customTypeIndexStore = stateStoreFactory.GetStore<CustomTypeIndexData>(StateStoreDefinitions.CharacterEncounter);
        _typeEncounterIndexStore = stateStoreFactory.GetStore<TypeEncounterIndexData>(StateStoreDefinitions.CharacterEncounter);
        _encounterPerspectiveIndexStore = stateStoreFactory.GetStore<EncounterPerspectiveIndexData>(StateStoreDefinitions.CharacterEncounter);
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

        var store = _encounterTypeStore;
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

        // Publish lifecycle event per FOUNDATION TENETS (Event-Driven Architecture)
        var createdAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix);
        await _messageBus.PublishEncounterTypeCreatedAsync(new EncounterTypeCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = createdAt,
            TypeId = data.TypeId,
            Code = data.Code,
            Name = data.Name,
            Description = data.Description,
            IsBuiltIn = data.IsBuiltIn,
            SortOrder = data.SortOrder,
            IsActive = data.IsActive,
            IsDeprecated = false,
            DeprecatedAt = null,
            DeprecationReason = null,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        }, cancellationToken);

        _logger.LogInformation("Created encounter type {Code} with ID {TypeId}", body.Code, typeId);
        return (StatusCodes.OK, MapToEncounterTypeResponse(data));
    }

    /// <summary>
    /// Retrieves an encounter type by its code.
    /// </summary>
    public async Task<(StatusCodes, EncounterTypeResponse?)> GetEncounterTypeAsync(GetEncounterTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting encounter type {Code}", body.Code);

        var store = _encounterTypeStore;
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

        var store = _encounterTypeStore;
        var types = new List<EncounterTypeResponse>();

        // Query all types with TYPE_KEY_PREFIX
        var allKeys = await GetAllTypeKeysAsync(store, cancellationToken);
        foreach (var key in allKeys)
        {
            var data = await store.GetAsync(key, cancellationToken);
            if (data == null) continue;

            // Apply filters
            if (!body.IncludeDeprecated && data.IsDeprecated) continue;
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

        var store = _encounterTypeStore;
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

        // Track changed fields for lifecycle event
        var changedFields = new List<string>();
        if (body.Name != null) { data.Name = body.Name; changedFields.Add("name"); }
        if (body.ChangeFields.IsFieldSet("description") && body.Description != data.Description) { data.Description = body.Description; changedFields.Add("description"); }
        if (body.DefaultEmotionalImpact != null) { data.DefaultEmotionalImpact = body.DefaultEmotionalImpact.Value; changedFields.Add("defaultEmotionalImpact"); }
        if (body.SortOrder != null) { data.SortOrder = body.SortOrder.Value; changedFields.Add("sortOrder"); }

        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(key, data, cancellationToken: cancellationToken);

        // Publish lifecycle event per FOUNDATION TENETS (Event-Driven Architecture)
        await _messageBus.PublishEncounterTypeUpdatedAsync(new EncounterTypeUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            TypeId = data.TypeId,
            Code = data.Code,
            Name = data.Name,
            Description = data.Description,
            IsBuiltIn = data.IsBuiltIn,
            SortOrder = data.SortOrder,
            IsActive = data.IsActive,
            IsDeprecated = data.IsDeprecated,
            DeprecatedAt = data.DeprecatedAt,
            DeprecationReason = data.DeprecationReason,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix),
            UpdatedAt = now,
            ChangedFields = changedFields
        }, cancellationToken);

        _logger.LogInformation("Updated encounter type {Code}", body.Code);
        return (StatusCodes.OK, MapToEncounterTypeResponse(data));
    }

    /// <summary>
    /// Deprecates an encounter type (Category B — one-way, no delete).
    /// Idempotent: returns OK if already deprecated.
    /// </summary>
    public async Task<(StatusCodes, EncounterTypeResponse?)> DeprecateEncounterTypeAsync(DeprecateEncounterTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deprecating encounter type {Code}", body.Code);

        var key = $"{TYPE_KEY_PREFIX}{body.Code.ToUpperInvariant()}";
        var data = await _encounterTypeStore.GetAsync(key, cancellationToken);

        if (data == null)
        {
            return (StatusCodes.NotFound, null);
        }

        // Idempotent: already deprecated is a success (per IMPLEMENTATION TENETS)
        if (data.IsDeprecated)
        {
            return (StatusCodes.OK, MapToEncounterTypeResponse(data));
        }

        var now = DateTimeOffset.UtcNow;
        data.IsDeprecated = true;
        data.DeprecatedAt = now;
        data.DeprecationReason = body.Reason;
        await _encounterTypeStore.SaveAsync(key, data, cancellationToken: cancellationToken);

        // Per IMPLEMENTATION TENETS: deprecation published as *.updated with changedFields
        await _messageBus.PublishEncounterTypeUpdatedAsync(new EncounterTypeUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            TypeId = data.TypeId,
            Code = data.Code,
            Name = data.Name,
            Description = data.Description,
            IsBuiltIn = data.IsBuiltIn,
            SortOrder = data.SortOrder,
            IsActive = data.IsActive,
            IsDeprecated = data.IsDeprecated,
            DeprecatedAt = data.DeprecatedAt,
            DeprecationReason = data.DeprecationReason,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix),
            UpdatedAt = now,
            ChangedFields = new List<string> { "isDeprecated", "deprecatedAt", "deprecationReason" }
        }, cancellationToken);

        _logger.LogInformation("Deprecated encounter type {Code}", body.Code);
        return (StatusCodes.OK, MapToEncounterTypeResponse(data));
    }

    /// <summary>
    /// Seeds built-in encounter types.
    /// </summary>
    public async Task<(StatusCodes, SeedEncounterTypesResponse?)> SeedEncounterTypesAsync(SeedEncounterTypesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding encounter types, forceReset={ForceReset}", body.ForceReset);

        var store = _encounterTypeStore;
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
        var typeStore = _encounterTypeStore;
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

        // Per IMPLEMENTATION TENETS: Category B instance creation guard
        if (typeData.IsDeprecated)
        {
            _logger.LogWarning("Cannot record encounter with deprecated type: {Type}", body.EncounterTypeCode);
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
        var encounterStore = _encounterStore;
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
        var perspectiveStore = _perspectiveStore;
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
                ImpactIntensity = hasProvidedPerspective ? provided?.ImpactIntensity ?? 0f : 0f,
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
        await _messageBus.PublishEncounterRecordedAsync(new EncounterRecordedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            EncounterId = encounterId,
            EncounterTypeCode = body.EncounterTypeCode.ToUpperInvariant(),
            Outcome = body.Outcome,
            RealmId = body.RealmId,
            LocationId = body.LocationId,
            ParticipantIds = participantIds,
            Context = body.Context,
            EncounterTimestamp = body.Timestamp
        }, cancellationToken);

        // Register character references with lib-resource for cleanup coordination
        foreach (var participantId in participantIds)
        {
            await RegisterCharacterReferenceAsync(encounterId.ToString(), participantId, cancellationToken);
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
            .Select(r => r.sentiment)
            .OfType<SentimentResponse>()
            .ToList();

        return (StatusCodes.OK, new BatchSentimentResponse
        {
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
        var perspectiveStore = _perspectiveStore;
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
        var perspectiveStore = _perspectiveStore;
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
        if (body.ImpactIntensity.HasValue) perspective.ImpactIntensity = body.ImpactIntensity.Value;
        if (body.SentimentShift.HasValue) perspective.SentimentShift = body.SentimentShift.Value;
        if (body.RememberedAs != null) perspective.RememberedAs = body.RememberedAs;
        perspective.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // GetWithETagAsync returns non-null etag for existing records;
        // coalesce satisfies compiler's nullable analysis (will never execute)
        var newEtag = await perspectiveStore.TrySaveAsync(perspectiveKey, perspective, etag ?? string.Empty, cancellationToken: cancellationToken);
        if (newEtag == null)
        {
            _logger.LogWarning("Concurrent modification detected for perspective {PerspectiveId}", found.PerspectiveId);
            return (StatusCodes.Conflict, null);
        }

        // Publish event - convert enum to string at API boundary for event schema
        await _messageBus.PublishEncounterPerspectiveUpdatedAsync(new EncounterPerspectiveUpdatedEvent
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
        }, cancellationToken);

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
        var perspectiveStore = _perspectiveStore;
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
        var newEtag = await perspectiveStore.TrySaveAsync(perspectiveKey, perspective, etag ?? string.Empty, cancellationToken: cancellationToken);
        if (newEtag == null)
        {
            _logger.LogWarning("Concurrent modification detected for perspective {PerspectiveId}", found.PerspectiveId);
            return (StatusCodes.Conflict, null);
        }

        // Publish event
        await _messageBus.PublishEncounterMemoryRefreshedAsync(new EncounterMemoryRefreshedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EncounterId = body.EncounterId,
            CharacterId = body.CharacterId,
            PerspectiveId = perspective.PerspectiveId,
            PreviousStrength = previousStrength,
            NewStrength = perspective.MemoryStrength
        }, cancellationToken);

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

        var encounterStore = _encounterStore;
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
        await _messageBus.PublishEncounterDeletedAsync(new EncounterDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            EncounterId = body.EncounterId,
            ParticipantIds = participantIds,
            PerspectivesDeleted = perspectivesDeleted,
            DeletedByCharacterCleanup = false,
            CleanupCharacterId = null
        }, cancellationToken);

        // Unregister character references with lib-resource
        foreach (var participantId in participantIds)
        {
            await UnregisterCharacterReferenceAsync(body.EncounterId.ToString(), participantId, cancellationToken);
        }

        _logger.LogInformation("Deleted encounter {EncounterId} with {Count} perspectives",
            body.EncounterId, perspectivesDeleted);

        return (StatusCodes.OK, new DeleteEncounterResponse
        {
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

            var perspectiveStore = _perspectiveStore;
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
            var encounterStore = _encounterStore;
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
                await _messageBus.PublishEncounterDeletedAsync(new EncounterDeletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    EncounterId = encounterId,
                    ParticipantIds = participantIds,
                    PerspectivesDeleted = otherPerspectivesDeleted + 1,
                    DeletedByCharacterCleanup = true,
                    CleanupCharacterId = body.CharacterId
                }, cancellationToken);
            }

            _logger.LogInformation("Deleted {Encounters} encounters and {Perspectives} perspectives for character {CharacterId}",
                encountersDeleted, perspectivesDeleted, body.CharacterId);

            return (StatusCodes.OK, new DeleteByCharacterResponse
            {
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
                MemoriesFaded = 0
            });
        }

        var perspectiveStore = _perspectiveStore;
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
            var globalIndexStore = _globalCharacterIndexStore;
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

                // Resolve decay amount based on time source
                bool decayed;
                bool faded;
                double decayAmount;

                if (_configuration.DecayTimeSource == TimeSource.GameTime)
                {
                    var encounter = await _encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{perspective.EncounterId}", cancellationToken);
                    if (encounter is null) continue;

                    try
                    {
                        var fromTime = perspective.LastDecayedAtUnix.HasValue
                            ? DateTimeOffset.FromUnixTimeSeconds(perspective.LastDecayedAtUnix.Value)
                            : DateTimeOffset.FromUnixTimeSeconds(perspective.CreatedAtUnix);
                        var elapsedResponse = await _worldstateClient.GetElapsedGameTimeAsync(
                            new GetElapsedGameTimeRequest
                            {
                                RealmId = encounter.RealmId,
                                FromRealTime = fromTime,
                                ToRealTime = DateTimeOffset.UtcNow
                            }, cancellationToken);

                        var gameHours = elapsedResponse.TotalGameSeconds / 3600.0;
                        var intervals = gameHours / _configuration.MemoryDecayIntervalHours;
                        if (intervals < 1) continue;

                        decayAmount = intervals * _configuration.MemoryDecayRate;
                        decayed = true;
                        var previewStrength = perspective.MemoryStrength - decayAmount;
                        faded = perspective.MemoryStrength >= _configuration.MemoryFadeThreshold &&
                                previewStrength < _configuration.MemoryFadeThreshold;
                    }
                    catch (ApiException ex)
                    {
                        _logger.LogWarning(ex, "Worldstate unavailable for realm {RealmId} during decay, skipping perspective {PerspectiveId}",
                            encounter.RealmId, perspectiveId);
                        await _messageBus.TryPublishErrorAsync(
                            "character-encounter",
                            "DecayMemories",
                            "ApiException",
                            ex.Message,
                            dependency: "worldstate",
                            endpoint: "worldstate/get-elapsed-game-time",
                            stack: ex.StackTrace,
                            cancellationToken: cancellationToken);
                        continue;
                    }
                }
                else
                {
                    (decayed, faded) = CalculateRealTimeDecay(perspective);
                    if (!decayed) continue;
                    decayAmount = GetRealTimeDecayAmount(perspective);
                }

                if (decayed)
                {
                    perspectivesProcessed++;
                    var previousStrength = perspective.MemoryStrength;

                    if (!dryRun)
                    {
                        perspective.MemoryStrength = Math.Max(0, previousStrength - (float)decayAmount);
                        perspective.LastDecayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var decayResult = await perspectiveStore.TrySaveAsync(perspectiveKey, perspective, pEtag ?? string.Empty, cancellationToken: cancellationToken);
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
                            await _messageBus.PublishEncounterMemoryFadedAsync(new EncounterMemoryFadedEvent
                            {
                                EventId = Guid.NewGuid(),
                                Timestamp = DateTimeOffset.UtcNow,
                                EncounterId = perspective.EncounterId,
                                CharacterId = perspective.CharacterId,
                                PerspectiveId = perspective.PerspectiveId,
                                PreviousStrength = previousStrength,
                                NewStrength = perspective.MemoryStrength,
                                FadeThreshold = (float)_configuration.MemoryFadeThreshold
                            }, cancellationToken);
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
            MemoriesFaded = memoriesFaded
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

            var encounterStore = _encounterStore;
            var perspectiveStore = _perspectiveStore;
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
            var jsonData = CompressionHelper.DecompressJsonData(compressedBytes);
            archiveData = BannouJson.Deserialize<CharacterEncounterArchive>(jsonData)
                ?? throw new InvalidOperationException("Deserialized archive data is null");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decompress archive data for character {CharacterId}", body.CharacterId);
            return (StatusCodes.BadRequest, null);
        }

        // Restore encounters and perspectives
        if (archiveData.HasEncounters && archiveData.Encounters.Count > 0)
        {
            var encounterStore = _encounterStore;
            var perspectiveStore = _perspectiveStore;

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
                        ImpactIntensity = perspectiveModel.ImpactIntensity,
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
            EncountersRestored = encountersRestored,
            PerspectivesRestored = perspectivesRestored
        });
    }

    // ============================================================================
    // Permission Registration
    // ============================================================================


    /// <summary>
    /// Sweeps deprecated encounter types with zero remaining encounters.
    /// Uses DeprecationCleanupHelper for standardized per-item error isolation,
    /// grace period evaluation, dry-run support, and logging (per IMPLEMENTATION TENETS B20).
    /// </summary>
    public async Task<(StatusCodes, CleanDeprecatedResponse?)> CleanDeprecatedEncounterTypesAsync(
        CleanDeprecatedRequest body, CancellationToken cancellationToken = default)
    {
        // Load all encounter types and filter to deprecated ones
        var allKeys = await GetAllTypeKeysAsync(_encounterTypeStore, cancellationToken);
        var deprecatedTypes = new List<EncounterTypeData>();
        foreach (var key in allKeys)
        {
            var data = await _encounterTypeStore.GetAsync(key, cancellationToken);
            if (data is { IsDeprecated: true })
            {
                deprecatedTypes.Add(data);
            }
        }

        var result = await DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
            deprecatedTypes,
            getEntityId: t => t.TypeId,
            getDeprecatedAt: t => t.DeprecatedAt,
            hasInstancesAsync: async (t, ct) =>
            {
                // Check the type-encounter index to see if any encounters reference this type
                var typeEncounterIndex = await _typeEncounterIndexStore.GetAsync(
                    $"{TYPE_ENCOUNTER_INDEX_PREFIX}{t.Code}", ct);
                return typeEncounterIndex != null && typeEncounterIndex.EncounterIds.Count > 0;
            },
            deleteAndPublishAsync: async (t, ct) =>
            {
                // Delete the encounter type record
                await _encounterTypeStore.DeleteAsync($"{TYPE_KEY_PREFIX}{t.Code}", ct);

                // Remove from custom type index (only custom types are in this index)
                if (!t.IsBuiltIn)
                {
                    await RemoveFromCustomTypeIndexAsync(t.Code, ct);
                }

                // Delete the type-encounter index entry (should be empty, but clean up the key)
                await _typeEncounterIndexStore.DeleteAsync(
                    $"{TYPE_ENCOUNTER_INDEX_PREFIX}{t.Code}", ct);

                // Publish the lifecycle deleted event
                var now = DateTimeOffset.UtcNow;
                await _messageBus.PublishEncounterTypeDeletedAsync(
                    new EncounterTypeDeletedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = now,
                        TypeId = t.TypeId,
                        Code = t.Code,
                        Name = t.Name,
                        Description = t.Description,
                        IsBuiltIn = t.IsBuiltIn,
                        SortOrder = t.SortOrder,
                        IsActive = t.IsActive,
                        CreatedAt = DateTimeOffset.FromUnixTimeSeconds(t.CreatedAtUnix),
                        UpdatedAt = now,
                        IsDeprecated = t.IsDeprecated,
                        DeprecatedAt = t.DeprecatedAt,
                        DeprecationReason = t.DeprecationReason,
                        DeletedReason = "Clean-deprecated sweep: deprecated with zero active encounters"
                    }, ct);
            },
            body.GracePeriodDays,
            body.DryRun,
            _logger,
            _telemetryProvider,
            cancellationToken);

        return (StatusCodes.OK, new CleanDeprecatedResponse
        {
            Cleaned = result.Cleaned,
            Remaining = result.Remaining,
            Errors = result.Errors,
            CleanedIds = result.CleanedIds.ToList()
        });
    }
}
