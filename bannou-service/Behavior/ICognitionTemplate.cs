// =============================================================================
// Cognition Template Types
// Types for cognition pipeline templates and overrides.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// A cognition pipeline template that defines the stages and handlers
/// for processing perceptions into intentions.
/// </summary>
/// <remarks>
/// <para>
/// Templates define the standard cognition pipeline structure:
/// </para>
/// <list type="number">
/// <item>Filter - Attention filtering based on priority weights</item>
/// <item>MemoryQuery - Query relevant memories</item>
/// <item>Significance - Assess perception significance</item>
/// <item>Storage - Store significant perceptions as memories</item>
/// <item>Intention - Evaluate goal impact and trigger replanning</item>
/// </list>
/// <para>
/// Templates can be loaded from embedded resources or YAML files.
/// Character-specific behavior is achieved through overrides applied at runtime.
/// </para>
/// </remarks>
public sealed record CognitionTemplate
{
    /// <summary>
    /// Unique identifier for this template.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Optional human-readable description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The ordered list of stages in this pipeline.
    /// </summary>
    public required IReadOnlyList<CognitionStageDefinition> Stages { get; init; }

    /// <summary>
    /// Template version for compatibility checking.
    /// </summary>
    public string Version { get; init; } = "1.0";
}

/// <summary>
/// Definition of a single stage in the cognition pipeline.
/// </summary>
/// <remarks>
/// Each stage can contain multiple handlers that execute in order.
/// The standard stages are: filter, memory_query, significance, storage, intention.
/// </remarks>
public sealed record CognitionStageDefinition
{
    /// <summary>
    /// Stage name (e.g., "filter", "significance", "intention").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Ordered list of handlers in this stage.
    /// </summary>
    public required IReadOnlyList<CognitionHandlerDefinition> Handlers { get; init; }
}

/// <summary>
/// Definition of a handler within a cognition stage.
/// </summary>
public sealed record CognitionHandlerDefinition
{
    /// <summary>
    /// Unique ID for this handler instance within the template.
    /// Used for targeting overrides.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The action handler name to invoke (e.g., "filter_attention", "assess_significance").
    /// This must match an IActionHandler registered with the system.
    /// </summary>
    public required string HandlerName { get; init; }

    /// <summary>
    /// Parameters to pass to the handler.
    /// These are merged with override parameters at runtime.
    /// </summary>
    public IReadOnlyDictionary<string, object> Parameters { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// Whether this handler is enabled by default.
    /// Can be overridden per-character.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Optional description of what this handler does.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Collection of overrides to apply to a cognition template.
/// </summary>
public sealed record CognitionOverrides
{
    /// <summary>
    /// List of overrides to apply, in order.
    /// </summary>
    public required IReadOnlyList<ICognitionOverride> Overrides { get; init; }

    /// <summary>
    /// Creates an empty override set.
    /// </summary>
    public static CognitionOverrides Empty { get; } = new() { Overrides = [] };

    /// <summary>
    /// Creates overrides from a list of override definitions.
    /// </summary>
    public static CognitionOverrides FromList(params ICognitionOverride[] overrides)
        => new() { Overrides = overrides };
}

/// <summary>
/// Base interface for cognition overrides.
/// </summary>
public interface ICognitionOverride
{
    /// <summary>
    /// The stage this override applies to.
    /// </summary>
    string Stage { get; }

    /// <summary>
    /// The type of override operation.
    /// </summary>
    CognitionOverrideType Type { get; }
}

/// <summary>
/// Types of cognition overrides.
/// </summary>
public enum CognitionOverrideType
{
    /// <summary>
    /// Override handler parameters only.
    /// </summary>
    Parameter,

    /// <summary>
    /// Add a new handler to the stage.
    /// </summary>
    Add,

    /// <summary>
    /// Disable an existing handler.
    /// </summary>
    Disable,

    /// <summary>
    /// Replace a handler with a different implementation.
    /// </summary>
    Replace,

    /// <summary>
    /// Reorder handlers within a stage.
    /// </summary>
    Reorder
}

/// <summary>
/// Override that modifies handler parameters.
/// </summary>
/// <remarks>
/// This is the most common override type. Parameters are deep-merged,
/// so you only need to specify the parameters you want to change.
/// </remarks>
public sealed record ParameterOverride : ICognitionOverride
{
    /// <inheritdoc/>
    public required string Stage { get; init; }

    /// <inheritdoc/>
    public CognitionOverrideType Type => CognitionOverrideType.Parameter;

    /// <summary>
    /// The handler ID to modify.
    /// </summary>
    public required string HandlerId { get; init; }

    /// <summary>
    /// Parameter overrides to apply.
    /// These are deep-merged with the handler's existing parameters.
    /// </summary>
    public required IReadOnlyDictionary<string, object> Parameters { get; init; }
}

/// <summary>
/// Override that adds a new handler to a stage.
/// </summary>
public sealed record AddHandlerOverride : ICognitionOverride
{
    /// <inheritdoc/>
    public required string Stage { get; init; }

    /// <inheritdoc/>
    public CognitionOverrideType Type => CognitionOverrideType.Add;

    /// <summary>
    /// Handler ID to insert after. Null means insert at the beginning.
    /// </summary>
    public string? AfterId { get; init; }

    /// <summary>
    /// Handler ID to insert before. Takes precedence over AfterId if both specified.
    /// </summary>
    public string? BeforeId { get; init; }

    /// <summary>
    /// The handler definition to add.
    /// </summary>
    public required CognitionHandlerDefinition Handler { get; init; }
}

/// <summary>
/// Override that disables an existing handler.
/// </summary>
public sealed record DisableHandlerOverride : ICognitionOverride
{
    /// <inheritdoc/>
    public required string Stage { get; init; }

    /// <inheritdoc/>
    public CognitionOverrideType Type => CognitionOverrideType.Disable;

    /// <summary>
    /// The handler ID to disable.
    /// </summary>
    public required string HandlerId { get; init; }

    /// <summary>
    /// Optional condition expression for conditional disabling.
    /// When specified, the handler is only disabled when this expression evaluates to true.
    /// </summary>
    public string? Condition { get; init; }
}

/// <summary>
/// Override that replaces a handler with a different implementation.
/// </summary>
public sealed record ReplaceHandlerOverride : ICognitionOverride
{
    /// <inheritdoc/>
    public required string Stage { get; init; }

    /// <inheritdoc/>
    public CognitionOverrideType Type => CognitionOverrideType.Replace;

    /// <summary>
    /// The handler ID to replace.
    /// </summary>
    public required string HandlerId { get; init; }

    /// <summary>
    /// The new handler definition.
    /// </summary>
    public required CognitionHandlerDefinition NewHandler { get; init; }
}

/// <summary>
/// Override that reorders handlers within a stage.
/// </summary>
public sealed record ReorderHandlerOverride : ICognitionOverride
{
    /// <inheritdoc/>
    public required string Stage { get; init; }

    /// <inheritdoc/>
    public CognitionOverrideType Type => CognitionOverrideType.Reorder;

    /// <summary>
    /// The handler ID to move.
    /// </summary>
    public required string HandlerId { get; init; }

    /// <summary>
    /// Insert after this handler ID. Null with BeforeId=null means move to beginning.
    /// </summary>
    public string? AfterId { get; init; }

    /// <summary>
    /// Insert before this handler ID. Takes precedence over AfterId.
    /// </summary>
    public string? BeforeId { get; init; }
}

/// <summary>
/// Standard cognition stage names.
/// </summary>
public static class CognitionStages
{
    /// <summary>
    /// Stage 1: Attention filtering.
    /// </summary>
    public const string Filter = "filter";

    /// <summary>
    /// Stage 2: Memory query.
    /// </summary>
    public const string MemoryQuery = "memory_query";

    /// <summary>
    /// Stage 3: Significance assessment.
    /// </summary>
    public const string Significance = "significance";

    /// <summary>
    /// Stage 4: Memory storage.
    /// </summary>
    public const string Storage = "storage";

    /// <summary>
    /// Stage 5: Consequence evaluation (morality/obligation cost assessment).
    /// Forward-compatible slot: inert until an EvaluateConsequencesHandler is registered.
    /// </summary>
    public const string EvaluateConsequences = "evaluate_consequences";

    /// <summary>
    /// Stage 6: Intention formation.
    /// </summary>
    public const string Intention = "intention";

    /// <summary>
    /// Gets all standard stage names in pipeline order.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Filter, MemoryQuery, Significance, Storage, EvaluateConsequences, Intention];
}
