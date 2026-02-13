// =============================================================================
// Cognition Builder and Pipeline Interfaces
// Interfaces for building and executing cognition pipelines.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Execution;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Builds cognition pipelines from templates with optional character-specific overrides.
/// </summary>
/// <remarks>
/// <para>
/// The builder applies overrides in order to produce a customized pipeline.
/// Override priority (most to least common):
/// </para>
/// <list type="number">
/// <item>Parameter overrides - Modify handler parameters (weights, thresholds, etc.)</item>
/// <item>Disable overrides - Conditionally or unconditionally disable handlers</item>
/// <item>Replace overrides - Swap handler implementations (advanced, rare)</item>
/// </list>
/// </remarks>
public interface ICognitionBuilder
{
    /// <summary>
    /// Builds a cognition pipeline from a template with optional overrides.
    /// </summary>
    /// <param name="templateId">The template ID to build from.</param>
    /// <param name="overrides">Optional overrides to apply.</param>
    /// <returns>The built pipeline, or null if template not found.</returns>
    ICognitionPipeline? Build(string templateId, CognitionOverrides? overrides = null);

    /// <summary>
    /// Builds a cognition pipeline from a template with optional overrides.
    /// </summary>
    /// <param name="template">The template to build from.</param>
    /// <param name="overrides">Optional overrides to apply.</param>
    /// <returns>The built pipeline.</returns>
    ICognitionPipeline Build(CognitionTemplate template, CognitionOverrides? overrides = null);

    /// <summary>
    /// Validates overrides against a template without building.
    /// </summary>
    /// <param name="templateId">The template ID to validate against.</param>
    /// <param name="overrides">The overrides to validate.</param>
    /// <returns>Validation result with any errors or warnings.</returns>
    CognitionOverrideValidation ValidateOverrides(string templateId, CognitionOverrides overrides);
}

/// <summary>
/// Result of validating cognition overrides.
/// </summary>
public sealed record CognitionOverrideValidation
{
    /// <summary>
    /// Whether the overrides are valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation errors that prevent building.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Validation warnings that don't prevent building but may indicate issues.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Creates a valid result with no errors.
    /// </summary>
    public static CognitionOverrideValidation Valid() => new() { IsValid = true };

    /// <summary>
    /// Creates an invalid result with errors.
    /// </summary>
    public static CognitionOverrideValidation Invalid(params string[] errors)
        => new() { IsValid = false, Errors = errors };
}

/// <summary>
/// A built cognition pipeline ready for execution.
/// </summary>
/// <remarks>
/// <para>
/// Pipelines are immutable once built. For different character configurations,
/// build separate pipelines with different overrides.
/// </para>
/// </remarks>
public interface ICognitionPipeline
{
    /// <summary>
    /// The template ID this pipeline was built from.
    /// </summary>
    string TemplateId { get; }

    /// <summary>
    /// The resolved stages in execution order.
    /// </summary>
    IReadOnlyList<ICognitionStage> Stages { get; }

    /// <summary>
    /// Processes a perception through the entire pipeline.
    /// </summary>
    /// <param name="perception">The perception to process.</param>
    /// <param name="context">The cognition execution context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The processing result.</returns>
    Task<CognitionResult> ProcessAsync(
        object perception,
        CognitionContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Processes multiple perceptions through the pipeline.
    /// </summary>
    /// <param name="perceptions">The perceptions to process.</param>
    /// <param name="context">The cognition execution context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The processing results.</returns>
    Task<CognitionResult> ProcessBatchAsync(
        IReadOnlyList<object> perceptions,
        CognitionContext context,
        CancellationToken ct = default);
}

/// <summary>
/// A stage within a cognition pipeline.
/// </summary>
public interface ICognitionStage
{
    /// <summary>
    /// The stage name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The handlers in this stage, in execution order.
    /// </summary>
    IReadOnlyList<ICognitionHandler> Handlers { get; }

    /// <summary>
    /// Executes all handlers in this stage.
    /// </summary>
    Task ExecuteAsync(CognitionContext context, CancellationToken ct);
}

/// <summary>
/// A handler within a cognition stage.
/// </summary>
public interface ICognitionHandler
{
    /// <summary>
    /// Unique ID of this handler instance.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// The handler name (action name).
    /// </summary>
    string HandlerName { get; }

    /// <summary>
    /// Whether this handler is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// The resolved parameters for this handler.
    /// </summary>
    IReadOnlyDictionary<string, object> Parameters { get; }

    /// <summary>
    /// Executes this handler.
    /// </summary>
    Task ExecuteAsync(CognitionContext context, CancellationToken ct);
}

/// <summary>
/// Context for cognition pipeline execution.
/// </summary>
/// <remarks>
/// <para>
/// The context carries state between stages and provides access to
/// the ABML execution infrastructure.
/// </para>
/// </remarks>
public sealed class CognitionContext
{
    /// <summary>
    /// The entity ID being processed. Null when no character is associated with the actor.
    /// </summary>
    public Guid? EntityId { get; init; }

    /// <summary>
    /// ABML execution context for running action handlers.
    /// </summary>
    public AbmlExecutionContext? AbmlContext { get; init; }

    /// <summary>
    /// The action handler registry for finding handlers.
    /// </summary>
    public IActionHandlerRegistry? HandlerRegistry { get; set; }

    /// <summary>
    /// Current perceptions being processed.
    /// </summary>
    public IReadOnlyList<object> Perceptions { get; set; } = [];

    /// <summary>
    /// Filtered perceptions after Stage 1.
    /// </summary>
    public IReadOnlyList<object> FilteredPerceptions { get; set; } = [];

    /// <summary>
    /// Fast-tracked threat perceptions that skip to Stage 5.
    /// </summary>
    public IReadOnlyList<object> FastTrackPerceptions { get; set; } = [];

    /// <summary>
    /// Retrieved memories from Stage 2.
    /// </summary>
    public IReadOnlyList<object> RelevantMemories { get; set; } = [];

    /// <summary>
    /// Significance scores from Stage 3.
    /// </summary>
    public IReadOnlyDictionary<object, float> SignificanceScores { get; set; } =
        new Dictionary<object, float>();

    /// <summary>
    /// Whether replanning is needed (from Stage 5).
    /// </summary>
    public bool RequiresReplan { get; set; }

    /// <summary>
    /// Urgency level for replanning.
    /// </summary>
    public float ReplanUrgency { get; set; }

    /// <summary>
    /// Additional context data for handlers.
    /// </summary>
    public IDictionary<string, object> Data { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Expression context for evaluating conditions.
    /// </summary>
    public Func<string, bool>? ConditionEvaluator { get; init; }

    /// <summary>
    /// Creates a context for an entity.
    /// </summary>
    public static CognitionContext ForEntity(Guid? entityId)
        => new() { EntityId = entityId };
}

/// <summary>
/// Result of processing perceptions through a cognition pipeline.
/// </summary>
public sealed record CognitionResult
{
    /// <summary>
    /// Whether processing completed successfully.
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// Perceptions that passed all stages and may trigger behavior.
    /// </summary>
    public IReadOnlyList<object> ProcessedPerceptions { get; init; } = [];

    /// <summary>
    /// Perceptions that were stored as memories.
    /// </summary>
    public IReadOnlyList<object> StoredMemories { get; init; } = [];

    /// <summary>
    /// Whether the GOAP planner should replan.
    /// </summary>
    public bool RequiresReplan { get; init; }

    /// <summary>
    /// Urgency level for replanning (0-1).
    /// </summary>
    public float ReplanUrgency { get; init; }

    /// <summary>
    /// Goals affected by the processed perceptions.
    /// </summary>
    public IReadOnlyList<string> AffectedGoals { get; init; } = [];

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Creates a success result.
    /// </summary>
    public static CognitionResult Successful(
        IReadOnlyList<object> processed,
        bool requiresReplan = false,
        float urgency = 0f)
        => new()
        {
            Success = true,
            ProcessedPerceptions = processed,
            RequiresReplan = requiresReplan,
            ReplanUrgency = urgency
        };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static CognitionResult Failed(string error)
        => new() { Success = false, Error = error };
}

/// <summary>
/// Registry for cognition templates.
/// </summary>
/// <remarks>
/// <para>
/// Templates can be registered from:
/// </para>
/// <list type="bullet">
/// <item>Embedded resources (compiled-in defaults)</item>
/// <item>YAML files (runtime configuration)</item>
/// <item>Programmatic construction</item>
/// </list>
/// </remarks>
public interface ICognitionTemplateRegistry
{
    /// <summary>
    /// Gets a template by ID.
    /// </summary>
    /// <param name="templateId">The template ID.</param>
    /// <returns>The template, or null if not found.</returns>
    CognitionTemplate? GetTemplate(string templateId);

    /// <summary>
    /// Checks if a template exists.
    /// </summary>
    /// <param name="templateId">The template ID.</param>
    /// <returns>True if the template exists.</returns>
    bool HasTemplate(string templateId);

    /// <summary>
    /// Registers a template.
    /// </summary>
    /// <param name="template">The template to register.</param>
    void RegisterTemplate(CognitionTemplate template);

    /// <summary>
    /// Gets all registered template IDs.
    /// </summary>
    /// <returns>The template IDs.</returns>
    IReadOnlyCollection<string> GetTemplateIds();

    /// <summary>
    /// Loads templates from a directory.
    /// </summary>
    /// <param name="directory">The directory containing YAML template files.</param>
    /// <param name="recursive">Whether to search subdirectories.</param>
    /// <returns>Number of templates loaded.</returns>
    int LoadFromDirectory(string directory, bool recursive = true);
}

/// <summary>
/// Standard cognition template IDs.
/// </summary>
public static class CognitionTemplates
{
    /// <summary>
    /// Base template for humanoid characters.
    /// </summary>
    public const string HumanoidBase = "humanoid-cognition-base";

    /// <summary>
    /// Base template for creature characters.
    /// </summary>
    public const string CreatureBase = "creature-cognition-base";

    /// <summary>
    /// Minimal template for objects (limited cognition).
    /// </summary>
    public const string ObjectBase = "object-cognition-base";
}
