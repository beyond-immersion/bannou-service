// ═══════════════════════════════════════════════════════════════════════════
// ABML Emit Event Handler
// Handles emit_event: actions by publishing typed events via templates.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Utilities;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles emit_event: ABML actions by looking up registered event templates,
/// substituting values, and publishing to the message bus.
/// </summary>
/// <remarks>
/// <para>
/// This handler enables behaviors to publish typed events without knowing
/// the event infrastructure details. Template owners (the plugins that define
/// the events) register templates with the EventTemplateRegistry, and behavior
/// authors simply reference templates by name with flat parameters:
/// </para>
/// <code>
/// - emit_event:
///     template: encounter_resolved
///     encounterId: ${encounter.id}
///     outcome: ${outcome}
///     durationSeconds: ${duration}
/// </code>
/// <para>
/// The handler:
/// 1. Looks up the template by name
/// 2. Evaluates expression parameters from the ABML scope
/// 3. Substitutes values into the template using TemplateSubstitutor
/// 4. Deserializes to the event type for validation
/// 5. Publishes via IMessageBus
/// </para>
/// </remarks>
public sealed class EmitEventHandler : IActionHandler
{
    private readonly IEventTemplateRegistry _registry;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<EmitEventHandler> _logger;

    /// <summary>
    /// Creates a new EmitEventHandler.
    /// </summary>
    /// <param name="registry">Event template registry for template lookup.</param>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public EmitEventHandler(
        IEventTemplateRegistry registry,
        IMessageBus messageBus,
        ILogger<EmitEventHandler> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action)
    {
        return action is DomainAction da &&
               da.Name.Equals("emit_event", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var domainAction = (DomainAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Extract template name (required)
        if (!domainAction.Parameters.TryGetValue("template", out var templateObj) ||
            templateObj is not string templateName ||
            string.IsNullOrWhiteSpace(templateName))
        {
            _logger.LogWarning("emit_event action missing required 'template' parameter");
            context.Logs.Add(new LogEntry("error", "emit_event: missing 'template' parameter", DateTime.UtcNow));
            return ActionResult.Continue;
        }

        // Look up template
        var template = _registry.Get(templateName);
        if (template == null)
        {
            _logger.LogWarning("emit_event: unknown template '{Template}'", templateName);
            context.Logs.Add(new LogEntry("error", $"emit_event: unknown template '{templateName}'", DateTime.UtcNow));
            return ActionResult.Continue;
        }

        // Build substitution context from remaining parameters
        // Evaluate expressions and convert to the format TemplateSubstitutor expects
        var substitutionContext = new Dictionary<string, object?>();

        foreach (var (key, value) in domainAction.Parameters)
        {
            // Skip the template parameter itself
            if (key.Equals("template", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Evaluate expression if it's a string that looks like an expression
            object? evaluatedValue;
            if (value is string stringValue)
            {
                evaluatedValue = EvaluateExpression(stringValue, scope, context.Evaluator);
            }
            else
            {
                evaluatedValue = value;
            }

            substitutionContext[key] = evaluatedValue;
        }

        // Substitute values into template
        string substitutedPayload;
        try
        {
            substitutedPayload = TemplateSubstitutor.Substitute(
                template.PayloadTemplate, substitutionContext);
        }
        catch (TemplateSubstitutionException ex)
        {
            _logger.LogWarning(ex, "emit_event: template substitution failed for '{Template}'", templateName);
            context.Logs.Add(new LogEntry("error",
                $"emit_event: substitution failed - {ex.Message}", DateTime.UtcNow));
            return ActionResult.Continue;
        }

        // Deserialize to event type for validation
        object? eventInstance;
        try
        {
            eventInstance = BannouJson.Deserialize(substitutedPayload, template.EventType);
            if (eventInstance == null)
            {
                _logger.LogWarning("emit_event: deserialization returned null for '{Template}'", templateName);
                context.Logs.Add(new LogEntry("error",
                    $"emit_event: payload doesn't match {template.EventType.Name}", DateTime.UtcNow));
                return ActionResult.Continue;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "emit_event: payload validation failed for '{Template}'", templateName);
            context.Logs.Add(new LogEntry("error",
                $"emit_event: validation failed - {ex.Message}", DateTime.UtcNow));
            return ActionResult.Continue;
        }

        // Publish the event
        try
        {
            await _messageBus.PublishAsync(template.Topic, eventInstance, ct);

            _logger.LogDebug("emit_event: published '{Template}' to '{Topic}'",
                templateName, template.Topic);
            context.Logs.Add(new LogEntry("emit_event",
                $"{templateName} → {template.Topic}", DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "emit_event: failed to publish '{Template}' to '{Topic}'",
                templateName, template.Topic);
            context.Logs.Add(new LogEntry("error",
                $"emit_event: publish failed - {ex.Message}", DateTime.UtcNow));
        }

        return ActionResult.Continue;
    }

    /// <summary>
    /// Evaluates a string value as an expression if it looks like one.
    /// </summary>
    private static object? EvaluateExpression(string value, IVariableScope scope, IExpressionEvaluator evaluator)
    {
        // Check if it's an expression (starts with ${)
        if (value.StartsWith("${") && value.EndsWith("}"))
        {
            var expressionBody = value[2..^1];
            return evaluator.Evaluate(expressionBody, scope);
        }

        // Not an expression - return as-is
        return value;
    }
}
