// ═══════════════════════════════════════════════════════════════════════════
// ABML Document Model
// Represents a parsed ABML document with flows and metadata.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents.Actions;

namespace BeyondImmersion.BannouService.Abml.Documents;

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
    /// Named flows containing action sequences.
    /// </summary>
    public IReadOnlyDictionary<string, Flow> Flows { get; init; } = new Dictionary<string, Flow>();
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
