using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace BeyondImmersion.BannouService.Item;

// =============================================================================
// ItemService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by ItemService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (ItemService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IItemService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (ItemService.Helpers.cs):
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
/// Private and internal helper methods for ItemService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class ItemService
{
    // Move private/internal helper methods here from ItemService.cs
    /// <summary>
    /// Modifies an item instance with a distributed lock to prevent container index races.
    /// </summary>
    private async Task<(StatusCodes, ItemInstanceResponse?)> ModifyItemInstanceWithLockAsync(
        ModifyItemInstanceRequest body,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.ModifyItemInstanceWithLockAsync");
        var lockOwner = $"modify-item-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.ItemLock,
            body.InstanceId.ToString(),
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for item instance {InstanceId}", body.InstanceId);
            return (StatusCodes.Conflict, null);
        }

        return await ModifyItemInstanceInternalAsync(body, cancellationToken);
    }

    /// <summary>
    /// Internal implementation of item instance modification.
    /// </summary>
    private async Task<(StatusCodes, ItemInstanceResponse?)> ModifyItemInstanceInternalAsync(
        ModifyItemInstanceRequest body,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.ModifyItemInstanceInternalAsync");
        var model = await _instanceStore.GetAsync($"{INST_PREFIX}{body.InstanceId}", cancellationToken);

        if (model is null)
        {
            return (StatusCodes.NotFound, null);
        }

        var now = DateTimeOffset.UtcNow;

        // Capture old container ID for index updates (must be captured before modification).
        // Uses ChangeFields 3-state semantics (Issue #722): setting newContainerId to a value
        // moves the item, setting it explicitly to null clears the container (unplaced item).
        var oldContainerId = model.ContainerId;
        var containerExplicitlySet = body.ChangeFields.IsFieldSet("newContainerId");
        var containerCleared = containerExplicitlySet && !body.NewContainerId.HasValue && oldContainerId.HasValue;
        var containerChanged = containerExplicitlySet && body.NewContainerId.HasValue && body.NewContainerId.Value != oldContainerId;

        // Apply modifications — every field checks ChangeFields so absent means "no change"
        if (body.ChangeFields.IsFieldSet("durabilityDelta") && body.DurabilityDelta.HasValue && model.CurrentDurability.HasValue)
        {
            var newDurability = Math.Max(0, model.CurrentDurability.Value + body.DurabilityDelta.Value);

            // Cap positive deltas against template MaxDurability per IMPLEMENTATION TENETS
            if (body.DurabilityDelta.Value > 0)
            {
                var template = await GetTemplateWithCacheAsync(model.TemplateId.ToString(), cancellationToken);
                if (template?.MaxDurability.HasValue == true)
                {
                    newDurability = Math.Min(template.MaxDurability.Value, newDurability);
                }
            }

            model.CurrentDurability = newDurability;
        }
        if (body.ChangeFields.IsFieldSet("quantityDelta") && body.QuantityDelta.HasValue)
        {
            model.Quantity = Math.Max(0, model.Quantity + body.QuantityDelta.Value);
        }
        if (body.ChangeFields.IsFieldSet("customStats"))
        {
            model.CustomStats = body.CustomStats is not null ? BannouJson.Serialize(body.CustomStats) : null;
        }
        if (body.ChangeFields.IsFieldSet("customName"))
        {
            model.CustomName = body.CustomName;
        }
        if (body.ChangeFields.IsFieldSet("instanceMetadata"))
        {
            model.InstanceMetadata = body.InstanceMetadata is not null ? BannouJson.Serialize(body.InstanceMetadata) : null;
        }
        if (containerCleared)
        {
            model.ContainerId = null;
            model.SlotIndex = null;
            model.SlotX = null;
            model.SlotY = null;
        }
        else if (containerChanged)
        {
            model.ContainerId = body.NewContainerId!.Value;
        }
        if (body.ChangeFields.IsFieldSet("newSlotIndex"))
        {
            model.SlotIndex = body.NewSlotIndex;
        }
        if (body.ChangeFields.IsFieldSet("newSlotX"))
        {
            model.SlotX = body.NewSlotX;
        }
        if (body.ChangeFields.IsFieldSet("newSlotY"))
        {
            model.SlotY = body.NewSlotY;
        }
        model.ModifiedAt = now;

        // Save the model first, then update indexes
        await _instanceStore.SaveAsync($"{INST_PREFIX}{body.InstanceId}", model, cancellationToken: cancellationToken);

        // Update container indexes if container changed or cleared (after successful save)
        if (containerCleared && oldContainerId.HasValue)
        {
            await RemoveFromListAsync(_instanceStringStore, $"{INST_CONTAINER_INDEX}{oldContainerId.Value}", body.InstanceId.ToString(), cancellationToken);
        }
        else if (containerChanged)
        {
            var newContainerId = body.NewContainerId
                ?? throw new InvalidOperationException("NewContainerId is null when containerChanged is true");
            if (oldContainerId.HasValue)
            {
                await RemoveFromListAsync(_instanceStringStore, $"{INST_CONTAINER_INDEX}{oldContainerId.Value}", body.InstanceId.ToString(), cancellationToken);
            }
            await AddToListAsync(_instanceStringStore, $"{INST_CONTAINER_INDEX}{newContainerId}", body.InstanceId.ToString(), cancellationToken);
        }

        // Invalidate cache after write
        await InvalidateInstanceCacheAsync(body.InstanceId.ToString(), cancellationToken);

        _instanceEventBatcher.AddModified(body.InstanceId, new ItemInstanceBatchModifiedEntry
        {
            InstanceId = body.InstanceId,
            TemplateId = model.TemplateId,
            ContainerId = model.ContainerId,
            RealmId = model.RealmId,
            Quantity = model.Quantity,
            OriginType = model.OriginType,
            CreatedAt = model.CreatedAt,
            ModifiedAt = now
        });

        _logger.LogDebug("Modified item instance {InstanceId}", body.InstanceId);
        return (StatusCodes.OK, MapInstanceToResponse(model));
    }
    /// <summary>
    /// Gets the remaining uncompleted milestone codes from a contract instance.
    /// </summary>
    private async Task<List<string>> GetRemainingMilestonesAsync(
        Guid contractInstanceId,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.GetRemainingMilestonesAsync");
        try
        {
            var response = await _contractClient.GetContractInstanceAsync(
                new GetContractInstanceRequest { ContractId = contractInstanceId },
                ct);

            // Filter milestones that are not yet completed
            return response.Milestones?
                .Where(m => m.Status != MilestoneStatus.Completed && m.Status != MilestoneStatus.Skipped)
                .Select(m => m.Code)
                .ToList() ?? new List<string>();
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Failed to get remaining milestones for contract {ContractId}", contractInstanceId);
            await _messageBus.TryPublishErrorAsync(
                "item",
                "GetRemainingMilestones",
                "ContractQueryFailed",
                ex.Message,
                dependency: "contract",
                endpoint: "contract/get-contract-instance",
                stack: ex.StackTrace,
                cancellationToken: ct);
            return new List<string>();
        }
    }

    /// <summary>
    /// Publishes an ItemUseStepCompletedEvent.
    /// </summary>
    private async Task PublishStepCompletedEventAsync(
        Guid instanceId,
        Guid templateId,
        string templateCode,
        Guid userId,
        EntityType userType,
        Guid contractInstanceId,
        string milestoneCode,
        List<string>? remainingMilestones,
        bool isComplete,
        bool consumed,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.PublishStepCompletedEventAsync");
        await _messageBus.PublishItemUseStepCompletedAsync(new ItemUseStepCompletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            InstanceId = instanceId,
            TemplateId = templateId,
            TemplateCode = templateCode,
            UserId = userId,
            UserType = userType,
            ContractInstanceId = contractInstanceId,
            MilestoneCode = milestoneCode,
            RemainingMilestones = remainingMilestones,
            IsComplete = isComplete,
            Consumed = consumed
        }, ct);
    }

    /// <summary>
    /// Publishes an ItemUseStepFailedEvent.
    /// </summary>
    private async Task PublishStepFailedEventAsync(
        Guid instanceId,
        Guid templateId,
        string templateCode,
        Guid userId,
        EntityType userType,
        Guid contractInstanceId,
        string milestoneCode,
        string reason,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.PublishStepFailedEventAsync");
        await _messageBus.PublishItemUseStepFailedAsync(new ItemUseStepFailedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            InstanceId = instanceId,
            TemplateId = templateId,
            TemplateCode = templateCode,
            UserId = userId,
            UserType = userType,
            ContractInstanceId = contractInstanceId,
            MilestoneCode = milestoneCode,
            Reason = reason
        }, ct);
    }

    /// <summary>
    /// Computes or retrieves the deterministic system party ID for item use contracts.
    /// Uses SHA-256 hash of game ID to generate a deterministic UUID v5.
    /// </summary>
    /// <param name="gameId">The game ID to derive the system party ID from.</param>
    /// <returns>The system party ID (from config if set, otherwise computed).</returns>
    private Guid GetOrComputeSystemPartyId(string gameId)
    {
        // If configured explicitly, use that
        if (_configuration.SystemPartyId.HasValue)
        {
            return _configuration.SystemPartyId.Value;
        }

        // Compute deterministic UUID from game ID using SHA-256
        // This ensures the same game always gets the same system party ID across instances
        var inputBytes = Encoding.UTF8.GetBytes($"item-system-party:{gameId}");
        var hashBytes = SHA256.HashData(inputBytes);

        // Take first 16 bytes of hash and convert to GUID
        // Set version (4 bits) and variant (2 bits) per UUID v5 spec
        var guidBytes = new byte[16];
        Array.Copy(hashBytes, guidBytes, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // Version 5
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // Variant 1

        return new Guid(guidBytes);
    }

    /// <summary>
    /// Executes the CanUse validation contract if configured.
    /// Returns (passed, failureReason).
    /// </summary>
    /// <remarks>
    /// Uses milestone code from configuration (ITEM_CAN_USE_MILESTONE_CODE, default "validate").
    /// Per IMPLEMENTATION TENETS: No hardcoded tunables.
    /// </remarks>
    private async Task<(bool Passed, string? FailureReason)> ExecuteCanUseValidationAsync(
        Guid canUseTemplateId,
        Guid userId,
        EntityType userType,
        Guid instanceId,
        Guid templateId,
        object? context,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.ExecuteCanUseValidationAsync");
        try
        {
            // Get system party for the validation contract
            var template = await GetTemplateWithCacheAsync(templateId.ToString(), ct);
            if (template is null)
            {
                return (false, "Template not found during validation");
            }

            var systemPartyId = GetOrComputeSystemPartyId(template.GameId);

            // Create validation contract instance
            var contractInstanceId = await CreateItemUseContractInstanceAsync(
                canUseTemplateId,
                userId,
                userType,
                systemPartyId,
                instanceId,
                templateId,
                context,
                ct);

            if (!contractInstanceId.HasValue)
            {
                return (false, "Failed to create validation contract");
            }

            // Complete configured milestone (default: "validate")
            var milestoneCode = _configuration.CanUseMilestoneCode;
            var response = await _contractClient.CompleteMilestoneAsync(
                new CompleteMilestoneRequest
                {
                    ContractId = contractInstanceId.Value,
                    MilestoneCode = milestoneCode
                }, ct);

            if (response.Milestone.Status != MilestoneStatus.Completed)
            {
                return (false, $"Validation milestone failed: {milestoneCode}");
            }

            return (true, null);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "CanUse validation failed for {InstanceId}", instanceId);
            await _messageBus.TryPublishErrorAsync(
                "item",
                "ExecuteCanUseValidation",
                "CanUseValidationFailed",
                ex.Message,
                dependency: "contract",
                endpoint: "contract/complete-milestone",
                stack: ex.StackTrace,
                cancellationToken: ct);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Executes the OnUseFailed handler contract if configured.
    /// </summary>
    /// <remarks>
    /// Uses milestone code from configuration (ITEM_ON_USE_FAILED_MILESTONE_CODE, default "handle_failure").
    /// Per IMPLEMENTATION TENETS: Log but don't propagate handler failures.
    /// </remarks>
    private async Task ExecuteOnUseFailedHandlerAsync(
        Guid onUseFailedTemplateId,
        Guid userId,
        EntityType userType,
        Guid instanceId,
        Guid templateId,
        string failureReason,
        object? context,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.ExecuteOnUseFailedHandlerAsync");
        try
        {
            // Get system party for the handler contract
            var template = await GetTemplateWithCacheAsync(templateId.ToString(), ct);
            if (template is null)
            {
                _logger.LogWarning(
                    "Template {TemplateId} not found for OnUseFailed handler",
                    templateId);
                return;
            }

            var systemPartyId = GetOrComputeSystemPartyId(template.GameId);

            // Merge failure reason into context
            var enrichedContext = new Dictionary<string, object?>
            {
                ["failureReason"] = failureReason
            };
            if (context is IDictionary<string, object?> contextDict)
            {
                foreach (var kvp in contextDict)
                {
                    enrichedContext[kvp.Key] = kvp.Value;
                }
            }

            // Create failure handler contract instance
            var contractInstanceId = await CreateItemUseContractInstanceAsync(
                onUseFailedTemplateId,
                userId,
                userType,
                systemPartyId,
                instanceId,
                templateId,
                enrichedContext,
                ct);

            if (!contractInstanceId.HasValue)
            {
                _logger.LogWarning(
                    "Failed to create OnUseFailed contract for {InstanceId}",
                    instanceId);
                return;
            }

            // Complete configured milestone (default: "handle_failure")
            var milestoneCode = _configuration.OnUseFailedMilestoneCode;
            await _contractClient.CompleteMilestoneAsync(
                new CompleteMilestoneRequest
                {
                    ContractId = contractInstanceId.Value,
                    MilestoneCode = milestoneCode
                }, ct);
        }
        catch (Exception ex)
        {
            // Log but don't propagate - handler failures shouldn't break the main flow
            _logger.LogError(ex, "OnUseFailed handler error for {InstanceId}", instanceId);
        }
    }

    /// <summary>
    /// Creates a transient contract instance for item use with user and system parties.
    /// </summary>
    /// <returns>Contract instance ID if successful, null otherwise.</returns>
    private async Task<Guid?> CreateItemUseContractInstanceAsync(
        Guid templateId,
        Guid userId,
        EntityType userType,
        Guid systemPartyId,
        Guid instanceId,
        Guid itemTemplateId,
        object? context,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.CreateItemUseContractInstanceAsync");
        try
        {
            // Build game metadata with item context for prebound API substitution
            var gameMetadata = new Dictionary<string, object>
            {
                ["itemInstanceId"] = instanceId.ToString(),
                ["itemTemplateId"] = itemTemplateId.ToString(),
                ["userId"] = userId.ToString(),
                ["userType"] = userType
            };

            // Merge any additional context provided by caller
            if (context is IDictionary<string, object> contextDict)
            {
                foreach (var kvp in contextDict)
                {
                    gameMetadata[kvp.Key] = kvp.Value;
                }
            }

            var request = new CreateContractInstanceRequest
            {
                TemplateId = templateId,
                Parties = new List<ContractPartyInput>
                {
                    new()
                    {
                        EntityId = userId,
                        EntityType = userType,
                        Role = "user"
                    },
                    new()
                    {
                        EntityId = systemPartyId,
                        EntityType = _configuration.SystemPartyType,
                        Role = "system"
                    }
                },
                GameMetadata = gameMetadata
            };

            var response = await _contractClient.CreateContractInstanceAsync(request, cancellationToken);
            return response.ContractId;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "API error creating contract instance for template {TemplateId}",
                templateId);
            await _messageBus.TryPublishErrorAsync(
                "item",
                "CreateItemUseContractInstance",
                "ContractCreationFailed",
                ex.Message,
                dependency: "contract",
                endpoint: "contract/create-contract-instance",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    /// <summary>
    /// Completes the "use" milestone on the contract instance, triggering prebound APIs.
    /// </summary>
    /// <returns>True if milestone completed successfully, false otherwise.</returns>
    private async Task<bool> CompleteUseMilestoneAsync(
        Guid contractInstanceId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.CompleteUseMilestoneAsync");
        try
        {
            var request = new CompleteMilestoneRequest
            {
                ContractId = contractInstanceId,
                MilestoneCode = _configuration.UseMilestoneCode
            };

            var response = await _contractClient.CompleteMilestoneAsync(request, cancellationToken);

            // Check if milestone was actually completed by checking its status
            return response.Milestone.Status == MilestoneStatus.Completed;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "API error completing milestone for contract {ContractId}",
                contractInstanceId);
            await _messageBus.TryPublishErrorAsync(
                "item",
                "CompleteUseMilestone",
                "MilestoneCompletionFailed",
                ex.Message,
                dependency: "contract",
                endpoint: "contract/complete-milestone",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return false;
        }
    }

    /// <summary>
    /// Consumes the item after successful use (decrements quantity or destroys).
    /// </summary>
    /// <returns>Tuple of (wasConsumed, remainingQuantity or null if destroyed).</returns>
    private async Task<(bool Consumed, double? RemainingQuantity)> ConsumeItemAsync(
        Guid instanceId,
        ItemInstanceModel instance,
        ItemTemplateModel template,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.ConsumeItemAsync");
        // For now, always consume on use (MVP behavior)
        // Future: per-template configuration for consumable vs reusable items

        if (instance.Quantity <= 1)
        {
            // Last item - destroy the instance
            if (instance.ContainerId.HasValue)
            {
                await RemoveFromListAsync(
                    _instanceStringStore,
                    $"{INST_CONTAINER_INDEX}{instance.ContainerId.Value}",
                    instanceId.ToString(),
                    cancellationToken);
            }
            await RemoveFromListAsync(
                _instanceStringStore,
                $"{INST_TEMPLATE_INDEX}{instance.TemplateId}",
                instanceId.ToString(),
                cancellationToken);
            await _instanceStore.DeleteAsync($"{INST_PREFIX}{instanceId}", cancellationToken);
            await InvalidateInstanceCacheAsync(instanceId.ToString(), cancellationToken);

            // Record destroy for batch event
            _instanceEventBatcher.AddDestroyed(new ItemInstanceBatchDestroyedEntry
            {
                InstanceId = instanceId,
                TemplateId = instance.TemplateId,
                ContainerId = instance.ContainerId,
                RealmId = instance.RealmId,
                Quantity = instance.Quantity,
                OriginType = instance.OriginType,
                CreatedAt = instance.CreatedAt,
                ModifiedAt = DateTimeOffset.UtcNow
            });

            return (true, null);
        }
        else
        {
            // Decrement quantity
            instance.Quantity -= 1;
            instance.ModifiedAt = DateTimeOffset.UtcNow;
            await _instanceStore.SaveAsync($"{INST_PREFIX}{instanceId}", instance, cancellationToken: cancellationToken);
            await InvalidateInstanceCacheAsync(instanceId.ToString(), cancellationToken);

            // Record modification for batch event
            _instanceEventBatcher.AddModified(instanceId, new ItemInstanceBatchModifiedEntry
            {
                InstanceId = instanceId,
                TemplateId = instance.TemplateId,
                ContainerId = instance.ContainerId,
                RealmId = instance.RealmId,
                Quantity = instance.Quantity,
                OriginType = instance.OriginType,
                CreatedAt = instance.CreatedAt,
                ModifiedAt = instance.ModifiedAt
            });

            return (true, instance.Quantity);
        }
    }

    /// <summary>
    /// Records a successful item use for batched event publishing.
    /// Events are batched by templateId+userId within the deduplication window.
    /// </summary>
    private async Task RecordUseSuccessAsync(
        Guid instanceId,
        Guid templateId,
        string templateCode,
        Guid userId,
        EntityType userType,
        Guid? targetId,
        EntityType? targetType,
        bool consumed,
        Guid contractInstanceId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.RecordUseSuccessAsync");
        var batchKey = $"{templateId}:{userId}";
        var now = DateTimeOffset.UtcNow;
        var windowSeconds = _configuration.UseEventDeduplicationWindowSeconds;
        var maxBatchSize = _configuration.UseEventBatchMaxSize;

        var record = new ItemUseRecord
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            TemplateCode = templateCode,
            UserId = userId,
            UserType = userType,
            TargetId = targetId,
            TargetType = targetType,
            UsedAt = now,
            Consumed = consumed,
            ContractInstanceId = contractInstanceId
        };

        // Pre-flush: if an expired batch exists for this key, publish it before adding new records.
        // Without this, AddOrUpdate would silently discard the old batch's records when replacing
        // it with a fresh one on window expiry (IMPLEMENTATION TENETS).
        if (_useBatches.TryGetValue(batchKey, out var existingBatch)
            && (now - existingBatch.WindowStart).TotalSeconds >= windowSeconds)
        {
            if (_useBatches.TryRemove(batchKey, out var expiredBatch))
            {
                await PublishItemUsedEventAsync(expiredBatch, cancellationToken);
            }
        }

        // Get or create batch (pre-flush already removed expired batches)
        var batch = _useBatches.GetOrAdd(batchKey, _ => new ItemUseBatchState());
        var totalCount = batch.AddRecord(record);

        // Publish if batch is full
        if (totalCount >= maxBatchSize)
        {
            // Try to remove and publish (only one thread will succeed)
            if (_useBatches.TryRemove(batchKey, out var publishBatch))
            {
                await PublishItemUsedEventAsync(publishBatch, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Records a failed item use for batched event publishing.
    /// Events are batched by templateId+userId within the deduplication window.
    /// </summary>
    private async Task RecordUseFailureAsync(
        Guid instanceId,
        Guid templateId,
        string templateCode,
        Guid userId,
        EntityType userType,
        string reason,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.RecordUseFailureAsync");
        var batchKey = $"{templateId}:{userId}";
        var now = DateTimeOffset.UtcNow;
        var windowSeconds = _configuration.UseEventDeduplicationWindowSeconds;
        var maxBatchSize = _configuration.UseEventBatchMaxSize;

        var record = new ItemUseFailureRecord
        {
            InstanceId = instanceId,
            TemplateId = templateId,
            TemplateCode = templateCode,
            UserId = userId,
            UserType = userType,
            FailedAt = now,
            Reason = reason
        };

        // Pre-flush: if an expired batch exists for this key, publish it before adding new records.
        // Without this, AddOrUpdate would silently discard the old batch's records when replacing
        // it with a fresh one on window expiry (IMPLEMENTATION TENETS).
        if (_failureBatches.TryGetValue(batchKey, out var existingBatch)
            && (now - existingBatch.WindowStart).TotalSeconds >= windowSeconds)
        {
            if (_failureBatches.TryRemove(batchKey, out var expiredBatch))
            {
                await PublishItemUseFailedEventAsync(expiredBatch, cancellationToken);
            }
        }

        // Get or create batch (pre-flush already removed expired batches)
        var batch = _failureBatches.GetOrAdd(batchKey, _ => new ItemUseFailureBatchState());
        var totalCount = batch.AddRecord(record);

        // Publish if batch is full
        if (totalCount >= maxBatchSize)
        {
            // Try to remove and publish (only one thread will succeed)
            if (_failureBatches.TryRemove(batchKey, out var publishBatch))
            {
                await PublishItemUseFailedEventAsync(publishBatch, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Publishes a batched ItemUsedEvent.
    /// </summary>
    private async Task PublishItemUsedEventAsync(
        ItemUseBatchState batch,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.PublishItemUsedEventAsync");
        var (records, totalCount) = batch.GetSnapshot();
        if (records.Count == 0) return;

        var evt = new ItemUsedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            BatchId = batch.BatchId,
            Uses = records,
            TotalCount = totalCount
        };

        await _messageBus.PublishItemUsedAsync(evt, cancellationToken);
        _logger.LogDebug(
            "Published batched item.used event: batchId={BatchId}, records={Count}, total={Total}",
            batch.BatchId, records.Count, totalCount);
    }

    /// <summary>
    /// Publishes a batched ItemUseFailedEvent.
    /// </summary>
    private async Task PublishItemUseFailedEventAsync(
        ItemUseFailureBatchState batch,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.PublishItemUseFailedEventAsync");
        var (records, totalCount) = batch.GetSnapshot();
        if (records.Count == 0) return;

        var evt = new ItemUseFailedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            BatchId = batch.BatchId,
            Failures = records,
            TotalCount = totalCount
        };

        await _messageBus.PublishItemUseFailedAsync(evt, cancellationToken);
        _logger.LogDebug(
            "Published batched item.use-failed event: batchId={BatchId}, records={Count}, total={Total}",
            batch.BatchId, records.Count, totalCount);
    }
    #region Helper Methods

    /// <summary>
    /// Resolves an item template by ID (preferred) or by code+gameId composite key.
    /// </summary>
    /// <param name="templateId">Template ID to look up directly.</param>
    /// <param name="code">Template code for composite key lookup (requires gameId).</param>
    /// <param name="gameId">Game ID for composite key lookup (requires code).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved template, or null if not found.</returns>
    private async Task<ItemTemplateModel?> ResolveTemplateAsync(string? templateId, string? code, string? gameId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.ResolveTemplateAsync");
        if (!string.IsNullOrEmpty(templateId))
        {
            return await GetTemplateWithCacheAsync(templateId, ct);
        }

        if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(gameId))
        {
            var id = await _templateStringStore.GetAsync($"{TPL_CODE_INDEX}{gameId}:{code}", ct);
            if (!string.IsNullOrEmpty(id))
            {
                return await GetTemplateWithCacheAsync(id, ct);
            }
        }

        return null;
    }

    /// <summary>
    /// Add a value to a JSON-serialized list in a state store with optimistic concurrency.
    /// Delegates to shared <see cref="StateStoreExtensions.AddToStringListAsync"/>.
    /// </summary>
    private async Task AddToListAsync(IStateStore<string> stringStore, string key, string value, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.AddToListAsync");
        await stringStore.AddToStringListAsync(key, value, _configuration.ListOperationMaxRetries, _logger, ct);
    }

    /// <summary>
    /// Remove a value from a JSON-serialized list in a state store with optimistic concurrency.
    /// Delegates to shared <see cref="StateStoreExtensions.RemoveFromStringListAsync"/>.
    /// </summary>
    private async Task RemoveFromListAsync(IStateStore<string> stringStore, string key, string value, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.RemoveFromListAsync");
        await stringStore.RemoveFromStringListAsync(key, value, _configuration.ListOperationMaxRetries, _logger, ct);
    }

    #endregion

    #region Cache Methods

    /// <summary>
    /// Get template with Redis cache read-through. Falls back to MySQL persistent store on cache miss.
    /// </summary>
    private async Task<ItemTemplateModel?> GetTemplateWithCacheAsync(string templateId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.GetTemplateWithCacheAsync");
        var cacheKey = $"{TPL_PREFIX}{templateId}";

        // Try cache first
        var cached = await _templateCacheStore.GetAsync(cacheKey, ct);
        if (cached is not null) return cached;

        // Fallback to persistent store
        var model = await _templateStore.GetAsync(cacheKey, ct);
        if (model is null) return null;

        // Populate cache
        await _templateCacheStore.SaveAsync(cacheKey, model,
            new StateOptions { Ttl = _configuration.TemplateCacheTtlSeconds }, ct);
        return model;
    }

    /// <summary>
    /// Populate template cache after a write operation.
    /// </summary>
    private async Task PopulateTemplateCacheAsync(string templateId, ItemTemplateModel model, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.PopulateTemplateCacheAsync");
        await _templateCacheStore.SaveAsync($"{TPL_PREFIX}{templateId}", model,
            new StateOptions { Ttl = _configuration.TemplateCacheTtlSeconds }, ct);
    }

    /// <summary>
    /// Invalidate template cache after a write/update operation.
    /// </summary>
    private async Task InvalidateTemplateCacheAsync(string templateId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.InvalidateTemplateCacheAsync");
        await _templateCacheStore.DeleteAsync($"{TPL_PREFIX}{templateId}", ct);
    }

    /// <summary>
    /// Get instance with Redis cache read-through. Falls back to MySQL persistent store on cache miss.
    /// </summary>
    private async Task<ItemInstanceModel?> GetInstanceWithCacheAsync(string instanceId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.GetInstanceWithCacheAsync");
        var cacheKey = $"{INST_PREFIX}{instanceId}";

        // Try cache first
        var cached = await _instanceCacheStore.GetAsync(cacheKey, ct);
        if (cached is not null) return cached;

        // Fallback to persistent store
        var model = await _instanceStore.GetAsync(cacheKey, ct);
        if (model is null) return null;

        // Populate cache
        await _instanceCacheStore.SaveAsync(cacheKey, model,
            new StateOptions { Ttl = _configuration.InstanceCacheTtlSeconds }, ct);
        return model;
    }

    /// <summary>
    /// Bulk get instances with Redis cache read-through. Falls back to MySQL for cache misses.
    /// Returns a dictionary keyed by instance ID (without prefix).
    /// </summary>
    private async Task<Dictionary<string, ItemInstanceModel>> GetInstancesBulkWithCacheAsync(
        IEnumerable<string> instanceIds,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.GetInstancesBulkWithCacheAsync");
        var idList = instanceIds.ToList();
        if (idList.Count == 0) return new Dictionary<string, ItemInstanceModel>();

        // Build cache keys
        var cacheKeys = idList.Select(id => $"{INST_PREFIX}{id}").ToList();

        // Try cache first (single bulk call)
        var cachedItems = await _instanceCacheStore.GetBulkAsync(cacheKeys, ct);

        // Build result from cache hits
        var result = new Dictionary<string, ItemInstanceModel>();
        var cacheMissKeys = new List<string>();

        foreach (var key in cacheKeys)
        {
            if (cachedItems.TryGetValue(key, out var model))
            {
                // Extract instance ID from key (format: "inst:{instanceId}")
                var instanceId = key.Substring(INST_PREFIX.Length);
                result[instanceId] = model;
            }
            else
            {
                cacheMissKeys.Add(key);
            }
        }

        // If all items were in cache, we're done
        if (cacheMissKeys.Count == 0) return result;

        // Fetch cache misses from persistent store (single bulk call)
        var persistentItems = await _instanceStore.GetBulkAsync(cacheMissKeys, ct);

        // Add persistent store results and populate cache
        if (persistentItems.Count > 0)
        {
            var cachePopulation = new List<KeyValuePair<string, ItemInstanceModel>>();

            foreach (var (key, model) in persistentItems)
            {
                var instanceId = key.Substring(INST_PREFIX.Length);
                result[instanceId] = model;
                cachePopulation.Add(new KeyValuePair<string, ItemInstanceModel>(key, model));
            }

            // Bulk populate cache for all fetched items
            await _instanceCacheStore.SaveBulkAsync(cachePopulation,
                new StateOptions { Ttl = _configuration.InstanceCacheTtlSeconds }, ct);
        }

        return result;
    }

    /// <summary>
    /// Populate instance cache after a write operation.
    /// </summary>
    private async Task PopulateInstanceCacheAsync(string instanceId, ItemInstanceModel model, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.PopulateInstanceCacheAsync");
        await _instanceCacheStore.SaveAsync($"{INST_PREFIX}{instanceId}", model,
            new StateOptions { Ttl = _configuration.InstanceCacheTtlSeconds }, ct);
    }

    /// <summary>
    /// Invalidate instance cache after a write/delete operation.
    /// </summary>
    private async Task InvalidateInstanceCacheAsync(string instanceId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.item", "ItemService.InvalidateInstanceCacheAsync");
        await _instanceCacheStore.DeleteAsync($"{INST_PREFIX}{instanceId}", ct);
    }

    #endregion

    #region Mapping Methods

    /// <summary>
    /// Maps an internal template storage model to its API response representation.
    /// </summary>
    /// <param name="model">The internal template model.</param>
    /// <returns>The API response model.</returns>
    private static ItemTemplateResponse MapTemplateToResponse(ItemTemplateModel model)
    {
        return new ItemTemplateResponse
        {
            TemplateId = model.TemplateId,
            Code = model.Code,
            GameId = model.GameId,
            Name = model.Name,
            Description = model.Description,
            Category = model.Category,
            Subcategory = model.Subcategory,
            Tags = model.Tags,
            Rarity = model.Rarity,
            QuantityModel = model.QuantityModel,
            MaxStackSize = model.MaxStackSize,
            UnitOfMeasure = model.UnitOfMeasure,
            WeightPrecision = model.WeightPrecision,
            Weight = model.Weight,
            Volume = model.Volume,
            GridWidth = model.GridWidth,
            GridHeight = model.GridHeight,
            CanRotate = model.CanRotate,
            BaseValue = model.BaseValue,
            Tradeable = model.Tradeable,
            Destroyable = model.Destroyable,
            SoulboundType = model.SoulboundType,
            HasDurability = model.HasDurability,
            MaxDurability = model.MaxDurability,
            Scope = model.Scope,
            AvailableRealms = model.AvailableRealms,
            Stats = model.Stats is not null ? BannouJson.Deserialize<object>(model.Stats) : null,
            Effects = model.Effects is not null ? BannouJson.Deserialize<object>(model.Effects) : null,
            Requirements = model.Requirements is not null ? BannouJson.Deserialize<object>(model.Requirements) : null,
            Display = model.Display is not null ? BannouJson.Deserialize<object>(model.Display) : null,
            Metadata = model.Metadata is not null ? BannouJson.Deserialize<object>(model.Metadata) : null,
            UseBehaviorContractTemplateId = model.UseBehaviorContractTemplateId,
            CanUseBehaviorContractTemplateId = model.CanUseBehaviorContractTemplateId,
            OnUseFailedBehaviorContractTemplateId = model.OnUseFailedBehaviorContractTemplateId,
            ItemUseBehavior = model.ItemUseBehavior,
            CanUseBehavior = model.CanUseBehavior,
            IsActive = model.IsActive,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            MigrationTargetId = model.MigrationTargetId,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt
        };
    }

    /// <summary>
    /// Maps an internal instance storage model to its API response representation.
    /// </summary>
    /// <param name="model">The internal instance model.</param>
    /// <returns>The API response model.</returns>
    private static ItemInstanceResponse MapInstanceToResponse(ItemInstanceModel model)
    {
        return new ItemInstanceResponse
        {
            InstanceId = model.InstanceId,
            TemplateId = model.TemplateId,
            ContainerId = model.ContainerId,
            RealmId = model.RealmId,
            Quantity = model.Quantity,
            SlotIndex = model.SlotIndex,
            SlotX = model.SlotX,
            SlotY = model.SlotY,
            Rotated = model.Rotated,
            CurrentDurability = model.CurrentDurability,
            BoundToId = model.BoundToId,
            BoundAt = model.BoundAt,
            CustomStats = model.CustomStats is not null ? BannouJson.Deserialize<object>(model.CustomStats) : null,
            CustomName = model.CustomName,
            InstanceMetadata = model.InstanceMetadata is not null ? BannouJson.Deserialize<object>(model.InstanceMetadata) : null,
            OriginType = model.OriginType,
            OriginId = model.OriginId,
            ContractInstanceId = model.ContractInstanceId,
            ContractBindingType = model.ContractBindingType,
            CreatedAt = model.CreatedAt,
            ModifiedAt = model.ModifiedAt
        };
    }

    #endregion
}
