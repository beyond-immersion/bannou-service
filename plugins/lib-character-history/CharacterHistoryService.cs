using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.History;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Note: InternalsVisibleTo attributes are in AssemblyInfo.cs

namespace BeyondImmersion.BannouService.CharacterHistory;

/// <summary>
/// Service implementation for character history and backstory management.
/// Provides storage for historical event participation and machine-readable backstory elements.
/// Uses shared History infrastructure helpers for dual-index and backstory storage patterns.
/// </summary>
[BannouService("character-history", typeof(ICharacterHistoryService), lifetime: ServiceLifetime.Scoped)]
public partial class CharacterHistoryService : ICharacterHistoryService
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<CharacterHistoryService> _logger;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly CharacterHistoryServiceConfiguration _configuration;
    private readonly IDualIndexHelper<ParticipationData> _participationHelper;
    private readonly IBackstoryStorageHelper<BackstoryData, BackstoryElementData> _backstoryHelper;

    private const string PARTICIPATION_KEY_PREFIX = "participation-";
    private const string PARTICIPATION_BY_EVENT_KEY_PREFIX = "participation-event-";
    private const string BACKSTORY_KEY_PREFIX = "backstory-";
    private const string PARTICIPATION_INDEX_KEY_PREFIX = "participation-index-";

    // Event topics - must match schema definitions in character-history-events.yaml
    private const string PARTICIPATION_RECORDED_TOPIC = "character-history.participation.recorded";
    private const string PARTICIPATION_DELETED_TOPIC = "character-history.participation.deleted";
    private const string BACKSTORY_CREATED_TOPIC = "character-history.backstory.created";
    private const string BACKSTORY_UPDATED_TOPIC = "character-history.backstory.updated";
    private const string BACKSTORY_DELETED_TOPIC = "character-history.backstory.deleted";
    private const string HISTORY_DELETED_TOPIC = "character-history.deleted";

    /// <summary>
    /// Initializes the CharacterHistory service with required dependencies.
    /// </summary>
    public CharacterHistoryService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<CharacterHistoryService> logger,
        IEventConsumer eventConsumer,
        CharacterHistoryServiceConfiguration configuration)
    {
        _messageBus = messageBus;
        _logger = logger;
        _stateStoreFactory = stateStoreFactory;
        _configuration = configuration;

        // Initialize participation helper using shared dual-index infrastructure
        _participationHelper = new DualIndexHelper<ParticipationData>(
            stateStoreFactory,
            StateStoreDefinitions.CharacterHistory,
            PARTICIPATION_KEY_PREFIX,
            PARTICIPATION_INDEX_KEY_PREFIX,
            PARTICIPATION_BY_EVENT_KEY_PREFIX);

        // Initialize backstory helper using shared backstory storage infrastructure
        _backstoryHelper = new BackstoryStorageHelper<BackstoryData, BackstoryElementData>(
            new BackstoryStorageConfiguration<BackstoryData, BackstoryElementData>
            {
                StateStoreFactory = stateStoreFactory,
                StateStoreName = StateStoreDefinitions.CharacterHistory,
                KeyPrefix = BACKSTORY_KEY_PREFIX,
                ElementMatcher = new BackstoryElementMatcher<BackstoryElementData>(
                    getType: e => e.ElementType.ToString(),
                    getKey: e => e.Key,
                    copyValues: (src, tgt) =>
                    {
                        tgt.Value = src.Value;
                        tgt.Strength = src.Strength;
                        tgt.RelatedEntityId = src.RelatedEntityId;
                        tgt.RelatedEntityType = src.RelatedEntityType;
                    },
                    clone: e => new BackstoryElementData
                    {
                        ElementType = e.ElementType,
                        Key = e.Key,
                        Value = e.Value,
                        Strength = e.Strength,
                        RelatedEntityId = e.RelatedEntityId,
                        RelatedEntityType = e.RelatedEntityType
                    }),
                GetEntityId = b => b.CharacterId.ToString(),
                SetEntityId = (b, id) => b.CharacterId = Guid.Parse(id),
                GetElements = b => b.Elements,
                SetElements = (b, els) => b.Elements = els,
                GetCreatedAtUnix = b => b.CreatedAtUnix,
                SetCreatedAtUnix = (b, ts) => b.CreatedAtUnix = ts,
                GetUpdatedAtUnix = b => b.UpdatedAtUnix,
                SetUpdatedAtUnix = (b, ts) => b.UpdatedAtUnix = ts
            });

        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    // ============================================================================
    // Participation Methods
    // ============================================================================

    /// <summary>
    /// Records a character's participation in a historical event.
    /// Returns Conflict if the character already has a participation record for this event.
    /// </summary>
    public async Task<(StatusCodes, HistoricalParticipation?)> RecordParticipationAsync(RecordParticipationRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Recording participation for character {CharacterId} in event {EventId}",
            body.CharacterId, body.EventId);

        try
        {
            // Check for existing participation - schema documents 409 Conflict for duplicates
            var existingRecords = await _participationHelper.GetRecordsByPrimaryKeyAsync(
                body.CharacterId.ToString(),
                cancellationToken);

            var duplicateExists = existingRecords.Any(r => r.EventId == body.EventId);
            if (duplicateExists)
            {
                _logger.LogWarning("Character {CharacterId} already has participation record for event {EventId}",
                    body.CharacterId, body.EventId);
                return (StatusCodes.Conflict, null);
            }

            var participationId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var participationData = new ParticipationData
            {
                ParticipationId = participationId,
                CharacterId = body.CharacterId,
                EventId = body.EventId,
                EventName = body.EventName,
                EventCategory = body.EventCategory,
                Role = body.Role,
                EventDateUnix = body.EventDate.ToUnixTimeSeconds(),
                Significance = body.Significance,
                Metadata = body.Metadata,
                CreatedAtUnix = now.ToUnixTimeSeconds()
            };

            // Use helper to store record and update both indices
            await _participationHelper.AddRecordAsync(
                participationData,
                participationId.ToString(),
                body.CharacterId.ToString(),
                body.EventId.ToString(),
                cancellationToken);

            // Publish typed event per FOUNDATION TENETS
            await _messageBus.TryPublishAsync(PARTICIPATION_RECORDED_TOPIC, new CharacterParticipationRecordedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                CharacterId = body.CharacterId,
                HistoricalEventId = body.EventId,
                ParticipationId = participationId,
                Role = body.Role
            }, cancellationToken: cancellationToken);

            // Register character reference with lib-resource for cleanup coordination
            await RegisterCharacterReferenceAsync(participationId.ToString(), body.CharacterId, cancellationToken);

            _logger.LogInformation("Recorded participation {ParticipationId} for character {CharacterId}",
                participationId, body.CharacterId);

            return (StatusCodes.OK, MapToHistoricalParticipation(participationData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording participation for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-history",
                "RecordParticipation",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-history/record-participation",
                details: new { body.CharacterId, body.EventId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets a character's historical event participation records.
    /// Uses server-side MySQL JSON queries for efficient pagination per IMPLEMENTATION TENETS.
    /// </summary>
    public async Task<(StatusCodes, ParticipationListResponse?)> GetParticipationAsync(GetParticipationRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting participation for character {CharacterId}", body.CharacterId);

        try
        {
            var jsonStore = _stateStoreFactory.GetJsonQueryableStore<ParticipationData>(
                StateStoreDefinitions.CharacterHistory);

            var conditions = BuildParticipationQueryConditions(
                "$.CharacterId", body.CharacterId,
                eventCategory: body.EventCategory,
                minimumSignificance: body.MinimumSignificance,
                role: null);

            var sortSpec = new JsonSortSpec { Path = "$.EventDateUnix", Descending = true };
            var (skip, take) = PaginationHelper.CalculatePagination(body.Page, body.PageSize);

            var result = await jsonStore.JsonQueryPagedAsync(
                conditions, skip, take, sortSpec, cancellationToken);

            var participations = result.Items
                .Select(item => MapToHistoricalParticipation(item.Value))
                .ToList();

            var paginatedResult = PaginationHelper.CreateResult(
                participations, (int)result.TotalCount, body.Page, body.PageSize);

            return (StatusCodes.OK, new ParticipationListResponse
            {
                Participations = paginatedResult.Items.ToList(),
                TotalCount = paginatedResult.TotalCount,
                Page = paginatedResult.Page,
                PageSize = paginatedResult.PageSize,
                HasNextPage = paginatedResult.HasNextPage,
                HasPreviousPage = paginatedResult.HasPreviousPage
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting participation for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-history",
                "GetParticipation",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-history/get-participation",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets all participants of a historical event.
    /// Uses server-side MySQL JSON queries for efficient pagination per IMPLEMENTATION TENETS.
    /// </summary>
    public async Task<(StatusCodes, ParticipationListResponse?)> GetEventParticipantsAsync(GetEventParticipantsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting participants for event {EventId}", body.EventId);

        try
        {
            var jsonStore = _stateStoreFactory.GetJsonQueryableStore<ParticipationData>(
                StateStoreDefinitions.CharacterHistory);

            var conditions = BuildParticipationQueryConditions(
                "$.EventId", body.EventId,
                eventCategory: null,
                minimumSignificance: null,
                role: body.Role);

            var sortSpec = new JsonSortSpec { Path = "$.Significance", Descending = true };
            var (skip, take) = PaginationHelper.CalculatePagination(body.Page, body.PageSize);

            var result = await jsonStore.JsonQueryPagedAsync(
                conditions, skip, take, sortSpec, cancellationToken);

            var participations = result.Items
                .Select(item => MapToHistoricalParticipation(item.Value))
                .ToList();

            var paginatedResult = PaginationHelper.CreateResult(
                participations, (int)result.TotalCount, body.Page, body.PageSize);

            return (StatusCodes.OK, new ParticipationListResponse
            {
                Participations = paginatedResult.Items.ToList(),
                TotalCount = paginatedResult.TotalCount,
                Page = paginatedResult.Page,
                PageSize = paginatedResult.PageSize,
                HasNextPage = paginatedResult.HasNextPage,
                HasPreviousPage = paginatedResult.HasPreviousPage
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting participants for event {EventId}", body.EventId);
            await _messageBus.TryPublishErrorAsync(
                "character-history",
                "GetEventParticipants",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-history/get-event-participants",
                details: new { body.EventId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deletes a participation record.
    /// </summary>
    public async Task<StatusCodes> DeleteParticipationAsync(DeleteParticipationRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting participation {ParticipationId}", body.ParticipationId);

        try
        {
            // First get the record to know the keys for index cleanup
            var data = await _participationHelper.GetRecordAsync(body.ParticipationId.ToString(), cancellationToken);

            if (data == null)
            {
                return StatusCodes.NotFound;
            }

            // Unregister character reference before deletion
            await UnregisterCharacterReferenceAsync(body.ParticipationId.ToString(), data.CharacterId, cancellationToken);

            // Use helper to remove record and update both indices
            await _participationHelper.RemoveRecordAsync(
                body.ParticipationId.ToString(),
                data.CharacterId.ToString(),
                data.EventId.ToString(),
                cancellationToken);

            // Publish typed event per FOUNDATION TENETS
            await _messageBus.TryPublishAsync(PARTICIPATION_DELETED_TOPIC, new CharacterParticipationDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ParticipationId = body.ParticipationId,
                CharacterId = data.CharacterId,
                HistoricalEventId = data.EventId
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Deleted participation {ParticipationId}", body.ParticipationId);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting participation {ParticipationId}", body.ParticipationId);
            await _messageBus.TryPublishErrorAsync(
                "character-history",
                "DeleteParticipation",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-history/delete-participation",
                details: new { body.ParticipationId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    // ============================================================================
    // Backstory Methods
    // ============================================================================

    /// <summary>
    /// Gets a character's backstory elements.
    /// </summary>
    public async Task<(StatusCodes, BackstoryResponse?)> GetBackstoryAsync(GetBackstoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting backstory for character {CharacterId}", body.CharacterId);

        try
        {
            var data = await _backstoryHelper.GetAsync(body.CharacterId.ToString(), cancellationToken);

            if (data == null)
            {
                _logger.LogDebug("No backstory found for character {CharacterId}", body.CharacterId);
                return (StatusCodes.NotFound, null);
            }

            var elements = data.Elements.Select(MapToBackstoryElement).ToList();

            // Apply filters
            if (body.ElementTypes != null && body.ElementTypes.Count > 0)
            {
                elements = elements.Where(e => body.ElementTypes.Contains(e.ElementType)).ToList();
            }
            if (body.MinimumStrength.HasValue)
            {
                elements = elements.Where(e => e.Strength >= body.MinimumStrength.Value).ToList();
            }

            var response = new BackstoryResponse
            {
                CharacterId = body.CharacterId,
                Elements = elements,
                CreatedAt = TimestampHelper.FromUnixSeconds(data.CreatedAtUnix),
                UpdatedAt = data.UpdatedAtUnix != data.CreatedAtUnix
                    ? TimestampHelper.FromUnixSeconds(data.UpdatedAtUnix)
                    : null
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting backstory for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-history",
                "GetBackstory",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-history/get-backstory",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Sets a character's backstory elements.
    /// </summary>
    public async Task<(StatusCodes, BackstoryResponse?)> SetBackstoryAsync(SetBackstoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Setting backstory for character {CharacterId}, replaceExisting={ReplaceExisting}",
            body.CharacterId, body.ReplaceExisting);

        try
        {
            var elementDataList = body.Elements.Select(MapToBackstoryElementData).ToList();
            var maxElements = _configuration.MaxBackstoryElements;

            if (body.ReplaceExisting)
            {
                // Replace mode: the final count equals the input count
                if (elementDataList.Count > maxElements)
                {
                    _logger.LogWarning(
                        "SetBackstory rejected for character {CharacterId}: {Count} elements exceeds limit of {Limit}",
                        body.CharacterId, elementDataList.Count, maxElements);
                    return (StatusCodes.BadRequest, null);
                }
            }
            else
            {
                // Merge mode: calculate post-merge count (existing + truly new elements)
                var existing = await _backstoryHelper.GetAsync(body.CharacterId.ToString(), cancellationToken);
                if (existing != null)
                {
                    var existingElements = existing.Elements;
                    var newElementCount = elementDataList.Count(newEl =>
                        !existingElements.Any(e =>
                            e.ElementType == newEl.ElementType && e.Key == newEl.Key));
                    var postMergeCount = existingElements.Count + newElementCount;

                    if (postMergeCount > maxElements)
                    {
                        _logger.LogWarning(
                            "SetBackstory merge rejected for character {CharacterId}: post-merge count {PostMerge} exceeds limit of {Limit} (existing={Existing}, new={New})",
                            body.CharacterId, postMergeCount, maxElements, existingElements.Count, newElementCount);
                        return (StatusCodes.BadRequest, null);
                    }
                }
                else
                {
                    // No existing backstory: the final count equals the input count
                    if (elementDataList.Count > maxElements)
                    {
                        _logger.LogWarning(
                            "SetBackstory rejected for character {CharacterId}: {Count} elements exceeds limit of {Limit}",
                            body.CharacterId, elementDataList.Count, maxElements);
                        return (StatusCodes.BadRequest, null);
                    }
                }
            }

            var result = await _backstoryHelper.SetAsync(
                body.CharacterId.ToString(),
                elementDataList,
                body.ReplaceExisting,
                cancellationToken);

            var response = new BackstoryResponse
            {
                CharacterId = body.CharacterId,
                Elements = result.Backstory.Elements.Select(MapToBackstoryElement).ToList(),
                CreatedAt = TimestampHelper.FromUnixSeconds(result.Backstory.CreatedAtUnix),
                UpdatedAt = TimestampHelper.FromUnixSeconds(result.Backstory.UpdatedAtUnix)
            };

            // Publish typed event per FOUNDATION TENETS
            var now = DateTimeOffset.UtcNow;
            if (result.IsNew)
            {
                await _messageBus.TryPublishAsync(BACKSTORY_CREATED_TOPIC, new CharacterBackstoryCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    CharacterId = body.CharacterId,
                    ElementCount = result.Backstory.Elements.Count
                }, cancellationToken: cancellationToken);

                // Register character reference with lib-resource for cleanup coordination (only on new backstory)
                await RegisterCharacterReferenceAsync($"backstory-{body.CharacterId}", body.CharacterId, cancellationToken);
            }
            else
            {
                await _messageBus.TryPublishAsync(BACKSTORY_UPDATED_TOPIC, new CharacterBackstoryUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    CharacterId = body.CharacterId,
                    ElementCount = result.Backstory.Elements.Count,
                    ReplaceExisting = body.ReplaceExisting
                }, cancellationToken: cancellationToken);
            }

            _logger.LogInformation("Backstory {Action} for character {CharacterId}, {Count} elements",
                result.IsNew ? "created" : "updated", body.CharacterId, result.Backstory.Elements.Count);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting backstory for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-history",
                "SetBackstory",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-history/set-backstory",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Adds a single backstory element to a character.
    /// </summary>
    public async Task<(StatusCodes, BackstoryResponse?)> AddBackstoryElementAsync(AddBackstoryElementRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Adding backstory element for character {CharacterId}, type {ElementType}",
            body.CharacterId, body.Element.ElementType);

        try
        {
            var elementData = MapToBackstoryElementData(body.Element);

            // Validate element count limit (only for truly new elements, not updates)
            var existing = await _backstoryHelper.GetAsync(body.CharacterId.ToString(), cancellationToken);
            if (existing != null)
            {
                var isUpdate = existing.Elements.Any(e =>
                    e.ElementType == elementData.ElementType && e.Key == elementData.Key);

                if (!isUpdate && existing.Elements.Count >= _configuration.MaxBackstoryElements)
                {
                    _logger.LogWarning(
                        "AddBackstoryElement rejected for character {CharacterId}: {Count} elements already at limit of {Limit}",
                        body.CharacterId, existing.Elements.Count, _configuration.MaxBackstoryElements);
                    return (StatusCodes.BadRequest, null);
                }
            }

            var result = await _backstoryHelper.AddElementAsync(
                body.CharacterId.ToString(),
                elementData,
                cancellationToken);

            var response = new BackstoryResponse
            {
                CharacterId = body.CharacterId,
                Elements = result.Backstory.Elements.Select(MapToBackstoryElement).ToList(),
                CreatedAt = TimestampHelper.FromUnixSeconds(result.Backstory.CreatedAtUnix),
                UpdatedAt = TimestampHelper.FromUnixSeconds(result.Backstory.UpdatedAtUnix)
            };

            // Publish typed event per FOUNDATION TENETS
            var now = DateTimeOffset.UtcNow;
            if (result.IsNew)
            {
                await _messageBus.TryPublishAsync(BACKSTORY_CREATED_TOPIC, new CharacterBackstoryCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    CharacterId = body.CharacterId,
                    ElementCount = result.Backstory.Elements.Count
                }, cancellationToken: cancellationToken);

                // Register character reference with lib-resource for cleanup coordination (only on new backstory)
                await RegisterCharacterReferenceAsync($"backstory-{body.CharacterId}", body.CharacterId, cancellationToken);
            }
            else
            {
                await _messageBus.TryPublishAsync(BACKSTORY_UPDATED_TOPIC, new CharacterBackstoryUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    CharacterId = body.CharacterId,
                    ElementCount = result.Backstory.Elements.Count,
                    ReplaceExisting = false
                }, cancellationToken: cancellationToken);
            }

            _logger.LogInformation("Backstory element added for character {CharacterId}, now {Count} elements",
                body.CharacterId, result.Backstory.Elements.Count);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding backstory element for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-history",
                "AddBackstoryElement",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-history/add-backstory-element",
                details: new { body.CharacterId, body.Element.ElementType },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deletes a character's backstory.
    /// </summary>
    public async Task<StatusCodes> DeleteBackstoryAsync(DeleteBackstoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting backstory for character {CharacterId}", body.CharacterId);

        try
        {
            var deleted = await _backstoryHelper.DeleteAsync(body.CharacterId.ToString(), cancellationToken);

            if (!deleted)
            {
                return StatusCodes.NotFound;
            }

            // Unregister character reference for backstory
            await UnregisterCharacterReferenceAsync($"backstory-{body.CharacterId}", body.CharacterId, cancellationToken);

            // Publish typed event per FOUNDATION TENETS
            await _messageBus.TryPublishAsync(BACKSTORY_DELETED_TOPIC, new CharacterBackstoryDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                CharacterId = body.CharacterId
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Backstory deleted for character {CharacterId}", body.CharacterId);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting backstory for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-history",
                "DeleteBackstory",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-history/delete-backstory",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    // ============================================================================
    // Utility Methods
    // ============================================================================

    /// <summary>
    /// Deletes all history data for a character.
    /// Used during character archival/compression.
    /// </summary>
    public async Task<(StatusCodes, DeleteAllHistoryResponse?)> DeleteAllHistoryAsync(DeleteAllHistoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting all history for character {CharacterId}", body.CharacterId);

        try
        {
            // Get all participations first to unregister their character references
            var participationRecords = await _participationHelper.GetRecordsByPrimaryKeyAsync(
                body.CharacterId.ToString(),
                cancellationToken);

            // Unregister all character references for participations
            foreach (var record in participationRecords)
            {
                await UnregisterCharacterReferenceAsync(record.ParticipationId.ToString(), body.CharacterId, cancellationToken);
            }

            // Use helper to delete all participations and update event indices
            var participationsDeleted = await _participationHelper.RemoveAllByPrimaryKeyAsync(
                body.CharacterId.ToString(),
                record => record.EventId.ToString(),
                cancellationToken);

            // Check if backstory exists to unregister its reference
            var existingBackstory = await _backstoryHelper.GetAsync(body.CharacterId.ToString(), cancellationToken);
            if (existingBackstory != null)
            {
                await UnregisterCharacterReferenceAsync($"backstory-{body.CharacterId}", body.CharacterId, cancellationToken);
            }

            // Use helper to delete backstory
            var backstoryDeleted = await _backstoryHelper.DeleteAsync(body.CharacterId.ToString(), cancellationToken);

            // Publish typed event per FOUNDATION TENETS
            await _messageBus.TryPublishAsync(HISTORY_DELETED_TOPIC, new CharacterHistoryDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                CharacterId = body.CharacterId,
                ParticipationsDeleted = participationsDeleted,
                BackstoryDeleted = backstoryDeleted
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Deleted all history for character {CharacterId}: {ParticipationsDeleted} participations, backstory={BackstoryDeleted}",
                body.CharacterId, participationsDeleted, backstoryDeleted);

            return (StatusCodes.OK, new DeleteAllHistoryResponse
            {
                CharacterId = body.CharacterId,
                ParticipationsDeleted = participationsDeleted,
                BackstoryDeleted = backstoryDeleted
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting all history for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-history",
                "DeleteAllHistory",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-history/delete-all",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Generates text summaries of a character's history for compression.
    /// </summary>
    public async Task<(StatusCodes, HistorySummaryResponse?)> SummarizeHistoryAsync(SummarizeHistoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Summarizing history for character {CharacterId}", body.CharacterId);

        try
        {
            var keyBackstoryPoints = new List<string>();
            var majorLifeEvents = new List<string>();

            // Get backstory and create summaries
            var backstory = await _backstoryHelper.GetAsync(body.CharacterId.ToString(), cancellationToken);

            if (backstory != null)
            {
                // Sort by strength and take top N
                var topElements = backstory.Elements
                    .OrderByDescending(e => e.Strength)
                    .Take(body.MaxBackstoryPoints);

                foreach (var element in topElements)
                {
                    var summary = GenerateBackstorySummary(element);
                    if (!string.IsNullOrEmpty(summary))
                    {
                        keyBackstoryPoints.Add(summary);
                    }
                }
            }

            // Get participation and create summaries using helper
            var participations = await _participationHelper.GetRecordsByPrimaryKeyAsync(
                body.CharacterId.ToString(),
                cancellationToken);

            if (participations.Count > 0)
            {
                // Sort by significance and take top N
                var topParticipations = participations
                    .OrderByDescending(p => p.Significance)
                    .Take(body.MaxLifeEvents);

                foreach (var participation in topParticipations)
                {
                    var summary = GenerateParticipationSummary(participation);
                    if (!string.IsNullOrEmpty(summary))
                    {
                        majorLifeEvents.Add(summary);
                    }
                }
            }

            var response = new HistorySummaryResponse
            {
                CharacterId = body.CharacterId,
                KeyBackstoryPoints = keyBackstoryPoints,
                MajorLifeEvents = majorLifeEvents
            };

            _logger.LogInformation("Generated history summary for character {CharacterId}: {BackstoryCount} backstory points, {EventCount} life events",
                body.CharacterId, keyBackstoryPoints.Count, majorLifeEvents.Count);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error summarizing history for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-history",
                "SummarizeHistory",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-history/summarize",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    // ============================================================================
    // Query Helpers
    // ============================================================================

    /// <summary>
    /// Builds MySQL JSON query conditions for participation listing.
    /// Uses server-side filtering to avoid loading all records into memory.
    /// </summary>
    private static List<QueryCondition> BuildParticipationQueryConditions(
        string entityIdPath,
        Guid entityId,
        EventCategory? eventCategory,
        float? minimumSignificance,
        ParticipationRole? role)
    {
        var conditions = new List<QueryCondition>
        {
            // Type discriminator: only ParticipationData records have ParticipationId
            // Prevents confusion with other data types stored in the same state store
            new QueryCondition
            {
                Path = "$.ParticipationId",
                Operator = QueryOperator.Exists,
                Value = true
            },
            // Primary entity filter (character or event)
            new QueryCondition
            {
                Path = entityIdPath,
                Operator = QueryOperator.Equals,
                Value = entityId.ToString()
            }
        };

        if (eventCategory.HasValue)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.EventCategory",
                Operator = QueryOperator.Equals,
                Value = eventCategory.Value.ToString()
            });
        }

        if (minimumSignificance.HasValue)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.Significance",
                Operator = QueryOperator.GreaterThanOrEqual,
                Value = minimumSignificance.Value
            });
        }

        if (role.HasValue)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.Role",
                Operator = QueryOperator.Equals,
                Value = role.Value.ToString()
            });
        }

        return conditions;
    }

    // ============================================================================
    // Mapping Helpers
    // ============================================================================

    private static HistoricalParticipation MapToHistoricalParticipation(ParticipationData data)
    {
        return new HistoricalParticipation
        {
            ParticipationId = data.ParticipationId,
            CharacterId = data.CharacterId,
            EventId = data.EventId,
            EventName = data.EventName,
            EventCategory = data.EventCategory,
            Role = data.Role,
            EventDate = TimestampHelper.FromUnixSeconds(data.EventDateUnix),
            Significance = data.Significance,
            Metadata = data.Metadata,
            CreatedAt = TimestampHelper.FromUnixSeconds(data.CreatedAtUnix)
        };
    }

    private static BackstoryElement MapToBackstoryElement(BackstoryElementData data)
    {
        return new BackstoryElement
        {
            ElementType = data.ElementType,
            Key = data.Key,
            Value = data.Value,
            Strength = data.Strength,
            RelatedEntityId = data.RelatedEntityId,
            RelatedEntityType = data.RelatedEntityType
        };
    }

    private static BackstoryElementData MapToBackstoryElementData(BackstoryElement element)
    {
        return new BackstoryElementData
        {
            ElementType = element.ElementType,
            Key = element.Key,
            Value = element.Value,
            Strength = element.Strength,
            RelatedEntityId = element.RelatedEntityId,
            RelatedEntityType = element.RelatedEntityType
        };
    }

    private static string GenerateBackstorySummary(BackstoryElementData element)
    {
        return element.ElementType switch
        {
            BackstoryElementType.ORIGIN => $"From {FormatValue(element.Value)}",
            BackstoryElementType.OCCUPATION => $"Worked as {FormatValue(element.Value)}",
            BackstoryElementType.TRAINING => $"Trained by {FormatValue(element.Value)}",
            BackstoryElementType.TRAUMA => $"Experienced {FormatValue(element.Value)}",
            BackstoryElementType.ACHIEVEMENT => $"Known for {FormatValue(element.Value)}",
            BackstoryElementType.SECRET => $"Hides {FormatValue(element.Value)}",
            BackstoryElementType.GOAL => $"Seeks to {FormatValue(element.Value)}",
            BackstoryElementType.FEAR => $"Fears {FormatValue(element.Value)}",
            BackstoryElementType.BELIEF => $"Believes in {FormatValue(element.Value)}",
            _ => $"{element.Key}: {element.Value}"
        };
    }

    private static string GenerateParticipationSummary(ParticipationData participation)
    {
        var roleText = participation.Role switch
        {
            ParticipationRole.LEADER => "led",
            ParticipationRole.COMBATANT => "fought in",
            ParticipationRole.VICTIM => "suffered in",
            ParticipationRole.WITNESS => "witnessed",
            ParticipationRole.BENEFICIARY => "benefited from",
            ParticipationRole.CONSPIRATOR => "conspired in",
            ParticipationRole.HERO => "was a hero of",
            ParticipationRole.SURVIVOR => "survived",
            _ => "participated in"
        };

        return $"{roleText} the {participation.EventName}";
    }

    private static string FormatValue(string value)
    {
        // Convert snake_case to readable text
        return value.Replace("_", " ").ToLowerInvariant();
    }

    // ============================================================================
    // Compression Methods
    // ============================================================================

    /// <summary>
    /// Gets history data for compression during character archival.
    /// Called by Resource service during character compression via compression callback.
    /// </summary>
    public async Task<(StatusCodes, CharacterHistoryArchive?)> GetCompressDataAsync(
        GetCompressDataRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting compress data for character {CharacterId}", body.CharacterId);

        try
        {
            // Get all participations for this character
            var participationRecords = await _participationHelper.GetRecordsByPrimaryKeyAsync(
                body.CharacterId.ToString(),
                cancellationToken);

            var participations = participationRecords
                .Select(MapToHistoricalParticipation)
                .OrderByDescending(p => p.EventDate)
                .ToList();

            // Get backstory data
            var backstoryData = await _backstoryHelper.GetAsync(body.CharacterId.ToString(), cancellationToken);

            // Return 404 only if BOTH are missing
            if (participations.Count == 0 && backstoryData == null)
            {
                _logger.LogDebug("No history data found for character {CharacterId}", body.CharacterId);
                return (StatusCodes.NotFound, null);
            }

            BackstoryResponse? backstoryResponse = null;
            if (backstoryData != null)
            {
                backstoryResponse = new BackstoryResponse
                {
                    CharacterId = body.CharacterId,
                    Elements = backstoryData.Elements.Select(MapToBackstoryElement).ToList(),
                    CreatedAt = TimestampHelper.FromUnixSeconds(backstoryData.CreatedAtUnix),
                    UpdatedAt = backstoryData.UpdatedAtUnix != backstoryData.CreatedAtUnix
                        ? TimestampHelper.FromUnixSeconds(backstoryData.UpdatedAtUnix)
                        : null
                };
            }

            // Generate summaries for reference
            var (_, summaries) = await SummarizeHistoryAsync(
                new SummarizeHistoryRequest
                {
                    CharacterId = body.CharacterId,
                    MaxBackstoryPoints = 10,
                    MaxLifeEvents = 10
                },
                cancellationToken);

            var response = new CharacterHistoryArchive
            {
                // ResourceArchiveBase fields
                ResourceId = body.CharacterId,
                ResourceType = "character-history",
                ArchivedAt = DateTimeOffset.UtcNow,
                SchemaVersion = 1,
                // Service-specific fields
                CharacterId = body.CharacterId,
                HasParticipations = participations.Count > 0,
                Participations = participations,
                HasBackstory = backstoryData != null,
                Backstory = backstoryResponse,
                Summaries = summaries
            };

            _logger.LogInformation(
                "Compress data retrieved for character {CharacterId}: participations={ParticipationCount}, hasBackstory={HasBackstory}",
                body.CharacterId, participations.Count, response.HasBackstory);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting compress data for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-history",
                "GetCompressData",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-history/get-compress-data",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Restores history data from a compressed archive.
    /// Called by Resource service during character decompression via decompression callback.
    /// </summary>
    public async Task<(StatusCodes, RestoreFromArchiveResponse?)> RestoreFromArchiveAsync(
        RestoreFromArchiveRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Restoring history data from archive for character {CharacterId}", body.CharacterId);

        var participationsRestored = 0;
        var backstoryRestored = false;

        try
        {
            // Decompress the archive data
            CharacterHistoryArchive archiveData;
            try
            {
                var compressedBytes = Convert.FromBase64String(body.Data);
                var jsonData = DecompressJsonData(compressedBytes);
                archiveData = BannouJson.Deserialize<CharacterHistoryArchive>(jsonData)
                    ?? throw new InvalidOperationException("Deserialized archive data is null");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decompress archive data for character {CharacterId}", body.CharacterId);
                return (StatusCodes.BadRequest, new RestoreFromArchiveResponse
                {
                    CharacterId = body.CharacterId,
                    ParticipationsRestored = 0,
                    BackstoryRestored = false,
                    Success = false,
                    ErrorMessage = $"Invalid archive data: {ex.Message}"
                });
            }

            // Restore participations if present in archive
            if (archiveData.HasParticipations && archiveData.Participations.Count > 0)
            {
                foreach (var participation in archiveData.Participations)
                {
                    var participationData = new ParticipationData
                    {
                        ParticipationId = participation.ParticipationId,
                        CharacterId = body.CharacterId,
                        EventId = participation.EventId,
                        EventName = participation.EventName,
                        EventCategory = participation.EventCategory,
                        Role = participation.Role,
                        EventDateUnix = participation.EventDate.ToUnixTimeSeconds(),
                        Significance = participation.Significance,
                        Metadata = participation.Metadata,
                        CreatedAtUnix = participation.CreatedAt.ToUnixTimeSeconds()
                    };

                    await _participationHelper.AddRecordAsync(
                        participationData,
                        participationData.ParticipationId.ToString(),
                        body.CharacterId.ToString(),
                        participationData.EventId.ToString(),
                        cancellationToken);

                    // Register character reference for restored participation
                    await RegisterCharacterReferenceAsync(participationData.ParticipationId.ToString(), body.CharacterId, cancellationToken);

                    participationsRestored++;
                }

                _logger.LogInformation("{Count} participations restored for character {CharacterId}",
                    participationsRestored, body.CharacterId);
            }

            // Restore backstory if present in archive
            if (archiveData.HasBackstory && archiveData.Backstory != null)
            {
                var elementDataList = archiveData.Backstory.Elements
                    .Select(MapToBackstoryElementData)
                    .ToList();

                await _backstoryHelper.SetAsync(
                    body.CharacterId.ToString(),
                    elementDataList,
                    replaceExisting: true,
                    cancellationToken);

                // Register character reference for restored backstory
                await RegisterCharacterReferenceAsync($"backstory-{body.CharacterId}", body.CharacterId, cancellationToken);

                backstoryRestored = true;
                _logger.LogInformation("Backstory restored for character {CharacterId} with {Count} elements",
                    body.CharacterId, elementDataList.Count);
            }

            _logger.LogInformation(
                "Archive restoration completed for character {CharacterId}: participations={ParticipationsRestored}, backstory={BackstoryRestored}",
                body.CharacterId, participationsRestored, backstoryRestored);

            return (StatusCodes.OK, new RestoreFromArchiveResponse
            {
                CharacterId = body.CharacterId,
                ParticipationsRestored = participationsRestored,
                BackstoryRestored = backstoryRestored,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring archive for character {CharacterId}", body.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-history",
                "RestoreFromArchive",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/character-history/restore-from-archive",
                details: new { body.CharacterId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
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

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogInformation("Registering CharacterHistory service permissions...");
        try
        {
            await CharacterHistoryPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
            _logger.LogInformation("CharacterHistory service permissions registered");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register CharacterHistory service permissions");
            await _messageBus.TryPublishErrorAsync(
                "character-history",
                "RegisterServicePermissions",
                ex.GetType().Name,
                ex.Message,
                dependency: "permission");
            throw;
        }
    }
}

// ============================================================================
// Internal Data Models
// ============================================================================

/// <summary>
/// Internal storage model for participation data.
/// </summary>
internal class ParticipationData
{
    public Guid ParticipationId { get; set; }
    public Guid CharacterId { get; set; }
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public EventCategory EventCategory { get; set; }
    public ParticipationRole Role { get; set; }
    public long EventDateUnix { get; set; }
    public float Significance { get; set; }
    public object? Metadata { get; set; }
    public long CreatedAtUnix { get; set; }
}

/// <summary>
/// Internal storage model for backstory data.
/// </summary>
internal class BackstoryData
{
    public Guid CharacterId { get; set; }
    public List<BackstoryElementData> Elements { get; set; } = new();
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
}

/// <summary>
/// Internal storage model for a backstory element.
/// </summary>
internal class BackstoryElementData
{
    public BackstoryElementType ElementType { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public float Strength { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
}
