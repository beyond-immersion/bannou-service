// =============================================================================
// Cognition Builder
// Builds cognition pipelines from templates with overrides applied.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.Bannou.Behavior.Cognition;

/// <summary>
/// Builds cognition pipelines from templates with optional character-specific overrides.
/// </summary>
/// <remarks>
/// <para>
/// The builder applies overrides in order:
/// </para>
/// <list type="number">
/// <item>Validate overrides against template</item>
/// <item>Apply parameter overrides (most common)</item>
/// <item>Apply disable overrides (conditional or unconditional)</item>
/// <item>Apply add handler overrides</item>
/// <item>Apply replace handler overrides (rare)</item>
/// <item>Apply reorder overrides</item>
/// </list>
/// </remarks>
public sealed class CognitionBuilder : ICognitionBuilder
{
    private readonly ICognitionTemplateRegistry _registry;
    private readonly IActionHandlerRegistry? _handlerRegistry;
    private readonly ILogger<CognitionBuilder>? _logger;
    private readonly ITelemetryProvider? _telemetryProvider;

    /// <summary>
    /// Creates a new cognition builder.
    /// </summary>
    /// <param name="registry">Template registry.</param>
    /// <param name="handlerRegistry">Optional action handler registry for pipeline execution.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="telemetryProvider">Optional telemetry provider for span instrumentation.</param>
    public CognitionBuilder(
        ICognitionTemplateRegistry registry,
        IActionHandlerRegistry? handlerRegistry = null,
        ILogger<CognitionBuilder>? logger = null,
        ITelemetryProvider? telemetryProvider = null)
    {
        _registry = registry;
        _handlerRegistry = handlerRegistry;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public ICognitionPipeline? Build(string templateId, CognitionOverrides? overrides = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        var template = _registry.GetTemplate(templateId);
        if (template == null)
        {
            _logger?.LogWarning("Cognition template not found: {TemplateId}", templateId);
            return null;
        }

        return Build(template, overrides);
    }

    /// <inheritdoc/>
    public ICognitionPipeline Build(CognitionTemplate template, CognitionOverrides? overrides = null)
    {

        // Clone stages for modification
        var mutableStages = CloneStages(template.Stages);

        // Apply overrides if present
        if (overrides != null && overrides.Overrides.Count > 0)
        {
            foreach (var @override in overrides.Overrides)
            {
                ApplyOverride(mutableStages, @override);
            }
        }

        // Build executable stages
        var executableStages = mutableStages
            .Select(kvp => CreateStage(kvp.Key, kvp.Value))
            .ToList();

        _logger?.LogDebug(
            "Built cognition pipeline from template {TemplateId} with {OverrideCount} overrides",
            template.Id,
            overrides?.Overrides.Count ?? 0);

        return new CognitionPipeline(template.Id, executableStages, _handlerRegistry, _telemetryProvider);
    }

    /// <inheritdoc/>
    public CognitionOverrideValidation ValidateOverrides(string templateId, CognitionOverrides overrides)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        var template = _registry.GetTemplate(templateId);
        if (template == null)
        {
            return CognitionOverrideValidation.Invalid($"Template not found: {templateId}");
        }

        var errors = new List<string>();
        var warnings = new List<string>();

        // Build lookup for validation
        var stageHandlers = template.Stages.ToDictionary(
            s => s.Name,
            s => s.Handlers.Select(h => h.Id).ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var @override in overrides.Overrides)
        {
            // Check stage exists
            if (!stageHandlers.ContainsKey(@override.Stage))
            {
                errors.Add($"Stage not found: {@override.Stage}");
                continue;
            }

            // Validate handler references
            switch (@override)
            {
                case ParameterOverride po:
                    if (!stageHandlers[@override.Stage].Contains(po.HandlerId))
                    {
                        errors.Add($"Handler not found in stage '{@override.Stage}': {po.HandlerId}");
                    }
                    break;

                case DisableHandlerOverride dho:
                    if (!stageHandlers[@override.Stage].Contains(dho.HandlerId))
                    {
                        errors.Add($"Handler not found in stage '{@override.Stage}': {dho.HandlerId}");
                    }
                    break;

                case ReplaceHandlerOverride rho:
                    if (!stageHandlers[@override.Stage].Contains(rho.HandlerId))
                    {
                        errors.Add($"Handler not found in stage '{@override.Stage}': {rho.HandlerId}");
                    }
                    break;

                case AddHandlerOverride aho:
                    if (aho.AfterId != null && !stageHandlers[@override.Stage].Contains(aho.AfterId))
                    {
                        warnings.Add($"AfterId handler not found in stage '{@override.Stage}': {aho.AfterId}");
                    }
                    if (aho.BeforeId != null && !stageHandlers[@override.Stage].Contains(aho.BeforeId))
                    {
                        warnings.Add($"BeforeId handler not found in stage '{@override.Stage}': {aho.BeforeId}");
                    }
                    break;

                case ReorderHandlerOverride roho:
                    if (!stageHandlers[@override.Stage].Contains(roho.HandlerId))
                    {
                        errors.Add($"Handler not found in stage '{@override.Stage}': {roho.HandlerId}");
                    }
                    break;
            }
        }

        return new CognitionOverrideValidation
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    private static Dictionary<string, List<MutableHandler>> CloneStages(
        IReadOnlyList<CognitionStageDefinition> stages)
    {
        var result = new Dictionary<string, List<MutableHandler>>(StringComparer.OrdinalIgnoreCase);

        foreach (var stage in stages)
        {
            var handlers = stage.Handlers
                .Select(h => new MutableHandler
                {
                    Id = h.Id,
                    HandlerName = h.HandlerName,
                    Description = h.Description,
                    Enabled = h.Enabled,
                    Parameters = new Dictionary<string, object>(h.Parameters)
                })
                .ToList();

            result[stage.Name] = handlers;
        }

        return result;
    }

    private void ApplyOverride(
        Dictionary<string, List<MutableHandler>> stages,
        ICognitionOverride @override)
    {
        if (!stages.TryGetValue(@override.Stage, out var handlers))
        {
            _logger?.LogWarning("Override targets unknown stage: {Stage}", @override.Stage);
            return;
        }

        switch (@override)
        {
            case ParameterOverride po:
                ApplyParameterOverride(handlers, po);
                break;

            case DisableHandlerOverride dho:
                ApplyDisableOverride(handlers, dho);
                break;

            case AddHandlerOverride aho:
                ApplyAddOverride(handlers, aho);
                break;

            case ReplaceHandlerOverride rho:
                ApplyReplaceOverride(handlers, rho);
                break;

            case ReorderHandlerOverride roho:
                ApplyReorderOverride(handlers, roho);
                break;

            default:
                _logger?.LogWarning("Unknown override type: {Type}", @override.GetType().Name);
                break;
        }
    }

    private void ApplyParameterOverride(List<MutableHandler> handlers, ParameterOverride po)
    {
        var handler = handlers.FirstOrDefault(h =>
            h.Id.Equals(po.HandlerId, StringComparison.OrdinalIgnoreCase));

        if (handler == null)
        {
            _logger?.LogWarning("Parameter override targets unknown handler: {HandlerId}", po.HandlerId);
            return;
        }

        // Deep merge parameters
        MergeParameters(handler.Parameters, po.Parameters);

        _logger?.LogDebug(
            "Applied parameter override to {HandlerId}: {ParamCount} parameters",
            po.HandlerId, po.Parameters.Count);
    }

    private void ApplyDisableOverride(List<MutableHandler> handlers, DisableHandlerOverride dho)
    {
        var handler = handlers.FirstOrDefault(h =>
            h.Id.Equals(dho.HandlerId, StringComparison.OrdinalIgnoreCase));

        if (handler == null)
        {
            _logger?.LogWarning("Disable override targets unknown handler: {HandlerId}", dho.HandlerId);
            return;
        }

        // Store condition for runtime evaluation if present
        if (!string.IsNullOrEmpty(dho.Condition))
        {
            handler.DisableCondition = dho.Condition;
        }
        else
        {
            handler.Enabled = false;
        }

        _logger?.LogDebug("Disabled handler {HandlerId}", dho.HandlerId);
    }

    private void ApplyAddOverride(List<MutableHandler> handlers, AddHandlerOverride aho)
    {
        var newHandler = new MutableHandler
        {
            Id = aho.Handler.Id,
            HandlerName = aho.Handler.HandlerName,
            Description = aho.Handler.Description,
            Enabled = aho.Handler.Enabled,
            Parameters = new Dictionary<string, object>(aho.Handler.Parameters)
        };

        // Determine insertion position
        int insertIndex = handlers.Count; // Default: end

        if (!string.IsNullOrEmpty(aho.BeforeId))
        {
            var beforeIndex = handlers.FindIndex(h =>
                h.Id.Equals(aho.BeforeId, StringComparison.OrdinalIgnoreCase));
            if (beforeIndex >= 0)
            {
                insertIndex = beforeIndex;
            }
        }
        else if (!string.IsNullOrEmpty(aho.AfterId))
        {
            var afterIndex = handlers.FindIndex(h =>
                h.Id.Equals(aho.AfterId, StringComparison.OrdinalIgnoreCase));
            if (afterIndex >= 0)
            {
                insertIndex = afterIndex + 1;
            }
        }
        else
        {
            // No position specified, insert at beginning
            insertIndex = 0;
        }

        handlers.Insert(insertIndex, newHandler);

        _logger?.LogDebug(
            "Added handler {HandlerId} at position {Position}",
            aho.Handler.Id, insertIndex);
    }

    private void ApplyReplaceOverride(List<MutableHandler> handlers, ReplaceHandlerOverride rho)
    {
        var index = handlers.FindIndex(h =>
            h.Id.Equals(rho.HandlerId, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            _logger?.LogWarning("Replace override targets unknown handler: {HandlerId}", rho.HandlerId);
            return;
        }

        handlers[index] = new MutableHandler
        {
            Id = rho.NewHandler.Id,
            HandlerName = rho.NewHandler.HandlerName,
            Description = rho.NewHandler.Description,
            Enabled = rho.NewHandler.Enabled,
            Parameters = new Dictionary<string, object>(rho.NewHandler.Parameters)
        };

        _logger?.LogDebug(
            "Replaced handler {OldId} with {NewId}",
            rho.HandlerId, rho.NewHandler.Id);
    }

    private void ApplyReorderOverride(List<MutableHandler> handlers, ReorderHandlerOverride roho)
    {
        var currentIndex = handlers.FindIndex(h =>
            h.Id.Equals(roho.HandlerId, StringComparison.OrdinalIgnoreCase));

        if (currentIndex < 0)
        {
            _logger?.LogWarning("Reorder override targets unknown handler: {HandlerId}", roho.HandlerId);
            return;
        }

        var handler = handlers[currentIndex];
        handlers.RemoveAt(currentIndex);

        // Determine new position
        int newIndex = 0;

        if (!string.IsNullOrEmpty(roho.BeforeId))
        {
            var beforeIndex = handlers.FindIndex(h =>
                h.Id.Equals(roho.BeforeId, StringComparison.OrdinalIgnoreCase));
            if (beforeIndex >= 0)
            {
                newIndex = beforeIndex;
            }
        }
        else if (!string.IsNullOrEmpty(roho.AfterId))
        {
            var afterIndex = handlers.FindIndex(h =>
                h.Id.Equals(roho.AfterId, StringComparison.OrdinalIgnoreCase));
            if (afterIndex >= 0)
            {
                newIndex = afterIndex + 1;
            }
        }

        handlers.Insert(newIndex, handler);

        _logger?.LogDebug(
            "Reordered handler {HandlerId} from {OldIndex} to {NewIndex}",
            roho.HandlerId, currentIndex, newIndex);
    }

    private static void MergeParameters(
        Dictionary<string, object> target,
        IReadOnlyDictionary<string, object> source)
    {
        foreach (var (key, value) in source)
        {
            if (value is IDictionary<string, object> sourceDict &&
                target.TryGetValue(key, out var existing) &&
                existing is Dictionary<string, object> targetDict)
            {
                // Deep merge nested dictionaries
                MergeParameters(targetDict, (IReadOnlyDictionary<string, object>)sourceDict);
            }
            else
            {
                // Replace value
                target[key] = value;
            }
        }
    }

    private CognitionStage CreateStage(string name, List<MutableHandler> handlers)
    {
        var executableHandlers = handlers
            .Select(h => new CognitionHandler(
                h.Id,
                h.HandlerName,
                h.Enabled,
                h.Parameters,
                h.DisableCondition,
                _telemetryProvider))
            .ToList();

        return new CognitionStage(name, executableHandlers, _telemetryProvider);
    }

    /// <summary>
    /// Mutable handler for override application.
    /// </summary>
    private sealed class MutableHandler
    {
        public required string Id { get; set; }
        public required string HandlerName { get; set; }
        public string? Description { get; set; }
        public bool Enabled { get; set; } = true;
        public string? DisableCondition { get; set; }
        public required Dictionary<string, object> Parameters { get; set; }
    }
}

/// <summary>
/// Executable cognition pipeline.
/// </summary>
internal sealed class CognitionPipeline : ICognitionPipeline
{
    private readonly IActionHandlerRegistry? _handlerRegistry;
    private readonly ITelemetryProvider? _telemetryProvider;

    public CognitionPipeline(
        string templateId,
        IReadOnlyList<ICognitionStage> stages,
        IActionHandlerRegistry? handlerRegistry,
        ITelemetryProvider? telemetryProvider = null)
    {
        TemplateId = templateId;
        Stages = stages;
        _handlerRegistry = handlerRegistry;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public string TemplateId { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ICognitionStage> Stages { get; }

    /// <inheritdoc/>
    public async Task<CognitionResult> ProcessAsync(
        object perception,
        CognitionContext context,
        CancellationToken ct = default)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "CognitionPipeline.ProcessAsync");
        return await ProcessBatchAsync([perception], context, ct);
    }

    /// <inheritdoc/>
    public async Task<CognitionResult> ProcessBatchAsync(
        IReadOnlyList<object> perceptions,
        CognitionContext context,
        CancellationToken ct = default)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "CognitionPipeline.ProcessBatchAsync");

        try
        {
            // Initialize context with perceptions
            context.Perceptions = perceptions;
            context.HandlerRegistry ??= _handlerRegistry;

            // Execute each stage in order
            foreach (var stage in Stages)
            {
                await stage.ExecuteAsync(context, ct);
                ct.ThrowIfCancellationRequested();
            }

            return new CognitionResult
            {
                Success = true,
                ProcessedPerceptions = context.FilteredPerceptions.ToList(),
                RequiresReplan = context.RequiresReplan,
                ReplanUrgency = context.ReplanUrgency
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CognitionResult.Failed(ex.Message);
        }
    }
}

/// <summary>
/// Executable cognition stage.
/// </summary>
internal sealed class CognitionStage : ICognitionStage
{
    private readonly ITelemetryProvider? _telemetryProvider;

    public CognitionStage(string name, IReadOnlyList<ICognitionHandler> handlers, ITelemetryProvider? telemetryProvider = null)
    {
        Name = name;
        Handlers = handlers;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public IReadOnlyList<ICognitionHandler> Handlers { get; }

    /// <inheritdoc/>
    public async Task ExecuteAsync(CognitionContext context, CancellationToken ct)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "CognitionStage.ExecuteAsync");
        foreach (var handler in Handlers)
        {
            if (!handler.IsEnabled)
            {
                continue;
            }

            // Check conditional disable
            if (handler is CognitionHandler ch &&
                !string.IsNullOrEmpty(ch.DisableCondition) &&
                context.ConditionEvaluator != null &&
                context.ConditionEvaluator(ch.DisableCondition))
            {
                continue;
            }

            await handler.ExecuteAsync(context, ct);
            ct.ThrowIfCancellationRequested();
        }
    }
}

/// <summary>
/// Executable cognition handler.
/// </summary>
internal sealed class CognitionHandler : ICognitionHandler
{
    private readonly ITelemetryProvider? _telemetryProvider;

    public CognitionHandler(
        string id,
        string handlerName,
        bool isEnabled,
        IReadOnlyDictionary<string, object> parameters,
        string? disableCondition = null,
        ITelemetryProvider? telemetryProvider = null)
    {
        Id = id;
        HandlerName = handlerName;
        IsEnabled = isEnabled;
        Parameters = parameters;
        DisableCondition = disableCondition;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public string HandlerName { get; }

    /// <inheritdoc/>
    public bool IsEnabled { get; }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, object> Parameters { get; }

    /// <summary>
    /// Optional condition for runtime disable evaluation.
    /// </summary>
    public string? DisableCondition { get; }

    /// <inheritdoc/>
    public async Task ExecuteAsync(CognitionContext context, CancellationToken ct)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "CognitionHandler.ExecuteAsync");
        var handlerRegistry = context.HandlerRegistry;
        if (handlerRegistry == null)
        {
            return; // No handler registry, skip execution
        }

        // Create a DomainAction to execute through the action handler system
        // Convert to nullable dictionary for DomainAction compatibility
        var nullableParams = Parameters.ToDictionary(
            kv => kv.Key,
            kv => (object?)kv.Value);
        var action = new DomainAction(HandlerName, nullableParams);

        var handler = handlerRegistry.GetHandler(action);
        if (handler == null)
        {
            return; // Handler not found, skip
        }

        // Create ABML execution context if not provided
        var abmlContext = context.AbmlContext;
        if (abmlContext == null)
        {
            return; // No execution context, skip
        }

        // Execute the handler
        await handler.ExecuteAsync(action, abmlContext, ct);
    }
}
