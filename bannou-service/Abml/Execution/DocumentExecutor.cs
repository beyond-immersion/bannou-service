// ═══════════════════════════════════════════════════════════════════════════
// ABML Document Executor
// Tree-walking interpreter for ABML documents.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Runtime;

namespace BeyondImmersion.BannouService.Abml.Execution;

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
                    // Transfer control to another flow
                    if (!context.Document.Flows.TryGetValue(gotoResult.FlowName, out var targetFlow))
                    {
                        return ActionResult.Error($"Flow not found: {gotoResult.FlowName}");
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

                    // Execute the target flow (tail call)
                    return await ExecuteFlowAsync(targetFlow, context, ct);

                case ReturnResult:
                case CompleteResult:
                    return result;

                case ErrorResult errorResult:
                    // Try to handle error with on_error handlers
                    var handled = await TryHandleErrorAsync(
                        flow, errorResult, action, context, ct);
                    if (!handled)
                    {
                        return errorResult;  // Propagate unhandled error
                    }
                    // Error was handled, continue with next action
                    break;
            }
        }

        // Flow completed normally
        return ActionResult.Continue;
    }

    private async ValueTask<bool> TryHandleErrorAsync(
        Flow flow,
        ErrorResult errorResult,
        ActionNode failedAction,
        ExecutionContext context,
        CancellationToken ct)
    {
        // No error handler? Cannot handle
        if (flow.OnError.Count == 0)
        {
            return false;
        }

        // Create error info and set _error variable
        var errorInfo = ErrorInfo.FromErrorResult(
            errorResult.Message,
            flow.Name,
            failedAction.GetType().Name);

        var scope = context.CallStack.Current?.Scope ?? context.RootScope;
        scope.SetValue("_error", errorInfo.ToDictionary());

        // Execute error handlers
        foreach (var errorAction in flow.OnError)
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
        }

        return true;  // Error was handled
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
