// =============================================================================
// ABML Document Merger
// Flattens a LoadedDocument tree into a single merged AbmlDocument for
// bytecode compilation.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorCompiler.Parser;

namespace BeyondImmersion.Bannou.Behavior.Compiler;

/// <summary>
/// Merges a LoadedDocument tree into a single flat AbmlDocument.
/// All imported flows are renamed with their namespace prefix.
/// This enables bytecode compilation to produce a single self-contained model.
/// </summary>
public sealed class DocumentMerger
{
    /// <summary>
    /// Merges a LoadedDocument tree into a single flat AbmlDocument.
    /// </summary>
    /// <param name="loaded">The loaded document with resolved imports.</param>
    /// <returns>A merged document with all flows flattened and renamed.</returns>
    public AbmlDocument Merge(LoadedDocument loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);

        var mergedFlows = new Dictionary<string, Flow>();
        var mergedGoals = new Dictionary<string, GoapGoalDefinition>();

        // Add local flows (no prefix) with rewritten references
        foreach (var (name, flow) in loaded.Document.Flows)
        {
            var rewrittenFlow = RewriteFlow(flow, loaded, "");
            mergedFlows[name] = rewrittenFlow;
        }

        // Add local goals (no prefix)
        foreach (var (name, goal) in loaded.Document.Goals)
        {
            mergedGoals[name] = goal;
        }

        // Recursively add imported flows and goals (with prefix)
        MergeImports(loaded, "", mergedFlows, mergedGoals);

        // Merge context variables from all documents
        var mergedContext = MergeContexts(loaded);

        return new AbmlDocument
        {
            Version = loaded.Document.Version,
            Metadata = loaded.Document.Metadata,
            Imports = [], // No imports in merged document
            Context = mergedContext,
            Goals = mergedGoals,
            Flows = mergedFlows,
            OnError = loaded.Document.OnError
        };
    }

    /// <summary>
    /// Recursively merges imports into the flat flow dictionary.
    /// </summary>
    private void MergeImports(
        LoadedDocument doc,
        string prefix,
        Dictionary<string, Flow> targetFlows,
        Dictionary<string, GoapGoalDefinition> targetGoals)
    {
        foreach (var (alias, imported) in doc.Imports)
        {
            var newPrefix = string.IsNullOrEmpty(prefix)
                ? alias
                : $"{prefix}.{alias}";

            // Add imported flows with namespace prefix
            foreach (var (name, flow) in imported.Document.Flows)
            {
                var qualifiedName = $"{newPrefix}.{name}";
                var rewrittenFlow = RewriteFlow(flow, imported, newPrefix);
                targetFlows[qualifiedName] = rewrittenFlow;
            }

            // Add imported goals with namespace prefix
            foreach (var (name, goal) in imported.Document.Goals)
            {
                var qualifiedName = $"{newPrefix}.{name}";
                targetGoals[qualifiedName] = goal;
            }

            // Recurse for nested imports
            MergeImports(imported, newPrefix, targetFlows, targetGoals);
        }
    }

    /// <summary>
    /// Rewrites a flow to use fully-qualified flow references.
    /// </summary>
    private Flow RewriteFlow(Flow flow, LoadedDocument context, string currentPrefix)
    {
        return new Flow
        {
            Name = string.IsNullOrEmpty(currentPrefix) ? flow.Name : $"{currentPrefix}.{flow.Name}",
            Triggers = flow.Triggers,
            Goap = flow.Goap,
            Actions = RewriteActions(flow.Actions, context, currentPrefix),
            OnError = RewriteActions(flow.OnError, context, currentPrefix)
        };
    }

    /// <summary>
    /// Rewrites a list of actions to use fully-qualified flow references.
    /// </summary>
    private IReadOnlyList<ActionNode> RewriteActions(
        IReadOnlyList<ActionNode> actions,
        LoadedDocument context,
        string currentPrefix)
    {
        if (actions.Count == 0)
        {
            return actions;
        }

        var result = new List<ActionNode>(actions.Count);
        foreach (var action in actions)
        {
            result.Add(RewriteAction(action, context, currentPrefix));
        }
        return result;
    }

    /// <summary>
    /// Rewrites a single action to use fully-qualified flow references.
    /// </summary>
    private ActionNode RewriteAction(ActionNode action, LoadedDocument context, string currentPrefix)
    {
        return action switch
        {
            // Flow reference actions - need rewriting
            CallAction call => new CallAction(ResolveFlowReference(call.Flow, context, currentPrefix)),

            GotoAction gotoAction => new GotoAction(
                ResolveFlowReference(gotoAction.Flow, context, currentPrefix),
                gotoAction.Args),

            // Actions with nested action lists - need recursive processing
            CondAction cond => new CondAction(
                cond.Branches.Select(b => new CondBranch(
                    b.When,
                    RewriteActions(b.Then, context, currentPrefix))).ToList(),
                cond.ElseBranch != null
                    ? RewriteActions(cond.ElseBranch, context, currentPrefix)
                    : null),

            ForEachAction forEach => new ForEachAction(
                forEach.Variable,
                forEach.Collection,
                RewriteActions(forEach.Do, context, currentPrefix)),

            RepeatAction repeat => new RepeatAction(
                repeat.Times,
                RewriteActions(repeat.Do, context, currentPrefix)),

            DomainAction domain => new DomainAction(
                domain.Name,
                domain.Parameters,
                domain.OnError != null
                    ? RewriteActions(domain.OnError, context, currentPrefix)
                    : null),

            // All other actions pass through unchanged
            _ => action
        };
    }

    /// <summary>
    /// Resolves a flow reference to its fully-qualified name in the merged document.
    /// </summary>
    /// <param name="flowRef">The flow reference (e.g., "my_flow" or "lib.helper").</param>
    /// <param name="context">The document context for resolution.</param>
    /// <param name="currentPrefix">The current namespace prefix.</param>
    /// <returns>The fully-qualified flow name.</returns>
    private static string ResolveFlowReference(string flowRef, LoadedDocument context, string currentPrefix)
    {
        // Try to resolve the reference in the current context
        if (!context.TryResolveFlow(flowRef, out _, out var resolvedDoc))
        {
            // Flow not found - return as-is (will fail at runtime)
            return flowRef;
        }

        // If resolved to the same document, apply the current prefix
        if (resolvedDoc == context)
        {
            return string.IsNullOrEmpty(currentPrefix) ? flowRef : $"{currentPrefix}.{flowRef}";
        }

        // Resolved to an imported document - need to find the path from root
        var path = FindImportPath(context, resolvedDoc, []);
        if (path == null)
        {
            // Shouldn't happen if TryResolveFlow succeeded, but handle gracefully
            return flowRef;
        }

        // Extract the base flow name (the part after the last dot, or the whole thing)
        var dotIndex = flowRef.LastIndexOf('.');
        var baseFlowName = dotIndex >= 0 ? flowRef[(dotIndex + 1)..] : flowRef;

        // Build the fully-qualified name
        var fullPath = string.IsNullOrEmpty(currentPrefix)
            ? path
            : [currentPrefix, .. path];

        return fullPath.Count > 0
            ? string.Join(".", fullPath) + "." + baseFlowName
            : baseFlowName;
    }

    /// <summary>
    /// Finds the import path from source to target document.
    /// </summary>
    private static List<string>? FindImportPath(
        LoadedDocument source,
        LoadedDocument? target,
        HashSet<LoadedDocument> visited)
    {
        if (target == null)
        {
            return null;
        }

        if (source == target)
        {
            return [];
        }

        if (!visited.Add(source))
        {
            return null; // Already visited - prevent cycles
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

        return null; // Target not found in this branch
    }

    /// <summary>
    /// Merges context definitions from all documents.
    /// Variables are prefixed with their namespace to avoid collisions.
    /// </summary>
    private ContextDefinition? MergeContexts(LoadedDocument loaded)
    {
        var mergedVariables = new Dictionary<string, VariableDefinition>();
        var mergedRequirements = new Dictionary<string, object?>();
        var mergedServices = new List<ServiceDependency>();

        // Add root document's context (no prefix)
        if (loaded.Document.Context != null)
        {
            foreach (var (name, def) in loaded.Document.Context.Variables)
            {
                mergedVariables[name] = def;
            }
            foreach (var (name, value) in loaded.Document.Context.Requirements)
            {
                mergedRequirements[name] = value;
            }
            mergedServices.AddRange(loaded.Document.Context.Services);
        }

        // Add imported contexts (with prefix)
        MergeImportedContexts(loaded, "", mergedVariables, mergedRequirements, mergedServices);

        if (mergedVariables.Count == 0 && mergedRequirements.Count == 0 && mergedServices.Count == 0)
        {
            return null;
        }

        return new ContextDefinition
        {
            Variables = mergedVariables,
            Requirements = mergedRequirements,
            Services = mergedServices
        };
    }

    /// <summary>
    /// Recursively merges imported document contexts.
    /// </summary>
    private static void MergeImportedContexts(
        LoadedDocument doc,
        string prefix,
        Dictionary<string, VariableDefinition> variables,
        Dictionary<string, object?> requirements,
        List<ServiceDependency> services)
    {
        foreach (var (alias, imported) in doc.Imports)
        {
            var newPrefix = string.IsNullOrEmpty(prefix)
                ? alias
                : $"{prefix}.{alias}";

            if (imported.Document.Context != null)
            {
                // Add variables with prefix
                foreach (var (name, def) in imported.Document.Context.Variables)
                {
                    variables[$"{newPrefix}.{name}"] = def;
                }

                // Add requirements with prefix
                foreach (var (name, value) in imported.Document.Context.Requirements)
                {
                    requirements[$"{newPrefix}.{name}"] = value;
                }

                // Add services (deduplicate by name)
                foreach (var svc in imported.Document.Context.Services)
                {
                    if (!services.Any(s => s.Name == svc.Name))
                    {
                        services.Add(svc);
                    }
                }
            }

            // Recurse
            MergeImportedContexts(imported, newPrefix, variables, requirements, services);
        }
    }
}
