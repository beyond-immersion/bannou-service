using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.History;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-character-history.tests")]

namespace BeyondImmersion.BannouService.CharacterHistory;

/// <summary>
/// Service implementation for character history and backstory management.
/// Provides storage for historical event participation and machine-readable backstory elements.
/// </summary>
[BannouService("character-history", typeof(ICharacterHistoryService), lifetime: ServiceLifetime.Scoped)]
public partial class CharacterHistoryService : ICharacterHistoryService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<CharacterHistoryService> _logger;
    private readonly CharacterHistoryServiceConfiguration _configuration;

    private const string STATE_STORE = "character-history-statestore";
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
        CharacterHistoryServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    // ============================================================================
    // Participation Methods
    // ============================================================================

    /// <summary>
    /// Records a character's participation in a historical event.
    /// </summary>
    public async Task<(StatusCodes, HistoricalParticipation?)> RecordParticipationAsync(RecordParticipationRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Recording participation for character {CharacterId} in event {EventId}",
            body.CharacterId, body.EventId);

        try
        {
            var participationId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var participation = new HistoricalParticipation
            {
                ParticipationId = participationId,
                CharacterId = body.CharacterId,
                EventId = body.EventId,
                EventName = body.EventName,
                EventCategory = body.EventCategory,
                Role = body.Role,
                EventDate = body.EventDate,
                Significance = body.Significance,
                Metadata = body.Metadata,
                CreatedAt = now
            };

            // Store the participation record
            var participationStore = _stateStoreFactory.GetStore<ParticipationData>(STATE_STORE);
            var participationData = new ParticipationData
            {
                ParticipationId = participationId.ToString(),
                CharacterId = body.CharacterId.ToString(),
                EventId = body.EventId.ToString(),
                EventName = body.EventName,
                EventCategory = body.EventCategory.ToString(),
                Role = body.Role.ToString(),
                EventDateUnix = body.EventDate.ToUnixTimeSeconds(),
                Significance = body.Significance,
                Metadata = body.Metadata,
                CreatedAtUnix = now.ToUnixTimeSeconds()
            };

            await participationStore.SaveAsync($"{PARTICIPATION_KEY_PREFIX}{participationId}", participationData, cancellationToken: cancellationToken);

            // Update the character's participation index
            var indexStore = _stateStoreFactory.GetStore<ParticipationIndexData>(STATE_STORE);
            var indexKey = $"{PARTICIPATION_INDEX_KEY_PREFIX}{body.CharacterId}";
            var index = await indexStore.GetAsync(indexKey, cancellationToken) ?? new ParticipationIndexData { CharacterId = body.CharacterId.ToString() };
            index.ParticipationIds.Add(participationId.ToString());
            await indexStore.SaveAsync(indexKey, index, cancellationToken: cancellationToken);

            // Update the event's participant index
            var eventIndexKey = $"{PARTICIPATION_BY_EVENT_KEY_PREFIX}{body.EventId}";
            var eventIndex = await indexStore.GetAsync(eventIndexKey, cancellationToken) ?? new ParticipationIndexData { CharacterId = body.EventId.ToString() };
            eventIndex.ParticipationIds.Add(participationId.ToString());
            await indexStore.SaveAsync(eventIndexKey, eventIndex, cancellationToken: cancellationToken);

            // Publish typed event (T5 compliant)
            await _messageBus.TryPublishAsync(PARTICIPATION_RECORDED_TOPIC, new CharacterParticipationRecordedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                CharacterId = body.CharacterId,
                HistoricalEventId = body.EventId,
                ParticipationId = participationId,
                Role = body.Role.ToString()
            }, cancellationToken: cancellationToken);

            _logger.LogInformation("Recorded participation {ParticipationId} for character {CharacterId}",
                participationId, body.CharacterId);

            return (StatusCodes.OK, participation);
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
    /// </summary>
    public async Task<(StatusCodes, ParticipationListResponse?)> GetParticipationAsync(GetParticipationRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting participation for character {CharacterId}", body.CharacterId);

        try
        {
            var indexStore = _stateStoreFactory.GetStore<ParticipationIndexData>(STATE_STORE);
            var indexKey = $"{PARTICIPATION_INDEX_KEY_PREFIX}{body.CharacterId}";
            var index = await indexStore.GetAsync(indexKey, cancellationToken);

            if (index == null || index.ParticipationIds.Count == 0)
            {
                return (StatusCodes.OK, new ParticipationListResponse
                {
                    Participations = new List<HistoricalParticipation>(),
                    TotalCount = 0,
                    Page = body.Page,
                    PageSize = body.PageSize,
                    HasNextPage = false,
                    HasPreviousPage = false
                });
            }

            var participationStore = _stateStoreFactory.GetStore<ParticipationData>(STATE_STORE);
            var allParticipations = new List<HistoricalParticipation>();

            foreach (var participationId in index.ParticipationIds)
            {
                var data = await participationStore.GetAsync($"{PARTICIPATION_KEY_PREFIX}{participationId}", cancellationToken);
                if (data != null)
                {
                    var participation = MapToHistoricalParticipation(data);

                    // Apply filters
                    if (body.EventCategory.HasValue && participation.EventCategory != body.EventCategory.Value)
                        continue;
                    if (body.MinimumSignificance.HasValue && participation.Significance < body.MinimumSignificance.Value)
                        continue;

                    allParticipations.Add(participation);
                }
            }

            // Sort by event date descending (most recent first)
            allParticipations = allParticipations.OrderByDescending(p => p.EventDate).ToList();

            // Paginate
            var totalCount = allParticipations.Count;
            var skip = (body.Page - 1) * body.PageSize;
            var paged = allParticipations.Skip(skip).Take(body.PageSize).ToList();

            var response = new ParticipationListResponse
            {
                Participations = paged,
                TotalCount = totalCount,
                Page = body.Page,
                PageSize = body.PageSize,
                HasNextPage = skip + paged.Count < totalCount,
                HasPreviousPage = body.Page > 1
            };

            return (StatusCodes.OK, response);
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
    /// </summary>
    public async Task<(StatusCodes, ParticipationListResponse?)> GetEventParticipantsAsync(GetEventParticipantsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting participants for event {EventId}", body.EventId);

        try
        {
            var indexStore = _stateStoreFactory.GetStore<ParticipationIndexData>(STATE_STORE);
            var indexKey = $"{PARTICIPATION_BY_EVENT_KEY_PREFIX}{body.EventId}";
            var index = await indexStore.GetAsync(indexKey, cancellationToken);

            if (index == null || index.ParticipationIds.Count == 0)
            {
                return (StatusCodes.OK, new ParticipationListResponse
                {
                    Participations = new List<HistoricalParticipation>(),
                    TotalCount = 0,
                    Page = body.Page,
                    PageSize = body.PageSize,
                    HasNextPage = false,
                    HasPreviousPage = false
                });
            }

            var participationStore = _stateStoreFactory.GetStore<ParticipationData>(STATE_STORE);
            var allParticipations = new List<HistoricalParticipation>();

            foreach (var participationId in index.ParticipationIds)
            {
                var data = await participationStore.GetAsync($"{PARTICIPATION_KEY_PREFIX}{participationId}", cancellationToken);
                if (data != null)
                {
                    var participation = MapToHistoricalParticipation(data);

                    // Apply role filter
                    if (body.Role.HasValue && participation.Role != body.Role.Value)
                        continue;

                    allParticipations.Add(participation);
                }
            }

            // Sort by significance descending
            allParticipations = allParticipations.OrderByDescending(p => p.Significance).ToList();

            // Paginate
            var totalCount = allParticipations.Count;
            var skip = (body.Page - 1) * body.PageSize;
            var paged = allParticipations.Skip(skip).Take(body.PageSize).ToList();

            var response = new ParticipationListResponse
            {
                Participations = paged,
                TotalCount = totalCount,
                Page = body.Page,
                PageSize = body.PageSize,
                HasNextPage = skip + paged.Count < totalCount,
                HasPreviousPage = body.Page > 1
            };

            return (StatusCodes.OK, response);
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
            var participationStore = _stateStoreFactory.GetStore<ParticipationData>(STATE_STORE);
            var participationKey = $"{PARTICIPATION_KEY_PREFIX}{body.ParticipationId}";
            var data = await participationStore.GetAsync(participationKey, cancellationToken);

            if (data == null)
            {
                return StatusCodes.NotFound;
            }

            // Delete from main store
            await participationStore.DeleteAsync(participationKey, cancellationToken);

            // Remove from character index
            var indexStore = _stateStoreFactory.GetStore<ParticipationIndexData>(STATE_STORE);
            var indexKey = $"{PARTICIPATION_INDEX_KEY_PREFIX}{data.CharacterId}";
            var index = await indexStore.GetAsync(indexKey, cancellationToken);
            if (index != null)
            {
                index.ParticipationIds.Remove(body.ParticipationId.ToString());
                await indexStore.SaveAsync(indexKey, index, cancellationToken: cancellationToken);
            }

            // Remove from event index
            var eventIndexKey = $"{PARTICIPATION_BY_EVENT_KEY_PREFIX}{data.EventId}";
            var eventIndex = await indexStore.GetAsync(eventIndexKey, cancellationToken);
            if (eventIndex != null)
            {
                eventIndex.ParticipationIds.Remove(body.ParticipationId.ToString());
                await indexStore.SaveAsync(eventIndexKey, eventIndex, cancellationToken: cancellationToken);
            }

            // Publish typed event (T5 compliant)
            await _messageBus.TryPublishAsync(PARTICIPATION_DELETED_TOPIC, new CharacterParticipationDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ParticipationId = body.ParticipationId,
                CharacterId = Guid.Parse(data.CharacterId),
                HistoricalEventId = Guid.Parse(data.EventId)
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
            var store = _stateStoreFactory.GetStore<BackstoryData>(STATE_STORE);
            var data = await store.GetAsync($"{BACKSTORY_KEY_PREFIX}{body.CharacterId}", cancellationToken);

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
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix),
                UpdatedAt = data.UpdatedAtUnix != data.CreatedAtUnix
                    ? DateTimeOffset.FromUnixTimeSeconds(data.UpdatedAtUnix)
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
            var store = _stateStoreFactory.GetStore<BackstoryData>(STATE_STORE);
            var key = $"{BACKSTORY_KEY_PREFIX}{body.CharacterId}";
            var existing = await store.GetAsync(key, cancellationToken);
            var isNew = existing == null;
            var now = DateTimeOffset.UtcNow;

            BackstoryData data;
            if (body.ReplaceExisting || isNew)
            {
                data = new BackstoryData
                {
                    CharacterId = body.CharacterId.ToString(),
                    Elements = body.Elements.Select(MapToBackstoryElementData).ToList(),
                    CreatedAtUnix = isNew ? now.ToUnixTimeSeconds() : existing!.CreatedAtUnix,
                    UpdatedAtUnix = now.ToUnixTimeSeconds()
                };
            }
            else
            {
                // Merge: update matching type+key pairs, add new ones
                data = existing!;
                foreach (var newElement in body.Elements)
                {
                    var existingElement = data.Elements.FirstOrDefault(
                        e => e.ElementType == newElement.ElementType.ToString() && e.Key == newElement.Key);
                    if (existingElement != null)
                    {
                        existingElement.Value = newElement.Value;
                        existingElement.Strength = newElement.Strength;
                        existingElement.RelatedEntityId = newElement.RelatedEntityId?.ToString();
                        existingElement.RelatedEntityType = newElement.RelatedEntityType;
                    }
                    else
                    {
                        data.Elements.Add(MapToBackstoryElementData(newElement));
                    }
                }
                data.UpdatedAtUnix = now.ToUnixTimeSeconds();
            }

            await store.SaveAsync(key, data, cancellationToken: cancellationToken);

            var response = new BackstoryResponse
            {
                CharacterId = body.CharacterId,
                Elements = data.Elements.Select(MapToBackstoryElement).ToList(),
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix),
                UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(data.UpdatedAtUnix)
            };

            // Publish typed event (T5 compliant)
            if (isNew)
            {
                await _messageBus.TryPublishAsync(BACKSTORY_CREATED_TOPIC, new CharacterBackstoryCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    CharacterId = body.CharacterId,
                    ElementCount = data.Elements.Count
                }, cancellationToken: cancellationToken);
            }
            else
            {
                await _messageBus.TryPublishAsync(BACKSTORY_UPDATED_TOPIC, new CharacterBackstoryUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    CharacterId = body.CharacterId,
                    ElementCount = data.Elements.Count,
                    ReplaceExisting = body.ReplaceExisting
                }, cancellationToken: cancellationToken);
            }

            _logger.LogInformation("Backstory {Action} for character {CharacterId}, {Count} elements",
                isNew ? "created" : "updated", body.CharacterId, data.Elements.Count);

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
            var store = _stateStoreFactory.GetStore<BackstoryData>(STATE_STORE);
            var key = $"{BACKSTORY_KEY_PREFIX}{body.CharacterId}";
            var existing = await store.GetAsync(key, cancellationToken);
            var isNew = existing == null;
            var now = DateTimeOffset.UtcNow;

            BackstoryData data;
            if (isNew)
            {
                data = new BackstoryData
                {
                    CharacterId = body.CharacterId.ToString(),
                    Elements = new List<BackstoryElementData> { MapToBackstoryElementData(body.Element) },
                    CreatedAtUnix = now.ToUnixTimeSeconds(),
                    UpdatedAtUnix = now.ToUnixTimeSeconds()
                };
            }
            else
            {
                data = existing!;
                // Check for existing element with same type+key and update it
                var existingElement = data.Elements.FirstOrDefault(
                    e => e.ElementType == body.Element.ElementType.ToString() && e.Key == body.Element.Key);
                if (existingElement != null)
                {
                    existingElement.Value = body.Element.Value;
                    existingElement.Strength = body.Element.Strength;
                    existingElement.RelatedEntityId = body.Element.RelatedEntityId?.ToString();
                    existingElement.RelatedEntityType = body.Element.RelatedEntityType;
                }
                else
                {
                    data.Elements.Add(MapToBackstoryElementData(body.Element));
                }
                data.UpdatedAtUnix = now.ToUnixTimeSeconds();
            }

            await store.SaveAsync(key, data, cancellationToken: cancellationToken);

            var response = new BackstoryResponse
            {
                CharacterId = body.CharacterId,
                Elements = data.Elements.Select(MapToBackstoryElement).ToList(),
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix),
                UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(data.UpdatedAtUnix)
            };

            // Publish typed event (T5 compliant)
            if (isNew)
            {
                await _messageBus.TryPublishAsync(BACKSTORY_CREATED_TOPIC, new CharacterBackstoryCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    CharacterId = body.CharacterId,
                    ElementCount = data.Elements.Count
                }, cancellationToken: cancellationToken);
            }
            else
            {
                await _messageBus.TryPublishAsync(BACKSTORY_UPDATED_TOPIC, new CharacterBackstoryUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    CharacterId = body.CharacterId,
                    ElementCount = data.Elements.Count,
                    ReplaceExisting = false
                }, cancellationToken: cancellationToken);
            }

            _logger.LogInformation("Backstory element added for character {CharacterId}, now {Count} elements",
                body.CharacterId, data.Elements.Count);

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
            var store = _stateStoreFactory.GetStore<BackstoryData>(STATE_STORE);
            var key = $"{BACKSTORY_KEY_PREFIX}{body.CharacterId}";
            var existing = await store.GetAsync(key, cancellationToken);

            if (existing == null)
            {
                return StatusCodes.NotFound;
            }

            await store.DeleteAsync(key, cancellationToken);

            // Publish typed event (T5 compliant)
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
            var participationsDeleted = 0;
            var backstoryDeleted = false;

            // Delete all participation records
            var indexStore = _stateStoreFactory.GetStore<ParticipationIndexData>(STATE_STORE);
            var indexKey = $"{PARTICIPATION_INDEX_KEY_PREFIX}{body.CharacterId}";
            var index = await indexStore.GetAsync(indexKey, cancellationToken);

            if (index != null)
            {
                var participationStore = _stateStoreFactory.GetStore<ParticipationData>(STATE_STORE);
                foreach (var participationId in index.ParticipationIds.ToList())
                {
                    var data = await participationStore.GetAsync($"{PARTICIPATION_KEY_PREFIX}{participationId}", cancellationToken);
                    if (data != null)
                    {
                        await participationStore.DeleteAsync($"{PARTICIPATION_KEY_PREFIX}{participationId}", cancellationToken);

                        // Remove from event index
                        var eventIndexKey = $"{PARTICIPATION_BY_EVENT_KEY_PREFIX}{data.EventId}";
                        var eventIndex = await indexStore.GetAsync(eventIndexKey, cancellationToken);
                        if (eventIndex != null)
                        {
                            eventIndex.ParticipationIds.Remove(participationId);
                            await indexStore.SaveAsync(eventIndexKey, eventIndex, cancellationToken: cancellationToken);
                        }

                        participationsDeleted++;
                    }
                }

                await indexStore.DeleteAsync(indexKey, cancellationToken);
            }

            // Delete backstory
            var backstoryStore = _stateStoreFactory.GetStore<BackstoryData>(STATE_STORE);
            var backstoryKey = $"{BACKSTORY_KEY_PREFIX}{body.CharacterId}";
            var backstory = await backstoryStore.GetAsync(backstoryKey, cancellationToken);
            if (backstory != null)
            {
                await backstoryStore.DeleteAsync(backstoryKey, cancellationToken);
                backstoryDeleted = true;
            }

            // Publish typed event (T5 compliant)
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
            var backstoryStore = _stateStoreFactory.GetStore<BackstoryData>(STATE_STORE);
            var backstory = await backstoryStore.GetAsync($"{BACKSTORY_KEY_PREFIX}{body.CharacterId}", cancellationToken);

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

            // Get participation and create summaries
            var indexStore = _stateStoreFactory.GetStore<ParticipationIndexData>(STATE_STORE);
            var index = await indexStore.GetAsync($"{PARTICIPATION_INDEX_KEY_PREFIX}{body.CharacterId}", cancellationToken);

            if (index != null && index.ParticipationIds.Count > 0)
            {
                var participationStore = _stateStoreFactory.GetStore<ParticipationData>(STATE_STORE);
                var participations = new List<ParticipationData>();

                foreach (var participationId in index.ParticipationIds)
                {
                    var data = await participationStore.GetAsync($"{PARTICIPATION_KEY_PREFIX}{participationId}", cancellationToken);
                    if (data != null)
                    {
                        participations.Add(data);
                    }
                }

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
    // Mapping Helpers
    // ============================================================================

    private static HistoricalParticipation MapToHistoricalParticipation(ParticipationData data)
    {
        return new HistoricalParticipation
        {
            ParticipationId = Guid.Parse(data.ParticipationId),
            CharacterId = Guid.Parse(data.CharacterId),
            EventId = Guid.Parse(data.EventId),
            EventName = data.EventName,
            EventCategory = Enum.Parse<EventCategory>(data.EventCategory),
            Role = Enum.Parse<ParticipationRole>(data.Role),
            EventDate = DateTimeOffset.FromUnixTimeSeconds(data.EventDateUnix),
            Significance = data.Significance,
            Metadata = data.Metadata,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix)
        };
    }

    private static BackstoryElement MapToBackstoryElement(BackstoryElementData data)
    {
        return new BackstoryElement
        {
            ElementType = Enum.Parse<BackstoryElementType>(data.ElementType),
            Key = data.Key,
            Value = data.Value,
            Strength = data.Strength,
            RelatedEntityId = !string.IsNullOrEmpty(data.RelatedEntityId) ? Guid.Parse(data.RelatedEntityId) : null,
            RelatedEntityType = data.RelatedEntityType
        };
    }

    private static BackstoryElementData MapToBackstoryElementData(BackstoryElement element)
    {
        return new BackstoryElementData
        {
            ElementType = element.ElementType.ToString(),
            Key = element.Key,
            Value = element.Value,
            Strength = element.Strength,
            RelatedEntityId = element.RelatedEntityId?.ToString(),
            RelatedEntityType = element.RelatedEntityType
        };
    }

    private static string GenerateBackstorySummary(BackstoryElementData element)
    {
        return element.ElementType switch
        {
            "ORIGIN" => $"From {FormatValue(element.Value)}",
            "OCCUPATION" => $"Worked as {FormatValue(element.Value)}",
            "TRAINING" => $"Trained by {FormatValue(element.Value)}",
            "TRAUMA" => $"Experienced {FormatValue(element.Value)}",
            "ACHIEVEMENT" => $"Known for {FormatValue(element.Value)}",
            "SECRET" => $"Hides {FormatValue(element.Value)}",
            "GOAL" => $"Seeks to {FormatValue(element.Value)}",
            "FEAR" => $"Fears {FormatValue(element.Value)}",
            "BELIEF" => $"Believes in {FormatValue(element.Value)}",
            _ => $"{element.Key}: {element.Value}"
        };
    }

    private static string GenerateParticipationSummary(ParticipationData participation)
    {
        var roleText = participation.Role switch
        {
            "LEADER" => "led",
            "COMBATANT" => "fought in",
            "VICTIM" => "suffered in",
            "WITNESS" => "witnessed",
            "BENEFICIARY" => "benefited from",
            "CONSPIRATOR" => "conspired in",
            "HERO" => "was a hero of",
            "SURVIVOR" => "survived",
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
    // Permission Registration
    // ============================================================================

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering CharacterHistory service permissions...");
        try
        {
            await CharacterHistoryPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
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
    public string ParticipationId { get; set; } = string.Empty;
    public string CharacterId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string EventCategory { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public long EventDateUnix { get; set; }
    public float Significance { get; set; }
    public object? Metadata { get; set; }
    public long CreatedAtUnix { get; set; }
}

/// <summary>
/// Internal storage model for participation index (by character or event).
/// </summary>
internal class ParticipationIndexData
{
    public string CharacterId { get; set; } = string.Empty;
    public List<string> ParticipationIds { get; set; } = new();
}

/// <summary>
/// Internal storage model for backstory data.
/// </summary>
internal class BackstoryData
{
    public string CharacterId { get; set; } = string.Empty;
    public List<BackstoryElementData> Elements { get; set; } = new();
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
}

/// <summary>
/// Internal storage model for a backstory element.
/// </summary>
internal class BackstoryElementData
{
    public string ElementType { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public float Strength { get; set; }
    public string? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
}
