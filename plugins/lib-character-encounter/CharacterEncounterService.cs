using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-character-encounter.tests")]

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

    // Key prefixes for different data types
    private const string ENCOUNTER_KEY_PREFIX = "enc-";
    private const string PERSPECTIVE_KEY_PREFIX = "pers-";
    private const string TYPE_KEY_PREFIX = "type-";
    private const string CHAR_INDEX_PREFIX = "char-idx-";
    private const string PAIR_INDEX_PREFIX = "pair-idx-";
    private const string LOCATION_INDEX_PREFIX = "loc-idx-";

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
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;

        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
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

        try
        {
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
                TypeId = typeId.ToString(),
                Code = body.Code.ToUpperInvariant(),
                Name = body.Name,
                Description = body.Description,
                IsBuiltIn = false,
                DefaultEmotionalImpact = body.DefaultEmotionalImpact?.ToString(),
                SortOrder = body.SortOrder ?? 100,
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await store.SaveAsync(key, data, cancellationToken: cancellationToken);

            _logger.LogInformation("Created encounter type {Code} with ID {TypeId}", body.Code, typeId);
            return (StatusCodes.OK, MapToEncounterTypeResponse(data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating encounter type {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "CreateEncounterType",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/type/create",
                details: new { body.Code },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Retrieves an encounter type by its code.
    /// </summary>
    public async Task<(StatusCodes, EncounterTypeResponse?)> GetEncounterTypeAsync(GetEncounterTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting encounter type {Code}", body.Code);

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting encounter type {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "GetEncounterType",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/type/get",
                details: new { body.Code },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Lists all encounter types with optional filtering.
    /// </summary>
    public async Task<(StatusCodes, EncounterTypeListResponse?)> ListEncounterTypesAsync(ListEncounterTypesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing encounter types");

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing encounter types");
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "ListEncounterTypes",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/type/list",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Updates an existing encounter type.
    /// </summary>
    public async Task<(StatusCodes, EncounterTypeResponse?)> UpdateEncounterTypeAsync(UpdateEncounterTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating encounter type {Code}", body.Code);

        try
        {
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
            if (body.DefaultEmotionalImpact != null) data.DefaultEmotionalImpact = body.DefaultEmotionalImpact.Value.ToString();
            if (body.SortOrder != null) data.SortOrder = body.SortOrder.Value;

            await store.SaveAsync(key, data, cancellationToken: cancellationToken);

            _logger.LogInformation("Updated encounter type {Code}", body.Code);
            return (StatusCodes.OK, MapToEncounterTypeResponse(data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating encounter type {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "UpdateEncounterType",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/type/update",
                details: new { body.Code },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deletes a custom encounter type.
    /// </summary>
    public async Task<StatusCodes> DeleteEncounterTypeAsync(DeleteEncounterTypeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting encounter type {Code}", body.Code);

        try
        {
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

            // Check if type is in use (would need to query encounters)
            // For now, we'll just soft-delete by marking inactive
            data.IsActive = false;
            await store.SaveAsync(key, data, cancellationToken: cancellationToken);

            _logger.LogInformation("Deleted (deactivated) encounter type {Code}", body.Code);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting encounter type {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "DeleteEncounterType",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/type/delete",
                details: new { body.Code },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    /// <summary>
    /// Seeds built-in encounter types.
    /// </summary>
    public async Task<(StatusCodes, SeedEncounterTypesResponse?)> SeedEncounterTypesAsync(SeedEncounterTypesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding encounter types, forceReset={ForceReset}", body.ForceReset);

        try
        {
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
                    existing.DefaultEmotionalImpact = builtIn.DefaultEmotionalImpact.ToString();
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding encounter types");
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "SeedEncounterTypes",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/type/seed",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
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

        try
        {
            // Validate minimum participants
            if (body.ParticipantIds.Count < 2)
            {
                _logger.LogWarning("Encounter requires at least 2 participants");
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

            var encounterId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var participantIds = body.ParticipantIds.Distinct().ToList();

            // Create encounter record
            var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);
            var encounterData = new EncounterData
            {
                EncounterId = encounterId.ToString(),
                Timestamp = body.Timestamp.ToUnixTimeSeconds(),
                RealmId = body.RealmId.ToString(),
                LocationId = body.LocationId?.ToString(),
                EncounterTypeCode = body.EncounterTypeCode.ToUpperInvariant(),
                Context = body.Context,
                Outcome = body.Outcome.ToString(),
                ParticipantIds = participantIds.Select(p => p.ToString()).ToList(),
                Metadata = body.Metadata,
                CreatedAtUnix = now.ToUnixTimeSeconds()
            };

            await encounterStore.SaveAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", encounterData, cancellationToken: cancellationToken);

            // Create perspectives for each participant
            var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
            var perspectives = new List<EncounterPerspectiveModel>();

            var providedPerspectives = body.Perspectives?.ToDictionary(p => p.CharacterId) ?? new Dictionary<Guid, PerspectiveInput>();
            var defaultEmotionalImpact = typeData.DefaultEmotionalImpact != null
                ? Enum.Parse<EmotionalImpact>(typeData.DefaultEmotionalImpact)
                : GetDefaultEmotionalImpactForOutcome(body.Outcome);

            foreach (var participantId in participantIds)
            {
                var perspectiveId = Guid.NewGuid();
                var hasProvidedPerspective = providedPerspectives.TryGetValue(participantId, out var provided);

                var perspectiveData = new PerspectiveData
                {
                    PerspectiveId = perspectiveId.ToString(),
                    EncounterId = encounterId.ToString(),
                    CharacterId = participantId.ToString(),
                    EmotionalImpact = hasProvidedPerspective && provided != null
                        ? provided.EmotionalImpact.ToString()
                        : defaultEmotionalImpact.ToString(),
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

                perspectives.Add(MapToPerspectiveModel(perspectiveData));
            }

            // Update pair indexes
            await UpdatePairIndexesAsync(participantIds, encounterId, cancellationToken);

            // Update location index if location provided
            if (body.LocationId.HasValue)
            {
                await AddToLocationIndexAsync(body.LocationId.Value, encounterId, cancellationToken);
            }

            // Publish event
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

            _logger.LogInformation("Recorded encounter {EncounterId} with {Count} perspectives",
                encounterId, perspectives.Count);

            return (StatusCodes.OK, new EncounterResponse
            {
                Encounter = MapToEncounterModel(encounterData),
                Perspectives = perspectives
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording encounter");
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "RecordEncounter",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/record",
                details: new { body.EncounterTypeCode, ParticipantCount = body.ParticipantIds.Count },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
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

        try
        {
            var perspectiveIds = await GetCharacterPerspectiveIdsAsync(body.CharacterId, cancellationToken);
            var encounters = new List<EncounterResponse>();
            var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
            var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);

            foreach (var perspectiveId in perspectiveIds)
            {
                var perspective = await perspectiveStore.GetAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", cancellationToken);
                if (perspective == null) continue;

                // Apply lazy decay
                perspective = await ApplyLazyDecayAsync(perspectiveStore, perspective, cancellationToken);

                // Filter by minimum memory strength
                if (body.MinimumMemoryStrength.HasValue && perspective.MemoryStrength < body.MinimumMemoryStrength.Value)
                    continue;

                var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{perspective.EncounterId}", cancellationToken);
                if (encounter == null) continue;

                // Apply filters
                if (!string.IsNullOrEmpty(body.EncounterTypeCode) &&
                    !encounter.EncounterTypeCode.Equals(body.EncounterTypeCode, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (body.Outcome.HasValue && encounter.Outcome != body.Outcome.Value.ToString())
                    continue;

                var encounterTimestamp = DateTimeOffset.FromUnixTimeSeconds(encounter.Timestamp);
                if (body.FromTimestamp.HasValue && encounterTimestamp < body.FromTimestamp.Value)
                    continue;
                if (body.ToTimestamp.HasValue && encounterTimestamp > body.ToTimestamp.Value)
                    continue;

                // Get all perspectives for this encounter
                var allPerspectives = await GetEncounterPerspectivesAsync(Guid.Parse(encounter.EncounterId), cancellationToken);

                encounters.Add(new EncounterResponse
                {
                    Encounter = MapToEncounterModel(encounter),
                    Perspectives = allPerspectives
                });
            }

            // Sort by timestamp descending
            encounters = encounters.OrderByDescending(e => e.Encounter.Timestamp).ToList();

            // Paginate
            var page = body.Page ?? 1;
            var pageSize = Math.Min(body.PageSize ?? _configuration.DefaultPageSize, _configuration.MaxPageSize);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying encounters for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "QueryByCharacter",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/query/by-character",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Queries encounters between two specific characters.
    /// </summary>
    public async Task<(StatusCodes, EncounterListResponse?)> QueryBetweenAsync(QueryBetweenRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying encounters between {CharA} and {CharB}", body.CharacterIdA, body.CharacterIdB);

        try
        {
            var encounterIds = await GetPairEncounterIdsAsync(body.CharacterIdA, body.CharacterIdB, cancellationToken);
            var encounters = new List<EncounterResponse>();
            var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);

            foreach (var encounterId in encounterIds)
            {
                var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
                if (encounter == null) continue;

                // Apply type filter
                if (!string.IsNullOrEmpty(body.EncounterTypeCode) &&
                    !encounter.EncounterTypeCode.Equals(body.EncounterTypeCode, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Get perspectives and check memory strength filter
                var perspectives = await GetEncounterPerspectivesAsync(Guid.Parse(encounter.EncounterId), cancellationToken);

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
            var page = body.Page ?? 1;
            var pageSize = Math.Min(body.PageSize ?? _configuration.DefaultPageSize, _configuration.MaxPageSize);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying encounters between {CharA} and {CharB}", body.CharacterIdA, body.CharacterIdB);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "QueryBetween",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/query/between",
                details: new { body.CharacterIdA, body.CharacterIdB },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Queries recent encounters at a location.
    /// </summary>
    public async Task<(StatusCodes, EncounterListResponse?)> QueryByLocationAsync(QueryByLocationRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying encounters at location {LocationId}", body.LocationId);

        try
        {
            var encounterIds = await GetLocationEncounterIdsAsync(body.LocationId, cancellationToken);
            var encounters = new List<EncounterResponse>();
            var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);

            foreach (var encounterId in encounterIds)
            {
                var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
                if (encounter == null) continue;

                // Apply filters
                if (!string.IsNullOrEmpty(body.EncounterTypeCode) &&
                    !encounter.EncounterTypeCode.Equals(body.EncounterTypeCode, StringComparison.OrdinalIgnoreCase))
                    continue;

                var encounterTimestamp = DateTimeOffset.FromUnixTimeSeconds(encounter.Timestamp);
                if (body.FromTimestamp.HasValue && encounterTimestamp < body.FromTimestamp.Value)
                    continue;

                var perspectives = await GetEncounterPerspectivesAsync(Guid.Parse(encounter.EncounterId), cancellationToken);

                encounters.Add(new EncounterResponse
                {
                    Encounter = MapToEncounterModel(encounter),
                    Perspectives = perspectives
                });
            }

            // Sort by timestamp descending
            encounters = encounters.OrderByDescending(e => e.Encounter.Timestamp).ToList();

            // Paginate
            var page = body.Page ?? 1;
            var pageSize = Math.Min(body.PageSize ?? _configuration.DefaultPageSize, _configuration.MaxPageSize);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying encounters at location {LocationId}", body.LocationId);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "QueryByLocation",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/query/by-location",
                details: new { body.LocationId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Checks if two characters have met.
    /// </summary>
    public async Task<(StatusCodes, HasMetResponse?)> HasMetAsync(HasMetRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking if {CharA} has met {CharB}", body.CharacterIdA, body.CharacterIdB);

        try
        {
            var encounterIds = await GetPairEncounterIdsAsync(body.CharacterIdA, body.CharacterIdB, cancellationToken);

            return (StatusCodes.OK, new HasMetResponse
            {
                HasMet = encounterIds.Count > 0,
                EncounterCount = encounterIds.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if {CharA} has met {CharB}", body.CharacterIdA, body.CharacterIdB);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "HasMet",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/has-met",
                details: new { body.CharacterIdA, body.CharacterIdB },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Calculates aggregate sentiment toward another character.
    /// </summary>
    public async Task<(StatusCodes, SentimentResponse?)> GetSentimentAsync(GetSentimentRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting sentiment of {CharacterId} toward {TargetId}", body.CharacterId, body.TargetCharacterId);

        try
        {
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

            var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
            var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);

            var totalSentiment = 0.0f;
            var totalWeight = 0.0f;
            var emotionCounts = new Dictionary<EmotionalImpact, int>();

            foreach (var encounterId in encounterIds)
            {
                var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
                if (encounter == null) continue;

                // Find the perspective for our character
                var perspectives = await GetEncounterPerspectivesAsync(Guid.Parse(encounterId), cancellationToken);
                var perspective = perspectives.FirstOrDefault(p => p.CharacterId == body.CharacterId);
                if (perspective == null) continue;

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sentiment for {CharacterId} toward {TargetId}",
                body.CharacterId, body.TargetCharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "GetSentiment",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/get-sentiment",
                details: new { body.CharacterId, body.TargetCharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Batch retrieves sentiment toward multiple targets.
    /// </summary>
    public async Task<(StatusCodes, BatchSentimentResponse?)> BatchGetSentimentAsync(BatchGetSentimentRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Batch getting sentiment for {CharacterId} toward {Count} targets",
            body.CharacterId, body.TargetCharacterIds.Count);

        try
        {
            if (body.TargetCharacterIds.Count > _configuration.MaxBatchSize)
            {
                _logger.LogWarning("Batch size {Size} exceeds maximum {Max}",
                    body.TargetCharacterIds.Count, _configuration.MaxBatchSize);
                return (StatusCodes.BadRequest, null);
            }

            var sentiments = new List<SentimentResponse>();

            foreach (var targetId in body.TargetCharacterIds)
            {
                var (status, sentiment) = await GetSentimentAsync(new GetSentimentRequest
                {
                    CharacterId = body.CharacterId,
                    TargetCharacterId = targetId
                }, cancellationToken);

                if (status == StatusCodes.OK && sentiment != null)
                {
                    sentiments.Add(sentiment);
                }
            }

            return (StatusCodes.OK, new BatchSentimentResponse
            {
                CharacterId = body.CharacterId,
                Sentiments = sentiments
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error batch getting sentiments for {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "BatchGetSentiment",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/batch-get",
                details: new { body.CharacterId, TargetCount = body.TargetCharacterIds.Count },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
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

        try
        {
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting perspective for {CharacterId} on {EncounterId}",
                body.CharacterId, body.EncounterId);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "GetPerspective",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/get-perspective",
                details: new { body.CharacterId, body.EncounterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Updates a character's perspective on an encounter.
    /// </summary>
    public async Task<(StatusCodes, PerspectiveResponse?)> UpdatePerspectiveAsync(UpdatePerspectiveRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating perspective of {CharacterId} on encounter {EncounterId}",
            body.CharacterId, body.EncounterId);

        try
        {
            var perspective = await FindPerspectiveAsync(body.EncounterId, body.CharacterId, cancellationToken);
            if (perspective == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var previousEmotion = perspective.EmotionalImpact;
            var previousSentiment = perspective.SentimentShift;

            // Apply updates
            if (body.EmotionalImpact.HasValue) perspective.EmotionalImpact = body.EmotionalImpact.Value.ToString();
            if (body.SentimentShift.HasValue) perspective.SentimentShift = body.SentimentShift.Value;
            if (body.RememberedAs != null) perspective.RememberedAs = body.RememberedAs;
            perspective.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
            await perspectiveStore.SaveAsync($"{PERSPECTIVE_KEY_PREFIX}{perspective.PerspectiveId}", perspective, cancellationToken: cancellationToken);

            // Publish event
            await _messageBus.TryPublishAsync(ENCOUNTER_PERSPECTIVE_UPDATED_TOPIC, new EncounterPerspectiveUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                EncounterId = body.EncounterId,
                CharacterId = body.CharacterId,
                PerspectiveId = Guid.Parse(perspective.PerspectiveId),
                PreviousEmotionalImpact = previousEmotion,
                NewEmotionalImpact = body.EmotionalImpact?.ToString(),
                PreviousSentimentShift = previousSentiment,
                NewSentimentShift = body.SentimentShift
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Updated perspective {PerspectiveId}", perspective.PerspectiveId);

            return (StatusCodes.OK, new PerspectiveResponse
            {
                Perspective = MapToPerspectiveModel(perspective)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating perspective for {CharacterId} on {EncounterId}",
                body.CharacterId, body.EncounterId);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "UpdatePerspective",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/update-perspective",
                details: new { body.CharacterId, body.EncounterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Refreshes memory strength for an encounter.
    /// </summary>
    public async Task<(StatusCodes, PerspectiveResponse?)> RefreshMemoryAsync(RefreshMemoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Refreshing memory of {CharacterId} for encounter {EncounterId}",
            body.CharacterId, body.EncounterId);

        try
        {
            var perspective = await FindPerspectiveAsync(body.EncounterId, body.CharacterId, cancellationToken);
            if (perspective == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var previousStrength = perspective.MemoryStrength;
            var boost = body.StrengthBoost ?? _configuration.MemoryRefreshBoost;
            perspective.MemoryStrength = Math.Clamp(perspective.MemoryStrength + boost, 0, 1);
            perspective.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
            await perspectiveStore.SaveAsync($"{PERSPECTIVE_KEY_PREFIX}{perspective.PerspectiveId}", perspective, cancellationToken: cancellationToken);

            // Publish event
            await _messageBus.TryPublishAsync(ENCOUNTER_MEMORY_REFRESHED_TOPIC, new EncounterMemoryRefreshedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                EncounterId = body.EncounterId,
                CharacterId = body.CharacterId,
                PerspectiveId = Guid.Parse(perspective.PerspectiveId),
                PreviousStrength = previousStrength,
                NewStrength = perspective.MemoryStrength
            }, cancellationToken: cancellationToken);

            _logger.LogDebug("Refreshed memory {PerspectiveId}: {OldStrength} -> {NewStrength}",
                perspective.PerspectiveId, previousStrength, perspective.MemoryStrength);

            return (StatusCodes.OK, new PerspectiveResponse
            {
                Perspective = MapToPerspectiveModel(perspective)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing memory for {CharacterId} on {EncounterId}",
                body.CharacterId, body.EncounterId);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "RefreshMemory",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/refresh-memory",
                details: new { body.CharacterId, body.EncounterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
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

        try
        {
            var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);
            var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{body.EncounterId}", cancellationToken);

            if (encounter == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var participantIds = encounter.ParticipantIds.Select(Guid.Parse).ToList();

            // Delete all perspectives
            var perspectivesDeleted = await DeleteEncounterPerspectivesAsync(body.EncounterId, cancellationToken);

            // Delete encounter
            await encounterStore.DeleteAsync($"{ENCOUNTER_KEY_PREFIX}{body.EncounterId}", cancellationToken);

            // Update pair indexes
            await RemoveFromPairIndexesAsync(participantIds, body.EncounterId, cancellationToken);

            // Update location index
            if (!string.IsNullOrEmpty(encounter.LocationId))
            {
                await RemoveFromLocationIndexAsync(Guid.Parse(encounter.LocationId), body.EncounterId, cancellationToken);
            }

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

            _logger.LogInformation("Deleted encounter {EncounterId} with {Count} perspectives",
                body.EncounterId, perspectivesDeleted);

            return (StatusCodes.OK, new DeleteEncounterResponse
            {
                EncounterId = body.EncounterId,
                PerspectivesDeleted = perspectivesDeleted
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting encounter {EncounterId}", body.EncounterId);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "DeleteEncounter",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/delete",
                details: new { body.EncounterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deletes all encounters involving a character.
    /// </summary>
    public async Task<(StatusCodes, DeleteByCharacterResponse?)> DeleteByCharacterAsync(DeleteByCharacterRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting all encounters for character {CharacterId}", body.CharacterId);

        try
        {
            var perspectiveIds = await GetCharacterPerspectiveIdsAsync(body.CharacterId, cancellationToken);
            var encountersDeleted = 0;
            var perspectivesDeleted = 0;

            var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
            var processedEncounterIds = new HashSet<string>();

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
            foreach (var encounterId in processedEncounterIds)
            {
                var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
                if (encounter == null) continue;

                // Delete all other perspectives for this encounter
                perspectivesDeleted += await DeleteEncounterPerspectivesAsync(Guid.Parse(encounterId), cancellationToken);

                // Delete the encounter
                await encounterStore.DeleteAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
                encountersDeleted++;

                // Update pair indexes
                var participantIds = encounter.ParticipantIds.Select(Guid.Parse).ToList();
                await RemoveFromPairIndexesAsync(participantIds, Guid.Parse(encounterId), cancellationToken);

                // Update location index
                if (!string.IsNullOrEmpty(encounter.LocationId))
                {
                    await RemoveFromLocationIndexAsync(Guid.Parse(encounter.LocationId), Guid.Parse(encounterId), cancellationToken);
                }

                // Publish event
                await _messageBus.TryPublishAsync(ENCOUNTER_DELETED_TOPIC, new EncounterDeletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    EncounterId = Guid.Parse(encounterId),
                    ParticipantIds = participantIds,
                    PerspectivesDeleted = encounter.ParticipantIds.Count,
                    DeletedByCharacterCleanup = true,
                    CleanupCharacterId = body.CharacterId
                }, cancellationToken: cancellationToken);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting encounters for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "DeleteByCharacter",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/delete-by-character",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Triggers memory decay processing.
    /// </summary>
    public async Task<(StatusCodes, DecayMemoriesResponse?)> DecayMemoriesAsync(DecayMemoriesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing memory decay, characterId={CharacterId}, dryRun={DryRun}",
            body.CharacterId, body.DryRun);

        try
        {
            if (!_configuration.MemoryDecayEnabled)
            {
                _logger.LogInformation("Memory decay is disabled");
                return (StatusCodes.OK, new DecayMemoriesResponse
                {
                    PerspectivesProcessed = 0,
                    MemoriesFaded = 0,
                    DryRun = body.DryRun ?? false
                });
            }

            var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
            var perspectivesProcessed = 0;
            var memoriesFaded = 0;
            var dryRun = body.DryRun ?? false;

            IEnumerable<Guid> perspectiveIds;
            if (body.CharacterId.HasValue)
            {
                perspectiveIds = await GetCharacterPerspectiveIdsAsync(body.CharacterId.Value, cancellationToken);
            }
            else
            {
                // Process all perspectives (in production, this would need batching)
                perspectiveIds = await GetAllPerspectiveIdsAsync(cancellationToken);
            }

            foreach (var perspectiveId in perspectiveIds)
            {
                var perspective = await perspectiveStore.GetAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", cancellationToken);
                if (perspective == null) continue;

                var (decayed, faded) = CalculateDecay(perspective);

                if (decayed)
                {
                    perspectivesProcessed++;

                    if (!dryRun)
                    {
                        perspective.MemoryStrength = Math.Max(0, perspective.MemoryStrength - GetDecayAmount(perspective));
                        perspective.LastDecayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        await perspectiveStore.SaveAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", perspective, cancellationToken: cancellationToken);
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
                                EncounterId = Guid.Parse(perspective.EncounterId),
                                CharacterId = Guid.Parse(perspective.CharacterId),
                                PerspectiveId = Guid.Parse(perspective.PerspectiveId),
                                PreviousStrength = perspective.MemoryStrength + GetDecayAmount(perspective),
                                NewStrength = perspective.MemoryStrength,
                                FadeThreshold = (float)_configuration.MemoryFadeThreshold
                            }, cancellationToken: cancellationToken);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing memory decay");
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "DecayMemories",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-encounter/decay-memories",
                details: new { body.CharacterId, body.DryRun },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    // ============================================================================
    // Permission Registration
    // ============================================================================

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering CharacterEncounter service permissions...");
        try
        {
            await CharacterEncounterPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
            _logger.LogInformation("CharacterEncounter service permissions registered");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register CharacterEncounter service permissions");
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "RegisterServicePermissions",
                ex.GetType().Name,
                ex.Message,
                dependency: "permission");
            throw;
        }
    }

    // ============================================================================
    // Helper Methods
    // ============================================================================

    private async Task<EncounterTypeData> SeedBuiltInTypeAsync(IStateStore<EncounterTypeData> store, BuiltInEncounterType builtIn, CancellationToken cancellationToken)
    {
        var data = new EncounterTypeData
        {
            TypeId = Guid.NewGuid().ToString(),
            Code = builtIn.Code,
            Name = builtIn.Name,
            Description = builtIn.Description,
            IsBuiltIn = true,
            DefaultEmotionalImpact = builtIn.DefaultEmotionalImpact.ToString(),
            SortOrder = builtIn.SortOrder,
            IsActive = true,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await store.SaveAsync($"{TYPE_KEY_PREFIX}{builtIn.Code}", data, cancellationToken: cancellationToken);
        return data;
    }

    private async Task EnsureBuiltInTypesSeededAsync(CancellationToken cancellationToken)
    {
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

    private Task<List<string>> GetAllTypeKeysAsync(IStateStore<EncounterTypeData> store, CancellationToken cancellationToken)
    {
        // For MySQL backend, we need to query by prefix
        // This is a simplified implementation - in production, use proper prefix queries
        var keys = new List<string>();
        foreach (var builtIn in BuiltInTypes)
        {
            keys.Add($"{TYPE_KEY_PREFIX}{builtIn.Code}");
        }
        // Also check for custom types by trying to query with pattern
        // For now, just return built-in type keys plus any custom ones we know about
        return Task.FromResult(keys);
    }

    private async Task<List<Guid>> GetCharacterPerspectiveIdsAsync(Guid characterId, CancellationToken cancellationToken)
    {
        var indexStore = _stateStoreFactory.GetStore<CharacterIndexData>(StateStoreDefinitions.CharacterEncounter);
        var index = await indexStore.GetAsync($"{CHAR_INDEX_PREFIX}{characterId}", cancellationToken);
        return index?.PerspectiveIds.Select(Guid.Parse).ToList() ?? new List<Guid>();
    }

    private async Task<List<Guid>> GetPairEncounterIdsAsync(Guid charA, Guid charB, CancellationToken cancellationToken)
    {
        var pairKey = GetPairKey(charA, charB);
        var indexStore = _stateStoreFactory.GetStore<PairIndexData>(StateStoreDefinitions.CharacterEncounter);
        var index = await indexStore.GetAsync($"{PAIR_INDEX_PREFIX}{pairKey}", cancellationToken);
        return index?.EncounterIds.Select(Guid.Parse).ToList() ?? new List<Guid>();
    }

    private async Task<List<Guid>> GetLocationEncounterIdsAsync(Guid locationId, CancellationToken cancellationToken)
    {
        var indexStore = _stateStoreFactory.GetStore<LocationIndexData>(StateStoreDefinitions.CharacterEncounter);
        var index = await indexStore.GetAsync($"{LOCATION_INDEX_PREFIX}{locationId}", cancellationToken);
        return index?.EncounterIds.Select(Guid.Parse).ToList() ?? new List<Guid>();
    }

    private async Task AddToCharacterIndexAsync(Guid characterId, Guid perspectiveId, CancellationToken cancellationToken)
    {
        var indexStore = _stateStoreFactory.GetStore<CharacterIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{CHAR_INDEX_PREFIX}{characterId}";
        var index = await indexStore.GetAsync(key, cancellationToken) ?? new CharacterIndexData { CharacterId = characterId.ToString() };
        if (!index.PerspectiveIds.Contains(perspectiveId.ToString()))
        {
            index.PerspectiveIds.Add(perspectiveId.ToString());
            await indexStore.SaveAsync(key, index, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromCharacterIndexAsync(Guid characterId, Guid perspectiveId, CancellationToken cancellationToken)
    {
        var indexStore = _stateStoreFactory.GetStore<CharacterIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{CHAR_INDEX_PREFIX}{characterId}";
        var index = await indexStore.GetAsync(key, cancellationToken);
        if (index != null)
        {
            index.PerspectiveIds.Remove(perspectiveId.ToString());
            await indexStore.SaveAsync(key, index, cancellationToken: cancellationToken);
        }
    }

    private async Task UpdatePairIndexesAsync(List<Guid> participantIds, Guid encounterId, CancellationToken cancellationToken)
    {
        var indexStore = _stateStoreFactory.GetStore<PairIndexData>(StateStoreDefinitions.CharacterEncounter);

        // Create pair indexes for each unique pair
        for (var i = 0; i < participantIds.Count; i++)
        {
            for (var j = i + 1; j < participantIds.Count; j++)
            {
                var pairKey = GetPairKey(participantIds[i], participantIds[j]);
                var key = $"{PAIR_INDEX_PREFIX}{pairKey}";
                var index = await indexStore.GetAsync(key, cancellationToken) ?? new PairIndexData
                {
                    CharacterIdA = participantIds[i] < participantIds[j] ? participantIds[i].ToString() : participantIds[j].ToString(),
                    CharacterIdB = participantIds[i] < participantIds[j] ? participantIds[j].ToString() : participantIds[i].ToString()
                };

                if (!index.EncounterIds.Contains(encounterId.ToString()))
                {
                    index.EncounterIds.Add(encounterId.ToString());
                    await indexStore.SaveAsync(key, index, cancellationToken: cancellationToken);
                }
            }
        }
    }

    private async Task RemoveFromPairIndexesAsync(List<Guid> participantIds, Guid encounterId, CancellationToken cancellationToken)
    {
        var indexStore = _stateStoreFactory.GetStore<PairIndexData>(StateStoreDefinitions.CharacterEncounter);

        for (var i = 0; i < participantIds.Count; i++)
        {
            for (var j = i + 1; j < participantIds.Count; j++)
            {
                var pairKey = GetPairKey(participantIds[i], participantIds[j]);
                var key = $"{PAIR_INDEX_PREFIX}{pairKey}";
                var index = await indexStore.GetAsync(key, cancellationToken);
                if (index != null)
                {
                    index.EncounterIds.Remove(encounterId.ToString());
                    await indexStore.SaveAsync(key, index, cancellationToken: cancellationToken);
                }
            }
        }
    }

    private async Task AddToLocationIndexAsync(Guid locationId, Guid encounterId, CancellationToken cancellationToken)
    {
        var indexStore = _stateStoreFactory.GetStore<LocationIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{LOCATION_INDEX_PREFIX}{locationId}";
        var index = await indexStore.GetAsync(key, cancellationToken) ?? new LocationIndexData { LocationId = locationId.ToString() };
        if (!index.EncounterIds.Contains(encounterId.ToString()))
        {
            index.EncounterIds.Add(encounterId.ToString());
            await indexStore.SaveAsync(key, index, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromLocationIndexAsync(Guid locationId, Guid encounterId, CancellationToken cancellationToken)
    {
        var indexStore = _stateStoreFactory.GetStore<LocationIndexData>(StateStoreDefinitions.CharacterEncounter);
        var key = $"{LOCATION_INDEX_PREFIX}{locationId}";
        var index = await indexStore.GetAsync(key, cancellationToken);
        if (index != null)
        {
            index.EncounterIds.Remove(encounterId.ToString());
            await indexStore.SaveAsync(key, index, cancellationToken: cancellationToken);
        }
    }

    private static string GetPairKey(Guid charA, Guid charB)
    {
        // Always put the smaller GUID first for consistent keying
        return charA < charB ? $"{charA}:{charB}" : $"{charB}:{charA}";
    }

    private async Task<List<EncounterPerspectiveModel>> GetEncounterPerspectivesAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
        var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);
        var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
        if (encounter == null) return new List<EncounterPerspectiveModel>();

        var perspectives = new List<EncounterPerspectiveModel>();
        foreach (var participantId in encounter.ParticipantIds)
        {
            var perspective = await FindPerspectiveByEncounterAndCharacterAsync(encounterId, Guid.Parse(participantId), cancellationToken);
            if (perspective != null)
            {
                // Apply lazy decay
                perspective = await ApplyLazyDecayAsync(perspectiveStore, perspective, cancellationToken);
                perspectives.Add(MapToPerspectiveModel(perspective));
            }
        }
        return perspectives;
    }

    private async Task<PerspectiveData?> FindPerspectiveAsync(Guid encounterId, Guid characterId, CancellationToken cancellationToken)
    {
        return await FindPerspectiveByEncounterAndCharacterAsync(encounterId, characterId, cancellationToken);
    }

    private async Task<PerspectiveData?> FindPerspectiveByEncounterAndCharacterAsync(Guid encounterId, Guid characterId, CancellationToken cancellationToken)
    {
        // Get character's perspective IDs and find the one for this encounter
        var perspectiveIds = await GetCharacterPerspectiveIdsAsync(characterId, cancellationToken);
        var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);

        foreach (var perspectiveId in perspectiveIds)
        {
            var perspective = await perspectiveStore.GetAsync($"{PERSPECTIVE_KEY_PREFIX}{perspectiveId}", cancellationToken);
            if (perspective != null && perspective.EncounterId == encounterId.ToString())
            {
                return perspective;
            }
        }
        return null;
    }

    private async Task<int> DeleteEncounterPerspectivesAsync(Guid encounterId, CancellationToken cancellationToken)
    {
        var perspectiveStore = _stateStoreFactory.GetStore<PerspectiveData>(StateStoreDefinitions.CharacterEncounter);
        var encounterStore = _stateStoreFactory.GetStore<EncounterData>(StateStoreDefinitions.CharacterEncounter);
        var encounter = await encounterStore.GetAsync($"{ENCOUNTER_KEY_PREFIX}{encounterId}", cancellationToken);
        if (encounter == null) return 0;

        var deleted = 0;
        foreach (var participantId in encounter.ParticipantIds)
        {
            var perspective = await FindPerspectiveByEncounterAndCharacterAsync(encounterId, Guid.Parse(participantId), cancellationToken);
            if (perspective != null)
            {
                await perspectiveStore.DeleteAsync($"{PERSPECTIVE_KEY_PREFIX}{perspective.PerspectiveId}", cancellationToken);
                await RemoveFromCharacterIndexAsync(Guid.Parse(participantId), Guid.Parse(perspective.PerspectiveId), cancellationToken);
                deleted++;
            }
        }
        return deleted;
    }

    private Task<IEnumerable<Guid>> GetAllPerspectiveIdsAsync(CancellationToken cancellationToken)
    {
        // This is a simplified implementation
        // In production, this would need proper query support or batching
        return Task.FromResult<IEnumerable<Guid>>(new List<Guid>());
    }

    private async Task<PerspectiveData> ApplyLazyDecayAsync(IStateStore<PerspectiveData> store, PerspectiveData perspective, CancellationToken cancellationToken)
    {
        if (!_configuration.MemoryDecayEnabled || _configuration.MemoryDecayMode != "lazy")
            return perspective;

        var (needsDecay, _) = CalculateDecay(perspective);
        if (!needsDecay) return perspective;

        var decayAmount = GetDecayAmount(perspective);
        var previousStrength = perspective.MemoryStrength;
        perspective.MemoryStrength = Math.Max(0, perspective.MemoryStrength - decayAmount);
        perspective.LastDecayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await store.SaveAsync($"{PERSPECTIVE_KEY_PREFIX}{perspective.PerspectiveId}", perspective, cancellationToken: cancellationToken);

        // Check if faded below threshold
        if (previousStrength >= _configuration.MemoryFadeThreshold && perspective.MemoryStrength < _configuration.MemoryFadeThreshold)
        {
            await _messageBus.TryPublishAsync(ENCOUNTER_MEMORY_FADED_TOPIC, new EncounterMemoryFadedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                EncounterId = Guid.Parse(perspective.EncounterId),
                CharacterId = Guid.Parse(perspective.CharacterId),
                PerspectiveId = Guid.Parse(perspective.PerspectiveId),
                PreviousStrength = previousStrength,
                NewStrength = perspective.MemoryStrength,
                FadeThreshold = (float)_configuration.MemoryFadeThreshold
            }, cancellationToken: cancellationToken);
        }

        return perspective;
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

    private static float? GetDefaultSentimentShiftForOutcome(EncounterOutcome outcome)
    {
        return outcome switch
        {
            EncounterOutcome.POSITIVE => 0.2f,
            EncounterOutcome.NEGATIVE => -0.2f,
            EncounterOutcome.NEUTRAL => 0f,
            EncounterOutcome.MEMORABLE => 0.1f,
            EncounterOutcome.TRANSFORMATIVE => 0.3f,
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
            TypeId = Guid.Parse(data.TypeId),
            Code = data.Code,
            Name = data.Name,
            Description = data.Description,
            IsBuiltIn = data.IsBuiltIn,
            DefaultEmotionalImpact = data.DefaultEmotionalImpact != null ? Enum.Parse<EmotionalImpact>(data.DefaultEmotionalImpact) : null,
            SortOrder = data.SortOrder,
            IsActive = data.IsActive,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix)
        };
    }

    private static EncounterModel MapToEncounterModel(EncounterData data)
    {
        return new EncounterModel
        {
            EncounterId = Guid.Parse(data.EncounterId),
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(data.Timestamp),
            RealmId = Guid.Parse(data.RealmId),
            LocationId = data.LocationId != null ? Guid.Parse(data.LocationId) : null,
            EncounterTypeCode = data.EncounterTypeCode,
            Context = data.Context,
            Outcome = Enum.Parse<EncounterOutcome>(data.Outcome),
            ParticipantIds = data.ParticipantIds.Select(Guid.Parse).ToList(),
            Metadata = data.Metadata,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix)
        };
    }

    private static EncounterPerspectiveModel MapToPerspectiveModel(PerspectiveData data)
    {
        return new EncounterPerspectiveModel
        {
            PerspectiveId = Guid.Parse(data.PerspectiveId),
            EncounterId = Guid.Parse(data.EncounterId),
            CharacterId = Guid.Parse(data.CharacterId),
            EmotionalImpact = Enum.Parse<EmotionalImpact>(data.EmotionalImpact),
            SentimentShift = data.SentimentShift,
            MemoryStrength = data.MemoryStrength,
            RememberedAs = data.RememberedAs,
            LastDecayedAt = data.LastDecayedAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(data.LastDecayedAtUnix.Value) : null,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix),
            UpdatedAt = data.UpdatedAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(data.UpdatedAtUnix.Value) : null
        };
    }
}

// ============================================================================
// Internal Data Models
// ============================================================================

internal record BuiltInEncounterType(string Code, string Name, string Description, EmotionalImpact DefaultEmotionalImpact, int SortOrder);

internal class EncounterTypeData
{
    public string TypeId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsBuiltIn { get; set; }
    public string? DefaultEmotionalImpact { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public long CreatedAtUnix { get; set; }
}

internal class EncounterData
{
    public string EncounterId { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public string RealmId { get; set; } = string.Empty;
    public string? LocationId { get; set; }
    public string EncounterTypeCode { get; set; } = string.Empty;
    public string? Context { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public List<string> ParticipantIds { get; set; } = new();
    public object? Metadata { get; set; }
    public long CreatedAtUnix { get; set; }
}

internal class PerspectiveData
{
    public string PerspectiveId { get; set; } = string.Empty;
    public string EncounterId { get; set; } = string.Empty;
    public string CharacterId { get; set; } = string.Empty;
    public string EmotionalImpact { get; set; } = string.Empty;
    public float? SentimentShift { get; set; }
    public float MemoryStrength { get; set; } = 1.0f;
    public string? RememberedAs { get; set; }
    public long? LastDecayedAtUnix { get; set; }
    public long CreatedAtUnix { get; set; }
    public long? UpdatedAtUnix { get; set; }
}

internal class CharacterIndexData
{
    public string CharacterId { get; set; } = string.Empty;
    public List<string> PerspectiveIds { get; set; } = new();
}

internal class PairIndexData
{
    public string CharacterIdA { get; set; } = string.Empty;
    public string CharacterIdB { get; set; } = string.Empty;
    public List<string> EncounterIds { get; set; } = new();
}

internal class LocationIndexData
{
    public string LocationId { get; set; } = string.Empty;
    public List<string> EncounterIds { get; set; } = new();
}
