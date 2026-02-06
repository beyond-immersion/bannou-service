// ═══════════════════════════════════════════════════════════════════════════
// ABML Document Executor
// Tree-walking interpreter for ABML documents.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Parser;
using BeyondImmersion.BannouService.Abml.Runtime;

namespace BeyondImmersion.BannouService.Abml.Execution;

/// <summary>
/// Result of attempting to handle an error.
/// </summary>
public enum ErrorHandleResult
{
    /// <summary>Error was not handled - propagate to caller.</summary>
    NotHandled,

    /// <summary>Error was handled, stop flow execution (default behavior).</summary>
    HandledStop,

    /// <summary>Error was handled and _error_handled=true, continue to next action.</summary>
    HandledContinue
}

/// <summary>
/// Interface for executing ABML documents.
/// </summary>
public interface IDocumentExecutor
{
    /// <summary>
    /// Executes an ABML document starting from the specified flow.
    /// </summary>
    /// <param name="document">The document to execute.</param>
    /// <param name="startFlow">The flow to start execution from.</param>
    /// <param name="initialScope">Optional initial variable scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    ValueTask<ExecutionResult> ExecuteAsync(
        AbmlDocument document,
        string startFlow,
        IVariableScope? initialScope = null,
        CancellationToken ct = default);

    /// <summary>
    /// Executes a loaded ABML document with resolved imports.
    /// </summary>
    /// <param name="loadedDocument">The loaded document with imports.</param>
    /// <param name="startFlow">The flow to start execution from (can be namespaced).</param>
    /// <param name="initialScope">Optional initial variable scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    ValueTask<ExecutionResult> ExecuteAsync(
        LoadedDocument loadedDocument,
        string startFlow,
        IVariableScope? initialScope = null,
        CancellationToken ct = default);
}

/// <summary>
/// Tree-walking interpreter for ABML documents.
/// </summary>
public sealed class DocumentExecutor : IDocumentExecutor
{
    private readonly IExpressionEvaluator _evaluator;
    private readonly IActionHandlerRegistry _handlers;

    /// <summary>
    /// Creates a new document executor with default configuration.
    /// </summary>
    public DocumentExecutor()
        : this(new ExpressionEvaluator(), ActionHandlerRegistry.CreateWithBuiltins())
    {
    }

    /// <summary>
    /// Creates a new document executor with custom evaluator and handlers.
    /// </summary>
    /// <param name="evaluator">Expression evaluator.</param>
    /// <param name="handlers">Action handler registry.</param>
    public DocumentExecutor(IExpressionEvaluator evaluator, IActionHandlerRegistry handlers)
    {
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    /// <inheritdoc/>
    public async ValueTask<ExecutionResult> ExecuteAsync(
        AbmlDocument document,
        string startFlow,
        IVariableScope? initialScope = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(startFlow);

        // Find the start flow
        if (!document.Flows.TryGetValue(startFlow, out var flow))
        {
            return ExecutionResult.Failure($"Flow not found: {startFlow}");
        }

        // Create execution context
        var rootScope = initialScope ?? new VariableScope();
        var context = new ExecutionContext
        {
            Document = document,
            RootScope = rootScope,
            Evaluator = _evaluator,
            Handlers = _handlers
        };

        // Initialize context variables from document
        InitializeContextVariables(document, context);

        // Push initial flow frame
        context.CallStack.Push(startFlow, rootScope);

        try
        {
            var result = await ExecuteFlowAsync(flow, context, ct);

            return result switch
            {
                CompleteResult complete => ExecutionResult.Success(complete.Value, context.Logs),
                ReturnResult returnResult => ExecutionResult.Success(returnResult.Value, context.Logs),
                ErrorResult error => ExecutionResult.Failure(error.Message, context.Logs),
                _ => ExecutionResult.Success(null, context.Logs)
            };
        }
        catch (OperationCanceledException)
        {
            return ExecutionResult.Failure("Execution cancelled", context.Logs);
        }
        catch (Exception ex)
        {
            return ExecutionResult.Failure($"Execution error: {ex.Message}", context.Logs);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ExecutionResult> ExecuteAsync(
        LoadedDocument loadedDocument,
        string startFlow,
        IVariableScope? initialScope = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(loadedDocument);
        ArgumentNullException.ThrowIfNull(startFlow);

        var document = loadedDocument.Document;

        // Find the start flow (supports namespaced imports)
        // Also get the resolved document for context-relative resolution
        if (!loadedDocument.TryResolveFlow(startFlow, out var flow, out var startDocument) || flow == null)
        {
            return ExecutionResult.Failure($"Flow not found: {startFlow}");
        }

        // Create execution context with LoadedDocument for import resolution
        var rootScope = initialScope ?? new VariableScope();
        var context = new ExecutionContext
        {
            Document = document,
            LoadedDocument = loadedDocument,
            // Set CurrentDocument to the document containing the start flow
            // This enables context-relative resolution from the start
            CurrentDocument = startDocument ?? loadedDocument,
            RootScope = rootScope,
            Evaluator = _evaluator,
            Handlers = _handlers
        };

        // Initialize context variables from document
        InitializeContextVariables(document, context);

        // Push initial flow frame
        context.CallStack.Push(startFlow, rootScope);

        try
        {
            var result = await ExecuteFlowAsync(flow, context, ct);

            return result switch
            {
                CompleteResult complete => ExecutionResult.Success(complete.Value, context.Logs),
                ReturnResult returnResult => ExecutionResult.Success(returnResult.Value, context.Logs),
                ErrorResult error => ExecutionResult.Failure(error.Message, context.Logs),
                _ => ExecutionResult.Success(null, context.Logs)
            };
        }
        catch (OperationCanceledException)
        {
            return ExecutionResult.Failure("Execution cancelled", context.Logs);
        }
        catch (Exception ex)
        {
            return ExecutionResult.Failure($"Execution error: {ex.Message}", context.Logs);
        }
    }

    private static void InitializeContextVariables(AbmlDocument document, ExecutionContext context)
    {
        if (document.Context?.Variables == null)
        {
            return;
        }

        foreach (var (name, definition) in document.Context.Variables)
        {
            if (definition.Default != null)
            {
                context.RootScope.SetValue(name, definition.Default);
            }
            else if (definition.Source != null)
            {
                // Source expressions will be evaluated when accessed
                // For now, just initialize to null
                context.RootScope.SetValue(name, null);
            }
        }
    }

    private async ValueTask<ActionResult> ExecuteFlowAsync(
        Flow flow, ExecutionContext context, CancellationToken ct)
    {
        foreach (var action in flow.Actions)
        {
            ct.ThrowIfCancellationRequested();

            var result = await ExecuteActionAsync(action, context, ct);

            switch (result)
            {
                case GotoResult gotoResult:
                    // Transfer control to another flow (supports namespaced imports)
                    // Also get the resolved document for context-relative resolution
                    if (!context.TryResolveFlow(gotoResult.FlowName, out var targetFlow, out var targetDocument) || targetFlow == null)
                    {
                        // Flow not found is an error - try to handle with on_error
                        var gotoError = ActionResult.Error($"Flow not found: {gotoResult.FlowName}");
                        var gotoHandleResult = await TryHandleErrorAsync(
                            flow, (ErrorResult)gotoError, action, context, ct);

                        switch (gotoHandleResult)
                        {
                            case ErrorHandleResult.NotHandled:
                                return gotoError;  // Propagate unhandled error

                            case ErrorHandleResult.HandledContinue:
                                // _error_handled was set - continue to next action
                                continue;

                            case ErrorHandleResult.HandledStop:
                            default:
                                // Error was handled - stop flow
                                return ActionResult.Continue;
                        }
                    }

                    // Pop current frame and push new one
                    context.CallStack.Pop();
                    var gotoScope = context.RootScope.CreateChild();

                    // Set args in the new scope
                    if (gotoResult.Args != null)
                    {
                        foreach (var (key, value) in gotoResult.Args)
                        {
                            gotoScope.SetValue(key, value);
                        }
                    }

                    context.CallStack.Push(gotoResult.FlowName, gotoScope);

                    // Update document context for goto (tail call switches context)
                    if (targetDocument != null)
                    {
                        context.CurrentDocument = targetDocument;
                    }

                    // Execute the target flow (tail call)
                    return await ExecuteFlowAsync((Flow)targetFlow, context, ct);

                case ReturnResult:
                case CompleteResult:
                    return result;

                case ErrorResult errorResult:
                    // Try to handle error with on_error handlers
                    var handleResult = await TryHandleErrorAsync(
                        flow, errorResult, action, context, ct);

                    switch (handleResult)
                    {
                        case ErrorHandleResult.NotHandled:
                            return errorResult;  // Propagate unhandled error

                        case ErrorHandleResult.HandledContinue:
                            // _error_handled was set to true - continue to next action
                            break;

                        case ErrorHandleResult.HandledStop:
                        default:
                            // Error was handled but _error_handled not set - stop flow
                            return ActionResult.Continue;
                    }
                    break;
            }
        }

        // Flow completed normally
        return ActionResult.Continue;
    }

    private async ValueTask<ErrorHandleResult> TryHandleActionErrorAsync(
        ActionNode failedAction,
        ErrorResult errorResult,
        string flowName,
        ExecutionContext context,
        CancellationToken ct)
    {
        // Check if action supports on_error
        if (failedAction is not IHasOnError { OnError: { Count: > 0 } onError })
        {
            return ErrorHandleResult.NotHandled;
        }

        // Create error info and set _error variable
        var errorInfo = ErrorInfo.FromErrorResult(
            errorResult.Message,
            flowName,
            failedAction.GetType().Name);

        var scope = context.CallStack.Current?.Scope ?? context.RootScope;
        scope.SetValue("_error", errorInfo.ToDictionary());

        // Execute action-level error handlers
        var handled = await ExecuteErrorHandlersAsync(onError, context, ct);
        if (!handled)
        {
            return ErrorHandleResult.NotHandled;
        }

        // Check if _error_handled was set to true for continuation
        return CheckErrorHandledFlag(scope);
    }

    private async ValueTask<ErrorHandleResult> TryHandleFlowErrorAsync(
        Flow flow,
        ErrorResult errorResult,
        ActionNode failedAction,
        ExecutionContext context,
        CancellationToken ct)
    {
        // No flow-level error handler? Cannot handle
        if (flow.OnError.Count == 0)
        {
            return ErrorHandleResult.NotHandled;
        }

        // Create error info and set _error variable
        var errorInfo = ErrorInfo.FromErrorResult(
            errorResult.Message,
            flow.Name,
            failedAction.GetType().Name);

        var scope = context.CallStack.Current?.Scope ?? context.RootScope;
        scope.SetValue("_error", errorInfo.ToDictionary());

        // Execute flow-level error handlers
        var handled = await ExecuteErrorHandlersAsync(flow.OnError, context, ct);
        if (!handled)
        {
            return ErrorHandleResult.NotHandled;
        }

        // Check if _error_handled was set to true for continuation
        return CheckErrorHandledFlag(scope);
    }

    private async ValueTask<ErrorHandleResult> TryHandleDocumentErrorAsync(
        ErrorResult errorResult,
        ActionNode failedAction,
        string flowName,
        ExecutionContext context,
        CancellationToken ct)
    {
        // No document-level error handler? Cannot handle
        if (context.Document.OnError == null)
        {
            return ErrorHandleResult.NotHandled;
        }

        // Find the error handler flow
        if (!context.Document.Flows.TryGetValue(context.Document.OnError, out var errorFlow))
        {
            return ErrorHandleResult.NotHandled;
        }

        // Create error info and set _error variable
        var errorInfo = ErrorInfo.FromErrorResult(
            errorResult.Message,
            flowName,
            failedAction.GetType().Name);

        var scope = context.CallStack.Current?.Scope ?? context.RootScope;
        scope.SetValue("_error", errorInfo.ToDictionary());

        // Push error handler flow and execute it
        var errorScope = scope.CreateChild();
        context.CallStack.Push(context.Document.OnError, errorScope);

        try
        {
            var result = await ExecuteFlowAsync(errorFlow, context, ct);
            if (result is ErrorResult)
            {
                return ErrorHandleResult.NotHandled;
            }

            // Check if _error_handled was set to true for continuation
            return CheckErrorHandledFlag(errorScope);
        }
        finally
        {
            context.CallStack.Pop();
        }
    }

    /// <summary>
    /// Checks if the _error_handled flag is set to true in the scope.
    /// </summary>
    /// <param name="scope">The scope to check.</param>
    /// <returns>HandledContinue if _error_handled is true, HandledStop otherwise.</returns>
    private static ErrorHandleResult CheckErrorHandledFlag(IVariableScope scope)
    {
        var errorHandled = scope.GetValue("_error_handled");
        return errorHandled is true
            ? ErrorHandleResult.HandledContinue
            : ErrorHandleResult.HandledStop;
    }

    private async ValueTask<bool> ExecuteErrorHandlersAsync(
        IReadOnlyList<ActionNode> handlers,
        ExecutionContext context,
        CancellationToken ct)
    {
        foreach (var errorAction in handlers)
        {
            ct.ThrowIfCancellationRequested();

            var result = await ExecuteActionAsync(errorAction, context, ct);

            // If error handler itself errors, propagate
            if (result is ErrorResult)
            {
                return false;
            }

            // If error handler returns, that's fine
            if (result is ReturnResult or CompleteResult)
            {
                break;
            }

            // If error handler uses goto, validate the target flow exists
            if (result is GotoResult gotoResult)
            {
                if (!context.TryResolveFlow(gotoResult.FlowName, out _))
                {
                    // Goto to non-existent flow is an error
                    return false;
                }
                // Valid goto from error handler - stop executing further handlers
                break;
            }
        }

        return true;  // Error was handled
    }

    /// <summary>
    /// Tries to handle an error through the 3-level chain:
    /// Action-level → Flow-level → Document-level.
    /// Returns whether the error was handled and whether to continue execution.
    /// </summary>
    private async ValueTask<ErrorHandleResult> TryHandleErrorAsync(
        Flow flow,
        ErrorResult errorResult,
        ActionNode failedAction,
        ExecutionContext context,
        CancellationToken ct)
    {
        // Level 1: Action-level on_error
        var actionResult = await TryHandleActionErrorAsync(failedAction, errorResult, flow.Name, context, ct);
        if (actionResult != ErrorHandleResult.NotHandled)
        {
            return actionResult;
        }

        // Level 2: Flow-level on_error
        var flowResult = await TryHandleFlowErrorAsync(flow, errorResult, failedAction, context, ct);
        if (flowResult != ErrorHandleResult.NotHandled)
        {
            return flowResult;
        }

        // Level 3: Document-level on_error
        var docResult = await TryHandleDocumentErrorAsync(errorResult, failedAction, flow.Name, context, ct);
        if (docResult != ErrorHandleResult.NotHandled)
        {
            return docResult;
        }

        return ErrorHandleResult.NotHandled;
    }

    private async ValueTask<ActionResult> ExecuteActionAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var handler = _handlers.GetHandler(action);
        if (handler == null)
        {
            return ActionResult.Error($"No handler for action type: {action.GetType().Name}");
        }

        return await handler.ExecuteAsync(action, context, ct);
    }
}
