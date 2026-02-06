// =============================================================================
// Semantic Analyzer
// Validates ABML documents before compilation.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;

namespace BeyondImmersion.Bannou.BehaviorCompiler.Compiler;

/// <summary>
/// Analyzes ABML documents for semantic correctness before compilation.
/// </summary>
public sealed class SemanticAnalyzer
{
    private readonly List<SemanticError> _errors = new();
    private readonly List<SemanticWarning> _warnings = new();
    private readonly HashSet<string> _definedFlows = new();
    private readonly HashSet<string> _referencedFlows = new();
    private readonly HashSet<string> _definedVariables = new();
    private readonly HashSet<string> _usedVariables = new();

    /// <summary>
    /// Domain action names that are forbidden for security reasons.
    /// Generic service call actions would give behaviors unrestricted access to any endpoint.
    /// All service interactions must use purpose-built, validated actions instead.
    /// </summary>
    private static readonly HashSet<string> ForbiddenDomainActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "service_call",
        "api_call",
        "http_call",
        "mesh_call",
        "invoke_service"
    };

    /// <summary>
    /// Analyzes an ABML document for semantic errors and warnings.
    /// </summary>
    /// <param name="document">The document to analyze.</param>
    /// <returns>The analysis result.</returns>
    public SemanticAnalysisResult Analyze(AbmlDocument document)
    {

        _errors.Clear();
        _warnings.Clear();
        _definedFlows.Clear();
        _referencedFlows.Clear();
        _definedVariables.Clear();
        _usedVariables.Clear();

        // Phase 1: Collect declarations
        CollectDeclarations(document);

        // Phase 2: Validate references
        ValidateReferences(document);

        // Phase 3: Check for unreachable code and other issues
        CheckCodeQuality(document);

        return new SemanticAnalysisResult(
            _errors.Count == 0,
            _errors.ToList(),
            _warnings.ToList());
    }

    private void CollectDeclarations(AbmlDocument document)
    {
        // Collect flow names
        foreach (var flowName in document.Flows.Keys)
        {
            _definedFlows.Add(flowName);
        }

        // Collect context variables
        if (document.Context?.Variables != null)
        {
            foreach (var (name, _) in document.Context.Variables)
            {
                _definedVariables.Add(name);
            }
        }

        // Collect variables defined in flows
        foreach (var (_, flow) in document.Flows)
        {
            CollectVariablesFromActions(flow.Actions);
        }
    }

    private void CollectVariablesFromActions(IReadOnlyList<ActionNode> actions)
    {
        foreach (var action in actions)
        {
            switch (action)
            {
                case SetAction set:
                    _definedVariables.Add(set.Variable);
                    break;
                case LocalAction local:
                    _definedVariables.Add(local.Variable);
                    break;
                case GlobalAction global:
                    _definedVariables.Add(global.Variable);
                    break;
                case IncrementAction inc:
                    _definedVariables.Add(inc.Variable);
                    break;
                case DecrementAction dec:
                    _definedVariables.Add(dec.Variable);
                    break;
                case CondAction cond:
                    foreach (var branch in cond.Branches)
                    {
                        CollectVariablesFromActions(branch.Then);
                    }
                    if (cond.ElseBranch != null)
                    {
                        CollectVariablesFromActions(cond.ElseBranch);
                    }
                    break;
                case ForEachAction forEach:
                    _definedVariables.Add(forEach.Variable);
                    CollectVariablesFromActions(forEach.Do);
                    break;
                case RepeatAction repeat:
                    CollectVariablesFromActions(repeat.Do);
                    break;
            }
        }
    }

    private void ValidateReferences(AbmlDocument document)
    {
        // Validate main flow exists
        if (!document.Flows.ContainsKey("main"))
        {
            _warnings.Add(new SemanticWarning(
                "No 'main' flow defined. Execution will start from the first defined flow.",
                SemanticWarningLevel.Info));
        }

        // Validate all flows
        foreach (var (flowName, flow) in document.Flows)
        {
            ValidateActionsInFlow(flowName, flow.Actions);
        }

        // Check for undefined flow references
        foreach (var flowName in _referencedFlows)
        {
            if (!_definedFlows.Contains(flowName))
            {
                _errors.Add(new SemanticError(
                    $"Reference to undefined flow: '{flowName}'",
                    SemanticErrorKind.UndefinedFlow));
            }
        }
    }

    private void ValidateActionsInFlow(string flowName, IReadOnlyList<ActionNode> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            ValidateAction(flowName, action, i, actions.Count);
        }
    }

    private void ValidateAction(string flowName, ActionNode action, int index, int totalActions)
    {
        switch (action)
        {
            case GotoAction goto_:
                _referencedFlows.Add(goto_.Flow);
                // Check for unreachable code after goto
                if (index < totalActions - 1)
                {
                    _warnings.Add(new SemanticWarning(
                        $"Unreachable code after 'goto {goto_.Flow}' in flow '{flowName}'",
                        SemanticWarningLevel.Warning));
                }
                break;

            case CallAction call:
                _referencedFlows.Add(call.Flow);
                break;

            case ReturnAction:
                if (index < totalActions - 1)
                {
                    _warnings.Add(new SemanticWarning(
                        $"Unreachable code after 'return' in flow '{flowName}'",
                        SemanticWarningLevel.Warning));
                }
                break;

            case ContinuationPointAction cp:
                _referencedFlows.Add(cp.DefaultFlow);
                ValidateContinuationPoint(cp);
                break;

            case CondAction cond:
                ValidateCondAction(flowName, cond);
                break;

            case ForEachAction forEach:
                ValidateActionsInFlow(flowName, forEach.Do);
                break;

            case RepeatAction repeat:
                if (repeat.Times <= 0)
                {
                    _warnings.Add(new SemanticWarning(
                        $"Repeat with {repeat.Times} times will never execute in flow '{flowName}'",
                        SemanticWarningLevel.Warning));
                }
                ValidateActionsInFlow(flowName, repeat.Do);
                break;

            case EmitIntentAction emit:
                ValidateEmitIntent(emit);
                break;

            case DomainAction domain:
                ValidateDomainAction(flowName, domain);
                break;
        }
    }

    private void ValidateDomainAction(string flowName, DomainAction action)
    {
        if (ForbiddenDomainActions.Contains(action.Name))
        {
            _errors.Add(new SemanticError(
                $"Domain action '{action.Name}' is forbidden in flow '{flowName}'. " +
                "Generic service call actions violate security policy. " +
                "Use purpose-built actions instead (e.g., load_snapshot, actor_command, spawn_watcher).",
                SemanticErrorKind.ForbiddenDomainAction));
        }

        // Validate actor_command action parameters
        if (action.Name.Equals("actor_command", StringComparison.OrdinalIgnoreCase))
        {
            ValidateActorCommand(flowName, action);
        }

        // Validate actor_query action parameters
        if (action.Name.Equals("actor_query", StringComparison.OrdinalIgnoreCase))
        {
            ValidateActorQuery(flowName, action);
        }
    }

    /// <summary>
    /// Validates actor_command action parameters (target, command).
    /// </summary>
    /// <param name="flowName">Current flow name for error reporting.</param>
    /// <param name="action">Domain action to validate.</param>
    private void ValidateActorCommand(string flowName, DomainAction action)
    {
        // Check required 'target' parameter
        if (!action.Parameters.ContainsKey("target"))
        {
            _errors.Add(new SemanticError(
                $"actor_command in flow '{flowName}' missing required 'target' parameter",
                SemanticErrorKind.MissingRequiredParameter));
        }

        // Check required 'command' parameter and validate it's a valid identifier
        if (!action.Parameters.TryGetValue("command", out var commandObj) || commandObj == null)
        {
            _errors.Add(new SemanticError(
                $"actor_command in flow '{flowName}' missing required 'command' parameter",
                SemanticErrorKind.MissingRequiredParameter));
        }
        else if (commandObj is string commandStr && !IsValidIdentifier(commandStr))
        {
            _errors.Add(new SemanticError(
                $"actor_command 'command' must be a valid identifier (alphanumeric + underscore), got '{commandStr}' in flow '{flowName}'",
                SemanticErrorKind.InvalidIdentifier));
        }
    }

    /// <summary>
    /// Validates actor_query action parameters (target, query, into).
    /// </summary>
    /// <param name="flowName">Current flow name for error reporting.</param>
    /// <param name="action">Domain action to validate.</param>
    private void ValidateActorQuery(string flowName, DomainAction action)
    {
        // Check required 'target' parameter
        if (!action.Parameters.ContainsKey("target"))
        {
            _errors.Add(new SemanticError(
                $"actor_query in flow '{flowName}' missing required 'target' parameter",
                SemanticErrorKind.MissingRequiredParameter));
        }

        // Check required 'query' parameter
        if (!action.Parameters.ContainsKey("query"))
        {
            _errors.Add(new SemanticError(
                $"actor_query in flow '{flowName}' missing required 'query' parameter",
                SemanticErrorKind.MissingRequiredParameter));
        }

        // Check required 'into' parameter and validate it's a valid variable name
        if (!action.Parameters.TryGetValue("into", out var intoObj) || intoObj == null)
        {
            _errors.Add(new SemanticError(
                $"actor_query in flow '{flowName}' missing required 'into' parameter",
                SemanticErrorKind.MissingRequiredParameter));
        }
        else if (intoObj is string intoStr && !IsValidIdentifier(intoStr))
        {
            _errors.Add(new SemanticError(
                $"actor_query 'into' must be a valid variable name (alphanumeric + underscore), got '{intoStr}' in flow '{flowName}'",
                SemanticErrorKind.InvalidIdentifier));
        }
    }

    /// <summary>
    /// Validates that a string is a valid identifier (starts with letter or underscore,
    /// followed by alphanumeric characters or underscores).
    /// </summary>
    /// <param name="name">The string to validate.</param>
    /// <returns>True if the string is a valid identifier.</returns>
    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!char.IsLetter(name[0]) && name[0] != '_') return false;
        return name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    private void ValidateCondAction(string flowName, CondAction cond)
    {
        if (cond.Branches.Count == 0)
        {
            _errors.Add(new SemanticError(
                $"Conditional in flow '{flowName}' has no branches",
                SemanticErrorKind.EmptyConditional));
            return;
        }

        foreach (var branch in cond.Branches)
        {
            if (string.IsNullOrWhiteSpace(branch.When))
            {
                _errors.Add(new SemanticError(
                    $"Conditional branch in flow '{flowName}' has empty condition",
                    SemanticErrorKind.EmptyCondition));
            }
            ValidateActionsInFlow(flowName, branch.Then);
        }

        if (cond.ElseBranch != null)
        {
            ValidateActionsInFlow(flowName, cond.ElseBranch);
        }
    }

    private void ValidateContinuationPoint(ContinuationPointAction cp)
    {
        if (string.IsNullOrWhiteSpace(cp.Name))
        {
            _errors.Add(new SemanticError(
                "Continuation point has empty name",
                SemanticErrorKind.InvalidContinuationPoint));
        }

        if (string.IsNullOrWhiteSpace(cp.DefaultFlow))
        {
            _errors.Add(new SemanticError(
                $"Continuation point '{cp.Name}' has no default flow",
                SemanticErrorKind.InvalidContinuationPoint));
        }
    }

    private void ValidateEmitIntent(EmitIntentAction emit)
    {
        var validChannels = new[] { "action", "locomotion", "attention", "stance", "vocalization" };
        if (!validChannels.Contains(emit.Channel.ToLowerInvariant()))
        {
            _warnings.Add(new SemanticWarning(
                $"Unknown intent channel '{emit.Channel}'. Valid channels: {string.Join(", ", validChannels)}",
                SemanticWarningLevel.Warning));
        }
    }

    private void CheckCodeQuality(AbmlDocument document)
    {
        // Check for unused flows
        foreach (var flowName in _definedFlows)
        {
            if (flowName != "main" && !_referencedFlows.Contains(flowName))
            {
                _warnings.Add(new SemanticWarning(
                    $"Flow '{flowName}' is defined but never referenced",
                    SemanticWarningLevel.Info));
            }
        }

        // Check for empty flows
        foreach (var (flowName, flow) in document.Flows)
        {
            if (flow.Actions.Count == 0)
            {
                _warnings.Add(new SemanticWarning(
                    $"Flow '{flowName}' is empty",
                    SemanticWarningLevel.Info));
            }
        }
    }
}

/// <summary>
/// Result of semantic analysis.
/// </summary>
public sealed class SemanticAnalysisResult
{
    /// <summary>
    /// Whether the document passed semantic analysis.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Semantic errors found during analysis.
    /// </summary>
    public IReadOnlyList<SemanticError> Errors { get; }

    /// <summary>
    /// Semantic warnings found during analysis.
    /// </summary>
    public IReadOnlyList<SemanticWarning> Warnings { get; }

    /// <summary>
    /// Creates a new semantic analysis result.
    /// </summary>
    public SemanticAnalysisResult(
        bool isValid,
        IReadOnlyList<SemanticError> errors,
        IReadOnlyList<SemanticWarning> warnings)
    {
        IsValid = isValid;
        Errors = errors;
        Warnings = warnings;
    }
}

/// <summary>
/// A semantic error that prevents compilation.
/// </summary>
public sealed class SemanticError
{
    /// <summary>
    /// The error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// The kind of error.
    /// </summary>
    public SemanticErrorKind Kind { get; }

    /// <summary>
    /// Creates a new semantic error.
    /// </summary>
    public SemanticError(string message, SemanticErrorKind kind)
    {
        Message = message;
        Kind = kind;
    }
}

/// <summary>
/// Kinds of semantic errors.
/// </summary>
public enum SemanticErrorKind
{
    /// <summary>Reference to an undefined flow.</summary>
    UndefinedFlow,

    /// <summary>Reference to an undefined variable.</summary>
    UndefinedVariable,

    /// <summary>Empty conditional with no branches.</summary>
    EmptyConditional,

    /// <summary>Conditional branch with empty condition.</summary>
    EmptyCondition,

    /// <summary>Invalid continuation point definition.</summary>
    InvalidContinuationPoint,

    /// <summary>Type mismatch in expression.</summary>
    TypeMismatch,

    /// <summary>Forbidden domain action that violates security policy.</summary>
    ForbiddenDomainAction,

    /// <summary>Missing required parameter for domain action.</summary>
    MissingRequiredParameter,

    /// <summary>Invalid identifier format.</summary>
    InvalidIdentifier,

    /// <summary>Other semantic error.</summary>
    Other
}

/// <summary>
/// A semantic warning that doesn't prevent compilation.
/// </summary>
public sealed class SemanticWarning
{
    /// <summary>
    /// The warning message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// The warning level.
    /// </summary>
    public SemanticWarningLevel Level { get; }

    /// <summary>
    /// Creates a new semantic warning.
    /// </summary>
    public SemanticWarning(string message, SemanticWarningLevel level)
    {
        Message = message;
        Level = level;
    }
}

/// <summary>
/// Levels of semantic warnings.
/// </summary>
public enum SemanticWarningLevel
{
    /// <summary>Informational message.</summary>
    Info,

    /// <summary>Warning that may indicate a problem.</summary>
    Warning
}
