// ═══════════════════════════════════════════════════════════════════════════
// ABML Execution Context
// Runtime state for document execution.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Runtime;

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
