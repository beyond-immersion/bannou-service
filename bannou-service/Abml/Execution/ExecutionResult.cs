// ═══════════════════════════════════════════════════════════════════════════
// ABML Execution Result
// Result types for document execution.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.BannouService.Abml.Execution;

/// <summary>
/// Result of document execution.
/// </summary>
public sealed class ExecutionResult
{
    /// <summary>
    /// Whether execution succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Return value from execution (if any).
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Error message (if failed).
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Log entries collected during execution.
    /// </summary>
    public IReadOnlyList<LogEntry> Logs { get; }

    private ExecutionResult(bool isSuccess, object? value, string? error, IReadOnlyList<LogEntry> logs)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        Logs = logs;
    }

    /// <summary>
    /// Creates a successful execution result.
    /// </summary>
    public static ExecutionResult Success(object? value = null, IReadOnlyList<LogEntry>? logs = null) =>
        new(true, value, null, logs ?? []);

    /// <summary>
    /// Creates a failed execution result.
    /// </summary>
    public static ExecutionResult Failure(string error, IReadOnlyList<LogEntry>? logs = null) =>
        new(false, null, error, logs ?? []);
}

/// <summary>
/// Result of executing a single action.
/// </summary>
public abstract record ActionResult
{
    /// <summary>
    /// Continue to the next action.
    /// </summary>
    public static ActionResult Continue { get; } = new ContinueResult();

    /// <summary>
    /// Complete execution with a value.
    /// </summary>
    public static ActionResult Complete(object? value = null) => new CompleteResult(value);

    /// <summary>
    /// Transfer control to another flow (tail call).
    /// </summary>
    public static ActionResult Goto(string flow, IReadOnlyDictionary<string, object?>? args = null)
        => new GotoResult(flow, args);

    /// <summary>
    /// Return from the current flow to the caller.
    /// </summary>
    public static ActionResult Return(object? value = null) => new ReturnResult(value);

    /// <summary>
    /// Execution failed with an error.
    /// </summary>
    public static ActionResult Error(string message) => new ErrorResult(message);
}

/// <summary>
/// Continue to the next action in the current flow.
/// </summary>
public sealed record ContinueResult : ActionResult;

/// <summary>
/// Execution completed with a value.
/// </summary>
/// <param name="Value">The return value.</param>
public sealed record CompleteResult(object? Value) : ActionResult;

/// <summary>
/// Transfer control to another flow (tail call - does not return).
/// </summary>
/// <param name="FlowName">Target flow name.</param>
/// <param name="Args">Arguments to pass to the flow.</param>
public sealed record GotoResult(
    string FlowName,
    IReadOnlyDictionary<string, object?>? Args = null) : ActionResult;

/// <summary>
/// Return from the current flow to the caller.
/// </summary>
/// <param name="Value">Optional return value.</param>
public sealed record ReturnResult(object? Value) : ActionResult;

/// <summary>
/// Execution failed with an error.
/// </summary>
/// <param name="Message">Error message.</param>
public sealed record ErrorResult(string Message) : ActionResult;
