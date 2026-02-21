// ═══════════════════════════════════════════════════════════════════════════
// ABML Domain Action Handler
// Handles domain-specific actions by routing to Intent Emitters.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Archetypes;
using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles domain-specific actions by routing them to Intent Emitters.
/// This is the integration point between ABML execution and the behavior system.
/// </summary>
public sealed class DomainActionHandler : IActionHandler
{
    private readonly IIntentEmitterRegistry? _emitters;
    private readonly IArchetypeRegistry? _archetypes;
    private readonly IControlGateRegistry? _controlGates;
    private readonly Func<Guid>? _entityIdResolver;
    private readonly Func<string, IReadOnlyDictionary<string, object?>, ValueTask<ActionResult>>? _callback;

    /// <summary>
    /// Creates a domain action handler with full intent emission support.
    /// </summary>
    /// <param name="emitters">Intent emitter registry for action→emission translation.</param>
    /// <param name="archetypes">Archetype registry for entity type resolution.</param>
    /// <param name="controlGates">Control gate registry for emission filtering.</param>
    /// <param name="entityIdResolver">Optional resolver for current entity ID.</param>
    public DomainActionHandler(
        IIntentEmitterRegistry emitters,
        IArchetypeRegistry archetypes,
        IControlGateRegistry controlGates,
        Func<Guid>? entityIdResolver = null)
    {
        _emitters = emitters ?? throw new ArgumentNullException(nameof(emitters));
        _archetypes = archetypes ?? throw new ArgumentNullException(nameof(archetypes));
        _controlGates = controlGates ?? throw new ArgumentNullException(nameof(controlGates));
        _entityIdResolver = entityIdResolver;
        _callback = null;
    }

    /// <summary>
    /// Creates a domain action handler with a custom callback (legacy mode).
    /// </summary>
    /// <param name="callback">Optional callback for handling domain actions.</param>
    public DomainActionHandler(Func<string, IReadOnlyDictionary<string, object?>, ValueTask<ActionResult>>? callback = null)
    {
        _callback = callback;
        _emitters = null;
        _archetypes = null;
        _controlGates = null;
        _entityIdResolver = null;
    }

    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is DomainAction;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var domainAction = (DomainAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Evaluate expressions in parameters
        var evaluatedParams = ValueEvaluator.EvaluateParameters(
            domainAction.Parameters, scope, context.Evaluator);

        // If there's a callback (legacy mode), use it
        if (_callback != null)
        {
            return await _callback(domainAction.Name, evaluatedParams);
        }

        // If no emitters configured, fall back to logging
        if (_emitters == null || _archetypes == null || _controlGates == null)
        {
            var paramsStr = string.Join(", ", evaluatedParams.Select(kv => $"{kv.Key}={kv.Value}"));
            context.Logs.Add(new LogEntry("domain", $"{domainAction.Name}({paramsStr})", DateTime.UtcNow));
            return ActionResult.Continue;
        }

        // 1. Resolve entity and archetype from context
        var entityId = ResolveEntityId(scope);
        var archetype = ResolveArchetype(scope);

        // 2. Create emission context
        var emissionContext = new IntentEmissionContext
        {
            EntityId = entityId,
            Archetype = archetype,
            DocumentType = context.Document.Metadata?.Type ?? "behavior",
            Data = BuildContextData(scope)
        };

        // 3. Look up emitter
        var emitter = _emitters.GetEmitter(domainAction.Name, emissionContext);
        if (emitter == null)
        {
            // No emitter - log and continue (backward compatibility)
            context.Logs.Add(new LogEntry("domain",
                $"{domainAction.Name} (no emitter)", DateTime.UtcNow));
            return ActionResult.Continue;
        }

        // 4. Emit intents
        var paramsDict = evaluatedParams.ToDictionary(
            kv => kv.Key,
            kv => kv.Value ?? new object());
        var emissions = await emitter.EmitAsync(paramsDict, emissionContext, ct);

        // 5. Filter through control gate
        var gate = _controlGates.Get(entityId);
        IReadOnlyList<IntentEmission> filteredEmissions = gate != null
            ? gate.FilterEmissions(emissions, ControlSource.Behavior)
            : emissions;

        // 6. Store emissions in context for later processing by runtime
        StoreEmissionsInContext(scope, filteredEmissions);

        // 7. Log the emission for debugging
        if (filteredEmissions.Count > 0)
        {
            var emissionSummary = string.Join(", ",
                filteredEmissions.Select(e => $"{e.Channel}:{e.Intent}@{e.Urgency:F2}"));
            context.Logs.Add(new LogEntry("emit", $"{domainAction.Name} → [{emissionSummary}]", DateTime.UtcNow));
        }

        return ActionResult.Continue;
    }

    private Guid ResolveEntityId(IVariableScope scope)
    {
        // Try to get from scope (agent.id)
        var agent = scope.GetValue("agent");
        if (agent is IDictionary<string, object?> agentDict)
        {
            if (agentDict.TryGetValue("id", out var idObj))
            {
                if (idObj is Guid guid) return guid;
                if (idObj is string str && Guid.TryParse(str, out var parsed)) return parsed;
            }
        }

        // Try entity.id
        var entity = scope.GetValue("entity");
        if (entity is IDictionary<string, object?> entityDict)
        {
            if (entityDict.TryGetValue("id", out var entityIdObj))
            {
                if (entityIdObj is Guid guid) return guid;
                if (entityIdObj is string str && Guid.TryParse(str, out var parsed)) return parsed;
            }
        }

        // Fallback to resolver or empty
        return _entityIdResolver?.Invoke() ?? Guid.Empty;
    }

    private IArchetypeDefinition? ResolveArchetype(IVariableScope scope)
    {
        if (_archetypes == null)
        {
            return null;
        }

        // Try to get archetype from scope
        var archetypeId = scope.GetValue("archetype");
        if (archetypeId is string id)
        {
            return _archetypes.GetArchetype(id) ?? _archetypes.GetDefaultArchetype();
        }

        // Try agent.archetype
        var agent = scope.GetValue("agent");
        if (agent is IDictionary<string, object?> agentDict &&
            agentDict.TryGetValue("archetype", out var agentArchetype) &&
            agentArchetype is string archId)
        {
            return _archetypes.GetArchetype(archId) ?? _archetypes.GetDefaultArchetype();
        }

        return _archetypes.GetDefaultArchetype();
    }

    private static IReadOnlyDictionary<string, object> BuildContextData(IVariableScope scope)
    {
        var data = new Dictionary<string, object>();

        // Copy relevant scope variables to context data
        var feelings = scope.GetValue("feelings");
        if (feelings != null)
            data["feelings"] = feelings;

        var goals = scope.GetValue("goals");
        if (goals != null)
            data["goals"] = goals;

        var perceptions = scope.GetValue("perceptions");
        if (perceptions != null)
            data["perceptions"] = perceptions;

        var state = scope.GetValue("state");
        if (state != null)
            data["state"] = state;

        return data;
    }

    private static void StoreEmissionsInContext(
        IVariableScope scope, IReadOnlyList<IntentEmission> emissions)
    {
        if (emissions.Count == 0)
        {
            return;
        }

        // Store in _intent_emissions for later processing by runtime
        var existing = scope.GetValue("_intent_emissions") as List<IntentEmission>;
        if (existing == null)
        {
            existing = new List<IntentEmission>();
            scope.SetValue("_intent_emissions", existing);
        }

        existing.AddRange(emissions);
    }
}
