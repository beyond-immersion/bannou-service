// ═══════════════════════════════════════════════════════════════════════════
// ABML Execution Context
// Runtime state for document execution.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.Bannou.BehaviorCompiler.Parser;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.Bannou.BehaviorExpressions.Runtime;

namespace BeyondImmersion.BannouService.Abml.Execution;

/// <summary>
/// Runtime context for ABML document execution.
/// </summary>
public sealed class ExecutionContext
{
    /// <summary>
    /// The document being executed.
    /// </summary>
    public required AbmlDocument Document { get; init; }

    /// <summary>
    /// The root loaded document with resolved imports.
    /// When null, only local flows are accessible.
    /// </summary>
    public LoadedDocument? LoadedDocument { get; init; }

    /// <summary>
    /// The current document context for flow resolution.
    /// This changes when calling flows in imported documents.
    /// Defaults to LoadedDocument if not explicitly set.
    /// </summary>
    public LoadedDocument? CurrentDocument { get; set; }

    /// <summary>
    /// Root variable scope.
    /// </summary>
    public required IVariableScope RootScope { get; init; }

    /// <summary>
    /// Expression evaluator for evaluating ${...} expressions.
    /// </summary>
    public required IExpressionEvaluator Evaluator { get; init; }

    /// <summary>
    /// Action handler registry for dispatching actions.
    /// </summary>
    public required IActionHandlerRegistry Handlers { get; init; }

    /// <summary>
    /// Optional actor ID when executing within an actor context.
    /// Required for actions like watch/unwatch that track per-actor state.
    /// </summary>
    public Guid? ActorId { get; init; }

    /// <summary>
    /// Call stack for flow execution.
    /// </summary>
    public FlowStack CallStack { get; } = new();

    /// <summary>
    /// Current execution state.
    /// </summary>
    public ExecutionState State { get; set; } = ExecutionState.Running;

    /// <summary>
    /// Return value from the execution.
    /// </summary>
    public object? ReturnValue { get; set; }

    /// <summary>
    /// Log messages collected during execution.
    /// </summary>
    public List<LogEntry> Logs { get; } = [];

    /// <summary>
    /// Tries to resolve a flow reference, supporting namespaced imports.
    /// Resolution is relative to CurrentDocument (context-relative).
    /// </summary>
    /// <param name="flowRef">Flow reference (e.g., "my_flow" or "common.my_flow").</param>
    /// <param name="flow">The resolved flow if found.</param>
    /// <returns>True if the flow was found.</returns>
    public bool TryResolveFlow(string flowRef, out Flow? flow)
    {
        return TryResolveFlow(flowRef, out flow, out _);
    }

    /// <summary>
    /// Tries to resolve a flow reference, supporting namespaced imports.
    /// Resolution is relative to CurrentDocument (context-relative).
    /// </summary>
    /// <param name="flowRef">Flow reference (e.g., "my_flow" or "common.my_flow").</param>
    /// <param name="flow">The resolved flow if found.</param>
    /// <param name="resolvedDocument">The document containing the resolved flow.</param>
    /// <returns>True if the flow was found.</returns>
    public bool TryResolveFlow(string flowRef, out Flow? flow, out LoadedDocument? resolvedDocument)
    {
        // Use CurrentDocument if set, otherwise fall back to LoadedDocument
        var contextDoc = CurrentDocument ?? LoadedDocument;

        if (contextDoc != null)
        {
            return contextDoc.TryResolveFlow(flowRef, out flow, out resolvedDocument);
        }

        // Fallback: direct lookup in Document.Flows (no namespace support)
        if (Document.Flows.TryGetValue(flowRef, out flow))
        {
            resolvedDocument = null;
            return true;
        }

        flow = null;
        resolvedDocument = null;
        return false;
    }
}

/// <summary>
/// Execution state.
/// </summary>
public enum ExecutionState
{
    /// <summary>Execution is in progress.</summary>
    Running,

    /// <summary>Execution completed successfully.</summary>
    Completed,

    /// <summary>Execution failed with an error.</summary>
    Failed,

    /// <summary>Execution yielded (for async operations).</summary>
    Yielded
}

/// <summary>
/// A log entry captured during execution.
/// </summary>
/// <param name="Level">Log level (info, debug, warn, error).</param>
/// <param name="Message">Log message.</param>
/// <param name="Timestamp">When the log was recorded.</param>
public sealed record LogEntry(string Level, string Message, DateTime Timestamp);

/// <summary>
/// Call stack for tracking flow execution.
/// </summary>
public sealed class FlowStack
{
    private readonly Stack<FlowFrame> _frames = new();

    /// <summary>
    /// Pushes a new flow frame onto the stack.
    /// </summary>
    public void Push(string flowName, IVariableScope scope)
        => _frames.Push(new FlowFrame(flowName, scope));

    /// <summary>
    /// Pops the top flow frame from the stack.
    /// </summary>
    public FlowFrame? Pop()
        => _frames.Count > 0 ? _frames.Pop() : null;

    /// <summary>
    /// Gets the current flow frame without removing it.
    /// </summary>
    public FlowFrame? Current
        => _frames.Count > 0 ? _frames.Peek() : null;

    /// <summary>
    /// Gets the current stack depth.
    /// </summary>
    public int Depth => _frames.Count;

    /// <summary>
    /// Clears all frames from the stack.
    /// </summary>
    public void Clear() => _frames.Clear();
}

/// <summary>
/// A frame on the flow call stack.
/// </summary>
/// <param name="FlowName">Name of the flow being executed.</param>
/// <param name="Scope">Variable scope for this flow.</param>
public sealed record FlowFrame(string FlowName, IVariableScope Scope);
