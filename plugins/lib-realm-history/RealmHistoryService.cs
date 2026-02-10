using BeyondImmersion.Bannou.Core;
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
/// Uses shared History infrastructure helpers for dual-index and backstory storage patterns.
/// </summary>
[BannouService("realm-history", typeof(IRealmHistoryService), lifetime: ServiceLifetime.Scoped)]
public partial class RealmHistoryService : IRealmHistoryService
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<RealmHistoryService> _logger;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly RealmHistoryServiceConfiguration _configuration;
    private readonly IDualIndexHelper<RealmParticipationData> _participationHelper;
    private readonly IBackstoryStorageHelper<RealmLoreData, RealmLoreElementData> _loreHelper;

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
        IEventConsumer eventConsumer,
        IDistributedLockProvider lockProvider)
    {
        _messageBus = messageBus;
        _logger = logger;
        _stateStoreFactory = stateStoreFactory;
        _configuration = configuration;

        // Initialize participation helper using shared dual-index infrastructure
        _participationHelper = new DualIndexHelper<RealmParticipationData>(
            stateStoreFactory,
            StateStoreDefinitions.RealmHistory,
            PARTICIPATION_KEY_PREFIX,
            PARTICIPATION_INDEX_KEY_PREFIX,
            PARTICIPATION_BY_EVENT_KEY_PREFIX,
            lockProvider,
            configuration.IndexLockTimeoutSeconds);

        // Initialize lore helper using shared backstory storage infrastructure
        _loreHelper = new BackstoryStorageHelper<RealmLoreData, RealmLoreElementData>(
            new BackstoryStorageConfiguration<RealmLoreData, RealmLoreElementData>
            {
                StateStoreFactory = stateStoreFactory,
                StateStoreName = StateStoreDefinitions.RealmHistory,
                KeyPrefix = LORE_KEY_PREFIX,
                ElementMatcher = new BackstoryElementMatcher<RealmLoreElementData>(
                    getType: e => e.ElementType.ToString(),
                    getKey: e => e.Key,
                    copyValues: (src, tgt) =>
                    {
                        tgt.Value = src.Value;
                        tgt.Strength = src.Strength;
                        tgt.RelatedEntityId = src.RelatedEntityId;
                        tgt.RelatedEntityType = src.RelatedEntityType;
                    },
                    clone: e => new RealmLoreElementData
                    {
                        ElementType = e.ElementType,
                        Key = e.Key,
                        Value = e.Value,
                        Strength = e.Strength,
                        RelatedEntityId = e.RelatedEntityId,
                        RelatedEntityType = e.RelatedEntityType
                    }),
                GetEntityId = l => l.RealmId.ToString(),
                SetEntityId = (l, id) => l.RealmId = Guid.Parse(id),
                GetElements = l => l.Elements,
                SetElements = (l, els) => l.Elements = els,
                GetCreatedAtUnix = l => l.CreatedAtUnix,
                SetCreatedAtUnix = (l, ts) => l.CreatedAtUnix = ts,
                GetUpdatedAtUnix = l => l.UpdatedAtUnix,
                SetUpdatedAtUnix = (l, ts) => l.UpdatedAtUnix = ts,
                LockProvider = lockProvider,
                LockTimeoutSeconds = configuration.IndexLockTimeoutSeconds
            });

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

            // Use helper to store record and update both indices
            // Acquires distributed lock on primary key per IMPLEMENTATION TENETS
            var addResult = await _participationHelper.AddRecordAsync(
                participationData,
                participationId.ToString(),
                body.RealmId.ToString(),
                body.EventId.ToString(),
                cancellationToken);

            if (!addResult.LockAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for realm {RealmId} participation recording", body.RealmId);
                return (StatusCodes.Conflict, null);
            }

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

            // Register realm reference with lib-resource for cleanup coordination
            await RegisterRealmReferenceAsync(participationId.ToString(), body.RealmId, cancellationToken);

            _logger.LogDebug("Recorded participation {ParticipationId} for realm {RealmId}",
                participationId, body.RealmId);

            return (StatusCodes.OK, MapToRealmHistoricalParticipation(participationData));
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
            var jsonStore = _stateStoreFactory.GetJsonQueryableStore<RealmParticipationData>(
                StateStoreDefinitions.RealmHistory);

            var conditions = BuildRealmParticipationQueryConditions(
                "$.RealmId", body.RealmId,
                eventCategory: body.EventCategory,
                minimumImpact: body.MinimumImpact,
                role: null);

            var sortSpec = new JsonSortSpec { Path = "$.EventDateUnix", Descending = true };
            var (skip, take) = PaginationHelper.CalculatePagination(body.Page, body.PageSize);

            var result = await jsonStore.JsonQueryPagedAsync(
                conditions, skip, take, sortSpec, cancellationToken);

            var participations = result.Items
                .Select(item => MapToRealmHistoricalParticipation(item.Value))
                .ToList();

            var paginatedResult = PaginationHelper.CreateResult(
                participations, (int)result.TotalCount, body.Page, body.PageSize);

            return (StatusCodes.OK, new RealmParticipationListResponse
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
    /// Uses server-side MySQL JSON queries for efficient pagination per IMPLEMENTATION TENETS.
    /// </summary>
    public async Task<(StatusCodes, RealmParticipationListResponse?)> GetRealmEventParticipantsAsync(
        GetRealmEventParticipantsRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting participants for event {EventId}", body.EventId);

        try
        {
            var jsonStore = _stateStoreFactory.GetJsonQueryableStore<RealmParticipationData>(
                StateStoreDefinitions.RealmHistory);

            var conditions = BuildRealmParticipationQueryConditions(
                "$.EventId", body.EventId,
                eventCategory: null,
                minimumImpact: null,
                role: body.Role);

            var sortSpec = new JsonSortSpec { Path = "$.Impact", Descending = true };
            var (skip, take) = PaginationHelper.CalculatePagination(body.Page, body.PageSize);

            var result = await jsonStore.JsonQueryPagedAsync(
                conditions, skip, take, sortSpec, cancellationToken);

            var participations = result.Items
                .Select(item => MapToRealmHistoricalParticipation(item.Value))
                .ToList();

            var paginatedResult = PaginationHelper.CreateResult(
                participations, (int)result.TotalCount, body.Page, body.PageSize);

            return (StatusCodes.OK, new RealmParticipationListResponse
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
            // First get the record to know the keys for index cleanup
            var data = await _participationHelper.GetRecordAsync(body.ParticipationId.ToString(), cancellationToken);

            if (data == null)
            {
                return StatusCodes.NotFound;
            }

            // Unregister realm reference before deletion
            await UnregisterRealmReferenceAsync(body.ParticipationId.ToString(), data.RealmId, cancellationToken);

            // Use helper to remove record and update both indices
            // Acquires distributed lock on primary key per IMPLEMENTATION TENETS
            var removeResult = await _participationHelper.RemoveRecordAsync(
                body.ParticipationId.ToString(),
                data.RealmId.ToString(),
                data.EventId.ToString(),
                cancellationToken);

            if (!removeResult.LockAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for participation {ParticipationId} deletion", body.ParticipationId);
                return StatusCodes.Conflict;
            }

            // Publish typed event per FOUNDATION TENETS
            await _messageBus.TryPublishAsync(PARTICIPATION_DELETED_TOPIC, new RealmParticipationDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ParticipationId = body.ParticipationId,
                RealmId = data.RealmId,
                HistoricalEventId = data.EventId
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
            var loreData = await _loreHelper.GetAsync(body.RealmId.ToString(), cancellationToken);

            if (loreData == null)
            {
                _logger.LogDebug("No lore found for realm {RealmId}", body.RealmId);
                return (StatusCodes.NotFound, null);
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
            var elementDataList = body.Elements.Select(MapToRealmLoreElementData).ToList();
            var maxElements = _configuration.MaxLoreElements;

            if (body.ReplaceExisting)
            {
                // Replace mode: the final count equals the input count
                if (elementDataList.Count > maxElements)
                {
                    _logger.LogWarning(
                        "SetLore rejected for realm {RealmId}: {Count} elements exceeds limit of {Limit}",
                        body.RealmId, elementDataList.Count, maxElements);
                    return (StatusCodes.BadRequest, null);
                }
            }
            else
            {
                // Merge mode: calculate post-merge count (existing + truly new elements)
                var existing = await _loreHelper.GetAsync(body.RealmId.ToString(), cancellationToken);
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
                            "SetLore merge rejected for realm {RealmId}: post-merge count {PostMerge} exceeds limit of {Limit} (existing={Existing}, new={New})",
                            body.RealmId, postMergeCount, maxElements, existingElements.Count, newElementCount);
                        return (StatusCodes.BadRequest, null);
                    }
                }
                else
                {
                    // No existing lore: the final count equals the input count
                    if (elementDataList.Count > maxElements)
                    {
                        _logger.LogWarning(
                            "SetLore rejected for realm {RealmId}: {Count} elements exceeds limit of {Limit}",
                            body.RealmId, elementDataList.Count, maxElements);
                        return (StatusCodes.BadRequest, null);
                    }
                }
            }

            // Acquires distributed lock on entity ID per IMPLEMENTATION TENETS
            var lockResult = await _loreHelper.SetAsync(
                body.RealmId.ToString(),
                elementDataList,
                body.ReplaceExisting,
                cancellationToken);

            if (!lockResult.LockAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for realm {RealmId} lore set", body.RealmId);
                return (StatusCodes.Conflict, null);
            }

            var result = lockResult.Value
                ?? throw new InvalidOperationException("Lock acquired but lore set result is null");

            var response = new RealmLoreResponse
            {
                RealmId = body.RealmId,
                Elements = result.Backstory.Elements.Select(MapToRealmLoreElement).ToList(),
                CreatedAt = TimestampHelper.FromUnixSeconds(result.Backstory.CreatedAtUnix),
                UpdatedAt = TimestampHelper.FromUnixSeconds(result.Backstory.UpdatedAtUnix)
            };

            // Publish typed event per FOUNDATION TENETS
            var now = DateTimeOffset.UtcNow;
            if (result.IsNew)
            {
                await _messageBus.TryPublishAsync(LORE_CREATED_TOPIC, new RealmLoreCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    RealmId = body.RealmId,
                    ElementCount = result.Backstory.Elements.Count
                }, cancellationToken: cancellationToken);

                // Register realm reference with lib-resource for cleanup coordination (only on new lore)
                await RegisterRealmReferenceAsync($"lore-{body.RealmId}", body.RealmId, cancellationToken);
            }
            else
            {
                await _messageBus.TryPublishAsync(LORE_UPDATED_TOPIC, new RealmLoreUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    RealmId = body.RealmId,
                    ElementCount = result.Backstory.Elements.Count,
                    ReplaceExisting = body.ReplaceExisting
                }, cancellationToken: cancellationToken);
            }

            _logger.LogDebug("Lore {Action} for realm {RealmId}, {Count} elements",
                result.IsNew ? "created" : "updated", body.RealmId, result.Backstory.Elements.Count);

            return (StatusCodes.OK, response);
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
            var elementData = MapToRealmLoreElementData(body.Element);

            // Validate element count limit (only for truly new elements, not updates)
            var existing = await _loreHelper.GetAsync(body.RealmId.ToString(), cancellationToken);
            if (existing != null)
            {
                var isUpdate = existing.Elements.Any(e =>
                    e.ElementType == elementData.ElementType && e.Key == elementData.Key);

                if (!isUpdate && existing.Elements.Count >= _configuration.MaxLoreElements)
                {
                    _logger.LogWarning(
                        "AddLoreElement rejected for realm {RealmId}: {Count} elements already at limit of {Limit}",
                        body.RealmId, existing.Elements.Count, _configuration.MaxLoreElements);
                    return (StatusCodes.BadRequest, null);
                }
            }

            // Acquires distributed lock on entity ID per IMPLEMENTATION TENETS
            var lockResult = await _loreHelper.AddElementAsync(
                body.RealmId.ToString(),
                elementData,
                cancellationToken);

            if (!lockResult.LockAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for realm {RealmId} lore element add", body.RealmId);
                return (StatusCodes.Conflict, null);
            }

            var result = lockResult.Value
                ?? throw new InvalidOperationException("Lock acquired but lore add element result is null");

            var response = new RealmLoreResponse
            {
                RealmId = body.RealmId,
                Elements = result.Backstory.Elements.Select(MapToRealmLoreElement).ToList(),
                CreatedAt = TimestampHelper.FromUnixSeconds(result.Backstory.CreatedAtUnix),
                UpdatedAt = TimestampHelper.FromUnixSeconds(result.Backstory.UpdatedAtUnix)
            };

            // Publish typed event per FOUNDATION TENETS
            var now = DateTimeOffset.UtcNow;
            if (result.IsNew)
            {
                await _messageBus.TryPublishAsync(LORE_CREATED_TOPIC, new RealmLoreCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    RealmId = body.RealmId,
                    ElementCount = result.Backstory.Elements.Count
                }, cancellationToken: cancellationToken);

                // Register realm reference with lib-resource for cleanup coordination (only on new lore)
                await RegisterRealmReferenceAsync($"lore-{body.RealmId}", body.RealmId, cancellationToken);
            }
            else
            {
                await _messageBus.TryPublishAsync(LORE_UPDATED_TOPIC, new RealmLoreUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    RealmId = body.RealmId,
                    ElementCount = result.Backstory.Elements.Count,
                    ReplaceExisting = false
                }, cancellationToken: cancellationToken);
            }

            _logger.LogDebug("Lore element {Action} for realm {RealmId}, now {Count} elements",
                result.IsNew ? "created" : "added", body.RealmId, result.Backstory.Elements.Count);

            return (StatusCodes.OK, response);
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
            // Acquires distributed lock on entity ID per IMPLEMENTATION TENETS
            var lockResult = await _loreHelper.DeleteAsync(body.RealmId.ToString(), cancellationToken);

            if (!lockResult.LockAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for realm {RealmId} lore deletion", body.RealmId);
                return StatusCodes.Conflict;
            }

            if (!lockResult.Value)
            {
                return StatusCodes.NotFound;
            }

            // Unregister realm reference for lore
            await UnregisterRealmReferenceAsync($"lore-{body.RealmId}", body.RealmId, cancellationToken);

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
            // Get all participations first to unregister their realm references
            var participationRecords = await _participationHelper.GetRecordsByPrimaryKeyAsync(
                body.RealmId.ToString(),
                cancellationToken);

            // Unregister all realm references for participations
            foreach (var record in participationRecords)
            {
                await UnregisterRealmReferenceAsync(record.ParticipationId.ToString(), body.RealmId, cancellationToken);
            }

            // Use helper to delete all participations and update event indices
            // Acquires distributed lock on primary key per IMPLEMENTATION TENETS
            var participationLockResult = await _participationHelper.RemoveAllByPrimaryKeyAsync(
                body.RealmId.ToString(),
                record => record.EventId.ToString(),
                cancellationToken);

            if (!participationLockResult.LockAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for realm {RealmId} participation bulk deletion", body.RealmId);
                return (StatusCodes.Conflict, null);
            }

            var participationsDeleted = participationLockResult.Value;

            // Check if lore exists to unregister its reference
            var existingLore = await _loreHelper.GetAsync(body.RealmId.ToString(), cancellationToken);
            if (existingLore != null)
            {
                await UnregisterRealmReferenceAsync($"lore-{body.RealmId}", body.RealmId, cancellationToken);
            }

            // Use helper to delete lore
            // Acquires distributed lock on entity ID per IMPLEMENTATION TENETS
            var loreLockResult = await _loreHelper.DeleteAsync(body.RealmId.ToString(), cancellationToken);

            if (!loreLockResult.LockAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for realm {RealmId} lore deletion during history purge", body.RealmId);
                return (StatusCodes.Conflict, null);
            }

            var loreDeleted = loreLockResult.Value;

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

            // Get lore and create summaries using helper
            var loreData = await _loreHelper.GetAsync(body.RealmId.ToString(), cancellationToken);

            if (loreData != null)
            {
                // Sort by strength and take top N
                var topElements = loreData.Elements
                    .OrderByDescending(e => e.Strength)
                    .Take(body.MaxLorePoints);

                foreach (var element in topElements)
                {
                    var summary = GenerateLoreSummary(element);
                    if (!string.IsNullOrEmpty(summary))
                    {
                        keyLorePoints.Add(summary);
                    }
                }
            }

            // Get participation and create summaries using helper
            var participations = await _participationHelper.GetRecordsByPrimaryKeyAsync(
                body.RealmId.ToString(),
                cancellationToken);

            if (participations.Count > 0)
            {
                // Sort by impact and take top N
                var topParticipations = participations
                    .OrderByDescending(p => p.Impact)
                    .Take(body.MaxHistoricalEvents);

                foreach (var participation in topParticipations)
                {
                    var summary = GenerateEventSummary(participation);
                    if (!string.IsNullOrEmpty(summary))
                    {
                        majorHistoricalEvents.Add(summary);
                    }
                }
            }

            var response = new RealmHistorySummaryResponse
            {
                RealmId = body.RealmId,
                KeyLorePoints = keyLorePoints,
                MajorHistoricalEvents = majorHistoricalEvents
            };

            _logger.LogDebug("Generated history summary for realm {RealmId}: {LoreCount} lore points, {EventCount} historical events",
                body.RealmId, keyLorePoints.Count, majorHistoricalEvents.Count);

            return (StatusCodes.OK, response);
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
    // Archive Methods (for lib-resource compression)
    // ============================================================================

    /// <summary>
    /// Gets realm history data for compression during realm archival.
    /// Called by Resource service during realm compression via compression callback.
    /// </summary>
    public async Task<(StatusCodes, RealmHistoryArchive?)> GetCompressDataAsync(
        GetCompressDataRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting compress data for realm {RealmId}", body.RealmId);

        try
        {
            // Get participations using helper
            var participationRecords = await _participationHelper.GetRecordsByPrimaryKeyAsync(
                body.RealmId.ToString(),
                cancellationToken);

            var participations = participationRecords
                .Select(MapToRealmHistoricalParticipation)
                .OrderByDescending(p => p.EventDate)
                .ToList();

            // Get lore using helper
            var loreData = await _loreHelper.GetAsync(body.RealmId.ToString(), cancellationToken);

            RealmLoreResponse? loreResponse = null;
            if (loreData != null)
            {
                loreResponse = new RealmLoreResponse
                {
                    RealmId = body.RealmId,
                    Elements = loreData.Elements.Select(MapToRealmLoreElement).ToList(),
                    CreatedAt = TimestampHelper.FromUnixSeconds(loreData.CreatedAtUnix),
                    UpdatedAt = TimestampHelper.FromUnixSeconds(loreData.UpdatedAtUnix)
                };
            }

            // Return 404 only if BOTH are missing
            if (participations.Count == 0 && loreData == null)
            {
                _logger.LogDebug("No history data found for realm {RealmId}", body.RealmId);
                return (StatusCodes.NotFound, null);
            }

            // Generate text summaries for the archive
            var summaries = GenerateSummariesForArchive(body.RealmId, participations, loreData);

            var response = new RealmHistoryArchive
            {
                // ResourceArchiveBase fields
                ResourceId = body.RealmId,
                ResourceType = "realm-history",
                ArchivedAt = DateTimeOffset.UtcNow,
                SchemaVersion = 1,
                // Service-specific fields
                RealmId = body.RealmId,
                HasParticipations = participations.Count > 0,
                Participations = participations,
                HasLore = loreData != null,
                LoreElements = loreResponse != null ? new List<RealmLoreResponse> { loreResponse } : new List<RealmLoreResponse>(),
                Summaries = summaries
            };

            _logger.LogInformation(
                "Compress data retrieved for realm {RealmId}: participations={ParticipationCount}, hasLore={HasLore}",
                body.RealmId, participations.Count, response.HasLore);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting compress data for realm {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm-history",
                "GetCompressData",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/realm-history/get-compress-data",
                details: new { body.RealmId },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Restores realm history data from a compressed archive.
    /// Called by Resource service during realm decompression via decompression callback.
    /// </summary>
    public async Task<(StatusCodes, RestoreFromArchiveResponse?)> RestoreFromArchiveAsync(
        RestoreFromArchiveRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Restoring history data from archive for realm {RealmId}", body.RealmId);

        var participationsRestored = 0;
        var loreRestored = 0;

        try
        {
            // Decompress the archive data
            RealmHistoryArchive archiveData;
            try
            {
                var compressedBytes = Convert.FromBase64String(body.Data);
                var jsonData = DecompressJsonData(compressedBytes);
                archiveData = BannouJson.Deserialize<RealmHistoryArchive>(jsonData)
                    ?? throw new InvalidOperationException("Deserialized archive data is null");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decompress archive data for realm {RealmId}", body.RealmId);
                return (StatusCodes.BadRequest, new RestoreFromArchiveResponse
                {
                    RealmId = body.RealmId,
                    ParticipationsRestored = 0,
                    LoreRestored = 0,
                    Success = false,
                    ErrorMessage = $"Invalid archive data: {ex.Message}"
                });
            }

            // Restore participations
            if (archiveData.HasParticipations && archiveData.Participations.Count > 0)
            {
                foreach (var participation in archiveData.Participations)
                {
                    var participationData = new RealmParticipationData
                    {
                        ParticipationId = participation.ParticipationId,
                        RealmId = participation.RealmId,
                        EventId = participation.EventId,
                        EventName = participation.EventName,
                        EventCategory = participation.EventCategory,
                        Role = participation.Role,
                        EventDateUnix = participation.EventDate.ToUnixTimeSeconds(),
                        Impact = participation.Impact,
                        Metadata = participation.Metadata,
                        CreatedAtUnix = participation.CreatedAt.ToUnixTimeSeconds()
                    };

                    // Acquires distributed lock on primary key per IMPLEMENTATION TENETS
                    var addResult = await _participationHelper.AddRecordAsync(
                        participationData,
                        participation.ParticipationId.ToString(),
                        participation.RealmId.ToString(),
                        participation.EventId.ToString(),
                        cancellationToken);

                    if (!addResult.LockAcquired)
                    {
                        _logger.LogWarning("Failed to acquire lock during archive restoration for realm {RealmId}", body.RealmId);
                        return (StatusCodes.Conflict, null);
                    }

                    // Re-register realm reference
                    await RegisterRealmReferenceAsync(participation.ParticipationId.ToString(), participation.RealmId, cancellationToken);

                    participationsRestored++;
                }
            }

            // Restore lore
            if (archiveData.HasLore && archiveData.LoreElements.Count > 0)
            {
                // Aggregate all elements from all lore responses
                var allElements = archiveData.LoreElements.SelectMany(lr => lr.Elements).ToList();
                var elementDataList = allElements.Select(MapToRealmLoreElementData).ToList();
                // Acquires distributed lock on entity ID per IMPLEMENTATION TENETS
                var setResult = await _loreHelper.SetAsync(
                    body.RealmId.ToString(),
                    elementDataList,
                    replaceExisting: true,
                    cancellationToken);

                if (!setResult.LockAcquired)
                {
                    _logger.LogWarning("Failed to acquire lock during lore restoration for realm {RealmId}", body.RealmId);
                    return (StatusCodes.Conflict, null);
                }

                // Re-register realm reference for lore
                await RegisterRealmReferenceAsync($"lore-{body.RealmId}", body.RealmId, cancellationToken);

                loreRestored = elementDataList.Count;
            }

            _logger.LogInformation(
                "Restored history data for realm {RealmId}: {ParticipationsRestored} participations, lore={LoreRestored}",
                body.RealmId, participationsRestored, loreRestored);

            return (StatusCodes.OK, new RestoreFromArchiveResponse
            {
                RealmId = body.RealmId,
                ParticipationsRestored = participationsRestored,
                LoreRestored = loreRestored,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring history data from archive for realm {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm-history",
                "RestoreFromArchive",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/realm-history/restore-from-archive",
                details: new { body.RealmId, participationsRestored, loreRestored },
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    private RealmHistorySummaryResponse GenerateSummariesForArchive(
        Guid realmId,
        List<RealmHistoricalParticipation> participations,
        RealmLoreData? loreData)
    {
        var keyLorePoints = new List<string>();
        var majorHistoricalEvents = new List<string>();

        // Generate lore summaries
        if (loreData != null)
        {
            var topElements = loreData.Elements
                .OrderByDescending(e => e.Strength)
                .Take(_configuration.ArchiveSummaryMaxLorePoints);

            foreach (var element in topElements)
            {
                var summary = GenerateLoreSummary(element);
                if (!string.IsNullOrEmpty(summary))
                {
                    keyLorePoints.Add(summary);
                }
            }
        }

        // Generate event summaries
        if (participations.Count > 0)
        {
            var topParticipations = participations
                .OrderByDescending(p => p.Impact)
                .Take(_configuration.ArchiveSummaryMaxHistoricalEvents);

            foreach (var participation in topParticipations)
            {
                var summary = GenerateEventSummaryFromModel(participation);
                if (!string.IsNullOrEmpty(summary))
                {
                    majorHistoricalEvents.Add(summary);
                }
            }
        }

        return new RealmHistorySummaryResponse
        {
            RealmId = realmId,
            KeyLorePoints = keyLorePoints,
            MajorHistoricalEvents = majorHistoricalEvents
        };
    }

    private static string GenerateEventSummaryFromModel(RealmHistoricalParticipation participation)
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

    private static string DecompressJsonData(byte[] compressedBytes)
    {
        using var input = new System.IO.MemoryStream(compressedBytes);
        using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new System.IO.StreamReader(gzip, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ============================================================================
    // Query Helpers
    // ============================================================================

    /// <summary>
    /// Builds MySQL JSON query conditions for realm participation listing.
    /// Uses server-side filtering to avoid loading all records into memory.
    /// </summary>
    private static List<QueryCondition> BuildRealmParticipationQueryConditions(
        string entityIdPath,
        Guid entityId,
        RealmEventCategory? eventCategory,
        float? minimumImpact,
        RealmEventRole? role)
    {
        var conditions = new List<QueryCondition>
        {
            // Type discriminator: only RealmParticipationData records have ParticipationId
            // Prevents confusion with other data types stored in the same state store
            new QueryCondition
            {
                Path = "$.ParticipationId",
                Operator = QueryOperator.Exists,
                Value = true
            },
            // Primary entity filter (realm or event)
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

        if (minimumImpact.HasValue)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.Impact",
                Operator = QueryOperator.GreaterThanOrEqual,
                Value = minimumImpact.Value
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
