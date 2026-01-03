using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.RelationshipType;

/// <summary>
/// Implementation of the RelationshipType service.
/// Manages hierarchical relationship types for character relationships.
/// </summary>
[BannouService("relationship-type", typeof(IRelationshipTypeService), lifetime: ServiceLifetime.Scoped)]
public partial class RelationshipTypeService : IRelationshipTypeService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<RelationshipTypeService> _logger;
    private readonly RelationshipTypeServiceConfiguration _configuration;
    private readonly IRelationshipClient _relationshipClient;

    private const string STATE_STORE = "relationship-type-statestore";
    private const string TYPE_KEY_PREFIX = "type:";
    private const string CODE_INDEX_PREFIX = "code-index:";
    private const string PARENT_INDEX_PREFIX = "parent-index:";
    private const string ALL_TYPES_KEY = "all-types";

    public RelationshipTypeService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<RelationshipTypeService> logger,
        RelationshipTypeServiceConfiguration configuration,
        IRelationshipClient relationshipClient,
        IEventConsumer eventConsumer)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _relationshipClient = relationshipClient;

        // Register event handlers via partial class (RelationshipTypeServiceEvents.cs)
        ((IBannouService)this).RegisterEventConsumers(eventConsumer);
    }

    #region Read Operations

    public async Task<(StatusCodes, RelationshipTypeResponse?)> GetRelationshipTypeAsync(
        GetRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting relationship type by ID: {TypeId}", body.RelationshipTypeId);

            var typeKey = BuildTypeKey(body.RelationshipTypeId.ToString());
            var model = await _stateStoreFactory.GetStore<RelationshipTypeModel>(STATE_STORE)
                .GetAsync(typeKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Relationship type not found: {TypeId}", body.RelationshipTypeId);
                return (StatusCodes.NotFound, null);
            }

            var response = await MapToResponseAsync(model, cancellationToken);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting relationship type: {TypeId}", body.RelationshipTypeId);
            await EmitErrorAsync("GetRelationshipType", "post:/relationship-type/get", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, RelationshipTypeResponse?)> GetRelationshipTypeByCodeAsync(
        GetRelationshipTypeByCodeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting relationship type by code: {Code}", body.Code);

            var codeIndexKey = BuildCodeIndexKey(body.Code.ToUpperInvariant());
            var typeId = await _stateStoreFactory.GetStore<string>(STATE_STORE)
                .GetAsync(codeIndexKey, cancellationToken);

            if (string.IsNullOrEmpty(typeId))
            {
                _logger.LogWarning("Relationship type not found for code: {Code}", body.Code);
                return (StatusCodes.NotFound, null);
            }

            var typeKey = BuildTypeKey(typeId);
            var model = await _stateStoreFactory.GetStore<RelationshipTypeModel>(STATE_STORE)
                .GetAsync(typeKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Relationship type not found: {TypeId}", typeId);
                return (StatusCodes.NotFound, null);
            }

            var response = await MapToResponseAsync(model, cancellationToken);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting relationship type by code: {Code}", body.Code);
            await EmitErrorAsync("GetRelationshipTypeByCode", "post:/relationship-type/get-by-code", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, RelationshipTypeListResponse?)> ListRelationshipTypesAsync(
        ListRelationshipTypesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Listing relationship types");

            var allTypeIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE)
                .GetAsync(ALL_TYPES_KEY, cancellationToken) ?? new List<string>();

            if (allTypeIds.Count == 0)
            {
                return (StatusCodes.OK, new RelationshipTypeListResponse
                {
                    Types = new List<RelationshipTypeResponse>(),
                    TotalCount = 0
                });
            }

            // Bulk load all types
            var keys = allTypeIds.Select(BuildTypeKey).ToList();
            var bulkResults = await _stateStoreFactory.GetStore<RelationshipTypeModel>(STATE_STORE)
                .GetBulkAsync(keys, cancellationToken);

            var types = new List<RelationshipTypeModel>();
            foreach (var (_, model) in bulkResults)
            {
                if (model != null) types.Add(model);
            }

            // Apply filters
            var filtered = types.AsEnumerable();

            // Filter out deprecated types by default
            if (body.IncludeDeprecated != true)
            {
                filtered = filtered.Where(t => !t.IsDeprecated);
            }

            if (!string.IsNullOrEmpty(body.Category))
            {
                filtered = filtered.Where(t =>
                    string.Equals(t.Category, body.Category, StringComparison.OrdinalIgnoreCase));
            }

            if (body.RootsOnly == true)
            {
                filtered = filtered.Where(t => string.IsNullOrEmpty(t.ParentTypeId));
            }

            var typesList = filtered.ToList();
            var responses = new List<RelationshipTypeResponse>();
            foreach (var model in typesList)
            {
                responses.Add(await MapToResponseAsync(model, cancellationToken));
            }

            return (StatusCodes.OK, new RelationshipTypeListResponse
            {
                Types = responses,
                TotalCount = responses.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing relationship types");
            await EmitErrorAsync("ListRelationshipTypes", "post:/relationship-type/list", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, RelationshipTypeListResponse?)> GetChildRelationshipTypesAsync(
        GetChildRelationshipTypesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting child types for parent: {ParentId}", body.ParentTypeId);

            // Verify parent exists
            var parentKey = BuildTypeKey(body.ParentTypeId.ToString());
            var parent = await _stateStoreFactory.GetStore<RelationshipTypeModel>(STATE_STORE)
                .GetAsync(parentKey, cancellationToken);

            if (parent == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var childIds = await GetChildTypeIdsAsync(body.ParentTypeId.ToString(), body.Recursive == true, cancellationToken);

            if (childIds.Count == 0)
            {
                return (StatusCodes.OK, new RelationshipTypeListResponse
                {
                    Types = new List<RelationshipTypeResponse>(),
                    TotalCount = 0
                });
            }

            // Bulk load children
            var keys = childIds.Select(BuildTypeKey).ToList();
            var bulkResults = await _stateStoreFactory.GetStore<RelationshipTypeModel>(STATE_STORE)
                .GetBulkAsync(keys, cancellationToken);

            var responses = new List<RelationshipTypeResponse>();
            foreach (var (_, model) in bulkResults)
            {
                if (model != null)
                {
                    responses.Add(await MapToResponseAsync(model, cancellationToken));
                }
            }

            return (StatusCodes.OK, new RelationshipTypeListResponse
            {
                Types = responses,
                TotalCount = responses.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting child types: {ParentId}", body.ParentTypeId);
            await EmitErrorAsync("GetChildRelationshipTypes", "post:/relationship-type/get-children", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, MatchesHierarchyResponse?)> MatchesHierarchyAsync(
        MatchesHierarchyRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Checking hierarchy match: {TypeId} -> {AncestorId}",
                body.TypeId, body.AncestorTypeId);

            // If they're the same, it's a match with depth 0
            if (body.TypeId == body.AncestorTypeId)
            {
                return (StatusCodes.OK, new MatchesHierarchyResponse
                {
                    Matches = true,
                    Depth = 0
                });
            }

            // Get the type and traverse up the hierarchy
            var typeKey = BuildTypeKey(body.TypeId.ToString());
            var store = _stateStoreFactory.GetStore<RelationshipTypeModel>(STATE_STORE);
            var currentType = await store.GetAsync(typeKey, cancellationToken);

            if (currentType == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Verify ancestor exists
            var ancestorKey = BuildTypeKey(body.AncestorTypeId.ToString());
            var ancestor = await store.GetAsync(ancestorKey, cancellationToken);

            if (ancestor == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var ancestorIdStr = body.AncestorTypeId.ToString();
            var depth = 0;
            var currentParentId = currentType.ParentTypeId;

            while (!string.IsNullOrEmpty(currentParentId))
            {
                depth++;
                if (currentParentId == ancestorIdStr)
                {
                    return (StatusCodes.OK, new MatchesHierarchyResponse
                    {
                        Matches = true,
                        Depth = depth
                    });
                }

                var parentKey = BuildTypeKey(currentParentId);
                var parentType = await store.GetAsync(parentKey, cancellationToken);

                currentParentId = parentType?.ParentTypeId;
            }

            return (StatusCodes.OK, new MatchesHierarchyResponse
            {
                Matches = false,
                Depth = -1
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking hierarchy match");
            await EmitErrorAsync("MatchesHierarchy", "post:/relationship-type/matches-hierarchy", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, RelationshipTypeListResponse?)> GetAncestorsAsync(
        GetAncestorsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting ancestors for type: {TypeId}", body.TypeId);

            var typeKey = BuildTypeKey(body.TypeId.ToString());
            var store = _stateStoreFactory.GetStore<RelationshipTypeModel>(STATE_STORE);
            var currentType = await store.GetAsync(typeKey, cancellationToken);

            if (currentType == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var ancestors = new List<RelationshipTypeResponse>();
            var currentParentId = currentType.ParentTypeId;

            while (!string.IsNullOrEmpty(currentParentId))
            {
                var parentKey = BuildTypeKey(currentParentId);
                var parentType = await store.GetAsync(parentKey, cancellationToken);

                if (parentType == null) break;

                ancestors.Add(await MapToResponseAsync(parentType, cancellationToken));
                currentParentId = parentType.ParentTypeId;
            }

            return (StatusCodes.OK, new RelationshipTypeListResponse
            {
                Types = ancestors,
                TotalCount = ancestors.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting ancestors: {TypeId}", body.TypeId);
            await EmitErrorAsync("GetAncestors", "post:/relationship-type/get-ancestors", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Write Operations

    public async Task<(StatusCodes, RelationshipTypeResponse?)> CreateRelationshipTypeAsync(
        CreateRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating relationship type: {Code}", body.Code);

            var code = body.Code.ToUpperInvariant();

            // Check if code already exists
            var codeIndexKey = BuildCodeIndexKey(code);
            var stringStore = _stateStoreFactory.GetStore<string>(STATE_STORE);
            var existingId = await stringStore.GetAsync(codeIndexKey, cancellationToken);

            if (!string.IsNullOrEmpty(existingId))
            {
                _logger.LogWarning("Relationship type with code already exists: {Code}", code);
                return (StatusCodes.Conflict, null);
            }

            // Validate parent if specified
            string? parentTypeCode = null;
            var depth = 0;
            var modelStore = _stateStoreFactory.GetStore<RelationshipTypeModel>(STATE_STORE);
            if (body.ParentTypeId.HasValue)
            {
                var parentKey = BuildTypeKey(body.ParentTypeId.Value.ToString());
                var parent = await modelStore.GetAsync(parentKey, cancellationToken);

                if (parent == null)
                {
                    _logger.LogWarning("Parent type not found: {ParentId}", body.ParentTypeId);
                    return (StatusCodes.BadRequest, null);
                }
                parentTypeCode = parent.Code;
                depth = parent.Depth + 1;
            }

            // Resolve inverse type if specified
            string? inverseTypeId = null;
            if (!string.IsNullOrEmpty(body.InverseTypeCode))
            {
                var inverseIndexKey = BuildCodeIndexKey(body.InverseTypeCode.ToUpperInvariant());
                inverseTypeId = await stringStore.GetAsync(inverseIndexKey, cancellationToken);
            }

            var typeId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var model = new RelationshipTypeModel
            {
                RelationshipTypeId = typeId.ToString(),
                Code = code,
                Name = body.Name,
                Description = body.Description,
                Category = body.Category,
                ParentTypeId = body.ParentTypeId?.ToString(),
                ParentTypeCode = parentTypeCode,
                InverseTypeId = inverseTypeId,
                InverseTypeCode = body.InverseTypeCode,
                IsBidirectional = body.IsBidirectional,
                Depth = depth,
                Metadata = body.Metadata,
                CreatedAt = now,
                UpdatedAt = now
            };

            // Save the model
            var typeKey = BuildTypeKey(typeId.ToString());
            await modelStore.SaveAsync(typeKey, model, cancellationToken: cancellationToken);

            // Update code index
            await stringStore.SaveAsync(codeIndexKey, typeId.ToString(), cancellationToken: cancellationToken);

            // Update parent's children index
            if (body.ParentTypeId.HasValue)
            {
                await AddToParentIndexAsync(body.ParentTypeId.Value.ToString(), typeId.ToString(), cancellationToken);
            }

            // Update all types list
            await AddToAllTypesListAsync(typeId.ToString(), cancellationToken);

            await PublishRelationshipTypeCreatedEventAsync(model, cancellationToken);

            var response = await MapToResponseAsync(model, cancellationToken);
            return (StatusCodes.Created, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating relationship type: {Code}", body.Code);
            await EmitErrorAsync("CreateRelationshipType", "post:/relationship-type/create", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, RelationshipTypeResponse?)> UpdateRelationshipTypeAsync(
        UpdateRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Updating relationship type: {TypeId}", body.RelationshipTypeId);

            var typeKey = BuildTypeKey(body.RelationshipTypeId.ToString());
            var modelStore = _stateStoreFactory.GetStore<RelationshipTypeModel>(STATE_STORE);
            var existing = await modelStore.GetAsync(typeKey, cancellationToken);

            if (existing == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var changedFields = new List<string>();

            if (!string.IsNullOrEmpty(body.Name) && body.Name != existing.Name)
            {
                existing.Name = body.Name;
                changedFields.Add("name");
            }

            if (body.Description != null && body.Description != existing.Description)
            {
                existing.Description = body.Description;
                changedFields.Add("description");
            }

            if (body.Category != null && body.Category != existing.Category)
            {
                existing.Category = body.Category;
                changedFields.Add("category");
            }

            if (body.IsBidirectional.HasValue && body.IsBidirectional.Value != existing.IsBidirectional)
            {
                existing.IsBidirectional = body.IsBidirectional.Value;
                changedFields.Add("isBidirectional");
            }

            if (body.Metadata != null)
            {
                existing.Metadata = body.Metadata;
                changedFields.Add("metadata");
            }

            // Handle parent type change
            if (body.ParentTypeId.HasValue)
            {
                var newParentId = body.ParentTypeId.Value.ToString();
                if (newParentId != existing.ParentTypeId)
                {
                    // Validate new parent exists
                    var parentKey = BuildTypeKey(newParentId);
                    var parent = await modelStore.GetAsync(parentKey, cancellationToken);

                    if (parent == null)
                    {
                        return (StatusCodes.BadRequest, null);
                    }

                    // Remove from old parent's index
                    if (!string.IsNullOrEmpty(existing.ParentTypeId))
                    {
                        await RemoveFromParentIndexAsync(existing.ParentTypeId, existing.RelationshipTypeId, cancellationToken);
                    }

                    // Add to new parent's index
                    await AddToParentIndexAsync(newParentId, existing.RelationshipTypeId, cancellationToken);

                    existing.ParentTypeId = newParentId;
                    existing.ParentTypeCode = parent.Code;
                    existing.Depth = parent.Depth + 1;
                    changedFields.Add("parentTypeId");
                }
            }

            // Handle inverse type code change
            if (body.InverseTypeCode != null && body.InverseTypeCode != existing.InverseTypeCode)
            {
                if (string.IsNullOrEmpty(body.InverseTypeCode))
                {
                    existing.InverseTypeId = null;
                    existing.InverseTypeCode = null;
                }
                else
                {
                    var inverseIndexKey = BuildCodeIndexKey(body.InverseTypeCode.ToUpperInvariant());
                    var inverseId = await _stateStoreFactory.GetStore<string>(STATE_STORE)
                        .GetAsync(inverseIndexKey, cancellationToken);
                    existing.InverseTypeId = inverseId;
                    existing.InverseTypeCode = body.InverseTypeCode;
                }
                changedFields.Add("inverseTypeCode");
            }

            if (changedFields.Count > 0)
            {
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await modelStore.SaveAsync(typeKey, existing, cancellationToken: cancellationToken);
                await PublishRelationshipTypeUpdatedEventAsync(existing, changedFields, cancellationToken);
            }

            var response = await MapToResponseAsync(existing, cancellationToken);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating relationship type: {TypeId}", body.RelationshipTypeId);
            await EmitErrorAsync("UpdateRelationshipType", "post:/relationship-type/update", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<StatusCodes> DeleteRelationshipTypeAsync(
        DeleteRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting relationship type: {TypeId}", body.RelationshipTypeId);

            var typeKey = BuildTypeKey(body.RelationshipTypeId.ToString());
            var modelStore = _stateStoreFactory.GetStore<RelationshipTypeModel>(STATE_STORE);
            var existing = await modelStore.GetAsync(typeKey, cancellationToken);

            if (existing == null)
            {
                return StatusCodes.NotFound;
            }

            // Check if type has children
            var childIds = await GetChildTypeIdsAsync(body.RelationshipTypeId.ToString(), false, cancellationToken);
            if (childIds.Count > 0)
            {
                _logger.LogWarning("Cannot delete type with children: {TypeId}", body.RelationshipTypeId);
                return StatusCodes.Conflict;
            }

            // Delete the type
            await modelStore.DeleteAsync(typeKey, cancellationToken);

            // Remove from code index
            var codeIndexKey = BuildCodeIndexKey(existing.Code);
            await _stateStoreFactory.GetStore<string>(STATE_STORE)
                .DeleteAsync(codeIndexKey, cancellationToken);

            // Remove from parent's children index
            if (!string.IsNullOrEmpty(existing.ParentTypeId))
            {
                await RemoveFromParentIndexAsync(existing.ParentTypeId, existing.RelationshipTypeId, cancellationToken);
            }

            // Remove from all types list
            await RemoveFromAllTypesListAsync(existing.RelationshipTypeId, cancellationToken);

            await PublishRelationshipTypeDeletedEventAsync(existing, cancellationToken);

            return StatusCodes.NoContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting relationship type: {TypeId}", body.RelationshipTypeId);
            await EmitErrorAsync("DeleteRelationshipType", "post:/relationship-type/delete", ex);
            return StatusCodes.InternalServerError;
        }
    }

    public async Task<(StatusCodes, SeedRelationshipTypesResponse?)> SeedRelationshipTypesAsync(
        SeedRelationshipTypesRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Seeding {Count} relationship types", body.Types.Count);

            var created = 0;
            var updated = 0;
            var skipped = 0;
            var errors = new List<string>();

            // First pass: create types without parent references
            var codeToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var stringStore = _stateStoreFactory.GetStore<string>(STATE_STORE);

            // Load existing code-to-id mappings
            foreach (var seedType in body.Types)
            {
                var code = seedType.Code.ToUpperInvariant();
                var codeIndexKey = BuildCodeIndexKey(code);
                var existingId = await stringStore.GetAsync(codeIndexKey, cancellationToken);

                if (!string.IsNullOrEmpty(existingId))
                {
                    codeToId[code] = existingId;
                }
            }

            // Process types in order (parent types first for depth calculation)
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = body.Types.ToList();
            var maxIterations = pending.Count * 2; // Prevent infinite loops
            var iteration = 0;

            while (pending.Count > 0 && iteration < maxIterations)
            {
                iteration++;
                var toProcess = pending.Where(t =>
                    string.IsNullOrEmpty(t.ParentTypeCode) ||
                    processed.Contains(t.ParentTypeCode.ToUpperInvariant())).ToList();

                if (toProcess.Count == 0)
                {
                    // Remaining items have unresolvable parents
                    foreach (var unresolved in pending)
                    {
                        errors.Add($"Unresolved parent type '{unresolved.ParentTypeCode}' for '{unresolved.Code}'");
                    }
                    break;
                }

                foreach (var seedType in toProcess)
                {
                    pending.Remove(seedType);
                    var code = seedType.Code.ToUpperInvariant();

                    try
                    {
                        if (codeToId.TryGetValue(code, out var existingId))
                        {
                            // Type exists
                            if (body.UpdateExisting == true)
                            {
                                // Update existing type
                                var updateRequest = new UpdateRelationshipTypeRequest
                                {
                                    RelationshipTypeId = Guid.Parse(existingId),
                                    Name = seedType.Name,
                                    Description = seedType.Description,
                                    Category = seedType.Category,
                                    InverseTypeCode = seedType.InverseTypeCode,
                                    IsBidirectional = seedType.IsBidirectional
                                };

                                if (!string.IsNullOrEmpty(seedType.ParentTypeCode) &&
                                    codeToId.TryGetValue(seedType.ParentTypeCode.ToUpperInvariant(), out var parentId))
                                {
                                    updateRequest.ParentTypeId = Guid.Parse(parentId);
                                }

                                var (status, _) = await UpdateRelationshipTypeAsync(updateRequest, cancellationToken);
                                if (status == StatusCodes.OK)
                                {
                                    updated++;
                                }
                                else
                                {
                                    errors.Add($"Failed to update '{code}': {status}");
                                }
                            }
                            else
                            {
                                skipped++;
                            }
                        }
                        else
                        {
                            // Create new type
                            var createRequest = new CreateRelationshipTypeRequest
                            {
                                Code = code,
                                Name = seedType.Name,
                                Description = seedType.Description,
                                Category = seedType.Category,
                                InverseTypeCode = seedType.InverseTypeCode,
                                IsBidirectional = seedType.IsBidirectional,
                                Metadata = seedType.Metadata
                            };

                            if (!string.IsNullOrEmpty(seedType.ParentTypeCode) &&
                                codeToId.TryGetValue(seedType.ParentTypeCode.ToUpperInvariant(), out var parentId))
                            {
                                createRequest.ParentTypeId = Guid.Parse(parentId);
                            }

                            var (status, response) = await CreateRelationshipTypeAsync(createRequest, cancellationToken);
                            if (status == StatusCodes.Created && response != null)
                            {
                                codeToId[code] = response.RelationshipTypeId.ToString();
                                created++;
                            }
                            else
                            {
                                errors.Add($"Failed to create '{code}': {status}");
                            }
                        }

                        processed.Add(code);
                    }
                    catch (Exception typeEx)
                    {
                        errors.Add($"Error processing '{code}': {typeEx.Message}");
                    }
                }
            }

            return (StatusCodes.OK, new SeedRelationshipTypesResponse
            {
                Created = created,
                Updated = updated,
                Skipped = skipped,
                Errors = errors
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error seeding relationship types");
            await EmitErrorAsync("SeedRelationshipTypes", "post:/relationship-type/seed", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Deprecation Operations

    public async Task<(StatusCodes, RelationshipTypeResponse?)> DeprecateRelationshipTypeAsync(
        DeprecateRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deprecating relationship type: {TypeId}", body.RelationshipTypeId);

            var typeKey = BuildTypeKey(body.RelationshipTypeId.ToString());
            var store = _stateStoreFactory.GetStore<RelationshipTypeModel>(STATE_STORE);
            var model = await store.GetAsync(typeKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Relationship type not found for deprecation: {TypeId}", body.RelationshipTypeId);
                return (StatusCodes.NotFound, null);
            }

            if (model.IsDeprecated)
            {
                _logger.LogWarning("Relationship type already deprecated: {TypeId}", body.RelationshipTypeId);
                return (StatusCodes.Conflict, null);
            }

            model.IsDeprecated = true;
            model.DeprecatedAt = DateTimeOffset.UtcNow;
            model.DeprecationReason = body.Reason;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await store.SaveAsync(typeKey, model, cancellationToken: cancellationToken);

            // Publish updated event with deprecation fields
            await PublishRelationshipTypeUpdatedEventAsync(model, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);

            _logger.LogInformation("Deprecated relationship type: {TypeId}", body.RelationshipTypeId);
            return (StatusCodes.OK, await MapToResponseAsync(model, cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deprecating relationship type: {TypeId}", body.RelationshipTypeId);
            await EmitErrorAsync("DeprecateRelationshipType", "post:/relationship-type/deprecate", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, RelationshipTypeResponse?)> UndeprecateRelationshipTypeAsync(
        UndeprecateRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Undeprecating relationship type: {TypeId}", body.RelationshipTypeId);

            var typeKey = BuildTypeKey(body.RelationshipTypeId.ToString());
            var store = _stateStoreFactory.GetStore<RelationshipTypeModel>(STATE_STORE);
            var model = await store.GetAsync(typeKey, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Relationship type not found for undeprecation: {TypeId}", body.RelationshipTypeId);
                return (StatusCodes.NotFound, null);
            }

            if (!model.IsDeprecated)
            {
                _logger.LogWarning("Relationship type not deprecated: {TypeId}", body.RelationshipTypeId);
                return (StatusCodes.Conflict, null);
            }

            model.IsDeprecated = false;
            model.DeprecatedAt = null;
            model.DeprecationReason = null;
            model.UpdatedAt = DateTimeOffset.UtcNow;

            await store.SaveAsync(typeKey, model, cancellationToken: cancellationToken);

            // Publish updated event with deprecation fields cleared
            await PublishRelationshipTypeUpdatedEventAsync(model, new[] { "isDeprecated", "deprecatedAt", "deprecationReason" }, cancellationToken);

            _logger.LogInformation("Undeprecated relationship type: {TypeId}", body.RelationshipTypeId);
            return (StatusCodes.OK, await MapToResponseAsync(model, cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error undeprecating relationship type: {TypeId}", body.RelationshipTypeId);
            await EmitErrorAsync("UndeprecateRelationshipType", "post:/relationship-type/undeprecate", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    public async Task<(StatusCodes, MergeRelationshipTypeResponse?)> MergeRelationshipTypeAsync(
        MergeRelationshipTypeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Merging relationship type {SourceId} into {TargetId}",
                body.SourceTypeId, body.TargetTypeId);

            // Verify source exists and is deprecated
            var sourceKey = BuildTypeKey(body.SourceTypeId.ToString());
            var store = _stateStoreFactory.GetStore<RelationshipTypeModel>(STATE_STORE);
            var sourceModel = await store.GetAsync(sourceKey, cancellationToken);

            if (sourceModel == null)
            {
                _logger.LogWarning("Source relationship type not found: {TypeId}", body.SourceTypeId);
                return (StatusCodes.NotFound, null);
            }

            if (!sourceModel.IsDeprecated)
            {
                _logger.LogWarning("Source relationship type must be deprecated before merging: {TypeId}", body.SourceTypeId);
                return (StatusCodes.BadRequest, null);
            }

            // Verify target exists
            var targetKey = BuildTypeKey(body.TargetTypeId.ToString());
            var targetModel = await store.GetAsync(targetKey, cancellationToken);

            if (targetModel == null)
            {
                _logger.LogWarning("Target relationship type not found: {TypeId}", body.TargetTypeId);
                return (StatusCodes.NotFound, null);
            }

            // Migrate all relationships from source type to target type
            var migratedCount = 0;
            var failedCount = 0;
            var migrationErrors = new List<MigrationError>();
            const int maxErrorsToTrack = 100;
            var page = 1;
            const int pageSize = 100;
            var hasMorePages = true;

            while (hasMorePages)
            {
                try
                {
                    var relationshipsResponse = await _relationshipClient.ListRelationshipsByTypeAsync(
                        new ListRelationshipsByTypeRequest
                        {
                            RelationshipTypeId = body.SourceTypeId,
                            Page = page,
                            PageSize = pageSize
                        },
                        cancellationToken);

                    if (relationshipsResponse.Relationships == null || relationshipsResponse.Relationships.Count == 0)
                    {
                        hasMorePages = false;
                        continue;
                    }

                    // Migrate each relationship to the target type
                    foreach (var relationship in relationshipsResponse.Relationships)
                    {
                        try
                        {
                            await _relationshipClient.UpdateRelationshipAsync(
                                new UpdateRelationshipRequest
                                {
                                    RelationshipId = relationship.RelationshipId,
                                    RelationshipTypeId = body.TargetTypeId
                                },
                                cancellationToken);
                            migratedCount++;
                        }
                        catch (Exception relEx)
                        {
                            _logger.LogError(relEx, "Failed to migrate relationship {RelationshipId} from type {SourceId} to {TargetId}",
                                relationship.RelationshipId, body.SourceTypeId, body.TargetTypeId);
                            failedCount++;

                            // Track error details (limited to avoid unbounded memory usage)
                            if (migrationErrors.Count < maxErrorsToTrack)
                            {
                                migrationErrors.Add(new MigrationError
                                {
                                    RelationshipId = relationship.RelationshipId,
                                    Error = relEx.Message
                                });
                            }
                        }
                    }

                    hasMorePages = relationshipsResponse.HasNextPage;
                    page++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching relationships for type migration at page {Page}", page);
                    hasMorePages = false;
                }
            }

            if (failedCount > 0)
            {
                _logger.LogError("Relationship type merge completed with {FailedCount} failed relationship migrations", failedCount);

                // Publish error event for monitoring/alerting
                await _messageBus.TryPublishErrorAsync(
                    "relationship-type",
                    "MergeRelationshipType",
                    "PartialMigrationFailure",
                    $"Failed to migrate {failedCount} relationships from type {body.SourceTypeId} to {body.TargetTypeId}",
                    dependency: "relationship-service",
                    endpoint: "post:/relationship-type/merge",
                    details: new { SourceTypeId = body.SourceTypeId, TargetTypeId = body.TargetTypeId, FailedCount = failedCount, MigratedCount = migratedCount },
                    stack: null,
                    cancellationToken: cancellationToken);
            }

            _logger.LogInformation("Merged relationship type {SourceId} into {TargetId}, migrated {MigratedCount} relationships (failed: {FailedCount})",
                body.SourceTypeId, body.TargetTypeId, migratedCount, failedCount);

            return (StatusCodes.OK, new MergeRelationshipTypeResponse
            {
                SourceTypeId = body.SourceTypeId,
                TargetTypeId = body.TargetTypeId,
                RelationshipsMigrated = migratedCount,
                RelationshipsFailed = failedCount,
                MigrationErrors = migrationErrors,
                SourceDeleted = false // Source remains as deprecated for historical references
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error merging relationship type {SourceId} into {TargetId}",
                body.SourceTypeId, body.TargetTypeId);
            await EmitErrorAsync("MergeRelationshipType", "post:/relationship-type/merge", ex);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Helper Methods

    private static string BuildTypeKey(string typeId) => $"{TYPE_KEY_PREFIX}{typeId}";
    private static string BuildCodeIndexKey(string code) => $"{CODE_INDEX_PREFIX}{code}";
    private static string BuildParentIndexKey(string parentId) => $"{PARENT_INDEX_PREFIX}{parentId}";

    private async Task<List<string>> GetChildTypeIdsAsync(string parentId, bool recursive, CancellationToken cancellationToken)
    {
        var parentIndexKey = BuildParentIndexKey(parentId);
        var directChildren = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE)
            .GetAsync(parentIndexKey, cancellationToken) ?? new List<string>();

        if (!recursive || directChildren.Count == 0)
        {
            return directChildren;
        }

        var allChildren = new List<string>(directChildren);
        foreach (var childId in directChildren)
        {
            var grandchildren = await GetChildTypeIdsAsync(childId, true, cancellationToken);
            allChildren.AddRange(grandchildren);
        }

        return allChildren;
    }

    private async Task AddToParentIndexAsync(string parentId, string childId, CancellationToken cancellationToken)
    {
        var parentIndexKey = BuildParentIndexKey(parentId);
        var store = _stateStoreFactory.GetStore<List<string>>(STATE_STORE);
        var children = await store.GetAsync(parentIndexKey, cancellationToken) ?? new List<string>();

        if (!children.Contains(childId))
        {
            children.Add(childId);
            await store.SaveAsync(parentIndexKey, children, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromParentIndexAsync(string parentId, string childId, CancellationToken cancellationToken)
    {
        var parentIndexKey = BuildParentIndexKey(parentId);
        var store = _stateStoreFactory.GetStore<List<string>>(STATE_STORE);
        var children = await store.GetAsync(parentIndexKey, cancellationToken) ?? new List<string>();

        if (children.Remove(childId))
        {
            await store.SaveAsync(parentIndexKey, children, cancellationToken: cancellationToken);
        }
    }

    private async Task AddToAllTypesListAsync(string typeId, CancellationToken cancellationToken)
    {
        var store = _stateStoreFactory.GetStore<List<string>>(STATE_STORE);
        var allTypes = await store.GetAsync(ALL_TYPES_KEY, cancellationToken) ?? new List<string>();

        if (!allTypes.Contains(typeId))
        {
            allTypes.Add(typeId);
            await store.SaveAsync(ALL_TYPES_KEY, allTypes, cancellationToken: cancellationToken);
        }
    }

    private async Task RemoveFromAllTypesListAsync(string typeId, CancellationToken cancellationToken)
    {
        var store = _stateStoreFactory.GetStore<List<string>>(STATE_STORE);
        var allTypes = await store.GetAsync(ALL_TYPES_KEY, cancellationToken) ?? new List<string>();

        if (allTypes.Remove(typeId))
        {
            await store.SaveAsync(ALL_TYPES_KEY, allTypes, cancellationToken: cancellationToken);
        }
    }

    private Task<RelationshipTypeResponse> MapToResponseAsync(RelationshipTypeModel model, CancellationToken cancellationToken)
    {
        return Task.FromResult(new RelationshipTypeResponse
        {
            RelationshipTypeId = Guid.Parse(model.RelationshipTypeId),
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            Category = model.Category,
            ParentTypeId = string.IsNullOrEmpty(model.ParentTypeId) ? null : Guid.Parse(model.ParentTypeId),
            ParentTypeCode = model.ParentTypeCode,
            InverseTypeId = string.IsNullOrEmpty(model.InverseTypeId) ? null : Guid.Parse(model.InverseTypeId),
            InverseTypeCode = model.InverseTypeCode,
            IsBidirectional = model.IsBidirectional,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            Depth = model.Depth,
            Metadata = model.Metadata,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        });
    }

    private async Task EmitErrorAsync(string operation, string endpoint, Exception ex)
    {
        await _messageBus.TryPublishErrorAsync(
            "relationship-type",
            operation,
            "unexpected_exception",
            ex.Message,
            dependency: null,
            endpoint: endpoint,
            details: null,
            stack: ex.StackTrace);
    }

    #endregion

    #region Event Publishing

    /// <summary>
    /// Publishes a relationship type created event.
    /// </summary>
    private async Task PublishRelationshipTypeCreatedEventAsync(RelationshipTypeModel model, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new RelationshipTypeCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RelationshipTypeId = Guid.Parse(model.RelationshipTypeId),
                Code = model.Code,
                Name = model.Name,
                Category = model.Category ?? string.Empty,
                ParentTypeId = string.IsNullOrEmpty(model.ParentTypeId) ? Guid.Empty : Guid.Parse(model.ParentTypeId)
            };

            await _messageBus.TryPublishAsync("relationship-type.created", eventModel);
            _logger.LogDebug("Published relationship-type.created event for {TypeId}", model.RelationshipTypeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish relationship-type.created event for {TypeId}", model.RelationshipTypeId);
        }
    }

    /// <summary>
    /// Publishes a relationship type updated event with full model data and changed fields.
    /// Used for all update operations including deprecation and restoration.
    /// </summary>
    private async Task PublishRelationshipTypeUpdatedEventAsync(RelationshipTypeModel model, IEnumerable<string> changedFields, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new RelationshipTypeUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RelationshipTypeId = Guid.Parse(model.RelationshipTypeId),
                Code = model.Code,
                Name = model.Name,
                Description = model.Description ?? string.Empty,
                Category = model.Category ?? string.Empty,
                ParentTypeId = string.IsNullOrEmpty(model.ParentTypeId) ? Guid.Empty : Guid.Parse(model.ParentTypeId),
                InverseTypeId = string.IsNullOrEmpty(model.InverseTypeId) ? Guid.Empty : Guid.Parse(model.InverseTypeId),
                IsBidirectional = model.IsBidirectional,
                Depth = model.Depth,
                IsDeprecated = model.IsDeprecated,
                DeprecatedAt = model.DeprecatedAt ?? default,
                DeprecationReason = model.DeprecationReason ?? string.Empty,
                CreatedAt = model.CreatedAt,
                UpdatedAt = model.UpdatedAt,
                ChangedFields = changedFields.ToList()
            };

            await _messageBus.TryPublishAsync("relationship-type.updated", eventModel);
            _logger.LogDebug("Published relationship-type.updated event for {TypeId} with changed fields: {ChangedFields}",
                model.RelationshipTypeId, string.Join(", ", changedFields));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish relationship-type.updated event for {TypeId}", model.RelationshipTypeId);
        }
    }

    /// <summary>
    /// Publishes a relationship type deleted event.
    /// </summary>
    private async Task PublishRelationshipTypeDeletedEventAsync(RelationshipTypeModel model, CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new RelationshipTypeDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RelationshipTypeId = Guid.Parse(model.RelationshipTypeId),
                Code = model.Code
            };

            await _messageBus.TryPublishAsync("relationship-type.deleted", eventModel);
            _logger.LogDebug("Published relationship-type.deleted event for {TypeId}", model.RelationshipTypeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish relationship-type.deleted event for {TypeId}", model.RelationshipTypeId);
        }
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permissions service on startup.
    /// Uses generated permission data from x-permissions sections in the OpenAPI schema.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering RelationshipType service permissions...");
        await RelationshipTypePermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
    }

    #endregion
}

/// <summary>
/// Internal model for storing relationship types in state store.
/// </summary>
internal class RelationshipTypeModel
{
    public string RelationshipTypeId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? ParentTypeId { get; set; }
    public string? ParentTypeCode { get; set; }
    public string? InverseTypeId { get; set; }
    public string? InverseTypeCode { get; set; }
    public bool IsBidirectional { get; set; }
    public int Depth { get; set; }
    public object? Metadata { get; set; }
    public bool IsDeprecated { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
    public string? DeprecationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
