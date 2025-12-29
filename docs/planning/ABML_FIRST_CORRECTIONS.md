# ABML First Corrections

> **Status**: CORRECTION PLAN
> **Created**: 2025-12-28
> **Purpose**: Address implementation deviations from ABML_FIRST_STEPS.md
> **Related**: [ABML_FIRST_STEPS.md](./ABML_FIRST_STEPS.md)

This document outlines corrections needed to align the ABML implementation with the original design intent and fix issues discovered during review.

---

## Summary of Corrections

| # | Issue | Severity | Current | Correction |
|---|-------|----------|---------|------------|
| 1 | Variable Scope | HIGH | Write-through to ancestors | Explicit `local:` vs `set:` with shadowing |
| 2 | Call Handler Scope | MEDIUM | Shares caller's scope | Independent child scope |
| 3 | Error Handling | HIGH | Missing | Implement `on_error` infrastructure |
| 4 | Function Call Convention | MEDIUM | Fixed R0..Rn | Flexible R[C] start position |
| 5 | Short-Circuit Compilation | LOW | Dual-use And opcode | Jumps only, pure And semantics |
| 6 | Multi-Channel Execution | CRITICAL | Not implemented | Cooperative scheduling |

---

## Correction 1: Variable Scope Semantics

### Problem

Current implementation uses **write-through semantics** - any `set:` modifies the nearest ancestor scope that contains that variable:

```csharp
// Current: VariableScope.cs:64-74
var targetScope = FindScopeWithVariable(name);
if (targetScope is VariableScope vs)
{
    vs._variables[name] = value;  // Writes to ancestor!
}
```

This causes loop variables and flow-local variables to clobber outer scope variables:

```yaml
# CURRENT BROKEN BEHAVIOR
flows:
  main:
    actions:
      - set: { variable: i, value: 100 }
      - for_each:
          variable: i           # Clobbers outer 'i'!
          collection: "${items}"
          do:
            - log: { message: "${i}" }
      - log: { message: "${i}" }  # Now 'i' is the last item, not 100
```

### Solution

Implement **explicit local vs global scope control** with three actions:

```yaml
# NEW BEHAVIOR
actions:
  # 'local:' always creates in current scope (shadows parent)
  - local: { variable: temp, value: 0 }

  # 'set:' writes to existing variable (searches up scope chain)
  # If not found, creates in current scope
  - set: { variable: counter, value: "${counter + 1}" }

  # 'global:' explicitly writes to document root scope
  - global: { variable: shared_state, value: "updated" }
```

### Implementation Changes

#### 1.1 Add New Action Types

```csharp
// ActionNode.cs - Add new action types

/// <summary>
/// Set a variable, searching scope chain. Creates locally if not found.
/// </summary>
public sealed record SetAction(string Variable, string Value) : ActionNode;

/// <summary>
/// Create/set a variable in the current local scope (shadows parent).
/// </summary>
public sealed record LocalAction(string Variable, string Value) : ActionNode;

/// <summary>
/// Set a variable in the document root scope.
/// </summary>
public sealed record GlobalAction(string Variable, string Value) : ActionNode;
```

#### 1.2 Fix VariableScope

```csharp
// VariableScope.cs - New implementation

public sealed class VariableScope : IVariableScope
{
    private readonly Dictionary<string, object?> _variables = new();

    public IVariableScope? Parent { get; }

    /// <summary>
    /// Sets a variable, searching up the scope chain.
    /// If found in an ancestor, updates it there.
    /// If not found anywhere, creates in THIS scope.
    /// </summary>
    public void SetValue(string name, object? value)
    {
        ValidateName(name);

        var targetScope = FindScopeWithVariable(name);
        if (targetScope is VariableScope vs)
        {
            vs._variables[name] = value;
        }
        else
        {
            // Not found anywhere - create locally
            _variables[name] = value;
        }
    }

    /// <summary>
    /// Sets a variable in this scope only, shadowing any parent variable.
    /// </summary>
    public void SetLocalValue(string name, object? value)
    {
        ValidateName(name);
        _variables[name] = value;  // Always local, ignores parent
    }

    /// <summary>
    /// Sets a variable in the root scope.
    /// </summary>
    public void SetGlobalValue(string name, object? value)
    {
        ValidateName(name);
        GetRootScope()._variables[name] = value;
    }

    private VariableScope GetRootScope()
    {
        var current = this;
        while (current.Parent is VariableScope parent)
            current = parent;
        return current;
    }

    // ... rest unchanged
}
```

#### 1.3 Add Handlers

```csharp
// LocalHandler.cs
public sealed class LocalHandler : IActionHandler
{
    public bool CanHandle(ActionNode action) => action is LocalAction;

    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var localAction = (LocalAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        var value = EvaluateValue(localAction.Value, scope, context);

        // SetLocalValue creates in current scope, shadows parent
        if (scope is VariableScope vs)
            vs.SetLocalValue(localAction.Variable, value);
        else
            scope.SetValue(localAction.Variable, value);

        return ValueTask.FromResult(ActionResult.Continue);
    }
}

// GlobalHandler.cs
public sealed class GlobalHandler : IActionHandler
{
    public bool CanHandle(ActionNode action) => action is GlobalAction;

    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var globalAction = (GlobalAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        var value = EvaluateValue(globalAction.Value, scope, context);

        // SetGlobalValue writes to root scope
        if (scope is VariableScope vs)
            vs.SetGlobalValue(globalAction.Variable, value);
        else
            context.RootScope.SetValue(globalAction.Variable, value);

        return ValueTask.FromResult(ActionResult.Continue);
    }
}
```

#### 1.4 Fix ForEachHandler

```csharp
// ForEachHandler.cs - Loop variable is always local

public async ValueTask<ActionResult> ExecuteAsync(...)
{
    var forEach = (ForEachAction)action;
    var scope = context.CallStack.Current?.Scope ?? context.RootScope;

    var collection = context.Evaluator.Evaluate(forEach.Collection, scope);
    if (collection is not IEnumerable enumerable)
        return collection == null ? ActionResult.Continue
            : ActionResult.Error($"Cannot iterate over {collection.GetType().Name}");

    foreach (var item in enumerable)
    {
        ct.ThrowIfCancellationRequested();

        // Create child scope for loop body
        var loopScope = scope.CreateChild();

        // Loop variable is LOCAL to the loop scope (shadows parent)
        if (loopScope is VariableScope vs)
            vs.SetLocalValue(forEach.Variable, item);
        else
            loopScope.SetValue(forEach.Variable, item);

        // Execute with loop scope
        var result = await ExecuteActionsWithScopeAsync(forEach.Do, context, loopScope, ct);
        if (result is not ContinueResult)
            return result;
    }

    return ActionResult.Continue;
}
```

### Test Cases

```csharp
[Fact]
public async Task ForEach_LoopVariable_DoesNotClobberOuterScope()
{
    var yaml = """
        version: "2.0"
        metadata:
          id: scope_test
        flows:
          start:
            actions:
              - set: { variable: i, value: "100" }
              - for_each:
                  variable: i
                  collection: "${items}"
                  do:
                    - log: { message: "Loop: ${i}" }
              - log: { message: "After: ${i}" }
        """;

    var scope = new VariableScope();
    scope.SetValue("items", new[] { "a", "b", "c" });

    var result = await _executor.ExecuteAsync(doc, "start", scope);

    // Outer 'i' should still be 100, not "c"
    Assert.Equal("After: 100", result.Logs.Last().Message);
}

[Fact]
public async Task Local_ShadowsParentVariable()
{
    var yaml = """
        version: "2.0"
        metadata:
          id: shadow_test
        flows:
          start:
            actions:
              - set: { variable: x, value: "outer" }
              - call: { flow: inner }
              - log: { message: "Outer x: ${x}" }
          inner:
            actions:
              - local: { variable: x, value: "inner" }
              - log: { message: "Inner x: ${x}" }
        """;

    var result = await _executor.ExecuteAsync(doc, "start");

    Assert.Equal("Inner x: inner", result.Logs[0].Message);
    Assert.Equal("Outer x: outer", result.Logs[1].Message);  // Not clobbered
}

[Fact]
public async Task Global_WritesToRootScope()
{
    var yaml = """
        version: "2.0"
        metadata:
          id: global_test
        flows:
          start:
            actions:
              - call: { flow: nested }
              - log: { message: "Result: ${result}" }
          nested:
            actions:
              - global: { variable: result, value: "from_nested" }
        """;

    var result = await _executor.ExecuteAsync(doc, "start");

    Assert.Equal("Result: from_nested", result.Logs[0].Message);
}
```

---

## Correction 2: Call Handler Scope Independence

### Problem

Current `CallHandler` shares the caller's scope directly:

```csharp
// Current: CallHandler.cs:32-35
var currentScope = context.CallStack.Current?.Scope ?? context.RootScope;
context.CallStack.Push(flowName, currentScope);  // Same scope!
```

This means called flows can accidentally modify caller's variables and there's no isolation.

### Solution

`call` should create a **child scope**, similar to how `goto` works:

```csharp
// CallHandler.cs - Fixed implementation

public async ValueTask<ActionResult> ExecuteAsync(
    ActionNode action, ExecutionContext context, CancellationToken ct)
{
    var callAction = (CallAction)action;
    var flowName = callAction.Flow;

    if (!context.Document.Flows.TryGetValue(flowName, out var targetFlow))
        return ActionResult.Error($"Flow not found: {flowName}");

    var currentScope = context.CallStack.Current?.Scope ?? context.RootScope;

    // Create CHILD scope - called flow gets its own namespace
    // but can still READ from parent (for parameters passed via variables)
    var callScope = currentScope.CreateChild();

    context.CallStack.Push(flowName, callScope);

    try
    {
        foreach (var flowAction in targetFlow.Actions)
        {
            ct.ThrowIfCancellationRequested();

            var handler = context.Handlers.GetHandler(flowAction);
            if (handler == null)
                return ActionResult.Error($"No handler for: {flowAction.GetType().Name}");

            var result = await handler.ExecuteAsync(flowAction, context, ct);

            switch (result)
            {
                case ReturnResult returnResult:
                    // Store return value in CALLER's scope (not call scope)
                    if (returnResult.Value != null)
                        currentScope.SetValue("_result", returnResult.Value);
                    return ActionResult.Continue;

                case GotoResult:
                case ErrorResult:
                case CompleteResult:
                    return result;
            }
        }

        return ActionResult.Continue;
    }
    finally
    {
        context.CallStack.Pop();
    }
}
```

### Behavioral Change

```yaml
# BEFORE (broken): called flow clobbers caller's variables
flows:
  main:
    - set: { variable: temp, value: "main_value" }
    - call: { flow: helper }
    - log: { message: "${temp}" }  # Shows "helper_value" (clobbered!)
  helper:
    - set: { variable: temp, value: "helper_value" }

# AFTER (fixed): called flow has isolated scope
# main's temp remains "main_value"
# If helper needs to return data, use 'return:' and '_result'
```

---

## Correction 3: Error Handling Infrastructure

### Problem

No `on_error` support exists. Any action failure terminates the entire document.

### Solution

Implement error handling at action and document levels.

### 3.1 Document Model Updates

```csharp
// AbmlDocument.cs additions

public sealed class AbmlDocument
{
    // ... existing fields ...

    /// <summary>
    /// Document-level error handler flow name.
    /// </summary>
    public string? OnError { get; init; }
}

public sealed class Flow
{
    // ... existing fields ...

    /// <summary>
    /// Flow-level error handler (inline actions or flow reference).
    /// </summary>
    public ErrorHandler? OnError { get; init; }
}

/// <summary>
/// Error handler definition.
/// </summary>
public sealed class ErrorHandler
{
    /// <summary>
    /// Inline actions to execute on error.
    /// </summary>
    public IReadOnlyList<ActionNode>? Actions { get; init; }

    /// <summary>
    /// Flow to call on error.
    /// </summary>
    public string? Flow { get; init; }
}
```

### 3.2 Action-Level Error Handling

```yaml
# YAML syntax for error handling

version: "2.0"
metadata:
  id: error_handling_example

# Document-level error handler
on_error: handle_fatal_error

flows:
  start:
    # Flow-level error handler
    on_error:
      actions:
        - log: { message: "Flow error: ${_error.message}", level: error }
        - set: { variable: _error_handled, value: true }

    actions:
      # Action-level error handling (inline)
      - animate:
          target: "${actor}"
          animation: wave
          on_error:
            - log: { message: "Animation failed, continuing", level: warn }

      # Action that might fail
      - call_service:
          service: economy
          method: deduct_gold
          amount: 100
          on_error:
            - log: { message: "Economy service unavailable" }
            - set: { variable: transaction_failed, value: true }

  handle_fatal_error:
    actions:
      - log: { message: "Fatal error in ${_error.flow}: ${_error.message}", level: error }
      - emit: behavior_crashed
```

### 3.3 Error Context

```csharp
// ExecutionContext.cs additions

public sealed class ExecutionContext
{
    // ... existing fields ...

    /// <summary>
    /// Current error being handled (null if no error).
    /// </summary>
    public ErrorInfo? CurrentError { get; set; }
}

/// <summary>
/// Information about an error during execution.
/// </summary>
public sealed record ErrorInfo(
    string Message,
    string FlowName,
    string ActionType,
    Exception? Exception = null);
```

### 3.4 Error Handling in Executor

```csharp
// DocumentExecutor.cs - Add error handling

private async ValueTask<ActionResult> ExecuteActionWithErrorHandling(
    ActionNode action,
    ExecutionContext context,
    ErrorHandler? flowErrorHandler,
    CancellationToken ct)
{
    try
    {
        var handler = _handlers.GetHandler(action);
        if (handler == null)
            return ActionResult.Error($"No handler for: {action.GetType().Name}");

        return await handler.ExecuteAsync(action, context, ct);
    }
    catch (Exception ex)
    {
        var errorInfo = new ErrorInfo(
            ex.Message,
            context.CallStack.Current?.FlowName ?? "unknown",
            action.GetType().Name,
            ex);

        context.CurrentError = errorInfo;

        // Expose error to expressions as _error variable
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;
        scope.SetValue("_error", new Dictionary<string, object?>
        {
            ["message"] = errorInfo.Message,
            ["flow"] = errorInfo.FlowName,
            ["action"] = errorInfo.ActionType
        });

        // Try action-level on_error first
        if (action is IHasErrorHandler { OnError: { } actionErrorHandler })
        {
            var handled = await ExecuteErrorHandler(actionErrorHandler, context, ct);
            if (handled)
            {
                context.CurrentError = null;
                return ActionResult.Continue;  // Error handled, continue
            }
        }

        // Try flow-level on_error
        if (flowErrorHandler != null)
        {
            var handled = await ExecuteErrorHandler(flowErrorHandler, context, ct);
            if (handled)
            {
                context.CurrentError = null;
                return ActionResult.Continue;
            }
        }

        // Try document-level on_error
        if (context.Document.OnError != null &&
            context.Document.Flows.TryGetValue(context.Document.OnError, out var errorFlow))
        {
            await ExecuteFlowAsync(errorFlow, context, ct);
        }

        // Error not handled
        return ActionResult.Error(errorInfo.Message);
    }
}

private async ValueTask<bool> ExecuteErrorHandler(
    ErrorHandler handler,
    ExecutionContext context,
    CancellationToken ct)
{
    if (handler.Actions != null)
    {
        foreach (var action in handler.Actions)
        {
            await ExecuteActionAsync(action, context, ct);
        }
    }
    else if (handler.Flow != null &&
             context.Document.Flows.TryGetValue(handler.Flow, out var flow))
    {
        await ExecuteFlowAsync(flow, context, ct);
    }

    // Check if handler set _error_handled
    var scope = context.CallStack.Current?.Scope ?? context.RootScope;
    var handled = scope.GetValue("_error_handled");
    return handled is true;
}
```

---

## Correction 4: Function Call Convention

### Problem

Implementation uses fixed R0..Rn convention, requiring register shuffling:

```csharp
// Current: ExpressionCompiler.cs:282-290
for (var i = 0; i < argCount; i++)
{
    var targetReg = (byte)i;
    var sourceReg = argRegisters[i];
    if (sourceReg != targetReg)
        _builder.Emit(OpCode.Move, targetReg, sourceReg, 0);
}
```

This can cause register corruption with nested function calls.

### Solution

Return to the document's flexible design where args start at R[C]:

```csharp
// OpCode.cs - Clarify semantics
/// <summary>
/// R[A] = Call(constants[B], argCount=C, args in R[0]..R[C-1])
/// Note: Next instruction's A byte contains actual arg start register if non-zero.
/// </summary>
Call,

// ExpressionCompiler.cs - Simplified, no shuffling needed

public byte VisitFunctionCall(FunctionCallNode node)
{
    var argCount = node.Arguments.Count;

    // Reserve contiguous registers for arguments
    var argStartReg = _builder.Registers.AllocateRange(argCount);

    // Compile each argument directly into its target register
    for (var i = 0; i < argCount; i++)
    {
        var argReg = (byte)(argStartReg + i);
        CompileIntoRegister(node.Arguments[i], argReg);
    }

    var destReg = _builder.Registers.Allocate();
    var funcIdx = _builder.Constants.Add(node.FunctionName);

    // Call: dest=A, func=B, argCount=C
    // Followed by: argStart in A
    _builder.Emit(OpCode.Call, destReg, funcIdx, (byte)argCount);
    _builder.Emit(OpCode.Nop, argStartReg, 0, 0);  // Arg start register

    // Free argument registers
    for (var i = 0; i < argCount; i++)
        _builder.Registers.Free((byte)(argStartReg + i));

    return destReg;
}

private void CompileIntoRegister(ExpressionNode node, byte targetReg)
{
    // Compile expression, then move result to target if needed
    var resultReg = node.Accept(this);
    if (resultReg != targetReg)
    {
        _builder.Emit(OpCode.Move, targetReg, resultReg, 0);
        _builder.Registers.Free(resultReg);
    }
}
```

### VM Update

```csharp
// ExpressionVm.cs - Updated Call handling

case OpCode.Call:
{
    var funcName = (string)constants[b];
    var argCount = c;
    var argStart = code[++pc].A;  // Next instruction holds arg start

    var args = new object?[argCount];
    for (var i = 0; i < argCount; i++)
        args[i] = _registers[argStart + i];

    _registers[a] = _functions.Invoke(funcName, args);
    pc++;
    break;
}
```

---

## Correction 5: Short-Circuit Compilation (Jumps Only)

### Problem

Current implementation overloads `And` opcode for boolean conversion:

```csharp
// Current: And R0, R0, R0 = convert to boolean (dual-use)
_builder.Emit(OpCode.And, leftReg, leftReg, leftReg);
```

### Solution

Use explicit boolean conversion or just rely on jumps. Add `ToBool` opcode for explicit conversion:

```csharp
// OpCode.cs - Add explicit boolean conversion
/// <summary>R[A] = IsTrue(R[B])</summary>
ToBool,

// ExpressionCompiler.cs - Clean short-circuit implementation

private byte CompileShortCircuitAnd(BinaryNode node)
{
    // left && right:
    // 1. Evaluate left
    // 2. If falsy, jump to END with false
    // 3. Evaluate right
    // 4. Convert right to boolean for result
    // 5. END

    var leftReg = node.Left.Accept(this);

    // Jump to false result if left is falsy
    var jumpToFalseIdx = _builder.Emit(OpCode.JumpIfFalse, leftReg, 0, 0);
    _builder.Registers.Free(leftReg);

    // Evaluate right side
    var rightReg = node.Right.Accept(this);
    var resultReg = _builder.Registers.Allocate();

    // Convert right to boolean
    _builder.Emit(OpCode.ToBool, resultReg, rightReg);
    _builder.Registers.Free(rightReg);

    // Jump over the false case
    var jumpToEndIdx = _builder.Emit(OpCode.Jump, 0, 0, 0);

    // False case: load false
    var falseCaseIdx = _builder.CodeLength;
    _builder.PatchJump(jumpToFalseIdx, falseCaseIdx);
    _builder.Emit(OpCode.LoadFalse, resultReg);

    // End
    var endIdx = _builder.CodeLength;
    _builder.PatchJump(jumpToEndIdx, endIdx);

    return resultReg;
}

// ExpressionVm.cs - Add ToBool handler
case OpCode.ToBool:
    _registers[a] = AbmlTypeCoercion.IsTrue(_registers[b]);
    break;

// And/Or are now pure boolean operations (both operands evaluated)
case OpCode.And:
    _registers[a] = AbmlTypeCoercion.IsTrue(_registers[b])
                 && AbmlTypeCoercion.IsTrue(_registers[c]);
    break;
```

---

## Correction 6: Multi-Channel Cooperative Scheduling

### Design: Cooperative Round-Robin Execution

Channels execute **cooperatively on a single thread** with deterministic interleaving:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    COOPERATIVE CHANNEL EXECUTION                             │
│                                                                              │
│  Tick 1:  camera[0] → actors[0] → effects[0]                                │
│  Tick 2:  camera[1] → actors[1] → effects[1]                                │
│  Tick 3:  camera[2] → actors[WAIT] → effects[2]  (actors waiting for signal)│
│  Tick 4:  camera[3] → effects[3]                  (actors still waiting)    │
│  Tick 5:  camera[EMIT] → actors[2] → effects[4]  (camera emits, actors wake)│
│                                                                              │
│  Single thread, deterministic order, predictable replay                     │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 6.1 Channel State Machine

```csharp
// Execution/Channels/ChannelState.cs

/// <summary>
/// Execution state for a single channel.
/// </summary>
public sealed class ChannelState
{
    /// <summary>Channel name.</summary>
    public required string Name { get; init; }

    /// <summary>Actions to execute.</summary>
    public required IReadOnlyList<ActionNode> Actions { get; init; }

    /// <summary>Current action index.</summary>
    public int CurrentIndex { get; set; }

    /// <summary>Channel execution status.</summary>
    public ChannelStatus Status { get; set; } = ChannelStatus.Running;

    /// <summary>Signals this channel is waiting for.</summary>
    public HashSet<string> WaitingFor { get; } = new();

    /// <summary>Channel-local variable scope.</summary>
    public required IVariableScope Scope { get; init; }

    /// <summary>Whether there are more actions to execute.</summary>
    public bool HasMoreActions => CurrentIndex < Actions.Count;

    /// <summary>Get current action without advancing.</summary>
    public ActionNode? CurrentAction =>
        HasMoreActions ? Actions[CurrentIndex] : null;

    /// <summary>Advance to next action.</summary>
    public void Advance() => CurrentIndex++;
}

public enum ChannelStatus
{
    Running,
    Waiting,
    Completed,
    Failed
}
```

### 6.2 Sync Point Registry

```csharp
// Execution/Channels/SyncPointRegistry.cs

/// <summary>
/// Manages sync points (emit/wait_for) between channels.
/// </summary>
public sealed class SyncPointRegistry
{
    // Signals that have been emitted: "@channel.signal_name"
    private readonly HashSet<string> _emittedSignals = new();

    // Channels waiting for signals: channel_name -> set of signals
    private readonly Dictionary<string, HashSet<string>> _waitingSets = new();

    /// <summary>
    /// Record that a channel emitted a signal.
    /// </summary>
    public void Emit(string channelName, string signalName)
    {
        var fullSignal = $"@{channelName}.{signalName}";
        _emittedSignals.Add(fullSignal);
    }

    /// <summary>
    /// Record that a channel is waiting for signals.
    /// </summary>
    public void RegisterWait(string channelName, IEnumerable<string> signals)
    {
        _waitingSets[channelName] = new HashSet<string>(signals);
    }

    /// <summary>
    /// Check if a channel's wait conditions are satisfied.
    /// </summary>
    public bool CanProceed(string channelName, WaitMode mode)
    {
        if (!_waitingSets.TryGetValue(channelName, out var signals))
            return true;

        return mode switch
        {
            WaitMode.All => signals.All(s => _emittedSignals.Contains(s)),
            WaitMode.Any => signals.Any(s => _emittedSignals.Contains(s)),
            _ => false
        };
    }

    /// <summary>
    /// Clear wait set for a channel that can now proceed.
    /// </summary>
    public void ClearWait(string channelName)
    {
        _waitingSets.Remove(channelName);
    }

    /// <summary>
    /// Get all channels that are waiting.
    /// </summary>
    public IEnumerable<string> GetWaitingChannels()
    {
        return _waitingSets.Keys;
    }

    /// <summary>
    /// Check for deadlock: all active channels are waiting and none can proceed.
    /// </summary>
    public bool DetectDeadlock(IEnumerable<string> activeChannels)
    {
        var active = activeChannels.ToList();
        if (active.Count == 0) return false;

        // If all active channels are waiting
        if (!active.All(c => _waitingSets.ContainsKey(c)))
            return false;

        // And none can proceed
        return !active.Any(c => CanProceed(c, WaitMode.All));
    }
}

public enum WaitMode
{
    All,  // all_of
    Any   // any_of
}
```

### 6.3 Channel Scheduler

```csharp
// Execution/Channels/ChannelScheduler.cs

/// <summary>
/// Cooperative scheduler for multi-channel execution.
/// Executes one action per channel per tick in round-robin order.
/// </summary>
public sealed class ChannelScheduler
{
    private readonly IActionHandlerRegistry _handlers;
    private readonly Dictionary<string, ChannelState> _channels = new();
    private readonly SyncPointRegistry _syncPoints = new();

    public ChannelScheduler(IActionHandlerRegistry handlers)
    {
        _handlers = handlers;
    }

    /// <summary>
    /// Execute all channels to completion.
    /// </summary>
    public async ValueTask<ExecutionResult> ExecuteAsync(
        AbmlDocument document,
        ExecutionContext context,
        CancellationToken ct = default)
    {
        // Initialize channel states
        foreach (var (name, channel) in document.Channels)
        {
            var channelScope = context.RootScope.CreateChild();
            _channels[name] = new ChannelState
            {
                Name = name,
                Actions = channel.Actions,
                Scope = channelScope
            };
        }

        // Main execution loop
        while (AnyChannelActive())
        {
            ct.ThrowIfCancellationRequested();

            var madeProgress = false;

            // Round-robin through channels
            foreach (var (name, state) in _channels)
            {
                if (state.Status != ChannelStatus.Running)
                    continue;

                // Check if waiting
                if (state.WaitingFor.Count > 0)
                {
                    if (!_syncPoints.CanProceed(name, WaitMode.All))
                        continue;

                    // Can proceed - clear wait state
                    state.WaitingFor.Clear();
                    _syncPoints.ClearWait(name);
                }

                // Execute one action
                if (state.HasMoreActions)
                {
                    var result = await ExecuteOneActionAsync(state, context, ct);
                    madeProgress = true;

                    if (!HandleActionResult(state, result))
                        continue;
                }
                else
                {
                    state.Status = ChannelStatus.Completed;
                }
            }

            // Deadlock detection
            if (!madeProgress && AnyChannelActive())
            {
                var activeChannels = _channels
                    .Where(c => c.Value.Status == ChannelStatus.Running)
                    .Select(c => c.Key);

                if (_syncPoints.DetectDeadlock(activeChannels))
                {
                    return ExecutionResult.Failure(
                        "Deadlock detected: all channels waiting for signals that will never arrive",
                        context.Logs);
                }
            }
        }

        return ExecutionResult.Success(null, context.Logs);
    }

    private async ValueTask<ActionResult> ExecuteOneActionAsync(
        ChannelState state,
        ExecutionContext context,
        CancellationToken ct)
    {
        var action = state.CurrentAction!;
        state.Advance();

        // Push channel frame
        context.CallStack.Push(state.Name, state.Scope);

        try
        {
            var handler = _handlers.GetHandler(action);
            if (handler == null)
                return ActionResult.Error($"No handler for: {action.GetType().Name}");

            return await handler.ExecuteAsync(action, context, ct);
        }
        finally
        {
            context.CallStack.Pop();
        }
    }

    private bool HandleActionResult(ChannelState state, ActionResult result)
    {
        switch (result)
        {
            case EmitResult emit:
                _syncPoints.Emit(state.Name, emit.Signal);
                return true;

            case WaitResult wait:
                state.WaitingFor.UnionWith(wait.Signals);
                _syncPoints.RegisterWait(state.Name, wait.Signals);
                return false;  // Don't advance, waiting

            case ErrorResult error:
                state.Status = ChannelStatus.Failed;
                return false;

            default:
                return true;
        }
    }

    private bool AnyChannelActive()
    {
        return _channels.Values.Any(c =>
            c.Status == ChannelStatus.Running &&
            (c.HasMoreActions || c.WaitingFor.Count > 0));
    }
}
```

### 6.4 Emit and WaitFor Actions

```csharp
// Documents/Actions/ActionNode.cs additions

/// <summary>
/// Emit a sync point signal.
/// </summary>
public sealed record EmitAction(string Signal) : ActionNode;

/// <summary>
/// Wait for sync point signal(s).
/// </summary>
public sealed record WaitForAction(
    IReadOnlyList<string> Signals,
    WaitMode Mode = WaitMode.All,
    TimeSpan? Timeout = null) : ActionNode;

// Execution/ExecutionResult.cs additions

/// <summary>
/// Channel emitted a sync point signal.
/// </summary>
public sealed record EmitResult(string Signal) : ActionResult;

/// <summary>
/// Channel is waiting for sync point signal(s).
/// </summary>
public sealed record WaitResult(
    IReadOnlyList<string> Signals,
    WaitMode Mode = WaitMode.All) : ActionResult;
```

### 6.5 Emit/WaitFor Handlers

```csharp
// Execution/Handlers/EmitHandler.cs

public sealed class EmitHandler : IActionHandler
{
    public bool CanHandle(ActionNode action) => action is EmitAction;

    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var emit = (EmitAction)action;

        // Log the emit for debugging
        context.Logs.Add(new LogEntry(
            "sync",
            $"emit: {emit.Signal}",
            DateTime.UtcNow));

        return ValueTask.FromResult<ActionResult>(new EmitResult(emit.Signal));
    }
}

// Execution/Handlers/WaitForHandler.cs

public sealed class WaitForHandler : IActionHandler
{
    public bool CanHandle(ActionNode action) => action is WaitForAction;

    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var waitFor = (WaitForAction)action;

        // Log the wait for debugging
        context.Logs.Add(new LogEntry(
            "sync",
            $"wait_for: {string.Join(", ", waitFor.Signals)}",
            DateTime.UtcNow));

        return ValueTask.FromResult<ActionResult>(
            new WaitResult(waitFor.Signals, waitFor.Mode));
    }
}
```

### 6.6 YAML Syntax

```yaml
# Multi-channel document with sync points
version: "2.0"
metadata:
  id: choreographed_cutscene

channels:
  camera:
    - log: "Camera: Establishing shot"
    - wait_for: @actors.in_position
    - log: "Camera: Crane up"
    - emit: crane_complete
    - wait_for: @actors.dialogue_done
    - log: "Camera: Fade out"
    - emit: scene_complete

  actors:
    - log: "Actors: Walking to marks"
    - emit: in_position
    - wait_for: @camera.crane_complete
    - log: "Actors: Starting dialogue"
    - log: "Actors: Dialogue complete"
    - emit: dialogue_done

  audio:
    - wait_for: @actors.in_position
    - log: "Audio: Start ambient"
    - wait_for:
        signals:
          - @actors.dialogue_done
          - @camera.scene_complete
        mode: any_of
    - log: "Audio: Fade music"
```

### 6.7 Test Cases

```csharp
[Fact]
public async Task Channels_ExecuteInRoundRobinOrder()
{
    var yaml = """
        version: "2.0"
        metadata:
          id: round_robin
        channels:
          a:
            - log: "A1"
            - log: "A2"
          b:
            - log: "B1"
            - log: "B2"
        """;

    var result = await _executor.ExecuteAsync(doc);

    // Round-robin: A1, B1, A2, B2
    Assert.Equal("A1", result.Logs[0].Message);
    Assert.Equal("B1", result.Logs[1].Message);
    Assert.Equal("A2", result.Logs[2].Message);
    Assert.Equal("B2", result.Logs[3].Message);
}

[Fact]
public async Task Channels_WaitFor_BlocksUntilEmit()
{
    var yaml = """
        version: "2.0"
        metadata:
          id: sync_test
        channels:
          producer:
            - log: "Producing"
            - emit: ready
          consumer:
            - wait_for: @producer.ready
            - log: "Consuming"
        """;

    var result = await _executor.ExecuteAsync(doc);

    // Consumer waits, producer runs, consumer proceeds
    Assert.Equal("Producing", result.Logs[0].Message);
    Assert.Equal("Consuming", result.Logs[1].Message);
}

[Fact]
public async Task Channels_DetectsDeadlock()
{
    var yaml = """
        version: "2.0"
        metadata:
          id: deadlock
        channels:
          a:
            - wait_for: @b.signal  # A waits for B
          b:
            - wait_for: @a.signal  # B waits for A - DEADLOCK
        """;

    var result = await _executor.ExecuteAsync(doc);

    Assert.False(result.IsSuccess);
    Assert.Contains("Deadlock", result.Error);
}

[Fact]
public async Task Channels_ExecutionIsDeterministic()
{
    var yaml = """
        version: "2.0"
        metadata:
          id: determinism
        channels:
          a:
            - log: "A1"
            - emit: a1_done
            - log: "A2"
          b:
            - wait_for: @a.a1_done
            - log: "B1"
            - log: "B2"
        """;

    // Run multiple times
    var results = new List<string[]>();
    for (var i = 0; i < 10; i++)
    {
        var result = await _executor.ExecuteAsync(doc);
        results.Add(result.Logs.Select(l => l.Message).ToArray());
    }

    // All runs should produce identical log order
    var first = results[0];
    Assert.All(results, r => Assert.Equal(first, r));
}
```

---

## Implementation Order

1. **Scope semantics** (Correction 1) - Foundation for all other fixes
2. **Call handler scope** (Correction 2) - Depends on scope semantics
3. **ToBool opcode** (Correction 5) - Small, isolated change
4. **Function call convention** (Correction 4) - Requires compiler changes
5. **Error handling** (Correction 3) - New infrastructure
6. **Multi-channel** (Correction 6) - Major new feature

---

## Testing Strategy

Each correction should include:
- Unit tests for the specific fix
- Regression tests to ensure existing behavior unchanged
- Edge case tests for the new behavior
- Integration tests combining multiple corrections

Total new tests: ~50-75

---

*This document should be reviewed and approved before implementation begins.*
