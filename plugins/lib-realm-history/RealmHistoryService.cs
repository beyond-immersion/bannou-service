using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.History;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-realm-history.tests")]

namespace BeyondImmersion.BannouService.RealmHistory;

/// <summary>
/// Service implementation for realm history and lore management.
/// Provides storage for historical event participation and machine-readable lore elements.
/// </summary>
[BannouService("realm-history", typeof(IRealmHistoryService), lifetime: ServiceLifetime.Scoped)]
public partial class RealmHistoryService : IRealmHistoryService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<RealmHistoryService> _logger;
    private readonly RealmHistoryServiceConfiguration _configuration;

    private const string PARTICIPATION_KEY_PREFIX = "realm-participation-";
    private const string PARTICIPATION_BY_EVENT_KEY_PREFIX = "realm-participation-event-";
    private const string LORE_KEY_PREFIX = "realm-lore-";
    private const string PARTICIPATION_INDEX_KEY_PREFIX = "realm-participation-index-";

    // Event topics
    private const string PARTICIPATION_RECORDED_TOPIC = "realm-history.participation.recorded";
    private const string PARTICIPATION_DELETED_TOPIC = "realm-history.participation.deleted";
    private const string LORE_CREATED_TOPIC = "realm-history.lore.created";
    private const string LORE_UPDATED_TOPIC = "realm-history.lore.updated";
    private const string LORE_DELETED_TOPIC = "realm-history.lore.deleted";
    private const string HISTORY_DELETED_TOPIC = "realm-history.deleted";

    /// <summary>
    /// Initializes the RealmHistory service with required dependencies.
    /// </summary>
    public RealmHistoryService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<RealmHistoryService> logger,
        RealmHistoryServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;

        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    // ============================================================================
    // Participation Methods
    // ============================================================================

    /// <summary>
    /// Records a realm's participation in a historical event.
    /// </summary>
    public async Task<(StatusCodes, RealmHistoricalParticipation?)> RecordRealmParticipationAsync(
        RecordRealmParticipationRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Recording participation for realm {RealmId} in event {EventId}",
            body.RealmId, body.EventId);

        try
        {
            var participationId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var participation = new RealmHistoricalParticipation
            {
                ParticipationId = participationId,
                RealmId = body.RealmId,
                EventId = body.EventId,
                EventName = body.EventName,
                EventCategory = body.EventCategory,
                Role = body.Role,
                EventDate = body.EventDate,
                Impact = body.Impact,
                Metadata = body.Metadata,
                CreatedAt = now
            };

            // Store the participation record
            var participationStore = _stateStoreFactory.GetStore<RealmParticipationData>(StateStoreDefinitions.RealmHistory);
            var participationData = new RealmParticipationData
            {
                ParticipationId = participationId,
                RealmId = body.RealmId,
                EventId = body.EventId,
                EventName = body.EventName,
                EventCategory = body.EventCategory,
                Role = body.Role,
                EventDateUnix = body.EventDate.ToUnixTimeSeconds(),
                Impact = body.Impact,
                Metadata = body.Metadata,
                CreatedAtUnix = now.ToUnixTimeSeconds()
            };

            await participationStore.SaveAsync(
                $"{PARTICIPATION_KEY_PREFIX}{participationId}",
                participationData,
                cancellationToken: cancellationToken);

            // Update the realm's participation index
            var indexStore = _stateStoreFactory.GetStore<RealmParticipationIndexData>(StateStoreDefinitions.RealmHistory);
            var indexKey = $"{PARTICIPATION_INDEX_KEY_PREFIX}{body.RealmId}";
            var index = await indexStore.GetAsync(indexKey, cancellationToken)
                ?? new RealmParticipationIndexData { RealmId = body.RealmId };
            index.ParticipationIds.Add(participationId);
            await indexStore.SaveAsync(indexKey, index, cancellationToken: cancellationToken);

            // Update the event's participant index
            var eventIndexKey = $"{PARTICIPATION_BY_EVENT_KEY_PREFIX}{body.EventId}";
            var eventIndex = await indexStore.GetAsync(eventIndexKey, cancellationToken)
                ?? new RealmParticipationIndexData { RealmId = body.EventId };
            eventIndex.ParticipationIds.Add(participationId);
            await indexStore.SaveAsync(eventIndexKey, eventIndex, cancellationToken: cancellationToken);

            // Publish typed event per FOUNDATION TENETS
            await _messageBus.TryPublishAsync(PARTICIPATION_RECORDED_TOPIC, new RealmParticipationRecordedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                RealmId = body.RealmId,
                HistoricalEventId = body.EventId,
                ParticipationId = participationId,
                Role = body.Role
            }, cancellationToken: cancellationToken);

            _logger.LogDebug("Recorded participation {ParticipationId} for realm {RealmId}",
                participationId, body.RealmId);

            return (StatusCodes.OK, participation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording participation for realm {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm-history",
                "RecordRealmParticipation",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/realm-history/record-participation",
                details: new { body.RealmId, body.EventId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets a realm's historical event participation records.
    /// </summary>
    public async Task<(StatusCodes, RealmParticipationListResponse?)> GetRealmParticipationAsync(
        GetRealmParticipationRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting participation for realm {RealmId}", body.RealmId);

        try
        {
            var indexStore = _stateStoreFactory.GetStore<RealmParticipationIndexData>(StateStoreDefinitions.RealmHistory);
            var indexKey = $"{PARTICIPATION_INDEX_KEY_PREFIX}{body.RealmId}";
            var index = await indexStore.GetAsync(indexKey, cancellationToken);

            if (index == null || index.ParticipationIds.Count == 0)
            {
                return (StatusCodes.OK, new RealmParticipationListResponse
                {
                    Participations = new List<RealmHistoricalParticipation>(),
                    TotalCount = 0,
                    Page = body.Page,
                    PageSize = body.PageSize,
                    HasNextPage = false,
                    HasPreviousPage = false
                });
            }

            var participationStore = _stateStoreFactory.GetStore<RealmParticipationData>(StateStoreDefinitions.RealmHistory);

            // Bulk fetch all participations in single call instead of N individual calls
            var keys = index.ParticipationIds.Select(id => $"{PARTICIPATION_KEY_PREFIX}{id}").ToList();
            var dataDict = await participationStore.GetBulkAsync(keys, cancellationToken);

            // Map, filter, and collect results
            var allParticipations = dataDict.Values
                .Select(MapToRealmHistoricalParticipation)
                .Where(p =>
                    (!body.EventCategory.HasValue || p.EventCategory == body.EventCategory.Value) &&
                    (!body.MinimumImpact.HasValue || p.Impact >= body.MinimumImpact.Value))
                .ToList();

            // Sort by event date descending (most recent first)
            allParticipations = allParticipations.OrderByDescending(p => p.EventDate).ToList();

            // Paginate
            var totalCount = allParticipations.Count;
            var (skip, take) = PaginationHelper.CalculatePagination(body.Page, body.PageSize);
            var pagedParticipations = allParticipations.Skip(skip).Take(take).ToList();

            return (StatusCodes.OK, new RealmParticipationListResponse
            {
                Participations = pagedParticipations,
                TotalCount = totalCount,
                Page = body.Page,
                PageSize = body.PageSize,
                HasNextPage = skip + take < totalCount,
                HasPreviousPage = body.Page > 1
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting participation for realm {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm-history",
                "GetRealmParticipation",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/realm-history/get-participation",
                details: new { body.RealmId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets all realms that participated in a historical event.
    /// </summary>
    public async Task<(StatusCodes, RealmParticipationListResponse?)> GetRealmEventParticipantsAsync(
        GetRealmEventParticipantsRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting participants for event {EventId}", body.EventId);

        try
        {
            var indexStore = _stateStoreFactory.GetStore<RealmParticipationIndexData>(StateStoreDefinitions.RealmHistory);
            var indexKey = $"{PARTICIPATION_BY_EVENT_KEY_PREFIX}{body.EventId}";
            var index = await indexStore.GetAsync(indexKey, cancellationToken);

            if (index == null || index.ParticipationIds.Count == 0)
            {
                return (StatusCodes.OK, new RealmParticipationListResponse
                {
                    Participations = new List<RealmHistoricalParticipation>(),
                    TotalCount = 0,
                    Page = body.Page,
                    PageSize = body.PageSize,
                    HasNextPage = false,
                    HasPreviousPage = false
                });
            }

            var participationStore = _stateStoreFactory.GetStore<RealmParticipationData>(StateStoreDefinitions.RealmHistory);

            // Bulk fetch all participations in single call instead of N individual calls
            var keys = index.ParticipationIds.Select(id => $"{PARTICIPATION_KEY_PREFIX}{id}").ToList();
            var dataDict = await participationStore.GetBulkAsync(keys, cancellationToken);

            // Map, filter, and sort results
            var allParticipations = dataDict.Values
                .Select(MapToRealmHistoricalParticipation)
                .Where(p => !body.Role.HasValue || p.Role == body.Role.Value)
                .OrderByDescending(p => p.Impact)
                .ToList();

            // Paginate
            var totalCount = allParticipations.Count;
            var (skip, take) = PaginationHelper.CalculatePagination(body.Page, body.PageSize);
            var pagedParticipations = allParticipations.Skip(skip).Take(take).ToList();

            return (StatusCodes.OK, new RealmParticipationListResponse
            {
                Participations = pagedParticipations,
                TotalCount = totalCount,
                Page = body.Page,
                PageSize = body.PageSize,
                HasNextPage = skip + take < totalCount,
                HasPreviousPage = body.Page > 1
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting participants for event {EventId}", body.EventId);
            await _messageBus.TryPublishErrorAsync(
                "realm-history",
                "GetRealmEventParticipants",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/realm-history/get-event-participants",
                details: new { body.EventId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deletes a specific participation record.
    /// </summary>
    public async Task<StatusCodes> DeleteRealmParticipationAsync(
        DeleteRealmParticipationRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting participation {ParticipationId}", body.ParticipationId);

        try
        {
            var participationStore = _stateStoreFactory.GetStore<RealmParticipationData>(StateStoreDefinitions.RealmHistory);
            var participationKey = $"{PARTICIPATION_KEY_PREFIX}{body.ParticipationId}";
            var participation = await participationStore.GetAsync(participationKey, cancellationToken);

            if (participation == null)
            {
                return StatusCodes.NotFound;
            }

            // Delete the participation record
            await participationStore.DeleteAsync(participationKey, cancellationToken);

            // Update realm index
            var indexStore = _stateStoreFactory.GetStore<RealmParticipationIndexData>(StateStoreDefinitions.RealmHistory);
            var realmIndexKey = $"{PARTICIPATION_INDEX_KEY_PREFIX}{participation.RealmId}";
            var realmIndex = await indexStore.GetAsync(realmIndexKey, cancellationToken);
            if (realmIndex != null)
            {
                realmIndex.ParticipationIds.Remove(body.ParticipationId);
                await indexStore.SaveAsync(realmIndexKey, realmIndex, cancellationToken: cancellationToken);
            }

            // Update event index
            var eventIndexKey = $"{PARTICIPATION_BY_EVENT_KEY_PREFIX}{participation.EventId}";
            var eventIndex = await indexStore.GetAsync(eventIndexKey, cancellationToken);
            if (eventIndex != null)
            {
                eventIndex.ParticipationIds.Remove(body.ParticipationId);
                await indexStore.SaveAsync(eventIndexKey, eventIndex, cancellationToken: cancellationToken);
            }

            // Publish typed event per FOUNDATION TENETS
            await _messageBus.TryPublishAsync(PARTICIPATION_DELETED_TOPIC, new RealmParticipationDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ParticipationId = body.ParticipationId,
                RealmId = participation.RealmId,
                HistoricalEventId = participation.EventId
            }, cancellationToken: cancellationToken);

            _logger.LogDebug("Deleted participation {ParticipationId}", body.ParticipationId);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting participation {ParticipationId}", body.ParticipationId);
            await _messageBus.TryPublishErrorAsync(
                "realm-history",
                "DeleteRealmParticipation",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/realm-history/delete-participation",
                details: new { body.ParticipationId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    // ============================================================================
    // Lore Methods
    // ============================================================================

    /// <summary>
    /// Gets lore elements for a realm.
    /// </summary>
    public async Task<(StatusCodes, RealmLoreResponse?)> GetRealmLoreAsync(
        GetRealmLoreRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting lore for realm {RealmId}", body.RealmId);

        try
        {
            var loreStore = _stateStoreFactory.GetStore<RealmLoreData>(StateStoreDefinitions.RealmHistory);
            var loreKey = $"{LORE_KEY_PREFIX}{body.RealmId}";
            var loreData = await loreStore.GetAsync(loreKey, cancellationToken);

            if (loreData == null)
            {
                return (StatusCodes.OK, new RealmLoreResponse
                {
                    RealmId = body.RealmId,
                    Elements = new List<RealmLoreElement>(),
                    CreatedAt = null,
                    UpdatedAt = null
                });
            }

            var elements = loreData.Elements
                .Select(MapToRealmLoreElement)
                .ToList();

            // Apply filters
            if (body.ElementTypes != null && body.ElementTypes.Count > 0)
            {
                elements = elements.Where(e => body.ElementTypes.Contains(e.ElementType)).ToList();
            }
            if (body.MinimumStrength.HasValue)
            {
                elements = elements.Where(e => e.Strength >= body.MinimumStrength.Value).ToList();
            }

            return (StatusCodes.OK, new RealmLoreResponse
            {
                RealmId = body.RealmId,
                Elements = elements,
                CreatedAt = TimestampHelper.FromUnixSeconds(loreData.CreatedAtUnix),
                UpdatedAt = TimestampHelper.FromUnixSeconds(loreData.UpdatedAtUnix)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting lore for realm {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm-history",
                "GetRealmLore",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/realm-history/get-lore",
                details: new { body.RealmId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Sets lore elements for a realm with merge or replace semantics.
    /// </summary>
    public async Task<(StatusCodes, RealmLoreResponse?)> SetRealmLoreAsync(
        SetRealmLoreRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Setting lore for realm {RealmId}, replaceExisting={ReplaceExisting}",
            body.RealmId, body.ReplaceExisting);

        try
        {
            var loreStore = _stateStoreFactory.GetStore<RealmLoreData>(StateStoreDefinitions.RealmHistory);
            var loreKey = $"{LORE_KEY_PREFIX}{body.RealmId}";
            var existing = await loreStore.GetAsync(loreKey, cancellationToken);
            var isNew = existing == null;
            var nowUnix = TimestampHelper.NowUnixSeconds();

            RealmLoreData loreData;

            if (body.ReplaceExisting || isNew)
            {
                // Replace all elements
                loreData = new RealmLoreData
                {
                    RealmId = body.RealmId,
                    Elements = body.Elements.Select(MapToRealmLoreElementData).ToList(),
                    CreatedAtUnix = isNew ? nowUnix : existing!.CreatedAtUnix,
                    UpdatedAtUnix = nowUnix
                };
            }
            else
            {
                // Merge: update matching type+key pairs, add new ones
                loreData = existing!;
                foreach (var newElement in body.Elements)
                {
                    var existingElement = loreData.Elements.FirstOrDefault(e =>
                        e.ElementType == newElement.ElementType &&
                        e.Key == newElement.Key);

                    if (existingElement != null)
                    {
                        // Update existing element
                        existingElement.Value = newElement.Value;
                        existingElement.Strength = newElement.Strength;
                        existingElement.RelatedEntityId = newElement.RelatedEntityId;
                        existingElement.RelatedEntityType = newElement.RelatedEntityType;
                    }
                    else
                    {
                        // Add new element
                        loreData.Elements.Add(MapToRealmLoreElementData(newElement));
                    }
                }
                loreData.UpdatedAtUnix = nowUnix;
            }

            await loreStore.SaveAsync(loreKey, loreData, cancellationToken: cancellationToken);

            // Publish typed event per FOUNDATION TENETS
            var now = DateTimeOffset.UtcNow;
            if (isNew)
            {
                await _messageBus.TryPublishAsync(LORE_CREATED_TOPIC, new RealmLoreCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    RealmId = body.RealmId,
                    ElementCount = loreData.Elements.Count
                }, cancellationToken: cancellationToken);
            }
            else
            {
                await _messageBus.TryPublishAsync(LORE_UPDATED_TOPIC, new RealmLoreUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    RealmId = body.RealmId,
                    ElementCount = loreData.Elements.Count,
                    ReplaceExisting = body.ReplaceExisting
                }, cancellationToken: cancellationToken);
            }

            _logger.LogDebug("Set lore for realm {RealmId}, {ElementCount} elements",
                body.RealmId, loreData.Elements.Count);

            return (StatusCodes.OK, new RealmLoreResponse
            {
                RealmId = body.RealmId,
                Elements = loreData.Elements.Select(MapToRealmLoreElement).ToList(),
                CreatedAt = TimestampHelper.FromUnixSeconds(loreData.CreatedAtUnix),
                UpdatedAt = TimestampHelper.FromUnixSeconds(loreData.UpdatedAtUnix)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting lore for realm {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm-history",
                "SetRealmLore",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/realm-history/set-lore",
                details: new { body.RealmId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Adds a single lore element to a realm.
    /// </summary>
    public async Task<(StatusCodes, RealmLoreResponse?)> AddRealmLoreElementAsync(
        AddRealmLoreElementRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Adding lore element for realm {RealmId}, type={ElementType}, key={Key}",
            body.RealmId, body.Element.ElementType, body.Element.Key);

        try
        {
            var loreStore = _stateStoreFactory.GetStore<RealmLoreData>(StateStoreDefinitions.RealmHistory);
            var loreKey = $"{LORE_KEY_PREFIX}{body.RealmId}";
            var existing = await loreStore.GetAsync(loreKey, cancellationToken);
            var isNew = existing == null;
            var nowUnix = TimestampHelper.NowUnixSeconds();

            RealmLoreData loreData;

            if (isNew)
            {
                loreData = new RealmLoreData
                {
                    RealmId = body.RealmId,
                    Elements = new List<RealmLoreElementData> { MapToRealmLoreElementData(body.Element) },
                    CreatedAtUnix = nowUnix,
                    UpdatedAtUnix = nowUnix
                };
            }
            else
            {
                loreData = existing!;
                var existingElement = loreData.Elements.FirstOrDefault(e =>
                    e.ElementType == body.Element.ElementType &&
                    e.Key == body.Element.Key);

                if (existingElement != null)
                {
                    // Update existing element
                    existingElement.Value = body.Element.Value;
                    existingElement.Strength = body.Element.Strength;
                    existingElement.RelatedEntityId = body.Element.RelatedEntityId;
                    existingElement.RelatedEntityType = body.Element.RelatedEntityType;
                }
                else
                {
                    // Add new element
                    loreData.Elements.Add(MapToRealmLoreElementData(body.Element));
                }
                loreData.UpdatedAtUnix = nowUnix;
            }

            await loreStore.SaveAsync(loreKey, loreData, cancellationToken: cancellationToken);

            // Publish typed event per FOUNDATION TENETS
            var now = DateTimeOffset.UtcNow;
            if (isNew)
            {
                await _messageBus.TryPublishAsync(LORE_CREATED_TOPIC, new RealmLoreCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    RealmId = body.RealmId,
                    ElementCount = loreData.Elements.Count
                }, cancellationToken: cancellationToken);
            }
            else
            {
                await _messageBus.TryPublishAsync(LORE_UPDATED_TOPIC, new RealmLoreUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    RealmId = body.RealmId,
                    ElementCount = loreData.Elements.Count,
                    ReplaceExisting = false
                }, cancellationToken: cancellationToken);
            }

            _logger.LogDebug("Added lore element for realm {RealmId}", body.RealmId);

            return (StatusCodes.OK, new RealmLoreResponse
            {
                RealmId = body.RealmId,
                Elements = loreData.Elements.Select(MapToRealmLoreElement).ToList(),
                CreatedAt = TimestampHelper.FromUnixSeconds(loreData.CreatedAtUnix),
                UpdatedAt = TimestampHelper.FromUnixSeconds(loreData.UpdatedAtUnix)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding lore element for realm {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm-history",
                "AddRealmLoreElement",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/realm-history/add-lore-element",
                details: new { body.RealmId, body.Element.ElementType, body.Element.Key },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deletes all lore for a realm.
    /// </summary>
    public async Task<StatusCodes> DeleteRealmLoreAsync(
        DeleteRealmLoreRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting lore for realm {RealmId}", body.RealmId);

        try
        {
            var loreStore = _stateStoreFactory.GetStore<RealmLoreData>(StateStoreDefinitions.RealmHistory);
            var loreKey = $"{LORE_KEY_PREFIX}{body.RealmId}";
            var existing = await loreStore.GetAsync(loreKey, cancellationToken);

            if (existing == null)
            {
                return StatusCodes.NotFound;
            }

            await loreStore.DeleteAsync(loreKey, cancellationToken);

            // Publish typed event per FOUNDATION TENETS
            await _messageBus.TryPublishAsync(LORE_DELETED_TOPIC, new RealmLoreDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RealmId = body.RealmId
            }, cancellationToken: cancellationToken);

            _logger.LogDebug("Deleted lore for realm {RealmId}", body.RealmId);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting lore for realm {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm-history",
                "DeleteRealmLore",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/realm-history/delete-lore",
                details: new { body.RealmId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    // ============================================================================
    // History Management Methods
    // ============================================================================

    /// <summary>
    /// Deletes all history data for a realm.
    /// </summary>
    public async Task<(StatusCodes, DeleteAllRealmHistoryResponse?)> DeleteAllRealmHistoryAsync(
        DeleteAllRealmHistoryRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting all history for realm {RealmId}", body.RealmId);

        try
        {
            var participationsDeleted = 0;
            var loreDeleted = false;

            // Delete all participation records
            var indexStore = _stateStoreFactory.GetStore<RealmParticipationIndexData>(StateStoreDefinitions.RealmHistory);
            var participationStore = _stateStoreFactory.GetStore<RealmParticipationData>(StateStoreDefinitions.RealmHistory);
            var realmIndexKey = $"{PARTICIPATION_INDEX_KEY_PREFIX}{body.RealmId}";
            var realmIndex = await indexStore.GetAsync(realmIndexKey, cancellationToken);

            if (realmIndex != null)
            {
                foreach (var participationId in realmIndex.ParticipationIds.ToList())
                {
                    var participationKey = $"{PARTICIPATION_KEY_PREFIX}{participationId}";
                    var participation = await participationStore.GetAsync(participationKey, cancellationToken);

                    if (participation != null)
                    {
                        // Remove from event index
                        var eventIndexKey = $"{PARTICIPATION_BY_EVENT_KEY_PREFIX}{participation.EventId}";
                        var eventIndex = await indexStore.GetAsync(eventIndexKey, cancellationToken);
                        if (eventIndex != null)
                        {
                            eventIndex.ParticipationIds.Remove(participationId);
                            await indexStore.SaveAsync(eventIndexKey, eventIndex, cancellationToken: cancellationToken);
                        }

                        // Delete the participation record
                        await participationStore.DeleteAsync(participationKey, cancellationToken);
                        participationsDeleted++;
                    }
                }

                // Delete the realm index
                await indexStore.DeleteAsync(realmIndexKey, cancellationToken);
            }

            // Delete lore
            var loreStore = _stateStoreFactory.GetStore<RealmLoreData>(StateStoreDefinitions.RealmHistory);
            var loreKey = $"{LORE_KEY_PREFIX}{body.RealmId}";
            var existingLore = await loreStore.GetAsync(loreKey, cancellationToken);
            if (existingLore != null)
            {
                await loreStore.DeleteAsync(loreKey, cancellationToken);
                loreDeleted = true;
            }

            // Publish typed event per FOUNDATION TENETS
            await _messageBus.TryPublishAsync(HISTORY_DELETED_TOPIC, new RealmHistoryDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RealmId = body.RealmId,
                ParticipationsDeleted = participationsDeleted,
                LoreDeleted = loreDeleted
            }, cancellationToken: cancellationToken);

            _logger.LogDebug(
                "Deleted all history for realm {RealmId}: {ParticipationsDeleted} participations, lore={LoreDeleted}",
                body.RealmId, participationsDeleted, loreDeleted);

            return (StatusCodes.OK, new DeleteAllRealmHistoryResponse
            {
                RealmId = body.RealmId,
                ParticipationsDeleted = participationsDeleted,
                LoreDeleted = loreDeleted
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting all history for realm {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm-history",
                "DeleteAllRealmHistory",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/realm-history/delete-all",
                details: new { body.RealmId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Generates text summaries for realm archival.
    /// </summary>
    public async Task<(StatusCodes, RealmHistorySummaryResponse?)> SummarizeRealmHistoryAsync(
        SummarizeRealmHistoryRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Summarizing history for realm {RealmId}", body.RealmId);

        try
        {
            var keyLorePoints = new List<string>();
            var majorHistoricalEvents = new List<string>();

            // Get lore points
            var loreStore = _stateStoreFactory.GetStore<RealmLoreData>(StateStoreDefinitions.RealmHistory);
            var loreKey = $"{LORE_KEY_PREFIX}{body.RealmId}";
            var loreData = await loreStore.GetAsync(loreKey, cancellationToken);

            if (loreData != null)
            {
                // Sort by strength and take top elements
                var topElements = loreData.Elements
                    .OrderByDescending(e => e.Strength)
                    .Take(body.MaxLorePoints);

                foreach (var element in topElements)
                {
                    var summary = GenerateLoreSummary(element);
                    keyLorePoints.Add(summary);
                }
            }

            // Get historical events
            var indexStore = _stateStoreFactory.GetStore<RealmParticipationIndexData>(StateStoreDefinitions.RealmHistory);
            var indexKey = $"{PARTICIPATION_INDEX_KEY_PREFIX}{body.RealmId}";
            var index = await indexStore.GetAsync(indexKey, cancellationToken);

            if (index != null && index.ParticipationIds.Count > 0)
            {
                var participationStore = _stateStoreFactory.GetStore<RealmParticipationData>(StateStoreDefinitions.RealmHistory);
                var allParticipations = new List<RealmParticipationData>();

                foreach (var participationId in index.ParticipationIds)
                {
                    var data = await participationStore.GetAsync(
                        $"{PARTICIPATION_KEY_PREFIX}{participationId}",
                        cancellationToken);
                    if (data != null)
                    {
                        allParticipations.Add(data);
                    }
                }

                // Sort by impact and take top events
                var topEvents = allParticipations
                    .OrderByDescending(p => p.Impact)
                    .Take(body.MaxHistoricalEvents);

                foreach (var participation in topEvents)
                {
                    var summary = GenerateEventSummary(participation);
                    majorHistoricalEvents.Add(summary);
                }
            }

            return (StatusCodes.OK, new RealmHistorySummaryResponse
            {
                RealmId = body.RealmId,
                KeyLorePoints = keyLorePoints,
                MajorHistoricalEvents = majorHistoricalEvents
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error summarizing history for realm {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm-history",
                "SummarizeRealmHistory",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/realm-history/summarize",
                details: new { body.RealmId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    // ============================================================================
    // Mapping Methods
    // ============================================================================

    private static RealmHistoricalParticipation MapToRealmHistoricalParticipation(RealmParticipationData data)
    {
        return new RealmHistoricalParticipation
        {
            ParticipationId = data.ParticipationId,
            RealmId = data.RealmId,
            EventId = data.EventId,
            EventName = data.EventName,
            EventCategory = data.EventCategory,
            Role = data.Role,
            EventDate = DateTimeOffset.FromUnixTimeSeconds(data.EventDateUnix),
            Impact = data.Impact,
            Metadata = data.Metadata,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(data.CreatedAtUnix)
        };
    }

    private static RealmLoreElement MapToRealmLoreElement(RealmLoreElementData data)
    {
        return new RealmLoreElement
        {
            ElementType = data.ElementType,
            Key = data.Key,
            Value = data.Value,
            Strength = data.Strength,
            RelatedEntityId = data.RelatedEntityId,
            RelatedEntityType = data.RelatedEntityType
        };
    }

    private static RealmLoreElementData MapToRealmLoreElementData(RealmLoreElement element)
    {
        return new RealmLoreElementData
        {
            ElementType = element.ElementType,
            Key = element.Key,
            Value = element.Value,
            Strength = element.Strength,
            RelatedEntityId = element.RelatedEntityId,
            RelatedEntityType = element.RelatedEntityType
        };
    }

    private static string GenerateLoreSummary(RealmLoreElementData element)
    {
        var typeLabel = element.ElementType switch
        {
            RealmLoreElementType.ORIGIN_MYTH => "Origin",
            RealmLoreElementType.CULTURAL_PRACTICE => "Cultural practice",
            RealmLoreElementType.POLITICAL_SYSTEM => "Political system",
            RealmLoreElementType.ECONOMIC_BASE => "Economic base",
            RealmLoreElementType.RELIGIOUS_TRADITION => "Religious tradition",
            RealmLoreElementType.GEOGRAPHIC_FEATURE => "Geographic feature",
            RealmLoreElementType.FAMOUS_FIGURE => "Famous figure",
            RealmLoreElementType.TECHNOLOGICAL_LEVEL => "Technology level",
            _ => element.ElementType.ToString()
        };

        return $"{typeLabel}: {element.Key} - {element.Value}";
    }

    private static string GenerateEventSummary(RealmParticipationData participation)
    {
        var roleVerb = participation.Role switch
        {
            RealmEventRole.ORIGIN => "originated",
            RealmEventRole.AGGRESSOR => "instigated",
            RealmEventRole.DEFENDER => "defended against",
            RealmEventRole.MEDIATOR => "mediated",
            RealmEventRole.AFFECTED => "was affected by",
            RealmEventRole.BENEFICIARY => "benefited from",
            RealmEventRole.INSTIGATOR => "instigated",
            RealmEventRole.NEUTRAL_PARTY => "observed",
            _ => "participated in"
        };

        return $"{participation.EventName} ({roleVerb})";
    }

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Overrides the default IBannouService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogDebug("Registering RealmHistory service permissions...");
        await RealmHistoryPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

    #endregion
}

// ============================================================================
// Internal Data Models
// ============================================================================

/// <summary>
/// Internal storage model for realm participation data.
/// </summary>
internal class RealmParticipationData
{
    public Guid ParticipationId { get; set; }
    public Guid RealmId { get; set; }
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public RealmEventCategory EventCategory { get; set; }
    public RealmEventRole Role { get; set; }
    public long EventDateUnix { get; set; }
    public float Impact { get; set; }
    public object? Metadata { get; set; }
    public long CreatedAtUnix { get; set; }
}

/// <summary>
/// Internal storage model for realm participation index (by realm or event).
/// </summary>
internal class RealmParticipationIndexData
{
    public Guid RealmId { get; set; }
    public List<Guid> ParticipationIds { get; set; } = new();
}

/// <summary>
/// Internal storage model for realm lore data.
/// </summary>
internal class RealmLoreData
{
    public Guid RealmId { get; set; }
    public List<RealmLoreElementData> Elements { get; set; } = new();
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
}

/// <summary>
/// Internal storage model for a realm lore element.
/// </summary>
internal class RealmLoreElementData
{
    public RealmLoreElementType ElementType { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public float Strength { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
}
