using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Status.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Status;

// =============================================================================
// StatusService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by StatusService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (StatusService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IStatusService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (StatusService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for StatusService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class StatusService
{
    // ========================================================================
    // PRIVATE HELPERS
    // ========================================================================

    /// <summary>
    /// Handles stacking behavior when granting a status that already exists on the entity.
    /// </summary>
    private async Task<(StatusCodes, GrantStatusResponse?)> HandleStackingAsync(
        GrantStatusRequest body,
        StatusTemplateModel template,
        List<StatusInstanceModel> existingInstances,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusService.HandleStackingAsync");
        var effectiveMaxStacks = Math.Min(template.MaxStacks, _configuration.MaxStacksPerStatus);
        var existing = existingInstances[0];

        switch (template.StackBehavior)
        {
            case StackBehavior.Ignore:
                await PublishGrantFailedEventAsync(
                    body, GrantFailureReason.StackBehaviorIgnore, existing.StatusInstanceId, cancellationToken);
                return (StatusCodes.Conflict, null);

            case StackBehavior.Replace:
                // Remove old instance, create new
                await RemoveInstanceInternalAsync(existing, StatusRemoveReason.Cancelled, cancellationToken);
                return await CreateNewStatusInstanceAsync(body, template, cancellationToken);

            case StackBehavior.RefreshDuration:
                // Update expiration on existing
                existing.ExpiresAt = CalculateExpiry(body, template);
                await _instanceStore.SaveAsync(
                    BuildInstanceIdKey(existing.StatusInstanceId), existing, cancellationToken: cancellationToken);
                await InvalidateActiveCacheAsync(body.EntityId, body.EntityType, cancellationToken);

                await _messageBus.PublishStatusStackedAsync(
                    new StatusStackedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        EntityId = body.EntityId,
                        EntityType = body.EntityType,
                        StatusTemplateCode = body.StatusTemplateCode,
                        StatusInstanceId = existing.StatusInstanceId,
                        OldStackCount = existing.StackCount,
                        NewStackCount = existing.StackCount
                    },
                    cancellationToken);

                // Push client event for duration refresh
                await PublishStatusClientEventAsync(body.EntityType, body.EntityId,
                    new StatusEffectChangedClientEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        ChangeType = StatusChangeType.Stacked,
                        EntityId = body.EntityId,
                        EntityType = body.EntityType,
                        StatusTemplateCode = body.StatusTemplateCode,
                        StatusInstanceId = existing.StatusInstanceId,
                        Category = template.Category,
                        StackCount = existing.StackCount,
                        ExpiresAt = existing.ExpiresAt
                    }, cancellationToken);

                return (StatusCodes.OK, new GrantStatusResponse
                {
                    StatusInstanceId = existing.StatusInstanceId,
                    StatusTemplateCode = body.StatusTemplateCode,
                    StackCount = existing.StackCount,
                    ContractInstanceId = existing.ContractInstanceId,
                    ItemInstanceId = existing.ItemInstanceId,
                    GrantedAt = existing.GrantedAt,
                    ExpiresAt = existing.ExpiresAt,
                    GrantResult = GrantResult.Refreshed
                });

            case StackBehavior.IncreaseIntensity:
                if (existing.StackCount >= effectiveMaxStacks)
                {
                    await PublishGrantFailedEventAsync(
                        body, GrantFailureReason.StackLimitReached, existing.StatusInstanceId, cancellationToken);
                    return (StatusCodes.Conflict, null);
                }

                var oldCount = existing.StackCount;
                existing.StackCount++;
                // Optionally refresh duration on stack
                if (body.DurationOverrideSeconds.HasValue)
                {
                    existing.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(body.DurationOverrideSeconds.Value);
                }
                await _instanceStore.SaveAsync(
                    BuildInstanceIdKey(existing.StatusInstanceId), existing, cancellationToken: cancellationToken);
                await InvalidateActiveCacheAsync(body.EntityId, body.EntityType, cancellationToken);

                await _messageBus.PublishStatusStackedAsync(
                    new StatusStackedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        EntityId = body.EntityId,
                        EntityType = body.EntityType,
                        StatusTemplateCode = body.StatusTemplateCode,
                        StatusInstanceId = existing.StatusInstanceId,
                        OldStackCount = oldCount,
                        NewStackCount = existing.StackCount
                    },
                    cancellationToken);

                // Push client event for stack increase
                await PublishStatusClientEventAsync(body.EntityType, body.EntityId,
                    new StatusEffectChangedClientEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        ChangeType = StatusChangeType.Stacked,
                        EntityId = body.EntityId,
                        EntityType = body.EntityType,
                        StatusTemplateCode = body.StatusTemplateCode,
                        StatusInstanceId = existing.StatusInstanceId,
                        Category = template.Category,
                        StackCount = existing.StackCount,
                        ExpiresAt = existing.ExpiresAt
                    }, cancellationToken);

                return (StatusCodes.OK, new GrantStatusResponse
                {
                    StatusInstanceId = existing.StatusInstanceId,
                    StatusTemplateCode = body.StatusTemplateCode,
                    StackCount = existing.StackCount,
                    ContractInstanceId = existing.ContractInstanceId,
                    ItemInstanceId = existing.ItemInstanceId,
                    GrantedAt = existing.GrantedAt,
                    ExpiresAt = existing.ExpiresAt,
                    GrantResult = GrantResult.Stacked
                });

            case StackBehavior.Independent:
                if (existingInstances.Count >= effectiveMaxStacks)
                {
                    await PublishGrantFailedEventAsync(
                        body, GrantFailureReason.StackLimitReached, existing.StatusInstanceId, cancellationToken);
                    return (StatusCodes.Conflict, null);
                }
                // Create a new independent instance
                return await CreateNewStatusInstanceAsync(body, template, cancellationToken);

            default:
                _logger.LogError(
                    "Unknown stack behavior {StackBehavior} for template {Code}",
                    template.StackBehavior, template.Code);
                return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Creates a new status instance with backing item and optional contract.
    /// </summary>
    private async Task<(StatusCodes, GrantStatusResponse?)> CreateNewStatusInstanceAsync(
        GrantStatusRequest body,
        StatusTemplateModel template,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusService.CreateNewStatusInstanceAsync");
        // Get or create container for this entity
        var container = await GetOrCreateContainerAsync(
            body.EntityId, body.EntityType, body.GameServiceId, cancellationToken);

        if (container == null)
        {
            await PublishGrantFailedEventAsync(body, GrantFailureReason.ItemCreationFailed, null, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }

        // Create item instance in the container
        Guid itemInstanceId;
        try
        {
            var itemResponse = await _itemClient.CreateItemInstanceAsync(
                new CreateItemInstanceRequest
                {
                    TemplateId = template.ItemTemplateId,
                    ContainerId = container.ContainerId,
                    // RealmId is required by Item for partitioning;
                    // using GameServiceId as the partition key per Collection pattern
                    RealmId = body.GameServiceId,
                    Quantity = 1
                },
                cancellationToken);
            itemInstanceId = itemResponse.InstanceId;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to create item instance for status grant");
            await PublishGrantFailedEventAsync(
                body, GrantFailureReason.ItemCreationFailed, null, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }

        // Create contract instance if template specifies one
        Guid? contractInstanceId = null;
        if (template.ContractTemplateId.HasValue)
        {
            try
            {
                var contractResponse = await _contractClient.CreateContractInstanceAsync(
                    new CreateContractInstanceRequest
                    {
                        TemplateId = template.ContractTemplateId.Value,
                        Parties = new List<ContractPartyInput>
                        {
                            new ContractPartyInput
                            {
                                EntityId = body.EntityId,
                                EntityType = body.EntityType,
                                Role = "subject"
                            }
                        }
                    },
                    cancellationToken);
                contractInstanceId = contractResponse.ContractId;
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "Failed to create contract for status grant, compensating");
                // Saga compensation: delete the item we just created
                try
                {
                    await _itemClient.DestroyItemInstanceAsync(
                        new DestroyItemInstanceRequest { InstanceId = itemInstanceId, Reason = DestroyReason.Destroyed },
                        cancellationToken);
                }
                catch (ApiException deleteEx)
                {
                    _logger.LogError(deleteEx,
                        "Failed to delete item {ItemInstanceId} during contract failure compensation",
                        itemInstanceId);
                }
                await PublishGrantFailedEventAsync(
                    body, GrantFailureReason.ContractFailed, null, cancellationToken);
                return (StatusCodes.InternalServerError, null);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var instanceId = Guid.NewGuid();
        var expiresAt = CalculateExpiry(body, template);

        var instance = new StatusInstanceModel
        {
            StatusInstanceId = instanceId,
            EntityId = body.EntityId,
            EntityType = body.EntityType,
            GameServiceId = body.GameServiceId,
            StatusTemplateCode = body.StatusTemplateCode,
            Category = template.Category,
            StackCount = 1,
            SourceId = body.SourceId,
            ContractInstanceId = contractInstanceId,
            ItemInstanceId = itemInstanceId,
            GrantedAt = now,
            ExpiresAt = expiresAt,
            Metadata = body.Metadata
        };

        // Save instance
        await _instanceStore.SaveAsync(BuildInstanceIdKey(instanceId), instance, cancellationToken: cancellationToken);

        // Invalidate active cache
        await InvalidateActiveCacheAsync(body.EntityId, body.EntityType, cancellationToken);

        // Publish granted event
        await _messageBus.PublishStatusGrantedAsync(
            new StatusGrantedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EntityId = body.EntityId,
                EntityType = body.EntityType,
                StatusTemplateCode = body.StatusTemplateCode,
                StatusInstanceId = instanceId,
                Category = template.Category,
                StackCount = 1,
                SourceId = body.SourceId,
                ExpiresAt = expiresAt,
                GrantResult = GrantResult.Granted
            },
            cancellationToken);

        // Push client event to sessions observing this entity
        await PublishStatusClientEventAsync(body.EntityType, body.EntityId,
            new StatusEffectChangedClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                ChangeType = StatusChangeType.Granted,
                EntityId = body.EntityId,
                EntityType = body.EntityType,
                StatusTemplateCode = body.StatusTemplateCode,
                StatusInstanceId = instanceId,
                Category = template.Category,
                StackCount = 1,
                ExpiresAt = expiresAt,
                SourceId = body.SourceId
            }, cancellationToken);

        _logger.LogInformation(
            "Granted status {Code} to {EntityType} {EntityId} (instance {InstanceId})",
            body.StatusTemplateCode, body.EntityType, body.EntityId, instanceId);

        return (StatusCodes.OK, new GrantStatusResponse
        {
            StatusInstanceId = instanceId,
            StatusTemplateCode = body.StatusTemplateCode,
            StackCount = 1,
            ContractInstanceId = contractInstanceId,
            ItemInstanceId = itemInstanceId,
            GrantedAt = now,
            ExpiresAt = expiresAt,
            GrantResult = GrantResult.Granted
        });
    }

    /// <summary>
    /// Gets or creates an inventory container for an entity's status effects.
    /// </summary>
    private async Task<StatusContainerModel?> GetOrCreateContainerAsync(
        Guid entityId, EntityType entityType, Guid gameServiceId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusService.GetOrCreateContainerAsync");
        var entityKey = BuildContainerEntityKey(entityId, entityType, gameServiceId);
        var existing = await _containerStore.GetAsync(entityKey, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        // Create container via inventory service
        ContainerResponse containerResponse;
        try
        {
            containerResponse = await _inventoryClient.CreateContainerAsync(
                new CreateContainerRequest
                {
                    OwnerId = entityId,
                    OwnerType = MapToContainerOwnerType(entityType),
                    ContainerType = "status_effects",
                    ConstraintModel = ContainerConstraintModel.Unlimited
                },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex,
                "Failed to create status container for {EntityType} {EntityId} via inventory service",
                entityType, entityId);
            await _messageBus.TryPublishErrorAsync(
                "status", "create-container", "InventoryError",
                $"Failed to create status container for {entityType} {entityId}",
                dependency: "inventory",
                cancellationToken: cancellationToken);
            return null;
        }

        var container = new StatusContainerModel
        {
            ContainerId = containerResponse.ContainerId,
            EntityId = entityId,
            EntityType = entityType,
            GameServiceId = gameServiceId
        };

        // Save with dual keys
        await _containerStore.SaveAsync(BuildContainerIdKey(container.ContainerId), container, cancellationToken: cancellationToken);
        await _containerStore.SaveAsync(entityKey, container, cancellationToken: cancellationToken);

        return container;
    }

    /// <summary>
    /// Removes a status instance: deletes backing item, cancels contract, deletes record, invalidates cache.
    /// </summary>
    private async Task RemoveInstanceInternalAsync(
        StatusInstanceModel instance, StatusRemoveReason reason, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusService.RemoveInstanceInternalAsync");
        // Delete backing item
        try
        {
            await _itemClient.DestroyItemInstanceAsync(
                new DestroyItemInstanceRequest { InstanceId = instance.ItemInstanceId, Reason = DestroyReason.Destroyed },
                cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete item instance {ItemInstanceId} for status removal",
                instance.ItemInstanceId);
        }

        // Cancel contract if exists
        if (instance.ContractInstanceId.HasValue)
        {
            try
            {
                await _contractClient.TerminateContractInstanceAsync(
                    new TerminateContractInstanceRequest
                    {
                        ContractId = instance.ContractInstanceId.Value,
                        RequestingEntityId = instance.EntityId,
                        RequestingEntityType = instance.EntityType,
                        Reason = "status-removed"
                    },
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to terminate contract {ContractInstanceId} for status removal",
                    instance.ContractInstanceId.Value);
            }
        }

        // Delete instance record
        await _instanceStore.DeleteAsync(BuildInstanceIdKey(instance.StatusInstanceId), cancellationToken);

        // Invalidate active cache
        await InvalidateActiveCacheAsync(instance.EntityId, instance.EntityType, cancellationToken);

        // Publish removed event
        await _messageBus.PublishStatusRemovedAsync(
            new StatusRemovedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                EntityId = instance.EntityId,
                EntityType = instance.EntityType,
                StatusTemplateCode = instance.StatusTemplateCode,
                StatusInstanceId = instance.StatusInstanceId,
                Reason = reason
            },
            cancellationToken);

        // Push client event to sessions observing this entity
        await PublishStatusClientEventAsync(instance.EntityType, instance.EntityId,
            new StatusEffectChangedClientEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ChangeType = reason == StatusRemoveReason.Cleansed
                    ? StatusChangeType.Cleansed
                    : StatusChangeType.Removed,
                EntityId = instance.EntityId,
                EntityType = instance.EntityType,
                StatusTemplateCode = instance.StatusTemplateCode,
                StatusInstanceId = instance.StatusInstanceId,
                Category = instance.Category
            }, cancellationToken);
    }

    /// <summary>
    /// Calculates the expiration time for a new or refreshed status.
    /// </summary>
    private DateTimeOffset? CalculateExpiry(GrantStatusRequest body, StatusTemplateModel template)
    {
        if (body.DurationOverrideSeconds.HasValue)
        {
            return DateTimeOffset.UtcNow.AddSeconds(body.DurationOverrideSeconds.Value);
        }

        if (template.DefaultDurationSeconds.HasValue)
        {
            return DateTimeOffset.UtcNow.AddSeconds(template.DefaultDurationSeconds.Value);
        }

        // If contract-managed (has ContractTemplateId), expiry is null (contract controls lifecycle)
        if (template.ContractTemplateId.HasValue)
        {
            return null;
        }

        // No explicit duration and no contract: use config default
        return DateTimeOffset.UtcNow.AddSeconds(_configuration.DefaultStatusDurationSeconds);
    }

    /// <summary>
    /// Publishes a status client event to all sessions observing the affected entity.
    /// Uses the entity's own type for routing so sessions watching a character
    /// receive status updates alongside inventory/collection changes.
    /// </summary>
    private async Task PublishStatusClientEventAsync(
        EntityType entityType,
        Guid entityId,
        StatusEffectChangedClientEvent clientEvent,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.status", "StatusService.PublishStatusClientEventAsync");
        await _entitySessionRegistry.PublishToEntitySessionsAsync(
            entityType.ToString().ToLowerInvariant(), entityId, clientEvent, ct);
    }

    /// <summary>
    /// Gets the active status cache for an entity, building it from MySQL on cache miss.
    /// Filters out expired statuses during rebuild and publishes expiration events.
    /// </summary>
    private async Task<ActiveStatusCacheModel> GetOrBuildActiveCacheAsync(
        Guid entityId, EntityType entityType, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusService.GetOrBuildActiveCacheAsync");
        var cacheKey = BuildActiveCacheKey(entityId, entityType);
        var cached = await _activeCacheStore.GetAsync(cacheKey, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        // Build from MySQL
        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.StatusInstanceId", Operator = QueryOperator.Exists, Value = true },
            new QueryCondition { Path = "$.EntityId", Operator = QueryOperator.Equals, Value = entityId },
            new QueryCondition { Path = "$.EntityType", Operator = QueryOperator.Equals, Value = entityType }
        };
        var result = await _instanceQueryStore.JsonQueryPagedAsync(
            conditions, 0, _configuration.MaxStatusesPerEntity, null, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var activeStatuses = new List<CachedStatusEntry>();

        foreach (var entry in result.Items)
        {
            try
            {
                var instance = entry.Value;

                // Lazy expiration: clean up expired statuses found during rebuild
                if (instance.ExpiresAt.HasValue && instance.ExpiresAt.Value <= now)
                {
                    await _messageBus.PublishStatusExpiredAsync(
                        new StatusExpiredEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = now,
                            EntityId = instance.EntityId,
                            EntityType = instance.EntityType,
                            StatusTemplateCode = instance.StatusTemplateCode,
                            StatusInstanceId = instance.StatusInstanceId
                        },
                        cancellationToken);

                    // Push client event for expiration
                    await PublishStatusClientEventAsync(instance.EntityType, instance.EntityId,
                        new StatusEffectChangedClientEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = now,
                            ChangeType = StatusChangeType.Expired,
                            EntityId = instance.EntityId,
                            EntityType = instance.EntityType,
                            StatusTemplateCode = instance.StatusTemplateCode,
                            StatusInstanceId = instance.StatusInstanceId,
                            Category = instance.Category
                        }, cancellationToken);

                    // Destroy backing item instance (match RemoveInstanceInternalAsync compensation)
                    try
                    {
                        await _itemClient.DestroyItemInstanceAsync(
                            new DestroyItemInstanceRequest { InstanceId = instance.ItemInstanceId, Reason = DestroyReason.Destroyed },
                            cancellationToken);
                    }
                    catch (ApiException ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to destroy item instance {ItemInstanceId} during lazy expiration of status {StatusInstanceId}",
                            instance.ItemInstanceId, instance.StatusInstanceId);
                    }

                    // Cancel contract if contract-managed
                    if (instance.ContractInstanceId.HasValue)
                    {
                        try
                        {
                            await _contractClient.TerminateContractInstanceAsync(
                                new TerminateContractInstanceRequest
                                {
                                    ContractId = instance.ContractInstanceId.Value,
                                    RequestingEntityId = instance.EntityId,
                                    RequestingEntityType = instance.EntityType,
                                    Reason = "status-expired"
                                },
                                cancellationToken);
                        }
                        catch (ApiException ex)
                        {
                            _logger.LogWarning(ex,
                                "Failed to terminate contract {ContractInstanceId} during lazy expiration of status {StatusInstanceId}",
                                instance.ContractInstanceId.Value, instance.StatusInstanceId);
                        }
                    }

                    // Remove expired instance record
                    await _instanceStore.DeleteAsync(
                        BuildInstanceIdKey(instance.StatusInstanceId), cancellationToken);
                    continue;
                }

                activeStatuses.Add(new CachedStatusEntry
                {
                    StatusInstanceId = instance.StatusInstanceId,
                    StatusTemplateCode = instance.StatusTemplateCode,
                    Category = instance.Category,
                    StackCount = instance.StackCount,
                    SourceId = instance.SourceId,
                    ExpiresAt = instance.ExpiresAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to process status instance {StatusInstanceId} during active cache rebuild",
                    entry.Value.StatusInstanceId);
            }
        }

        var cache = new ActiveStatusCacheModel
        {
            EntityId = entityId,
            EntityType = entityType,
            Statuses = activeStatuses,
            CachedAt = now
        };

        // Save to cache with TTL; MaxCachedEntities is enforced by Redis eviction policy
        await _activeCacheStore.SaveAsync(cacheKey, cache,
            new StateOptions { Ttl = _configuration.StatusCacheTtlSeconds },
            cancellationToken);

        return cache;
    }

    /// <summary>
    /// Gets the seed effects cache for an entity, building it from the Seed service on cache miss.
    /// </summary>
    private async Task<SeedEffectsCacheModel> GetOrBuildSeedEffectsCacheAsync(
        Guid entityId, EntityType entityType, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusService.GetOrBuildSeedEffectsCacheAsync");
        var cacheKey = BuildSeedEffectsCacheKey(entityId, entityType);
        var cached = await _seedEffectsCacheStore.GetAsync(cacheKey, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        // Build from Seed service (L2 — hard dependency, constructor-injected per FOUNDATION TENETS)
        var effects = new List<CachedSeedEffect>();

        try
        {
            var seedsResponse = await _seedClient.GetSeedsByOwnerAsync(
                new GetSeedsByOwnerRequest
                {
                    OwnerId = entityId,
                    OwnerType = entityType
                },
                cancellationToken);

            foreach (var seed in seedsResponse.Seeds)
            {
                var capsResponse = await _seedClient.GetCapabilityManifestAsync(
                    new GetCapabilityManifestRequest { SeedId = seed.SeedId },
                    cancellationToken);

                foreach (var cap in capsResponse.Capabilities)
                {
                    effects.Add(new CachedSeedEffect
                    {
                        CapabilityCode = cap.CapabilityCode,
                        Domain = cap.Domain,
                        Fidelity = cap.Fidelity,
                        SeedId = seed.SeedId,
                        SeedTypeCode = seed.SeedTypeCode
                    });
                }
            }
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch seed capabilities for {EntityType} {EntityId}",
                entityType, entityId);
        }

        var cache = new SeedEffectsCacheModel
        {
            EntityId = entityId,
            EntityType = entityType,
            Effects = effects,
            CachedAt = DateTimeOffset.UtcNow
        };

        // Save to cache with TTL
        await _seedEffectsCacheStore.SaveAsync(cacheKey, cache,
            new StateOptions { Ttl = _configuration.SeedEffectsCacheTtlSeconds },
            cancellationToken);

        return cache;
    }

    /// <summary>
    /// Invalidates the active status cache for an entity.
    /// </summary>
    private async Task InvalidateActiveCacheAsync(
        Guid entityId, EntityType entityType, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusService.InvalidateActiveCacheAsync");
        try
        {
            await _activeCacheStore.DeleteAsync(
                BuildActiveCacheKey(entityId, entityType), cancellationToken);
        }
        catch (Exception ex)
        {
            // Cache invalidation failure is non-fatal; cache will expire via TTL
            _logger.LogWarning(ex,
                "Failed to invalidate active cache for {EntityType} {EntityId}",
                entityType, entityId);
            await _messageBus.TryPublishErrorAsync(
                "status", "invalidate-active-cache", "CacheError",
                $"Failed to invalidate active cache for {entityType} {entityId}",
                dependency: "state",
                severity: ServiceErrorEventSeverity.Warning,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Invalidates the seed effects cache for an entity.
    /// </summary>
    private async Task InvalidateSeedEffectsCacheAsync(
        Guid entityId, EntityType entityType, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusService.InvalidateSeedEffectsCacheAsync");
        try
        {
            await _seedEffectsCacheStore.DeleteAsync(
                BuildSeedEffectsCacheKey(entityId, entityType), cancellationToken);
        }
        catch (Exception ex)
        {
            // Cache invalidation failure is non-fatal; cache will expire via TTL
            _logger.LogWarning(ex,
                "Failed to invalidate seed effects cache for {EntityType} {EntityId}",
                entityType, entityId);
            await _messageBus.TryPublishErrorAsync(
                "status", "invalidate-seed-effects-cache", "CacheError",
                $"Failed to invalidate seed effects cache for {entityType} {entityId}",
                dependency: "state",
                severity: ServiceErrorEventSeverity.Warning,
                cancellationToken: cancellationToken);
        }
    }

    /// <summary>
    /// Publishes a StatusGrantFailedEvent for tracking and diagnostics.
    /// </summary>
    private async Task PublishGrantFailedEventAsync(
        GrantStatusRequest body, GrantFailureReason reason,
        Guid? existingStatusInstanceId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusService.PublishGrantFailedEventAsync");
        await _messageBus.PublishStatusGrantFailedAsync(
            new StatusGrantFailedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                EntityId = body.EntityId,
                EntityType = body.EntityType,
                StatusTemplateCode = body.StatusTemplateCode,
                Reason = reason,
                ExistingStatusInstanceId = existingStatusInstanceId
            },
            cancellationToken);
    }

    #region Response Builders

    private static StatusTemplateResponse ToTemplateResponse(StatusTemplateModel model) => new()
    {
        StatusTemplateId = model.StatusTemplateId,
        GameServiceId = model.GameServiceId,
        Code = model.Code,
        DisplayName = model.DisplayName,
        Description = model.Description,
        Category = model.Category,
        Stackable = model.Stackable,
        MaxStacks = model.MaxStacks,
        StackBehavior = model.StackBehavior,
        ContractTemplateId = model.ContractTemplateId,
        ItemTemplateId = model.ItemTemplateId,
        DefaultDurationSeconds = model.DefaultDurationSeconds,
        IconAssetId = model.IconAssetId,
        IsDeprecated = model.IsDeprecated,
        DeprecatedAt = model.DeprecatedAt,
        DeprecationReason = model.DeprecationReason,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt
    };

    private static StatusInstanceResponse ToInstanceResponse(StatusInstanceModel model) => new()
    {
        StatusInstanceId = model.StatusInstanceId,
        EntityId = model.EntityId,
        EntityType = model.EntityType,
        StatusTemplateCode = model.StatusTemplateCode,
        Category = model.Category,
        StackCount = model.StackCount,
        SourceId = model.SourceId,
        ContractInstanceId = model.ContractInstanceId,
        ItemInstanceId = model.ItemInstanceId,
        GrantedAt = model.GrantedAt,
        ExpiresAt = model.ExpiresAt,
        Metadata = model.Metadata
    };

    #endregion
}
