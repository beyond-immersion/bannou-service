// ═══════════════════════════════════════════════════════════════════════════
// ABML Document Model
// Represents a parsed ABML document with flows and metadata.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;

namespace BeyondImmersion.Bannou.BehaviorCompiler.Documents;

/// <summary>
/// Represents a complete ABML document.
/// </summary>
public sealed class AbmlDocument
{
    /// <summary>
    /// The ABML version (e.g., "2.0").
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Document metadata (id, type, description, etc.).
    /// </summary>
    public required DocumentMetadata Metadata { get; init; }

    /// <summary>
    /// Import definitions for schemas and other documents.
    /// </summary>
    public IReadOnlyList<ImportDefinition> Imports { get; init; } = [];

    /// <summary>
    /// Context definition with variables, requirements, and services.
    /// </summary>
    public ContextDefinition? Context { get; init; }

    /// <summary>
    /// GOAP goal definitions for behavior planning.
    /// Maps goal name to its definition with priority and conditions.
    /// </summary>
    public IReadOnlyDictionary<string, GoapGoalDefinition> Goals { get; init; } =
        new Dictionary<string, GoapGoalDefinition>();

    /// <summary>
    /// Options block for actor self-description.
    /// Enables Event Brain queries via /actor/query-options endpoint.
    /// </summary>
    public OptionsDefinition? Options { get; init; }

    /// <summary>
    /// Named flows containing action sequences.
    /// </summary>
    public IReadOnlyDictionary<string, Flow> Flows { get; init; } = new Dictionary<string, Flow>();

    /// <summary>
    /// Document-level error handler flow name.
    /// Called when an error is not handled by action-level or flow-level handlers.
    /// </summary>
    public string? OnError { get; init; }
}

/// <summary>
/// Document metadata.
/// </summary>
public sealed class DocumentMetadata
{
    /// <summary>
    /// Unique identifier for this document.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Document type: behavior, dialogue, cutscene, dialplan, or timeline.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// Human-readable description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Categorization tags.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Hint for runtime that execution is deterministic.
    /// </summary>
    public bool Deterministic { get; init; }

    /// <summary>
    /// Resource snapshot types this behavior accesses.
    /// Used as default filter for load_snapshot when no explicit filter provided.
    /// Enables compile-time path validation when IResourceTemplateRegistry is available.
    /// </summary>
    public IReadOnlyList<string> ResourceTemplates { get; init; } = [];

    /// <summary>
    /// Optional cognition template override for actor execution.
    /// When specified, overrides the actor template's cognition configuration.
    /// Example: "humanoid-cognition-base", "creature-cognition-base".
    /// </summary>
    public string? CognitionTemplate { get; init; }

    /// <summary>
    /// Whether this behavior enables the evaluate_consequences cognition stage.
    /// When true, the actor's cognition pipeline includes moral/obligation cost evaluation.
    /// Default is false (no moral reasoning overhead).
    /// </summary>
    public bool Conscience { get; init; }
}

/// <summary>
/// Import definition for schemas or other ABML documents.
/// </summary>
public sealed class ImportDefinition
{
    /// <summary>
    /// Schema file path (for type imports).
    /// </summary>
    public string? Schema { get; init; }

    /// <summary>
    /// Types to import from schema.
    /// </summary>
    public IReadOnlyList<string> Types { get; init; } = [];

    /// <summary>
    /// ABML document file path (for document imports).
    /// </summary>
    public string? File { get; init; }

    /// <summary>
    /// Namespace alias for imported document.
    /// </summary>
    public string? As { get; init; }
}

/// <summary>
/// Context definition with variables, requirements, and services.
/// </summary>
public sealed class ContextDefinition
{
    /// <summary>
    /// Variable definitions.
    /// </summary>
    public IReadOnlyDictionary<string, VariableDefinition> Variables { get; init; } =
        new Dictionary<string, VariableDefinition>();

    /// <summary>
    /// Requirements that must be met to execute this document.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Requirements { get; init; } =
        new Dictionary<string, object?>();

    /// <summary>
    /// Service dependencies.
    /// </summary>
    public IReadOnlyList<ServiceDependency> Services { get; init; } = [];
}

/// <summary>
/// Variable definition within context.
/// </summary>
public sealed class VariableDefinition
{
    /// <summary>
    /// Variable type (e.g., "int", "string", "bool").
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// Default value for the variable.
    /// </summary>
    public object? Default { get; init; }

    /// <summary>
    /// Source expression for bound variables.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Computed expression for derived variables.
    /// </summary>
    public string? Computed { get; init; }
}

/// <summary>
/// Service dependency declaration.
/// </summary>
public sealed class ServiceDependency
{
    /// <summary>
    /// Service name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Whether the service is required.
    /// </summary>
    public bool Required { get; init; }
}

/// <summary>
/// Represents a named flow containing action sequences.
/// </summary>
public sealed class Flow
{
    /// <summary>
    /// Flow name.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Triggers that can activate this flow.
    /// </summary>
    public IReadOnlyList<FlowTrigger> Triggers { get; init; } = [];

    /// <summary>
    /// GOAP metadata for this flow (preconditions, effects, cost).
    /// When present, this flow can be used as a GOAP action.
    /// </summary>
    public GoapFlowMetadata? Goap { get; init; }

    /// <summary>
    /// Actions to execute in this flow.
    /// </summary>
    public IReadOnlyList<ActionNode> Actions { get; init; } = [];

    /// <summary>
    /// Error handler actions for this flow.
    /// When an error occurs, these actions execute with _error variable set.
    /// </summary>
    public IReadOnlyList<ActionNode> OnError { get; init; } = [];
}

/// <summary>
/// Flow trigger definition.
/// </summary>
public sealed class FlowTrigger
{
    /// <summary>
    /// Event pattern to trigger on.
    /// </summary>
    public string? Event { get; init; }

    /// <summary>
    /// Condition expression that must be true.
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>
    /// Time range for triggering (e.g., "08:00-17:00").
    /// </summary>
    public string? TimeRange { get; init; }

    /// <summary>
    /// Cron-style schedule.
    /// </summary>
    public string? Schedule { get; init; }
}

/// <summary>
/// GOAP goal definition from ABML goals: section.
/// Goals define desired world state conditions with priorities.
/// </summary>
public sealed class GoapGoalDefinition
{
    /// <summary>
    /// Goal priority (higher = more important).
    /// Default is 50 for normal priority.
    /// </summary>
    public int Priority { get; init; } = 50;

    /// <summary>
    /// Conditions that define goal satisfaction.
    /// Keys are world state property names, values are literal conditions.
    /// Examples: "hunger" -> "&lt;= 0.3", "gold" -> "&gt;= 50"
    /// </summary>
    public IReadOnlyDictionary<string, string> Conditions { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Options definition for actor self-description.
/// Maps option types (combat, dialogue, etc.) to their option definitions.
/// </summary>
public sealed class OptionsDefinition
{
    /// <summary>
    /// Options grouped by type (combat, dialogue, social, exploration, or custom).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<OptionDefinition>> OptionsByType { get; init; } =
        new Dictionary<string, IReadOnlyList<OptionDefinition>>();
}

/// <summary>
/// Definition of a single option within an options block.
/// Expressions are evaluated at runtime to produce ActorOption values.
/// </summary>
public sealed class OptionDefinition
{
    /// <summary>
    /// Unique identifier for this action within the option type.
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// Preference expression (0-1). Can be a literal float or an expression like "${combat.style == 'aggressive' ? 0.9 : 0.6}".
    /// </summary>
    public required string Preference { get; init; }

    /// <summary>
    /// Risk expression (0-1). Can be a literal float or an expression. Optional.
    /// </summary>
    public string? Risk { get; init; }

    /// <summary>
    /// Availability expression. Can be a literal bool or an expression like "${equipment.has_sword}".
    /// </summary>
    public required string Available { get; init; }

    /// <summary>
    /// Human-readable requirements for this option.
    /// </summary>
    public IReadOnlyList<string> Requirements { get; init; } = [];

    /// <summary>
    /// Cooldown in milliseconds. Can be a literal int or an expression. Optional.
    /// </summary>
    public string? CooldownMs { get; init; }

    /// <summary>
    /// Tags for categorization (e.g., "melee", "offensive", "loud").
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>
/// GOAP metadata from flow goap: block.
/// Describes a flow as a GOAP action with preconditions, effects, and cost.
/// </summary>
public sealed class GoapFlowMetadata
{
    /// <summary>
    /// Preconditions that must be satisfied to execute this action.
    /// Keys are world state property names, values are literal conditions.
    /// Examples: "hunger" -> "&gt; 0.6", "gold" -> "&gt;= 5"
    /// </summary>
    public IReadOnlyDictionary<string, string> Preconditions { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Effects applied to world state when action completes.
    /// Values can be absolute ("0.5", "tavern") or delta ("-0.8", "+5").
    /// Examples: "hunger" -> "-0.8", "location" -> "tavern"
    /// </summary>
    public IReadOnlyDictionary<string, string> Effects { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Action cost for A* planning (lower = preferred).
    /// Default is 1.0 for unit cost.
    /// </summary>
    public float Cost { get; init; } = 1.0f;
}
