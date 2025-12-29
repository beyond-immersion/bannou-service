// ═══════════════════════════════════════════════════════════════════════════
// ABML Action Node Types
// AST nodes representing actions in an ABML document.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.BannouService.Abml.Documents.Actions;

/// <summary>
/// Base type for all action nodes in an ABML document.
/// </summary>
public abstract record ActionNode;

// ═══════════════════════════════════════════════════════════════════════════
// CONTROL FLOW ACTIONS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Conditional branching action (if/else-if/else).
/// </summary>
/// <param name="Branches">Ordered list of condition branches.</param>
/// <param name="ElseBranch">Actions to execute if no condition matches.</param>
public sealed record CondAction(
    IReadOnlyList<CondBranch> Branches,
    IReadOnlyList<ActionNode>? ElseBranch) : ActionNode;

/// <summary>
/// A single branch in a conditional action.
/// </summary>
/// <param name="When">Condition expression string.</param>
/// <param name="Then">Actions to execute if condition is true.</param>
public sealed record CondBranch(
    string When,
    IReadOnlyList<ActionNode> Then);

/// <summary>
/// Iteration over a collection.
/// </summary>
/// <param name="Variable">Loop variable name.</param>
/// <param name="Collection">Expression string evaluating to a collection.</param>
/// <param name="Do">Actions to execute for each item.</param>
public sealed record ForEachAction(
    string Variable,
    string Collection,
    IReadOnlyList<ActionNode> Do) : ActionNode;

/// <summary>
/// Bounded repetition.
/// </summary>
/// <param name="Times">Number of times to repeat.</param>
/// <param name="Do">Actions to execute each iteration.</param>
public sealed record RepeatAction(
    int Times,
    IReadOnlyList<ActionNode> Do) : ActionNode;

/// <summary>
/// Transfer control to another flow (tail call - does not return).
/// </summary>
/// <param name="Flow">Target flow name.</param>
/// <param name="Args">Arguments to pass to the target flow.</param>
public sealed record GotoAction(
    string Flow,
    IReadOnlyDictionary<string, string>? Args = null) : ActionNode;

/// <summary>
/// Call another flow as a subroutine (returns to caller).
/// </summary>
/// <param name="Flow">Target flow name.</param>
public sealed record CallAction(string Flow) : ActionNode;

/// <summary>
/// Return from the current flow.
/// </summary>
/// <param name="Value">Optional return value expression.</param>
public sealed record ReturnAction(string? Value = null) : ActionNode;

// ═══════════════════════════════════════════════════════════════════════════
// VARIABLE ACTIONS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Set a variable, searching scope chain. Creates locally if not found.
/// </summary>
/// <param name="Variable">Variable name.</param>
/// <param name="Value">Expression string for the value.</param>
public sealed record SetAction(
    string Variable,
    string Value) : ActionNode;

/// <summary>
/// Create/set a variable in the current local scope (shadows parent).
/// </summary>
/// <param name="Variable">Variable name.</param>
/// <param name="Value">Expression string for the value.</param>
public sealed record LocalAction(
    string Variable,
    string Value) : ActionNode;

/// <summary>
/// Set a variable in the document root scope.
/// </summary>
/// <param name="Variable">Variable name.</param>
/// <param name="Value">Expression string for the value.</param>
public sealed record GlobalAction(
    string Variable,
    string Value) : ActionNode;

/// <summary>
/// Increment a numeric variable.
/// </summary>
/// <param name="Variable">Variable name.</param>
/// <param name="By">Amount to increment by (default 1).</param>
public sealed record IncrementAction(
    string Variable,
    int By = 1) : ActionNode;

/// <summary>
/// Decrement a numeric variable.
/// </summary>
/// <param name="Variable">Variable name.</param>
/// <param name="By">Amount to decrement by (default 1).</param>
public sealed record DecrementAction(
    string Variable,
    int By = 1) : ActionNode;

/// <summary>
/// Clear/unset a variable.
/// </summary>
/// <param name="Variable">Variable name to clear.</param>
public sealed record ClearAction(string Variable) : ActionNode;

// ═══════════════════════════════════════════════════════════════════════════
// UTILITY ACTIONS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Log a message (for debugging and testing).
/// </summary>
/// <param name="Message">Message to log (may contain expressions).</param>
/// <param name="Level">Log level (info, debug, warn, error).</param>
public sealed record LogAction(
    string Message,
    string Level = "info") : ActionNode;

// ═══════════════════════════════════════════════════════════════════════════
// DOMAIN ACTIONS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Generic domain action (handler-provided).
/// </summary>
/// <param name="Name">Action name (e.g., "animate", "speak", "move_to").</param>
/// <param name="Parameters">Action parameters.</param>
public sealed record DomainAction(
    string Name,
    IReadOnlyDictionary<string, object?> Parameters) : ActionNode;

// ═══════════════════════════════════════════════════════════════════════════
// CHANNEL ACTIONS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Emit a signal to other channels.
/// </summary>
/// <param name="Signal">Signal name to emit.</param>
/// <param name="Payload">Optional payload expression.</param>
public sealed record EmitAction(
    string Signal,
    string? Payload = null) : ActionNode;

/// <summary>
/// Wait for a signal from another channel.
/// </summary>
/// <param name="Signal">Signal name to wait for.</param>
public sealed record WaitForAction(string Signal) : ActionNode;

/// <summary>
/// Synchronization barrier - all channels wait here until all arrive.
/// </summary>
/// <param name="Point">Sync point name.</param>
public sealed record SyncAction(string Point) : ActionNode;
