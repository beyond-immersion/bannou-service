using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Helpers;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Contract;

// =============================================================================
// ContractService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by ContractService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (ContractService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IContractService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (ContractService.Helpers.cs):
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
/// Private and internal helper methods for ContractService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class ContractService
{
    // Move private/internal helper methods here from ContractService.cs
    /// <summary>
    /// Executes a list of prebound APIs in batches of configured size.
    /// APIs within each batch execute concurrently; batches execute sequentially.
    /// </summary>
    private async Task<int> ExecutePreboundApisBatchedAsync(
        ContractInstanceModel contract,
        List<PreboundApiModel> apis,
        string trigger,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.contract", "ContractService.ExecutePreboundApisBatchedAsync");
        var executed = 0;
        var batchSize = _configuration.PreboundApiBatchSize;

        for (var i = 0; i < apis.Count; i += batchSize)
        {
            var batch = apis.Skip(i).Take(batchSize);
            var tasks = batch.Select(api => ExecutePreboundApiAsync(contract, api, trigger, ct));
            await Task.WhenAll(tasks);
            executed += Math.Min(batchSize, apis.Count - i);
        }

        return executed;
    }

    private async Task ExecutePreboundApiAsync(
        ContractInstanceModel contract,
        PreboundApiModel api,
        string trigger,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.contract", "ContractService.ExecutePreboundApiAsync");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_configuration.PreboundApiTimeoutMs);
        var effectiveCt = timeoutCts.Token;

        try
        {
            _logger.LogInformation("Executing prebound API: {Service}{Endpoint} for contract {ContractId}",
                api.ServiceName, api.Endpoint, contract.ContractId);

            // Build context dictionary for template substitution
            var context = BuildContractContext(contract);

            // Build PreboundApi for ServiceNavigator
            var apiDefinition = new PreboundApi
            {
                ServiceName = api.ServiceName,
                Endpoint = api.Endpoint,
                PayloadTemplate = api.PayloadTemplate,
                Description = api.Description,
                ExecutionMode = api.ExecutionMode
            };

            // Execute via ServiceNavigator with configured timeout
            var result = await _navigator.ExecutePreboundApiAsync(apiDefinition, context, effectiveCt);

            if (!result.SubstitutionSucceeded)
            {
                _logger.LogWarning("Template substitution failed for prebound API {Service}{Endpoint}: {Error}",
                    api.ServiceName, api.Endpoint, result.SubstitutionError);

                await _messageBus.PublishContractPreboundApiFailedAsync(new ContractPreboundApiFailedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ContractId = contract.ContractId,
                    Trigger = trigger,
                    ServiceName = api.ServiceName,
                    Endpoint = api.Endpoint,
                    ErrorMessage = $"Template substitution failed: {result.SubstitutionError}",
                    StatusCode = null
                });
                return;
            }

            // Transform response if transformation rules are configured
            if (api.ResponseTransformation != null && result.Result != null)
            {
                var transformResult = ResponseTransformer.Transform(
                    result.Result.StatusCode,
                    result.Result.ResponseBody,
                    api.ResponseTransformation);

                if (!transformResult.IsSuccess)
                {
                    _logger.LogWarning("Prebound API response transformation indicates failure for {Service}{Endpoint}: {Outcome} - {Description}",
                        api.ServiceName, api.Endpoint, transformResult.Outcome, transformResult.MatchedRuleDescription);

                    await _messageBus.PublishContractPreboundApiValidationFailedAsync(new ContractPreboundApiValidationFailedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        ContractId = contract.ContractId,
                        Trigger = trigger,
                        ServiceName = api.ServiceName,
                        Endpoint = api.Endpoint,
                        StatusCode = transformResult.StatusCode,
                        ValidationOutcome = transformResult.Outcome == TransformationOutcome.TransientFailure
                            ? ValidationOutcome.TransientFailure
                            : ValidationOutcome.PermanentFailure,
                        FailureReason = transformResult.MatchedRuleDescription
                    });
                    return;
                }
            }

            // Publish execution event
            await _messageBus.PublishContractPreboundApiExecutedAsync(new ContractPreboundApiExecutedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ContractId = contract.ContractId,
                Trigger = trigger,
                ServiceName = api.ServiceName,
                Endpoint = api.Endpoint,
                StatusCode = result.Result?.StatusCode ?? 0
            });
        }
        catch (ApiException ex)
        {
            // Expected API error from downstream service - log at Warning level
            _logger.LogWarning(ex, "Prebound API returned error: {Service}{Endpoint} - {StatusCode}",
                api.ServiceName, api.Endpoint, ex.StatusCode);

            await _messageBus.PublishContractPreboundApiFailedAsync(new ContractPreboundApiFailedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ContractId = contract.ContractId,
                Trigger = trigger,
                ServiceName = api.ServiceName,
                Endpoint = api.Endpoint,
                ErrorMessage = ex.Message,
                StatusCode = ex.StatusCode
            });
        }
        catch (Exception ex)
        {
            // Unexpected error - log at Error level
            _logger.LogError(ex, "Failed to execute prebound API: {Service}{Endpoint}",
                api.ServiceName, api.Endpoint);

            await _messageBus.PublishContractPreboundApiFailedAsync(new ContractPreboundApiFailedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                ContractId = contract.ContractId,
                Trigger = trigger,
                ServiceName = api.ServiceName,
                Endpoint = api.Endpoint,
                ErrorMessage = ex.Message,
                StatusCode = null
            });
        }
    }
    /// Processes an overdue milestone with lazy deadline enforcement.
    /// Returns true if the milestone was processed (and contract may have been modified).
    /// </summary>
    private async Task<bool> ProcessOverdueMilestoneAsync(
        ContractInstanceModel contract,
        MilestoneInstanceModel milestone,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.contract", "ContractService.ProcessOverdueMilestoneAsync");

        // Only process active milestones with deadlines
        if (milestone.Status != MilestoneStatus.Active || !milestone.ActivatedAt.HasValue)
            return false;

        if (string.IsNullOrEmpty(milestone.Deadline))
            return false;

        var duration = ParseIsoDuration(milestone.Deadline);
        if (!duration.HasValue)
            return false;

        var absoluteDeadline = milestone.ActivatedAt.Value.Add(duration.Value);
        if (absoluteDeadline >= DateTimeOffset.UtcNow)
            return false; // Not overdue

        // Milestone is overdue - determine behavior
        if (milestone.Required)
        {
            // Required milestones always fail and trigger breach
            milestone.Status = MilestoneStatus.Failed;
            milestone.FailedAt = DateTimeOffset.UtcNow;
            contract.UpdatedAt = DateTimeOffset.UtcNow;

            await ReportBreachInternalAsync(
                contract,
                milestone.Code,
                BreachType.MilestoneDeadline,
                $"Required milestone '{milestone.Code}' deadline expired (deadline was {absoluteDeadline:O})",
                cancellationToken);

            return true;
        }
        else
        {
            // Optional milestones use DeadlineBehavior
            var behavior = milestone.DeadlineBehavior ?? MilestoneDeadlineBehavior.Skip;

            switch (behavior)
            {
                case MilestoneDeadlineBehavior.Skip:
                    // Skip to next milestone without breach
                    milestone.Status = MilestoneStatus.Skipped;
                    contract.UpdatedAt = DateTimeOffset.UtcNow;

                    // Activate next milestone if any
                    if (contract.Milestones != null)
                    {
                        var currentIndex = contract.Milestones.FindIndex(m => m.Code == milestone.Code);
                        if (currentIndex >= 0 && currentIndex + 1 < contract.Milestones.Count)
                        {
                            contract.Milestones[currentIndex + 1].Status = MilestoneStatus.Active;
                            contract.Milestones[currentIndex + 1].ActivatedAt = DateTimeOffset.UtcNow;
                            contract.CurrentMilestoneIndex = currentIndex + 1;
                        }
                    }
                    return true;

                case MilestoneDeadlineBehavior.Warn:
                    // Log warning but don't fail - milestone stays active
                    _logger.LogWarning(
                        "Optional milestone {MilestoneCode} in contract {ContractId} is overdue (deadline was {Deadline})",
                        milestone.Code, contract.ContractId, absoluteDeadline);
                    // Don't modify state - return false so caller doesn't re-save
                    return false;

                case MilestoneDeadlineBehavior.Breach:
                    // Optional milestone explicitly configured to trigger breach
                    milestone.Status = MilestoneStatus.Failed;
                    milestone.FailedAt = DateTimeOffset.UtcNow;
                    contract.UpdatedAt = DateTimeOffset.UtcNow;

                    await ReportBreachInternalAsync(
                        contract,
                        milestone.Code,
                        BreachType.MilestoneDeadline,
                        $"Optional milestone '{milestone.Code}' deadline expired (configured for breach, deadline was {absoluteDeadline:O})",
                        cancellationToken);

                    return true;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Reports a breach internally without requiring an external request.
    /// Used for automatic deadline breaches and other system-initiated breaches.
    /// </summary>
    private async Task ReportBreachInternalAsync(
        ContractInstanceModel contract,
        string breachedMilestoneCode,
        BreachType breachType,
        string description,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.contract", "ContractService.ReportBreachInternalAsync");

        var breachId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var breach = new BreachModel
        {
            BreachId = breachId,
            ContractId = contract.ContractId,
            BreachingEntityId = null, // System-initiated breach has no specific entity
            BreachingEntityType = null, // System-initiated breach has no specific entity
            BreachType = breachType,
            BreachedTermOrMilestone = breachedMilestoneCode,
            Description = description,
            Status = BreachStatus.Detected,
            DetectedAt = now,
            CureDeadline = null
        };

        // Save breach record
        var breachKey = BuildBreachKey(breachId);
        await _breachStore
            .SaveAsync(breachKey, breach, cancellationToken: cancellationToken);

        // Add breach to contract
        contract.BreachIds ??= new List<Guid>();
        contract.BreachIds.Add(breachId);

        // Publish breach detected event (reuse existing helper)
        await PublishBreachDetectedEventAsync(contract, breach, cancellationToken);

        _logger.LogInformation(
            "Internal breach reported for contract {ContractId}: {BreachType} - {Description}",
            contract.ContractId, breachType, description);

        // Check breach threshold for auto-termination
        await CheckBreachThresholdAsync(contract, cancellationToken);
    }

    /// <summary>
    /// Checks if the contract has exceeded its breach threshold and auto-terminates if so.
    /// </summary>
    private async Task CheckBreachThresholdAsync(
        ContractInstanceModel contract,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.contract", "ContractService.CheckBreachThresholdAsync");

        var threshold = contract.Terms?.BreachThreshold ?? 0;
        if (threshold <= 0) return;

        // Count active breaches (Detected or CurePeriod)
        var activeBreachCount = 0;
        foreach (var breachId in contract.BreachIds ?? new List<Guid>())
        {
            var breach = await _breachStore
                .GetAsync(BuildBreachKey(breachId), cancellationToken);

            if (breach != null &&
                (breach.Status == BreachStatus.Detected || breach.Status == BreachStatus.CurePeriod))
            {
                activeBreachCount++;
            }
        }

        if (activeBreachCount >= threshold)
        {
            await TerminateContractDueToBreachThresholdAsync(
                contract, activeBreachCount, threshold, cancellationToken);
        }
    }

    /// <summary>
    /// Terminates a contract due to breach threshold being exceeded.
    /// </summary>
    private async Task TerminateContractDueToBreachThresholdAsync(
        ContractInstanceModel contract,
        int breachCount,
        int threshold,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.contract", "ContractService.TerminateContractDueToBreachThresholdAsync");

        var reason = $"Breach threshold exceeded ({breachCount}/{threshold})";
        var previousStatus = contract.Status;

        contract.Status = ContractStatus.Terminated;
        contract.TerminatedAt = DateTimeOffset.UtcNow;
        contract.UpdatedAt = DateTimeOffset.UtcNow;

        // Save the updated contract
        var instanceKey = BuildInstanceKey(contract.ContractId);
        await _instanceStore
            .SaveAsync(instanceKey, contract, cancellationToken: cancellationToken);

        // Update status index
        await RemoveFromListAsync(
            BuildStatusIndexKey(previousStatus.ToString().ToLowerInvariant()),
            contract.ContractId.ToString(),
            cancellationToken);
        await AddToListAsync(
            BuildStatusIndexKey("terminated"),
            contract.ContractId.ToString(),
            cancellationToken);

        // Publish termination event (system-initiated, breach-related)
        await PublishContractTerminatedEventAsync(
            contract,
            terminatedById: null, // System-initiated termination has no specific entity
            terminatedByType: null,
            reason: reason,
            wasBreachRelated: true,
            cancellationToken);

        _logger.LogInformation(
            "Contract {ContractId} auto-terminated due to breach threshold: {Reason}",
            contract.ContractId, reason);
    }
    private async Task PublishTemplateCreatedEventAsync(ContractTemplateModel model, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.contract", "ContractService.PublishTemplateCreatedEventAsync");
        await _messageBus.PublishContractTemplateCreatedAsync(new ContractTemplateCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TemplateId = model.TemplateId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            RealmId = model.RealmId,
            MinParties = model.MinParties,
            MaxParties = model.MaxParties,
            DefaultEnforcementMode = model.DefaultEnforcementMode,
            Transferable = model.Transferable,
            IsActive = model.IsActive,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            CreatedAt = model.CreatedAt
        });
    }
    private async Task PublishTemplateUpdatedEventAsync(
        ContractTemplateModel model, List<string> changedFields, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.contract", "ContractService.PublishTemplateUpdatedEventAsync");
        await _messageBus.PublishContractTemplateUpdatedAsync(new ContractTemplateUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TemplateId = model.TemplateId,
            Code = model.Code,
            Name = model.Name,
            Description = model.Description,
            RealmId = model.RealmId,
            MinParties = model.MinParties,
            MaxParties = model.MaxParties,
            DefaultEnforcementMode = model.DefaultEnforcementMode,
            Transferable = model.Transferable,
            IsActive = model.IsActive,
            IsDeprecated = model.IsDeprecated,
            DeprecatedAt = model.DeprecatedAt,
            DeprecationReason = model.DeprecationReason,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? DateTimeOffset.UtcNow,
            ChangedFields = changedFields
        });
    }
    private async Task PublishInstanceUpdatedEventAsync(
        ContractInstanceModel model, List<string> changedFields, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.contract", "ContractService.PublishInstanceUpdatedEventAsync");

        await _messageBus.PublishContractInstanceUpdatedAsync(new ContractInstanceUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
            TemplateId = model.TemplateId,
            TemplateCode = model.TemplateCode,
            Status = model.Status,
            ProposedAt = model.ProposedAt,
            AcceptedAt = model.AcceptedAt,
            EffectiveFrom = model.EffectiveFrom,
            EffectiveUntil = model.EffectiveUntil,
            TerminatedAt = model.TerminatedAt,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? model.CreatedAt,
            ChangedFields = changedFields
        });
    }
    private async Task PublishInstanceDeletedEventAsync(
        ContractInstanceModel model, string reason, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.contract", "ContractService.PublishInstanceDeletedEventAsync");

        await _messageBus.PublishContractInstanceDeletedAsync(new ContractInstanceDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
            TemplateId = model.TemplateId,
            TemplateCode = model.TemplateCode,
            Status = model.Status,
            ProposedAt = model.ProposedAt,
            AcceptedAt = model.AcceptedAt,
            EffectiveFrom = model.EffectiveFrom,
            EffectiveUntil = model.EffectiveUntil,
            TerminatedAt = model.TerminatedAt,
            CreatedAt = model.CreatedAt,
            UpdatedAt = model.UpdatedAt ?? model.CreatedAt,
            DeletedReason = reason
        });
    }
    /// <summary>
    /// Publishes a contract expired event when a contract reaches its effectiveUntil date
    /// or its consent window expires.
    /// </summary>
    private async Task PublishContractExpiredEventAsync(ContractInstanceModel model, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.contract", "ContractService.PublishContractExpiredEventAsync");

        await _messageBus.PublishContractExpiredAsync(new ContractExpiredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
            TemplateCode = model.TemplateCode,
            EffectiveUntil = model.EffectiveUntil ?? DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Publishes a payment due event for a contract with recurring or one-time payment terms.
    /// </summary>
    internal async Task PublishPaymentDueEventAsync(ContractInstanceModel model, int paymentNumber, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.contract", "ContractService.PublishPaymentDueEventAsync");

        var parties = model.Parties?.Select(p => new PartyInfo
        {
            EntityId = p.EntityId,
            EntityType = p.EntityType,
            Role = p.Role
        }).ToList();

        await _messageBus.PublishContractPaymentDueAsync(new ContractPaymentDueEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            ContractId = model.ContractId,
            TemplateCode = model.TemplateCode,
            PaymentSchedule = model.Terms?.PaymentSchedule ?? PaymentSchedule.OneTime,
            PaymentFrequency = model.Terms?.PaymentFrequency,
            PaymentNumber = paymentNumber,
            Parties = parties
        });
    }
}
