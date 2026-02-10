using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Realm;

/// <summary>
/// Implementation of the Realm service.
/// Manages realm definitions - top-level persistent worlds (e.g., REALM_1, REALM_2).
/// Each realm operates as an independent peer with distinct characteristics.
/// </summary>
[BannouService("realm", typeof(IRealmService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class RealmService : IRealmService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<RealmService> _logger;
    private readonly RealmServiceConfiguration _configuration;
    private readonly IResourceClient _resourceClient;
    private readonly ISpeciesClient _speciesClient;
    private readonly ILocationClient _locationClient;
    private readonly ICharacterClient _characterClient;

    private const string REALM_KEY_PREFIX = "realm:";
    private const string CODE_INDEX_PREFIX = "code-index:";
    private const string ALL_REALMS_KEY = "all-realms";

    public RealmService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<RealmService> logger,
        RealmServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IResourceClient resourceClient,
        ISpeciesClient speciesClient,
        ILocationClient locationClient,
        ICharacterClient characterClient)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _resourceClient = resourceClient;
        _speciesClient = speciesClient;
        _locationClient = locationClient;
        _characterClient = characterClient;

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
    }

    /// <summary>
    /// Get realm by unique code (e.g., "REALM_1", "REALM_2").
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> GetRealmByCodeAsync(
        GetRealmByCodeRequest body,
        CancellationToken cancellationToken = default)
    {
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
    }

    /// <summary>
    /// List all realms with optional filtering.
    /// </summary>
    public async Task<(StatusCodes, RealmListResponse?)> ListRealmsAsync(
        ListRealmsRequest body,
        CancellationToken cancellationToken = default)
    {
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
    }

    /// <summary>
    /// Fast validation endpoint to check if realm exists and is active.
    /// </summary>
    public async Task<(StatusCodes, RealmExistsResponse?)> RealmExistsAsync(
        RealmExistsRequest body,
        CancellationToken cancellationToken = default)
    {
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
    }

    /// <summary>
    /// Batch validation endpoint to check if multiple realms exist and are active.
    /// Uses bulk state store operations for efficient validation.
    /// </summary>
    public async Task<(StatusCodes, RealmsExistBatchResponse?)> RealmsExistBatchAsync(
        RealmsExistBatchRequest body,
        CancellationToken cancellationToken = default)
    {
        {
            _logger.LogDebug("Checking existence of {Count} realms", body.RealmIds.Count);

            if (body.RealmIds.Count == 0)
            {
                return (StatusCodes.OK, new RealmsExistBatchResponse
                {
                    Results = new List<RealmExistsResponse>(),
                    AllExist = true,
                    AllActive = true,
                    InvalidRealmIds = new List<Guid>(),
                    DeprecatedRealmIds = new List<Guid>()
                });
            }

            // Use bulk loading for efficiency (single state store call)
            var realmModels = await LoadRealmsByIdsAsync(body.RealmIds.ToList(), cancellationToken);
            var modelLookup = realmModels.ToDictionary(m => m.RealmId);

            var results = new List<RealmExistsResponse>();
            var invalidRealmIds = new List<Guid>();
            var deprecatedRealmIds = new List<Guid>();

            // Build results in same order as request
            foreach (var realmId in body.RealmIds)
            {
                if (modelLookup.TryGetValue(realmId, out var model))
                {
                    var isActive = model.IsActive && !model.IsDeprecated;
                    results.Add(new RealmExistsResponse
                    {
                        Exists = true,
                        IsActive = isActive,
                        RealmId = model.RealmId
                    });

                    if (!isActive)
                    {
                        deprecatedRealmIds.Add(realmId);
                    }
                }
                else
                {
                    results.Add(new RealmExistsResponse
                    {
                        Exists = false,
                        IsActive = false,
                        RealmId = null
                    });
                    invalidRealmIds.Add(realmId);
                }
            }

            return (StatusCodes.OK, new RealmsExistBatchResponse
            {
                Results = results,
                AllExist = invalidRealmIds.Count == 0,
                AllActive = invalidRealmIds.Count == 0 && deprecatedRealmIds.Count == 0,
                InvalidRealmIds = invalidRealmIds,
                DeprecatedRealmIds = deprecatedRealmIds
            });
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

            // Update all-realms list with ETag-based optimistic concurrency
            await AddToRealmListAsync(realmId, cancellationToken);

            // Publish realm created event
            await PublishRealmCreatedEventAsync(model, cancellationToken);

            _logger.LogInformation("Created realm: {RealmId} with code {Code}", realmId, code);
            return (StatusCodes.OK, MapToResponse(model));
        }
    }

    /// <summary>
    /// Update an existing realm.
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> UpdateRealmAsync(
        UpdateRealmRequest body,
        CancellationToken cancellationToken = default)
    {
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
    }

    /// <summary>
    /// Hard delete a realm. Only deprecated realms with zero references can be deleted.
    /// </summary>
    public async Task<StatusCodes> DeleteRealmAsync(
        DeleteRealmRequest body,
        CancellationToken cancellationToken = default)
    {
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

            // Check for external references via lib-resource (L1 - allowed per SERVICE_HIERARCHY)
            // L3/L4 services like RealmHistory register their realm references with lib-resource
            try
            {
                var resourceCheck = await _resourceClient.CheckReferencesAsync(
                    new BeyondImmersion.BannouService.Resource.CheckReferencesRequest
                    {
                        ResourceType = "realm",
                        ResourceId = body.RealmId
                    }, cancellationToken);

                if (resourceCheck != null && resourceCheck.RefCount > 0)
                {
                    var sourceTypes = resourceCheck.Sources != null
                        ? string.Join(", ", resourceCheck.Sources.Select(s => s.SourceType))
                        : "unknown";
                    _logger.LogWarning(
                        "Cannot delete realm {RealmId} - has {RefCount} external references from: {SourceTypes}",
                        body.RealmId, resourceCheck.RefCount, sourceTypes);

                    // Execute cleanup callbacks (CASCADE/DETACH) before proceeding
                    var cleanupResult = await _resourceClient.ExecuteCleanupAsync(
                        new ExecuteCleanupRequest
                        {
                            ResourceType = "realm",
                            ResourceId = body.RealmId,
                            CleanupPolicy = CleanupPolicy.ALL_REQUIRED
                        }, cancellationToken);

                    if (!cleanupResult.Success)
                    {
                        _logger.LogWarning(
                            "Cleanup blocked for realm {RealmId}: {Reason}",
                            body.RealmId, cleanupResult.AbortReason ?? "cleanup failed");
                        return StatusCodes.Conflict;
                    }
                }
            }
            catch (ApiException ex) when (ex.StatusCode == 404)
            {
                // No references registered - this is normal
                _logger.LogDebug("No lib-resource references found for realm {RealmId}", body.RealmId);
            }
            catch (ApiException ex)
            {
                // lib-resource unavailable - fail closed to protect referential integrity
                _logger.LogError(ex,
                    "lib-resource unavailable when checking references for realm {RealmId}, blocking deletion for safety",
                    body.RealmId);
                await _messageBus.TryPublishErrorAsync(
                    "realm", "DeleteRealm", "resource_service_unavailable",
                    $"lib-resource unavailable when checking references for realm {body.RealmId}",
                    dependency: "resource", endpoint: "post:/realm/delete",
                    details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
                return StatusCodes.ServiceUnavailable;
            }

            // Delete the model
            await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm).DeleteAsync(realmKey, cancellationToken);

            // Delete code index
            var codeIndexKey = BuildCodeIndexKey(model.Code);
            await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Realm).DeleteAsync(codeIndexKey, cancellationToken);

            // Remove from all-realms list with ETag-based optimistic concurrency
            await RemoveFromRealmListAsync(body.RealmId, cancellationToken);

            // Publish realm deleted event
            await PublishRealmDeletedEventAsync(model, null, cancellationToken);

            _logger.LogInformation("Deleted realm: {RealmId} ({Code})", body.RealmId, model.Code);
            return StatusCodes.OK;
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
    }

    /// <summary>
    /// Restore a deprecated realm.
    /// </summary>
    public async Task<(StatusCodes, RealmResponse?)> UndeprecateRealmAsync(
        UndeprecateRealmRequest body,
        CancellationToken cancellationToken = default)
    {
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
    }

    #endregion

    #region Merge Operation

    /// <summary>
    /// Merge a deprecated source realm into a target realm by migrating all entities.
    /// Migration order: Species → Locations (root-first tree moves) → Characters.
    /// Follows continue-on-individual-failure policy per design decisions.
    /// </summary>
    public async Task<(StatusCodes, MergeRealmsResponse?)> MergeRealmsAsync(
        MergeRealmsRequest body,
        CancellationToken cancellationToken = default)
    {
        {
            _logger.LogInformation("Starting realm merge: source {SourceRealmId} into target {TargetRealmId}",
                body.SourceRealmId, body.TargetRealmId);

            // Validate source != target
            if (body.SourceRealmId == body.TargetRealmId)
            {
                _logger.LogWarning("Cannot merge realm into itself: {RealmId}", body.SourceRealmId);
                return (StatusCodes.BadRequest, null);
            }

            // Load source realm
            var sourceKey = BuildRealmKey(body.SourceRealmId);
            var sourceRealm = await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm)
                .GetAsync(sourceKey, cancellationToken);

            if (sourceRealm == null)
            {
                _logger.LogWarning("Source realm not found for merge: {SourceRealmId}", body.SourceRealmId);
                return (StatusCodes.NotFound, null);
            }

            // Source must be deprecated
            if (!sourceRealm.IsDeprecated)
            {
                _logger.LogWarning("Source realm {SourceRealmId} must be deprecated before merge", body.SourceRealmId);
                return (StatusCodes.BadRequest, null);
            }

            // Block merge FROM VOID realm (system infrastructure)
            if (sourceRealm.Metadata is System.Text.Json.JsonElement metadataElement
                && metadataElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && metadataElement.TryGetProperty("isSystemType", out var isSystemType)
                && isSystemType.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                _logger.LogWarning("Cannot merge from system realm {SourceRealmId} ({Code})",
                    body.SourceRealmId, sourceRealm.Code);
                return (StatusCodes.BadRequest, null);
            }

            // Load target realm
            var targetKey = BuildRealmKey(body.TargetRealmId);
            var targetRealm = await _stateStoreFactory.GetStore<RealmModel>(StateStoreDefinitions.Realm)
                .GetAsync(targetKey, cancellationToken);

            if (targetRealm == null)
            {
                _logger.LogWarning("Target realm not found for merge: {TargetRealmId}", body.TargetRealmId);
                return (StatusCodes.NotFound, null);
            }

            // Warn when merging into a system realm (e.g., VOID) — entities will be orphaned from gameplay
            if (targetRealm.Metadata is System.Text.Json.JsonElement targetMetadata
                && targetMetadata.ValueKind == System.Text.Json.JsonValueKind.Object
                && targetMetadata.TryGetProperty("isSystemType", out var isTargetSystem)
                && isTargetSystem.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                _logger.LogWarning(
                    "Merging into system realm {TargetRealmId} ({Code}) - all migrated entities will be orphaned from gameplay",
                    body.TargetRealmId, targetRealm.Code);
            }

            var pageSize = _configuration.MergePageSize;

            // Phase A: Migrate species (add to target realm, remove from source)
            var (speciesMigrated, speciesFailed) = await MigrateSpeciesAsync(
                body.SourceRealmId, body.TargetRealmId, pageSize, cancellationToken);

            // Phase B: Migrate locations (root-first tree moves)
            var (locationsMigrated, locationsFailed) = await MigrateLocationsAsync(
                body.SourceRealmId, body.TargetRealmId, pageSize, cancellationToken);

            // Phase C: Migrate characters
            var (charactersMigrated, charactersFailed) = await MigrateCharactersAsync(
                body.SourceRealmId, body.TargetRealmId, pageSize, cancellationToken);

            var totalFailed = speciesFailed + locationsFailed + charactersFailed;

            // Publish realm.merged event
            await PublishRealmMergedEventAsync(
                sourceRealm, targetRealm,
                speciesMigrated, speciesFailed,
                locationsMigrated, locationsFailed,
                charactersMigrated, charactersFailed,
                cancellationToken);

            // Optional: delete source realm if requested and zero failures
            var sourceDeleted = false;
            if (body.DeleteAfterMerge && totalFailed == 0)
            {
                var deleteStatus = await DeleteRealmAsync(
                    new DeleteRealmRequest { RealmId = body.SourceRealmId }, cancellationToken);

                sourceDeleted = deleteStatus == StatusCodes.OK;
                if (!sourceDeleted)
                {
                    _logger.LogWarning(
                        "Post-merge deletion of source realm {SourceRealmId} returned {Status} (merge itself succeeded)",
                        body.SourceRealmId, deleteStatus);
                }
            }
            else if (body.DeleteAfterMerge && totalFailed > 0)
            {
                _logger.LogWarning(
                    "Skipping post-merge deletion of source realm {SourceRealmId} due to {FailedCount} migration failures",
                    body.SourceRealmId, totalFailed);
            }

            _logger.LogInformation(
                "Realm merge complete: source {SourceRealmId} into target {TargetRealmId} - " +
                "species {SpeciesMigrated}/{SpeciesFailed}, locations {LocationsMigrated}/{LocationsFailed}, " +
                "characters {CharactersMigrated}/{CharactersFailed}, deleted: {Deleted}",
                body.SourceRealmId, body.TargetRealmId,
                speciesMigrated, speciesFailed,
                locationsMigrated, locationsFailed,
                charactersMigrated, charactersFailed,
                sourceDeleted);

            return (StatusCodes.OK, new MergeRealmsResponse
            {
                SourceRealmId = body.SourceRealmId,
                TargetRealmId = body.TargetRealmId,
                SpeciesMigrated = speciesMigrated,
                SpeciesFailed = speciesFailed,
                LocationsMigrated = locationsMigrated,
                LocationsFailed = locationsFailed,
                CharactersMigrated = charactersMigrated,
                CharactersFailed = charactersFailed,
                SourceDeleted = sourceDeleted
            });
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Service error during realm merge: source {SourceRealmId} target {TargetRealmId}",
                body.SourceRealmId, body.TargetRealmId);
            await _messageBus.TryPublishErrorAsync(
                "realm", "MergeRealms", "service_error", ex.Message,
                dependency: "mesh", endpoint: "post:/realm/merge",
                details: null, stack: ex.StackTrace, cancellationToken: cancellationToken);
            return ((StatusCodes)ex.StatusCode, null);
        }
    }

    /// <summary>
    /// Migrates species from source realm to target realm.
    /// For each species: add to target realm, then remove from source realm.
    /// Preserves species availability in target (idempotent add).
    /// Always re-queries page 1 since successfully migrated species leave the source list.
    /// </summary>
    private async Task<(int migrated, int failed)> MigrateSpeciesAsync(
        Guid sourceRealmId, Guid targetRealmId, int pageSize, CancellationToken cancellationToken)
    {
        var migrated = 0;
        var failed = 0;
        var failedSpeciesIds = new HashSet<Guid>();

        while (true)
        {
            var listResponse = await _speciesClient.ListSpeciesByRealmAsync(
                new ListSpeciesByRealmRequest
                {
                    RealmId = sourceRealmId,
                    Page = 1,
                    PageSize = pageSize
                }, cancellationToken);

            if (listResponse.Species.Count == 0)
            {
                break;
            }

            var pageProgress = false;

            foreach (var species in listResponse.Species)
            {
                // Skip species that already failed (avoid re-trying in a loop)
                if (failedSpeciesIds.Contains(species.SpeciesId))
                {
                    continue;
                }

                try
                {
                    // Add to target realm (idempotent - no-op if already present)
                    await _speciesClient.AddSpeciesToRealmAsync(
                        new AddSpeciesToRealmRequest
                        {
                            SpeciesId = species.SpeciesId,
                            RealmId = targetRealmId
                        }, cancellationToken);

                    // Remove from source realm
                    await _speciesClient.RemoveSpeciesFromRealmAsync(
                        new RemoveSpeciesFromRealmRequest
                        {
                            SpeciesId = species.SpeciesId,
                            RealmId = sourceRealmId
                        }, cancellationToken);

                    migrated++;
                    pageProgress = true;
                }
                catch (Exception ex)
                {
                    failed++;
                    failedSpeciesIds.Add(species.SpeciesId);
                    _logger.LogWarning(ex,
                        "Failed to migrate species {SpeciesId} ({Code}) from realm {Source} to {Target}",
                        species.SpeciesId, species.Code, sourceRealmId, targetRealmId);
                }
            }

            // If no progress was made this iteration, all remaining are failed - stop
            if (!pageProgress)
            {
                break;
            }
        }

        _logger.LogInformation("Species migration: {Migrated} migrated, {Failed} failed", migrated, failed);
        return (migrated, failed);
    }

    /// <summary>
    /// Migrates locations from source realm to target realm using root-first tree moves.
    /// For each root location: collects descendants while still in source realm, then transfers
    /// root first, then descendants in depth order (shallowest first) to preserve hierarchy.
    /// </summary>
    private async Task<(int migrated, int failed)> MigrateLocationsAsync(
        Guid sourceRealmId, Guid targetRealmId, int pageSize, CancellationToken cancellationToken)
    {
        var migrated = 0;
        var failed = 0;

        // Step 1: Get all root locations from source realm (paginated)
        var rootLocations = new List<LocationResponse>();
        var page = 1;
        bool hasMore;

        do
        {
            var listResponse = await _locationClient.ListRootLocationsAsync(
                new ListRootLocationsRequest
                {
                    RealmId = sourceRealmId,
                    IncludeDeprecated = true,
                    Page = page,
                    PageSize = pageSize
                }, cancellationToken);

            rootLocations.AddRange(listResponse.Locations);
            hasMore = listResponse.HasNextPage;
            page++;
        }
        while (hasMore);

        // Step 2: For each root, collect descendants BEFORE transferring (parent index
        // depends on source realm), then transfer root + descendants in depth order
        foreach (var rootLocation in rootLocations)
        {
            try
            {
                // Collect all descendants while tree is intact in source realm
                var descendants = new List<LocationResponse>();
                var descPage = 1;
                bool descHasMore;

                do
                {
                    var descResponse = await _locationClient.GetLocationDescendantsAsync(
                        new GetLocationDescendantsRequest
                        {
                            LocationId = rootLocation.LocationId,
                            IncludeDeprecated = true,
                            Page = descPage,
                            PageSize = pageSize
                        }, cancellationToken);

                    descendants.AddRange(descResponse.Locations);
                    descHasMore = descResponse.HasNextPage;
                    descPage++;
                }
                while (descHasMore);

                // Sort descendants by depth (shallowest first) so parents are
                // always in target realm before their children are re-parented
                descendants.Sort((a, b) => a.Depth.CompareTo(b.Depth));

                // Transfer root location to target realm (becomes root there, depth 0)
                await _locationClient.TransferLocationToRealmAsync(
                    new TransferLocationToRealmRequest
                    {
                        LocationId = rootLocation.LocationId,
                        TargetRealmId = targetRealmId
                    }, cancellationToken);

                migrated++;

                // Transfer each descendant in depth order, then re-parent
                foreach (var descendant in descendants)
                {
                    try
                    {
                        // Transfer to target realm (becomes root, parent cleared)
                        await _locationClient.TransferLocationToRealmAsync(
                            new TransferLocationToRealmRequest
                            {
                                LocationId = descendant.LocationId,
                                TargetRealmId = targetRealmId
                            }, cancellationToken);

                        // Re-parent under its original parent (now in target realm)
                        if (descendant.ParentLocationId.HasValue)
                        {
                            await _locationClient.SetLocationParentAsync(
                                new SetLocationParentRequest
                                {
                                    LocationId = descendant.LocationId,
                                    ParentLocationId = descendant.ParentLocationId.Value
                                }, cancellationToken);
                        }

                        migrated++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogWarning(ex,
                            "Failed to migrate descendant location {LocationId} ({Code}) from realm {Source} to {Target}",
                            descendant.LocationId, descendant.Code, sourceRealmId, targetRealmId);

                        // If this descendant's parent was already transferred to target realm,
                        // detach the parent reference to avoid cross-realm parent pointers
                        if (descendant.ParentLocationId.HasValue)
                        {
                            try
                            {
                                await _locationClient.RemoveLocationParentAsync(
                                    new RemoveLocationParentRequest
                                    {
                                        LocationId = descendant.LocationId
                                    }, cancellationToken);
                            }
                            catch (Exception detachEx)
                            {
                                _logger.LogWarning(detachEx,
                                    "Failed to detach parent for orphaned location {LocationId} after migration failure",
                                    descendant.LocationId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex,
                    "Failed to migrate root location tree {LocationId} ({Code}) from realm {Source} to {Target}",
                    rootLocation.LocationId, rootLocation.Code, sourceRealmId, targetRealmId);
            }
        }

        _logger.LogInformation("Location migration: {Migrated} migrated, {Failed} failed", migrated, failed);
        return (migrated, failed);
    }

    /// <summary>
    /// Migrates characters from source realm to target realm.
    /// Always re-queries page 1 since successfully transferred characters leave the source list.
    /// Tracks failed character IDs to avoid infinite retry loops.
    /// </summary>
    private async Task<(int migrated, int failed)> MigrateCharactersAsync(
        Guid sourceRealmId, Guid targetRealmId, int pageSize, CancellationToken cancellationToken)
    {
        var migrated = 0;
        var failed = 0;
        var failedCharacterIds = new HashSet<Guid>();

        while (true)
        {
            var listResponse = await _characterClient.GetCharactersByRealmAsync(
                new GetCharactersByRealmRequest
                {
                    RealmId = sourceRealmId,
                    Page = 1,
                    PageSize = pageSize
                }, cancellationToken);

            if (listResponse.Characters.Count == 0)
            {
                break;
            }

            var pageProgress = false;

            foreach (var character in listResponse.Characters)
            {
                // Skip characters that already failed (avoid re-trying in a loop)
                if (failedCharacterIds.Contains(character.CharacterId))
                {
                    continue;
                }

                try
                {
                    await _characterClient.TransferCharacterToRealmAsync(
                        new TransferCharacterToRealmRequest
                        {
                            CharacterId = character.CharacterId,
                            TargetRealmId = targetRealmId
                        }, cancellationToken);

                    migrated++;
                    pageProgress = true;
                }
                catch (Exception ex)
                {
                    failed++;
                    failedCharacterIds.Add(character.CharacterId);
                    _logger.LogWarning(ex,
                        "Failed to migrate character {CharacterId} from realm {Source} to {Target}",
                        character.CharacterId, sourceRealmId, targetRealmId);
                }
            }

            // If no progress was made this iteration, all remaining are failed - stop
            if (!pageProgress)
            {
                break;
            }
        }

        _logger.LogInformation("Character migration: {Migrated} migrated, {Failed} failed", migrated, failed);
        return (migrated, failed);
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
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Add a realm ID to the all-realms list index.
    /// Uses ETag-based optimistic concurrency per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    private async Task AddToRealmListAsync(Guid realmId, CancellationToken cancellationToken)
    {
        var listStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Realm);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (realmIds, etag) = await listStore.GetWithETagAsync(ALL_REALMS_KEY, cancellationToken);
            realmIds ??= new List<Guid>();

            if (realmIds.Contains(realmId))
            {
                return; // Already in list
            }

            realmIds.Add(realmId);
            var result = await listStore.TrySaveAsync(ALL_REALMS_KEY, realmIds, etag ?? string.Empty, cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on realm list, retrying add (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to add realm {RealmId} to list after 3 attempts", realmId);
    }

    /// <summary>
    /// Remove a realm ID from the all-realms list index.
    /// Uses ETag-based optimistic concurrency per IMPLEMENTATION TENETS (Multi-Instance Safety).
    /// </summary>
    private async Task RemoveFromRealmListAsync(Guid realmId, CancellationToken cancellationToken)
    {
        var listStore = _stateStoreFactory.GetStore<List<Guid>>(StateStoreDefinitions.Realm);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var (realmIds, etag) = await listStore.GetWithETagAsync(ALL_REALMS_KEY, cancellationToken);
            if (realmIds == null || !realmIds.Remove(realmId))
            {
                return; // Not in list or already removed
            }

            var result = await listStore.TrySaveAsync(ALL_REALMS_KEY, realmIds, etag ?? string.Empty, cancellationToken);
            if (result != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on realm list, retrying remove (attempt {Attempt})", attempt + 1);
        }

        _logger.LogWarning("Failed to remove realm {RealmId} from list after 3 attempts", realmId);
    }

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
                DeprecatedAt = model.DeprecatedAt,
                DeprecationReason = model.DeprecationReason,
                Metadata = model.Metadata,
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
    /// Publishes a realm merged event with migration statistics.
    /// </summary>
    private async Task PublishRealmMergedEventAsync(
        RealmModel sourceRealm,
        RealmModel targetRealm,
        int speciesMigrated,
        int speciesFailed,
        int locationsMigrated,
        int locationsFailed,
        int charactersMigrated,
        int charactersFailed,
        CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new RealmMergedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SourceRealmId = sourceRealm.RealmId,
                SourceRealmCode = sourceRealm.Code,
                TargetRealmId = targetRealm.RealmId,
                TargetRealmCode = targetRealm.Code,
                SpeciesMigrated = speciesMigrated,
                SpeciesFailed = speciesFailed,
                LocationsMigrated = locationsMigrated,
                LocationsFailed = locationsFailed,
                CharactersMigrated = charactersMigrated,
                CharactersFailed = charactersFailed
            };

            await _messageBus.TryPublishAsync("realm.merged", eventModel, cancellationToken: cancellationToken);
            _logger.LogDebug("Published realm.merged event for source {SourceRealmId} into target {TargetRealmId}",
                sourceRealm.RealmId, targetRealm.RealmId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish realm.merged event for source {SourceRealmId}", sourceRealm.RealmId);
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
                DeprecatedAt = model.DeprecatedAt,
                DeprecationReason = model.DeprecationReason,
                Metadata = model.Metadata,
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
