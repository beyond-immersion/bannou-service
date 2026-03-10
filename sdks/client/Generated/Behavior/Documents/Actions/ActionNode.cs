// ═══════════════════════════════════════════════════════════════════════════
// ABML Action Node Types
// AST nodes representing actions in an ABML document.
// ═══════════════════════════════════════════════════════════════════════════

namespace BeyondImmersion.Bannou.Client.Behavior.Documents.Actions;

/// <summary>
/// Base type for all action nodes in an ABML document.
/// </summary>
public abstract record ActionNode;

/// <summary>
/// Interface for actions that support action-level error handling.
/// </summary>
public interface IHasOnError
{
    /// <summary>
    /// Actions to execute if this action fails.
    /// </summary>
    IReadOnlyList<ActionNode>? OnError { get; }
}

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
// RESOURCE LOADING ACTIONS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Loads a resource snapshot and registers it as a variable provider.
/// </summary>
/// <remarks>
/// <para>
/// Loads snapshot data from the Resource service and makes it available
/// via the standard ABML expression syntax:
/// <code>
/// ${candidate.personality.aggression}  # Access personality.aggression from snapshot
/// ${candidate.history.participations}  # Access history.participations
/// </code>
/// </para>
/// <para>
/// The snapshot is registered in the document's root scope for document-wide access.
/// If the snapshot cannot be loaded, an empty provider is registered that returns
/// null for all paths (graceful degradation).
/// </para>
/// </remarks>
/// <param name="Name">Provider name for expression access (e.g., "candidate").</param>
/// <param name="ResourceType">Resource type (e.g., "character").</param>
/// <param name="ResourceId">Expression evaluating to resource GUID.</param>
/// <param name="Filter">Optional list of source types to include (e.g., ["character-personality", "character-history"]).</param>
public sealed record LoadSnapshotAction(
    string Name,
    string ResourceType,
    string ResourceId,
    IReadOnlyList<string>? Filter = null) : ActionNode;

/// <summary>
/// Prefetches multiple resource snapshots into cache for batch operations.
/// </summary>
/// <remarks>
/// <para>
/// Use before iterating over a collection to batch-load all snapshots upfront:
/// <code>
/// - prefetch_snapshots:
///     resource_type: character
///     resource_ids: ${participants | map('id')}
///     filter:
///       - character-personality
///
/// - foreach:
///     variable: p
///     collection: ${participants}
///     do:
///       - load_snapshot:  # Cache hit - instant
///           name: char
///           resource_type: character
///           resource_id: ${p.id}
/// </code>
/// </para>
/// </remarks>
/// <param name="ResourceType">Resource type (e.g., "character").</param>
/// <param name="ResourceIds">Expression evaluating to list of resource GUIDs.</param>
/// <param name="Filter">Optional list of source types to include.</param>
public sealed record PrefetchSnapshotsAction(
    string ResourceType,
    string ResourceIds,
    IReadOnlyList<string>? Filter = null) : ActionNode;

// ═══════════════════════════════════════════════════════════════════════════
// WATCHER MANAGEMENT ACTIONS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Spawns a regional watcher for the specified realm.
/// </summary>
/// <remarks>
/// <para>
/// Creates a new watcher instance via the Puppetmaster service:
/// <code>
/// - spawn_watcher:
///     watcher_type: regional
///     realm_id: ${event.realmId}
///     behavior_id: watcher-regional
///     into: spawned_watcher_id
/// </code>
/// </para>
/// </remarks>
/// <param name="WatcherType">Expression evaluating to watcher type string (e.g., "regional", "thematic").</param>
/// <param name="RealmId">Expression evaluating to realm GUID (optional - uses context if not specified).</param>
/// <param name="BehaviorId">Expression evaluating to behavior document ID (optional - uses default for type).</param>
/// <param name="Into">Optional variable name to store the created watcher ID.</param>
public sealed record SpawnWatcherAction(
    string WatcherType,
    string? RealmId = null,
    string? BehaviorId = null,
    string? Into = null) : ActionNode;

/// <summary>
/// Stops a running regional watcher.
/// </summary>
/// <remarks>
/// <para>
/// Stops a watcher by ID via the Puppetmaster service:
/// <code>
/// - stop_watcher:
///     watcher_id: ${watcher_to_stop}
/// </code>
/// </para>
/// </remarks>
/// <param name="WatcherId">Expression evaluating to watcher GUID to stop.</param>
public sealed record StopWatcherAction(
    string WatcherId) : ActionNode;

/// <summary>
/// Lists active watchers with optional filtering.
/// </summary>
/// <remarks>
/// <para>
/// Queries active watchers and stores results in a variable:
/// <code>
/// - list_watchers:
///     into: active_watchers
///     realm_id: ${realm_id}
///     watcher_type: regional
/// </code>
/// </para>
/// </remarks>
/// <param name="Into">Required variable name to store the list of watcher info objects.</param>
/// <param name="RealmId">Optional expression evaluating to realm GUID filter.</param>
/// <param name="WatcherType">Optional watcher type filter string.</param>
public sealed record ListWatchersAction(
    string Into,
    string? RealmId = null,
    string? WatcherType = null) : ActionNode;

// ═══════════════════════════════════════════════════════════════════════════
// RESOURCE WATCH ACTIONS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Subscribe to change notifications for a resource.
/// </summary>
/// <remarks>
/// <para>
/// Registers a watch on a resource so the actor receives perceptions when
/// the resource is modified:
/// <code>
/// - watch:
///     resource_type: character
///     resource_id: ${target_id}
///     sources:
///       - character-personality
///       - character-history
///     on_change: handle_target_changed
/// </code>
/// </para>
/// <para>
/// When the watched resource changes, either:
/// - If on_change is specified, the named flow is invoked with the change perception
/// - Otherwise, a perception is injected into the actor's bounded channel
/// </para>
/// </remarks>
/// <param name="ResourceType">Resource type (e.g., "character", "realm").</param>
/// <param name="ResourceId">Expression evaluating to resource GUID.</param>
/// <param name="Sources">Optional list of source types to watch (e.g., ["character-personality"]).</param>
/// <param name="OnChange">Optional flow name to invoke when the resource changes.</param>
public sealed record WatchAction(
    string ResourceType,
    string ResourceId,
    IReadOnlyList<string>? Sources = null,
    string? OnChange = null) : ActionNode;

/// <summary>
/// Unsubscribe from change notifications for a resource.
/// </summary>
/// <remarks>
/// <para>
/// Removes a previously registered watch:
/// <code>
/// - unwatch:
///     resource_type: character
///     resource_id: ${target_id}
/// </code>
/// </para>
/// </remarks>
/// <param name="ResourceType">Resource type to stop watching.</param>
/// <param name="ResourceId">Expression evaluating to resource GUID to stop watching.</param>
public sealed record UnwatchAction(
    string ResourceType,
    string ResourceId) : ActionNode;

// ═══════════════════════════════════════════════════════════════════════════
// DOMAIN ACTIONS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Generic domain action (handler-provided).
/// Supports action-level on_error for handling failures.
/// </summary>
/// <param name="Name">Action name (e.g., "animate", "speak", "move_to", "service_call").</param>
/// <param name="Parameters">Action parameters.</param>
/// <param name="OnError">Optional action-level error handlers.</param>
public sealed record DomainAction(
    string Name,
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyList<ActionNode>? OnError = null) : ActionNode, IHasOnError;

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

// ═══════════════════════════════════════════════════════════════════════════
// STREAMING COMPOSITION ACTIONS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Continuation point for streaming composition.
/// Allows extensions to attach during execution.
/// </summary>
/// <remarks>
/// <para>
/// When execution reaches a continuation point:
/// 1. If an extension is attached, control transfers to the extension
/// 2. Otherwise, waits up to Timeout for an extension to arrive
/// 3. If timeout expires, executes DefaultFlow
/// </para>
/// <para>
/// This enables streaming composition: game server receives complete cinematic,
/// starts executing, and can optionally receive extensions mid-execution.
/// </para>
/// </remarks>
/// <param name="Name">Unique name for this continuation point.</param>
/// <param name="Timeout">Maximum time to wait for extension (e.g., "2s", "500ms").</param>
/// <param name="DefaultFlow">Flow to execute if no extension arrives.</param>
public sealed record ContinuationPointAction(
    string Name,
    string Timeout,
    string DefaultFlow) : ActionNode;

// ═══════════════════════════════════════════════════════════════════════════
// BEHAVIOR OUTPUT ACTIONS
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Emit an action intent with urgency for multi-model coordination.
/// Used by compiled behavior models for intent channel output.
/// </summary>
/// <param name="Action">Action name (e.g., "attack", "block", "dodge").</param>
/// <param name="Channel">Intent channel (action, locomotion, attention, stance).</param>
/// <param name="Urgency">Urgency expression (0.0-1.0). Higher urgency wins.</param>
/// <param name="Target">Optional target expression.</param>
public sealed record EmitIntentAction(
    string Action,
    string Channel = "action",
    string Urgency = "1.0",
    string? Target = null) : ActionNode;
