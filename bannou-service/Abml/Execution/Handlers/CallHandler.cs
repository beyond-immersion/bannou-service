// ═══════════════════════════════════════════════════════════════════════════
// ABML Call Handler
// Executes flow call actions (subroutine with return).
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Parser;

namespace BeyondImmersion.BannouService.Abml.Execution.Handlers;

/// <summary>
/// Handles call actions (flow call with return).
/// </summary>
public sealed class CallHandler : IActionHandler
{
    /// <inheritdoc/>
    public bool CanHandle(ActionNode action) => action is CallAction;

    /// <inheritdoc/>
    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var callAction = (CallAction)action;
        var flowName = callAction.Flow;

        // Find the target flow (supports namespaced imports like "common.my_flow")
        // Also get the resolved document for context-relative resolution
        if (!context.TryResolveFlow(flowName, out var targetFlow, out var resolvedDocument) || targetFlow == null)
        {
            return ActionResult.Error($"Flow not found: {flowName}");
        }

        // Get current scope
        var currentScope = context.CallStack.Current?.Scope ?? context.RootScope;

        // Create CHILD scope - called flow gets its own namespace
        // but can still READ from parent (for parameters passed via variables)
        var callScope = currentScope.CreateChild();

        // Push new frame with child scope for isolation
        context.CallStack.Push(flowName, callScope);

        // Track document context change for context-relative resolution
        var previousDocument = context.CurrentDocument;
        if (resolvedDocument != null && resolvedDocument != previousDocument)
        {
            context.CurrentDocument = resolvedDocument;
        }

        try
        {
            // Execute the called flow
            foreach (var flowAction in targetFlow.Actions)
            {
                ct.ThrowIfCancellationRequested();

                var handler = context.Handlers.GetHandler(flowAction);
                if (handler == null)
                {
                    return ActionResult.Error($"No handler for action type: {flowAction.GetType().Name}");
                }

                var result = await handler.ExecuteAsync(flowAction, context, ct);

                switch (result)
                {
                    case ReturnResult returnResult:
                        // Return from called flow - store value and continue
                        if (returnResult.Value != null)
                        {
                            currentScope.SetValue("_result", returnResult.Value);
                        }
                        return ActionResult.Continue;

                    case GotoResult gotoResult:
                        // Goto from within called flow - resolve NOW while we have correct context
                        // then return with the pre-resolved flow name (using full path from root)
                        if (context.TryResolveFlow(gotoResult.FlowName, out _, out var gotoDocument))
                        {
                            // Build the fully-qualified flow name from root perspective
                            // by finding the path from root to the target document
                            var resolvedFlowName = ResolveFullyQualifiedFlowName(
                                context.LoadedDocument,
                                gotoDocument,
                                gotoResult.FlowName);
                            return new GotoResult(resolvedFlowName, gotoResult.Args);
                        }
                        // If resolution fails, return the original (will error at executor level)
                        return gotoResult;

                    case ErrorResult:
                    case CompleteResult:
                        return result;
                }
            }

            // Flow completed normally
            return ActionResult.Continue;
        }
        finally
        {
            // Restore previous document context
            context.CurrentDocument = previousDocument;
            // Pop the call frame
            context.CallStack.Pop();
        }
    }

    /// <summary>
    /// Resolves a flow name to its fully-qualified path from the root document.
    /// This allows goto targets to be resolved from the correct context,
    /// then executed by the main executor from the root context.
    /// </summary>
    private static string ResolveFullyQualifiedFlowName(
        LoadedDocument? root,
        LoadedDocument? target,
        string flowName)
    {
        if (root == null || target == null || root == target)
        {
            // Same document or no context - return as-is
            return flowName;
        }

        // Extract the base flow name (without any namespace prefix)
        var dotIndex = flowName.LastIndexOf('.');
        var baseFlowName = dotIndex >= 0 ? flowName[(dotIndex + 1)..] : flowName;

        // Find path from root to target document
        var path = FindImportPath(root, target, []);
        if (path == null)
        {
            // Target not reachable from root - return as-is and let it fail
            return flowName;
        }

        // Build fully qualified name: alias1.alias2.flowName
        return path.Count > 0
            ? string.Join(".", path) + "." + baseFlowName
            : baseFlowName;
    }

    /// <summary>
    /// Finds the import path from source to target document.
    /// Returns list of aliases forming the path, or null if not reachable.
    /// </summary>
    private static List<string>? FindImportPath(
        LoadedDocument source,
        LoadedDocument target,
        HashSet<LoadedDocument> visited)
    {
        if (source == target)
        {
            return [];
        }

        if (!visited.Add(source))
        {
            return null;  // Already visited - prevent cycles
        }

        foreach (var (alias, imported) in source.Imports)
        {
            var subPath = FindImportPath(imported, target, visited);
            if (subPath != null)
            {
                subPath.Insert(0, alias);
                return subPath;
            }
        }

        return null;  // Target not found in this branch
    }
}
