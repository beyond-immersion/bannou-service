using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Realm;

/// <summary>
/// Implementation of the Realm service.
/// Manages realm definitions - top-level persistent worlds in Arcadia (e.g., Omega, Arcadia, Fantasia).
/// Each realm operates as an independent peer with distinct characteristics.
/// </summary>
[BannouService("realm", typeof(IRealmService), lifetime: ServiceLifetime.Scoped)]
public partial class RealmService : IRealmService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<RealmService> _logger;
    private readonly RealmServiceConfiguration _configuration;

    private const string REALM_KEY_PREFIX = "realm:";
    private const string CODE_INDEX_PREFIX = "code-index:";
    private const string ALL_REALMS_KEY = "all-realms";

    public RealmService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<RealmService> logger,
        RealmServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;

        // Register event handlers via partial class (RealmServiceEvents.cs)
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    #region Key Building Helpers

    private static string BuildRealmKey(Guid realmId) => $"{REALM_KEY_PREFIX}{realmId}";
    private static string BuildCodeIndexKey(string code) => $"{CODE_INDEX_PREFIX}{code.ToUpperInvariant()}";

    #endregion

    #region Read Operations

    /// <summary>
    /// Get realm by ID.
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> GetRealmAsync(
        GetRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting realm by ID: {RealmId}", body.RealmId);

            var realmKey = BuildRealmKey(body.RealmId);
            var model = await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).GetAsync(realmKey, cancellationToken);

            if (model == null)
            {
                _logger.LogDebug("Realm not found: {RealmId}", body.RealmId);
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting realm: {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm", "GetRealm", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/realm/get",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Get realm by unique code (e.g., "REALM_1", "REALM_2").
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> GetRealmByCodeAsync(
        GetRealmByCodeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting realm by code: {Code}", body.Code);

            var codeIndexKey = BuildCodeIndexKey(body.Code);
            var realmIdStr = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Realm).GetAsync(codeIndexKey, cancellationToken);

            if (string.IsNullOrEmpty(realmIdStr) || !Guid.TryParse(realmIdStr, out var realmId))
            {
                _logger.LogDebug("Realm not found by code: {Code}", body.Code);
                return (StatusCodes.NotFound, null);
            }

            var realmKey = BuildRealmKey(realmId);
            var model = await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).GetAsync(realmKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Realm data inconsistency - code index exists but realm not found: {Code}", body.Code);
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting realm by code: {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "realm", "GetRealmByCode", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/realm/get-by-code",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// List all realms with optional filtering.
    /// </summary>
    public async Task<(StatusCodes, RealmListResponse?)> ListRealmsAsync(
        ListRealmsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Listing realms with filters - Category: {Category}, IsActive: {IsActive}, IncludeDeprecated: {IncludeDeprecated}",
                body.Category, body.IsActive, body.IncludeDeprecated);

            var allRealmIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Realm).GetAsync(ALL_REALMS_KEY, cancellationToken);

            if (allRealmIds == null || allRealmIds.Count == 0)
            {
                return (StatusCodes.OK, new RealmListResponse
                {
                    Realms = new List<RealmResponse>(),
                    TotalCount = 0,
                    Page = body.Page,
                    PageSize = body.PageSize
                });
            }

            var realmList = await LoadRealmsByIdsAsync(allRealmIds, cancellationToken);

            // Apply filters
            var filtered = realmList.AsEnumerable();

            // Filter out deprecated unless explicitly included
            if (!body.IncludeDeprecated)
            {
                filtered = filtered.Where(r => !r.IsDeprecated);
            }

            if (!string.IsNullOrEmpty(body.Category))
            {
                filtered = filtered.Where(r => string.Equals(r.Category, body.Category, StringComparison.OrdinalIgnoreCase));
            }

            if (body.IsActive.HasValue)
            {
                filtered = filtered.Where(r => r.IsActive == body.IsActive.Value);
            }

            var filteredList = filtered.ToList();
            var totalCount = filteredList.Count;

            // Apply pagination
            var page = body.Page;
            var pageSize = body.PageSize;
            var pagedList = filteredList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(MapToResponse)
                .ToList();

            return (StatusCodes.OK, new RealmListResponse
            {
                Realms = pagedList,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasNextPage = page * pageSize < totalCount,
                HasPreviousPage = page > 1
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing realms");
            await _messageBus.TryPublishErrorAsync(
                "realm", "ListRealms", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/realm/list",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Fast validation endpoint to check if realm exists and is active.
    /// </summary>
    public async Task<(StatusCodes, RealmExistsResponse?)> RealmExistsAsync(
        RealmExistsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Checking if realm exists: {RealmId}", body.RealmId);

            var realmKey = BuildRealmKey(body.RealmId);
            var model = await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).GetAsync(realmKey, cancellationToken);

            if (model == null)
            {
                return (StatusCodes.OK, new RealmExistsResponse
                {
                    Exists = false,
                    IsActive = false,
                    RealmId = null
                });
            }

            return (StatusCodes.OK, new RealmExistsResponse
            {
                Exists = true,
                IsActive = model.IsActive && !model.IsDeprecated,
                RealmId = model.RealmId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking realm existence: {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm", "RealmExists", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/realm/exists",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Write Operations

    /// <summary>
    /// Create a new realm.
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> CreateRealmAsync(
        CreateRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Creating realm with code: {Code}", body.Code);

            var code = body.Code.ToUpperInvariant();

            // Check if code already exists
            var codeIndexKey = BuildCodeIndexKey(code);
            var existingIdStr = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Realm).GetAsync(codeIndexKey, cancellationToken);

            if (!string.IsNullOrEmpty(existingIdStr))
            {
                _logger.LogDebug("Realm with code already exists: {Code}", code);
                return (StatusCodes.Conflict, null);
            }

            var realmId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var model = new RealmModel
            {
                RealmId = realmId,
                Code = code,
                Name = body.Name,
                GameServiceId = body.GameServiceId,
                Description = body.Description,
                Category = body.Category,
                IsActive = body.IsActive,
                IsDeprecated = false,
                DeprecatedAt = null,
                DeprecationReason = null,
                Metadata = body.Metadata,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Save the model
            var realmKey = BuildRealmKey(realmId);
            await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).SaveAsync(realmKey, model, cancellationToken: cancellationToken);

            // Update code index (stored as string for state store compatibility)
            await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Realm).SaveAsync(codeIndexKey, realmId.ToString(), cancellationToken: cancellationToken);

            // Update all-realms list
            var allRealmIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Realm).GetAsync(ALL_REALMS_KEY, cancellationToken) ?? new List<Guid>();
            if (!allRealmIds.Contains(realmId))
            {
                allRealmIds.Add(realmId);
                await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Realm).SaveAsync(ALL_REALMS_KEY, allRealmIds, cancellationToken: cancellationToken);
            }

            // Publish realm created event
            await PublishRealmCreatedEventAsync(model, cancellationToken);

            _logger.LogInformation("Created realm: {RealmId} with code {Code}", realmId, code);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating realm: {Code}", body.Code);
            await _messageBus.TryPublishErrorAsync(
                "realm", "CreateRealm", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/realm/create",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Update an existing realm.
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> UpdateRealmAsync(
        UpdateRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Updating realm: {RealmId}", body.RealmId);

            var realmKey = BuildRealmKey(body.RealmId);
            var model = await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).GetAsync(realmKey, cancellationToken);

            if (model == null)
            {
                _logger.LogDebug("Realm not found for update: {RealmId}", body.RealmId);
                return (StatusCodes.NotFound, null);
            }

            // Track changed fields and apply updates
            var changedFields = new List<string>();

            if (body.Name != null && body.Name != model.Name)
            {
                model.Name = body.Name;
                changedFields.Add("name");
            }
            if (body.Description != null && body.Description != model.Description)
            {
                model.Description = body.Description;
                changedFields.Add("description");
            }
            if (body.Category != null && body.Category != model.Category)
            {
                model.Category = body.Category;
                changedFields.Add("category");
            }
            if (body.IsActive.HasValue && body.IsActive.Value != model.IsActive)
            {
                model.IsActive = body.IsActive.Value;
                changedFields.Add("isActive");
            }
            if (body.GameServiceId.HasValue && body.GameServiceId.Value != model.GameServiceId)
            {
                model.GameServiceId = body.GameServiceId.Value;
                changedFields.Add("gameServiceId");
            }
            if (body.Metadata != null)
            {
                model.Metadata = body.Metadata;
                changedFields.Add("metadata");
            }

            if (changedFields.Count > 0)
            {
                model.UpdatedAt = DateTimeOffset.UtcNow;
                await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).SaveAsync(realmKey, model, cancellationToken: cancellationToken);

                // Publish realm updated event
                await PublishRealmUpdatedEventAsync(model, changedFields, cancellationToken);
            }

            _logger.LogInformation("Updated realm: {RealmId}", body.RealmId);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating realm: {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm", "UpdateRealm", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/realm/update",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Hard delete a realm. Only deprecated realms with zero references can be deleted.
    /// </summary>
    public async Task<StatusCodes> DeleteRealmAsync(
        DeleteRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Deleting realm: {RealmId}", body.RealmId);

            var realmKey = BuildRealmKey(body.RealmId);
            var model = await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).GetAsync(realmKey, cancellationToken);

            if (model == null)
            {
                _logger.LogDebug("Realm not found for deletion: {RealmId}", body.RealmId);
                return StatusCodes.NotFound;
            }

            // Realm should be deprecated before deletion
            if (!model.IsDeprecated)
            {
                _logger.LogDebug("Cannot delete realm {Code}: realm must be deprecated first", model.Code);
                return StatusCodes.Conflict;
            }

            // Delete the model
            await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).DeleteAsync(realmKey, cancellationToken);

            // Delete code index
            var codeIndexKey = BuildCodeIndexKey(model.Code);
            await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Realm).DeleteAsync(codeIndexKey, cancellationToken);

            // Remove from all-realms list
            var allRealmIds = await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Realm).GetAsync(ALL_REALMS_KEY, cancellationToken) ?? new List<Guid>();
            if (allRealmIds.Remove(body.RealmId))
            {
                await _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Realm).SaveAsync(ALL_REALMS_KEY, allRealmIds, cancellationToken: cancellationToken);
            }

            // Publish realm deleted event
            await PublishRealmDeletedEventAsync(model, null, cancellationToken);

            _logger.LogInformation("Deleted realm: {RealmId} ({Code})", body.RealmId, model.Code);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting realm: {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm", "DeleteRealm", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/realm/delete",
                details: null, stack: ex.StackTrace);
            return StatusCodes.InternalServerError;
        }
    }

    #endregion

    #region Deprecation Operations

    /// <summary>
    /// Deprecate a realm (soft-delete).
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> DeprecateRealmAsync(
        DeprecateRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Deprecating realm: {RealmId}", body.RealmId);

            var realmKey = BuildRealmKey(body.RealmId);
            var model = await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).GetAsync(realmKey, cancellationToken);

            if (model == null)
            {
                _logger.LogDebug("Realm not found for deprecation: {RealmId}", body.RealmId);
                return (StatusCodes.NotFound, null);
            }

            if (model.IsDeprecated)
            {
                _logger.LogDebug("Realm already deprecated: {RealmId}", body.RealmId);
                return (StatusCodes.Conflict, null);
            }

            model.IsDeprecated = true;
            model.DeprecatedAt = DateTimeOffset.UtcNow;
            model.DeprecationReason = body.Reason;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).SaveAsync(realmKey, model, cancellationToken: cancellationToken);

            // Publish realm updated event with deprecation fields
            await PublishRealmUpdatedEventAsync(model, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);

            _logger.LogInformation("Deprecated realm: {RealmId}", body.RealmId);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deprecating realm: {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm", "DeprecateRealm", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/realm/deprecate",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Restore a deprecated realm.
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> UndeprecateRealmAsync(
        UndeprecateRealmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Undeprecating realm: {RealmId}", body.RealmId);

            var realmKey = BuildRealmKey(body.RealmId);
            var model = await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).GetAsync(realmKey, cancellationToken);

            if (model == null)
            {
                _logger.LogDebug("Realm not found for undeprecation: {RealmId}", body.RealmId);
                return (StatusCodes.NotFound, null);
            }

            if (!model.IsDeprecated)
            {
                _logger.LogDebug("Realm is not deprecated: {RealmId}", body.RealmId);
                return (StatusCodes.BadRequest, null);
            }

            model.IsDeprecated = false;
            model.DeprecatedAt = null;
            model.DeprecationReason = null;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).SaveAsync(realmKey, model, cancellationToken: cancellationToken);

            // Publish realm updated event with deprecation fields cleared
            await PublishRealmUpdatedEventAsync(model, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);

            _logger.LogInformation("Undeprecated realm: {RealmId}", body.RealmId);
            return (StatusCodes.OK, MapToResponse(model));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error undeprecating realm: {RealmId}", body.RealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm", "UndeprecateRealm", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/realm/undeprecate",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Seed Operation

    /// <summary>
    /// Idempotent operation to seed realms from configuration.
    /// </summary>
    public async Task<(StatusCodes, SeedRealmsResponse?)> SeedRealmsAsync(
        SeedRealmsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Seeding {Count} realms, updateExisting: {UpdateExisting}",
                body.Realms.Count, body.UpdateExisting);

            var created = 0;
            var updated = 0;
            var skipped = 0;
            var errors = new List<string>();

            foreach (var seedRealm in body.Realms)
            {
                try
                {
                    var code = seedRealm.Code.ToUpperInvariant();
                    var codeIndexKey = BuildCodeIndexKey(code);
                    var existingIdStr = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Realm).GetAsync(codeIndexKey, cancellationToken);

                    if (!string.IsNullOrEmpty(existingIdStr) && Guid.TryParse(existingIdStr, out var existingId))
                    {
                        if (body.UpdateExisting == true)
                        {
                            // Update existing
                            var realmKey = BuildRealmKey(existingId);
                            var existingModel = await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).GetAsync(realmKey, cancellationToken);

                            if (existingModel != null)
                            {
                                existingModel.Name = seedRealm.Name;
                                existingModel.GameServiceId = seedRealm.GameServiceId;
                                if (seedRealm.Description != null) existingModel.Description = seedRealm.Description;
                                if (seedRealm.Category != null) existingModel.Category = seedRealm.Category;
                                existingModel.IsActive = seedRealm.IsActive;
                                if (seedRealm.Metadata != null) existingModel.Metadata = seedRealm.Metadata;
                                existingModel.UpdatedAt = DateTimeOffset.UtcNow;

                                await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).SaveAsync(realmKey, existingModel, cancellationToken: cancellationToken);
                                updated++;
                                _logger.LogDebug("Updated existing realm: {Code}", code);
                            }
                        }
                        else
                        {
                            skipped++;
                            _logger.LogDebug("Skipped existing realm: {Code}", code);
                        }
                    }
                    else
                    {
                        // Create new
                        var createRequest = new CreateRealmRequest
                        {
                            Code = code,
                            Name = seedRealm.Name,
                            GameServiceId = seedRealm.GameServiceId,
                            Description = seedRealm.Description,
                            Category = seedRealm.Category,
                            IsActive = seedRealm.IsActive,
                            Metadata = seedRealm.Metadata
                        };

                        var (status, _) = await CreateRealmAsync(createRequest, cancellationToken);

                        if (status == StatusCodes.OK)
                        {
                            created++;
                            _logger.LogDebug("Created new realm: {Code}", code);
                        }
                        else
                        {
                            errors.Add($"Failed to create realm {code}: {status}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error processing realm {seedRealm.Code}: {ex.Message}");
                    _logger.LogWarning(ex, "Error seeding realm: {Code}", seedRealm.Code);
                }
            }

            _logger.LogInformation("Seed complete - Created: {Created}, Updated: {Updated}, Skipped: {Skipped}, Errors: {Errors}",
                created, updated, skipped, errors.Count);

            return (StatusCodes.OK, new SeedRealmsResponse
            {
                Created = created,
                Updated = updated,
                Skipped = skipped,
                Errors = errors
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding realms");
            await _messageBus.TryPublishErrorAsync(
                "realm", "SeedRealms", "unexpected_exception", ex.Message,
                dependency: "state", endpoint: "post:/realm/seed",
                details: null, stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Helper Methods

    private async Task<List<RealmModel>> LoadRealmsByIdsAsync(List<Guid> realmIds, CancellationToken cancellationToken)
    {
        if (realmIds.Count == 0)
        {
            return new List<RealmModel>();
        }

        var keys = realmIds.Select(BuildRealmKey).ToList();
        var bulkResults = await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).GetBulkAsync(keys, cancellationToken);

        var realmList = new List<RealmModel>();
        foreach (var (_, model) in bulkResults)
        {
            if (model != null)
            {
                realmList.Add(model);
            }
        }

        return realmList;
    }

    private static RealmResponse MapToResponse(RealmModel model)
    {
        return new RealmResponse
        {
            RealmId = model.RealmId,
            Code = model.Code,
            Name = model.Name,
            GameServiceId = model.GameServiceId,
            Description = model.Description,
            Category = model.Category,
            IsActive = model.IsActive,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    #endregion

    #region Event Publishing

    /// <summary>
    /// Publishes a realm created event.
    /// </summary>
    private async Task PublishRealmCreatedEventAsync(RealmModel model, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new RealmCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RealmId = model.RealmId,
                Code = model.Code,
                Name = model.Name,
                GameServiceId = model.GameServiceId,
                Category = model.Category,
                IsActive = model.IsActive
            };

            await _messageBus.TryPublishAsync("realm.created", eventModel, cancellationToken: cancellationToken);
            _logger.LogDebug("Published realm.created event for {RealmId}", model.RealmId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish realm.created event for {RealmId}", model.RealmId);
        }
    }

    /// <summary>
    /// Publishes a realm updated event with current state and changed fields.
    /// </summary>
    private async Task PublishRealmUpdatedEventAsync(RealmModel model, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new RealmUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RealmId = model.RealmId,
                Code = model.Code,
                Name = model.Name,
                GameServiceId = model.GameServiceId,
                Description = model.Description,
                Category = model.Category,
                IsActive = model.IsActive,
                IsDeprecated = model.IsDeprecated,
                DeprecatedAt = model.DeprecatedAt ?? default,
                DeprecationReason = model.DeprecationReason,
                CreatedAt = model.CreatedAt,
                UpdatedAt = model.UpdatedAt,
                ChangedFields = changedFields.ToList()
            };

            await _messageBus.TryPublishAsync("realm.updated", eventModel, cancellationToken: cancellationToken);
            _logger.LogDebug("Published realm.updated event for {RealmId} with changed fields: {ChangedFields}",
                model.RealmId, string.Join(", ", changedFields));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish realm.updated event for {RealmId}", model.RealmId);
        }
    }

    /// <summary>
    /// Publishes a realm deleted event with final state before deletion.
    /// </summary>
    private async Task PublishRealmDeletedEventAsync(RealmModel model, string? deletedReason, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new RealmDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RealmId = model.RealmId,
                Code = model.Code,
                Name = model.Name,
                GameServiceId = model.GameServiceId,
                Description = model.Description,
                Category = model.Category,
                IsActive = model.IsActive,
                IsDeprecated = model.IsDeprecated,
                DeprecatedAt = model.DeprecatedAt ?? default,
                DeprecationReason = model.DeprecationReason,
                CreatedAt = model.CreatedAt,
                UpdatedAt = model.UpdatedAt,
                DeletedReason = deletedReason
            };

            await _messageBus.TryPublishAsync("realm.deleted", eventModel, cancellationToken: cancellationToken);
            _logger.LogDebug("Published realm.deleted event for {RealmId}", model.RealmId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish realm.deleted event for {RealmId}", model.RealmId);
        }
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Uses generated permission data from x-permissions sections in the OpenAPI schema.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogDebug("Registering Realm service permissions...");
        await RealmPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

    #endregion
}

/// <summary>
/// Internal storage model for realm data.
/// </summary>
internal class RealmModel
{
    public Guid RealmId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid GameServiceId { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeprecated { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
    public string? DeprecationReason { get; set; }
    public object? Metadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
