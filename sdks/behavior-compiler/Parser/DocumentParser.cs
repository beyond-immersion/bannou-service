// ═══════════════════════════════════════════════════════════════════════════
// ABML Document Parser
// Parses YAML documents into ABML AST.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeyondImmersion.Bannou.BehaviorCompiler.Parser;

/// <summary>
/// Parses ABML YAML documents into AST representation.
/// </summary>
public sealed class DocumentParser
{
    private readonly IDeserializer _deserializer;

    /// <summary>
    /// Creates a new document parser.
    /// </summary>
    public DocumentParser()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    /// <summary>
    /// Parses a YAML string into an ABML document.
    /// </summary>
    /// <param name="yaml">The YAML content to parse.</param>
    /// <returns>Parse result with document or errors.</returns>
    public ParseResult<AbmlDocument> Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var errors = new List<ParseError>();

        try
        {
            var dict = _deserializer.Deserialize<Dictionary<string, object?>>(yaml);
            if (dict == null)
            {
                return ParseResult<AbmlDocument>.Failure("Empty document");
            }

            // Parse version (required)
            if (!dict.TryGetValue("version", out var versionObj) || versionObj is not string version)
            {
                errors.Add(new ParseError("Missing required 'version' field"));
                return ParseResult<AbmlDocument>.Failure(errors);
            }

            if (!version.StartsWith("2."))
            {
                errors.Add(new ParseError($"Unsupported version: {version}. Expected 2.x"));
                return ParseResult<AbmlDocument>.Failure(errors);
            }

            // Parse metadata (required)
            var metadata = ParseMetadata(dict, errors);
            if (metadata == null)
            {
                return ParseResult<AbmlDocument>.Failure(errors);
            }

            // Parse optional sections
            var imports = ParseImports(dict, errors);
            var context = ParseContext(dict, errors);
            var options = ParseOptions(dict, errors);
            var goals = ParseGoals(dict, errors);
            var flows = ParseFlows(dict, errors);

            // Parse document-level on_error (flow name to call on unhandled error)
            string? docOnError = null;
            if (dict.TryGetValue("on_error", out var docOnErrorObj) && docOnErrorObj is string onErrorFlow)
            {
                docOnError = onErrorFlow;
            }

            if (errors.Count > 0)
            {
                return ParseResult<AbmlDocument>.Failure(errors);
            }

            return ParseResult<AbmlDocument>.Success(new AbmlDocument
            {
                Version = version,
                Metadata = metadata,
                Imports = imports,
                Context = context,
                Options = options,
                Goals = goals,
                Flows = flows,
                OnError = docOnError
            });
        }
        catch (YamlException ex)
        {
            errors.Add(new ParseError(ex.Message, (int)ex.Start.Line, (int)ex.Start.Column));
            return ParseResult<AbmlDocument>.Failure(errors);
        }
        catch (Exception ex)
        {
            errors.Add(new ParseError($"Unexpected error: {ex.Message}"));
            return ParseResult<AbmlDocument>.Failure(errors);
        }
    }

    private DocumentMetadata? ParseMetadata(Dictionary<string, object?> dict, List<ParseError> errors)
    {
        if (!dict.TryGetValue("metadata", out var metaObj))
        {
            errors.Add(new ParseError("Missing required 'metadata' field"));
            return null;
        }

        if (metaObj is not Dictionary<object, object> meta)
        {
            errors.Add(new ParseError("'metadata' must be an object"));
            return null;
        }

        if (!meta.TryGetValue("id", out var idObj) || idObj is not string id)
        {
            errors.Add(new ParseError("Missing required 'metadata.id' field"));
            return null;
        }

        return new DocumentMetadata
        {
            Id = id,
            Type = meta.TryGetValue("type", out var t) ? t as string : null,
            Description = meta.TryGetValue("description", out var d) ? d as string : null,
            Tags = ParseStringList(meta.TryGetValue("tags", out var tags) ? tags : null),
            Deterministic = meta.TryGetValue("deterministic", out var det) && ParseBool(det)
        };
    }

    private static bool ParseBool(object? value) => value switch
    {
        bool b => b,
        string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private IReadOnlyList<ImportDefinition> ParseImports(Dictionary<string, object?> dict, List<ParseError> errors)
    {
        if (!dict.TryGetValue("imports", out var importsObj) || importsObj is not IList<object> importsList)
        {
            return [];
        }

        var imports = new List<ImportDefinition>();
        foreach (var item in importsList)
        {
            if (item is Dictionary<object, object> importDict)
            {
                imports.Add(new ImportDefinition
                {
                    Schema = importDict.TryGetValue("schema", out var s) ? s as string : null,
                    Types = ParseStringList(importDict.TryGetValue("types", out var t) ? t : null),
                    File = importDict.TryGetValue("file", out var f) ? f as string : null,
                    As = importDict.TryGetValue("as", out var a) ? a as string : null
                });
            }
        }

        return imports;
    }

    private ContextDefinition? ParseContext(Dictionary<string, object?> dict, List<ParseError> errors)
    {
        if (!dict.TryGetValue("context", out var contextObj) || contextObj is not Dictionary<object, object> context)
        {
            return null;
        }

        var variables = new Dictionary<string, VariableDefinition>();
        if (context.TryGetValue("variables", out var varsObj) && varsObj is Dictionary<object, object> varsDict)
        {
            foreach (var (key, value) in varsDict)
            {
                if (key is string varName && value is Dictionary<object, object> varDef)
                {
                    variables[varName] = new VariableDefinition
                    {
                        Type = varDef.TryGetValue("type", out var t) ? t as string : null,
                        Default = varDef.TryGetValue("default", out var d) ? d : null,
                        Source = varDef.TryGetValue("source", out var s) ? s as string : null,
                        Computed = varDef.TryGetValue("computed", out var c) ? c as string : null
                    };
                }
            }
        }

        var requirements = new Dictionary<string, object?>();
        if (context.TryGetValue("requirements", out var reqObj) && reqObj is Dictionary<object, object> reqDict)
        {
            foreach (var (key, value) in reqDict)
            {
                if (key is string reqName)
                {
                    requirements[reqName] = value;
                }
            }
        }

        var services = new List<ServiceDependency>();
        if (context.TryGetValue("services", out var svcObj) && svcObj is IList<object> svcList)
        {
            foreach (var item in svcList)
            {
                if (item is Dictionary<object, object> svcDict &&
                    svcDict.TryGetValue("name", out var nameObj) && nameObj is string name)
                {
                    services.Add(new ServiceDependency
                    {
                        Name = name,
                        Required = svcDict.TryGetValue("required", out var req) && ParseBool(req)
                    });
                }
            }
        }

        return new ContextDefinition
        {
            Variables = variables,
            Requirements = requirements,
            Services = services
        };
    }

    private OptionsDefinition? ParseOptions(Dictionary<string, object?> dict, List<ParseError> errors)
    {
        if (!dict.TryGetValue("options", out var optionsObj) || optionsObj is not Dictionary<object, object> optionsDict)
        {
            return null;
        }

        var optionsByType = new Dictionary<string, IReadOnlyList<OptionDefinition>>();

        foreach (var (key, value) in optionsDict)
        {
            if (key is not string optionType)
            {
                continue;
            }

            if (value is not IList<object> optionsList)
            {
                errors.Add(new ParseError($"Options type '{optionType}' must be a list"));
                continue;
            }

            var options = new List<OptionDefinition>();
            foreach (var item in optionsList)
            {
                var option = ParseOptionDefinition(item, optionType, errors);
                if (option != null)
                {
                    options.Add((OptionDefinition)option);
                }
            }

            optionsByType[optionType] = options;
        }

        return new OptionsDefinition
        {
            OptionsByType = optionsByType
        };
    }

    private OptionDefinition? ParseOptionDefinition(object? item, string optionType, List<ParseError> errors)
    {
        if (item is not Dictionary<object, object> optionDict)
        {
            errors.Add(new ParseError($"Option in '{optionType}' must be an object"));
            return null;
        }

        // actionId is required
        if (!optionDict.TryGetValue("actionId", out var actionIdObj) || actionIdObj is not string actionId)
        {
            errors.Add(new ParseError($"Option in '{optionType}' requires 'actionId' field"));
            return null;
        }

        // preference is required - can be literal or expression
        string? preference = null;
        if (optionDict.TryGetValue("preference", out var prefObj))
        {
            preference = ConvertToExpressionString(prefObj);
        }
        if (string.IsNullOrEmpty(preference))
        {
            errors.Add(new ParseError($"Option '{actionId}' in '{optionType}' requires 'preference' field"));
            return null;
        }

        // available is required - can be literal or expression
        string? available = null;
        if (optionDict.TryGetValue("available", out var availObj))
        {
            available = ConvertToExpressionString(availObj);
        }
        if (string.IsNullOrEmpty(available))
        {
            errors.Add(new ParseError($"Option '{actionId}' in '{optionType}' requires 'available' field"));
            return null;
        }

        // Optional fields
        string? risk = null;
        if (optionDict.TryGetValue("risk", out var riskObj))
        {
            risk = ConvertToExpressionString(riskObj);
        }

        string? cooldownMs = null;
        if (optionDict.TryGetValue("cooldownMs", out var cooldownObj))
        {
            cooldownMs = ConvertToExpressionString(cooldownObj);
        }

        var requirements = ParseStringList(optionDict.TryGetValue("requirements", out var reqObj) ? reqObj : null);
        var tags = ParseStringList(optionDict.TryGetValue("tags", out var tagsObj) ? tagsObj : null);

        return new OptionDefinition
        {
            ActionId = actionId,
            Preference = preference,
            Risk = risk,
            Available = available,
            Requirements = requirements,
            CooldownMs = cooldownMs,
            Tags = tags
        };
    }

    /// <summary>
    /// Converts a value to an expression string. Handles literals and expressions.
    /// </summary>
    private static string? ConvertToExpressionString(object? value)
    {
        return value switch
        {
            string s => s,
            bool b => b.ToString().ToLowerInvariant(),
            int i => i.ToString(),
            long l => l.ToString(),
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => value?.ToString()
        };
    }

    private IReadOnlyDictionary<string, GoapGoalDefinition> ParseGoals(
        Dictionary<string, object?> dict, List<ParseError> errors)
    {
        if (!dict.TryGetValue("goals", out var goalsObj) || goalsObj is not Dictionary<object, object> goalsDict)
        {
            return new Dictionary<string, GoapGoalDefinition>();
        }

        var goals = new Dictionary<string, GoapGoalDefinition>();
        foreach (var (key, value) in goalsDict)
        {
            if (key is not string goalName)
            {
                continue;
            }

            if (value is not Dictionary<object, object> goalDict)
            {
                errors.Add(new ParseError($"Goal '{goalName}' must be an object"));
                continue;
            }

            var priority = 50;
            if (goalDict.TryGetValue("priority", out var priorityObj))
            {
                priority = priorityObj switch
                {
                    int i => i,
                    long l => (int)l,
                    string s when int.TryParse(s, out var parsed) => parsed,
                    _ => 50
                };
            }

            var conditions = new Dictionary<string, string>();
            if (goalDict.TryGetValue("conditions", out var condObj) &&
                condObj is Dictionary<object, object> condDict)
            {
                foreach (var (condKey, condValue) in condDict)
                {
                    if (condKey is string condName && condValue is string condExpr)
                    {
                        conditions[condName] = condExpr;
                    }
                }
            }

            goals[goalName] = new GoapGoalDefinition
            {
                Priority = priority,
                Conditions = conditions
            };
        }

        return goals;
    }

    private IReadOnlyDictionary<string, Flow> ParseFlows(Dictionary<string, object?> dict, List<ParseError> errors)
    {
        if (!dict.TryGetValue("flows", out var flowsObj) || flowsObj is not Dictionary<object, object> flowsDict)
        {
            return new Dictionary<string, Flow>();
        }

        var flows = new Dictionary<string, Flow>();
        foreach (var (key, value) in flowsDict)
        {
            if (key is not string flowName)
            {
                continue;
            }

            if (value is not Dictionary<object, object> flowDict)
            {
                errors.Add(new ParseError($"Flow '{flowName}' must be an object"));
                continue;
            }

            var triggers = ParseTriggers(flowDict, errors);
            var goap = ParseGoapMetadata(flowDict, errors);
            var actions = ParseActions(flowDict.TryGetValue("actions", out var actionsObj) ? actionsObj : null, errors);
            var onError = ParseActions(flowDict.TryGetValue("on_error", out var onErrorObj) ? onErrorObj : null, errors);

            flows[flowName] = new Flow
            {
                Name = flowName,
                Triggers = triggers,
                Goap = goap,
                Actions = actions,
                OnError = onError
            };
        }

        return flows;
    }

    private IReadOnlyList<FlowTrigger> ParseTriggers(Dictionary<object, object> flowDict, List<ParseError> errors)
    {
        if (!flowDict.TryGetValue("triggers", out var triggersObj) || triggersObj is not IList<object> triggersList)
        {
            return [];
        }

        var triggers = new List<FlowTrigger>();
        foreach (var item in triggersList)
        {
            if (item is Dictionary<object, object> triggerDict)
            {
                triggers.Add(new FlowTrigger
                {
                    Event = triggerDict.TryGetValue("event", out var e) ? e as string : null,
                    Condition = triggerDict.TryGetValue("condition", out var c) ? c as string : null,
                    TimeRange = triggerDict.TryGetValue("time_range", out var t) ? t as string : null,
                    Schedule = triggerDict.TryGetValue("schedule", out var s) ? s as string : null
                });
            }
        }

        return triggers;
    }

    private GoapFlowMetadata? ParseGoapMetadata(Dictionary<object, object> flowDict, List<ParseError> errors)
    {
        if (!flowDict.TryGetValue("goap", out var goapObj) || goapObj is not Dictionary<object, object> goapDict)
        {
            return null;
        }

        var preconditions = new Dictionary<string, string>();
        if (goapDict.TryGetValue("preconditions", out var preObj) &&
            preObj is Dictionary<object, object> preDict)
        {
            foreach (var (key, value) in preDict)
            {
                if (key is string propName && value is string condition)
                {
                    preconditions[propName] = condition;
                }
            }
        }

        var effects = new Dictionary<string, string>();
        if (goapDict.TryGetValue("effects", out var effObj) &&
            effObj is Dictionary<object, object> effDict)
        {
            foreach (var (key, value) in effDict)
            {
                if (key is string propName && value is string effect)
                {
                    effects[propName] = effect;
                }
            }
        }

        var cost = 1.0f;
        if (goapDict.TryGetValue("cost", out var costObj))
        {
            cost = costObj switch
            {
                int i => i,
                long l => l,
                float f => f,
                double d => (float)d,
                string s when float.TryParse(s, out var parsed) => parsed,
                _ => 1.0f
            };
        }

        return new GoapFlowMetadata
        {
            Preconditions = preconditions,
            Effects = effects,
            Cost = cost
        };
    }

    private IReadOnlyList<ActionNode> ParseActions(object? actionsObj, List<ParseError> errors)
    {
        if (actionsObj is not IList<object> actionsList)
        {
            return [];
        }

        var actions = new List<ActionNode>();
        foreach (var item in actionsList)
        {
            var action = ParseAction(item, errors);
            if (action != null)
            {
                actions.Add((ActionNode)action);
            }
        }

        return actions;
    }

    private ActionNode? ParseAction(object? actionObj, List<ParseError> errors)
    {
        if (actionObj is not Dictionary<object, object> actionDict || actionDict.Count == 0)
        {
            return null;
        }

        // Get the first (and should be only) key-value pair
        var (actionType, actionValue) = actionDict.First();
        if (actionType is not string actionName)
        {
            return null;
        }

        return actionName switch
        {
            "cond" => ParseCondAction(actionValue, errors),
            "for_each" => ParseForEachAction(actionValue, errors),
            "repeat" => ParseRepeatAction(actionValue, errors),
            "goto" => ParseGotoAction(actionValue, errors),
            "call" => ParseCallAction(actionValue, errors),
            "return" => ParseReturnAction(actionValue, errors),
            "set" => ParseSetAction(actionValue, errors),
            "local" => ParseLocalAction(actionValue, errors),
            "global" => ParseGlobalAction(actionValue, errors),
            "increment" => ParseIncrementAction(actionValue, errors),
            "decrement" => ParseDecrementAction(actionValue, errors),
            "clear" => ParseClearAction(actionValue, errors),
            "log" => ParseLogAction(actionValue, errors),
            "emit" => ParseEmitAction(actionValue, errors),
            "wait_for" => ParseWaitForAction(actionValue, errors),
            "sync" => ParseSyncAction(actionValue, errors),
            "continuation_point" => ParseContinuationPointAction(actionValue, errors),
            "emit_intent" => ParseEmitIntentAction(actionValue, errors),
            _ => ParseDomainAction(actionName, actionValue, errors)
        };
    }

    private CondAction? ParseCondAction(object? value, List<ParseError> errors)
    {
        if (value is not IList<object> branchesList)
        {
            errors.Add(new ParseError("'cond' must be a list of branches"));
            return null;
        }

        var branches = new List<CondBranch>();
        IReadOnlyList<ActionNode>? elseBranch = null;

        foreach (var item in branchesList)
        {
            if (item is not Dictionary<object, object> branchDict)
            {
                continue;
            }

            if (branchDict.TryGetValue("when", out var whenObj) && whenObj is string when)
            {
                var then = ParseActions(branchDict.TryGetValue("then", out var thenObj) ? thenObj : null, errors);
                branches.Add(new CondBranch(when, then));
            }
            else if (branchDict.TryGetValue("else", out var elseObj))
            {
                elseBranch = ParseActions(elseObj, errors);
            }
        }

        return new CondAction(branches, elseBranch);
    }

    private ForEachAction? ParseForEachAction(object? value, List<ParseError> errors)
    {
        if (value is not Dictionary<object, object> dict)
        {
            errors.Add(new ParseError("'for_each' must be an object"));
            return null;
        }

        if (!dict.TryGetValue("variable", out var varObj) || varObj is not string variable)
        {
            errors.Add(new ParseError("'for_each' requires 'variable' field"));
            return null;
        }

        if (!dict.TryGetValue("collection", out var collObj) || collObj is not string collection)
        {
            errors.Add(new ParseError("'for_each' requires 'collection' field"));
            return null;
        }

        var doActions = ParseActions(dict.TryGetValue("do", out var doObj) ? doObj : null, errors);

        return new ForEachAction(variable, collection, doActions);
    }

    private RepeatAction? ParseRepeatAction(object? value, List<ParseError> errors)
    {
        if (value is not Dictionary<object, object> dict)
        {
            errors.Add(new ParseError("'repeat' must be an object"));
            return null;
        }

        var times = 0;
        if (dict.TryGetValue("times", out var timesObj))
        {
            times = timesObj switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 0
            };
        }

        if (times <= 0)
        {
            errors.Add(new ParseError("'repeat' requires positive 'times' value"));
            return null;
        }

        var doActions = ParseActions(dict.TryGetValue("do", out var doObj) ? doObj : null, errors);

        return new RepeatAction(times, doActions);
    }

    private GotoAction? ParseGotoAction(object? value, List<ParseError> errors)
    {
        string? flowName = null;
        Dictionary<string, string>? args = null;

        if (value is string str)
        {
            flowName = str;
        }
        else if (value is Dictionary<object, object> dict)
        {
            if (dict.TryGetValue("flow", out var flowObj) && flowObj is string fn)
            {
                flowName = fn;
            }

            if (dict.TryGetValue("args", out var argsObj) && argsObj is Dictionary<object, object> argsDict)
            {
                args = new Dictionary<string, string>();
                foreach (var (k, v) in argsDict)
                {
                    if (k is string key && v is string val)
                    {
                        args[key] = val;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(flowName))
        {
            errors.Add(new ParseError("'goto' requires 'flow' field"));
            return null;
        }

        return new GotoAction(flowName, args);
    }

    private CallAction? ParseCallAction(object? value, List<ParseError> errors)
    {
        string? flowName = null;

        if (value is string str)
        {
            flowName = str;
        }
        else if (value is Dictionary<object, object> dict &&
                dict.TryGetValue("flow", out var flowObj) && flowObj is string fn)
        {
            flowName = fn;
        }

        if (string.IsNullOrEmpty(flowName))
        {
            errors.Add(new ParseError("'call' requires 'flow' field"));
            return null;
        }

        return new CallAction(flowName);
    }

    private ReturnAction ParseReturnAction(object? value, List<ParseError> errors)
    {
        string? returnValue = null;

        if (value is string str)
        {
            returnValue = str;
        }
        else if (value is Dictionary<object, object> dict &&
                dict.TryGetValue("value", out var valObj) && valObj is string val)
        {
            returnValue = val;
        }

        return new ReturnAction(returnValue);
    }

    private SetAction? ParseSetAction(object? value, List<ParseError> errors)
    {
        if (value is not Dictionary<object, object> dict)
        {
            errors.Add(new ParseError("'set' must be an object"));
            return null;
        }

        if (!dict.TryGetValue("variable", out var varObj) || varObj is not string variable)
        {
            errors.Add(new ParseError("'set' requires 'variable' field"));
            return null;
        }

        if (!dict.TryGetValue("value", out var valObj))
        {
            errors.Add(new ParseError("'set' requires 'value' field"));
            return null;
        }

        // Convert value to string (expression)
        var valueStr = valObj?.ToString() ?? "null";

        return new SetAction(variable, valueStr);
    }

    private LocalAction? ParseLocalAction(object? value, List<ParseError> errors)
    {
        if (value is not Dictionary<object, object> dict)
        {
            errors.Add(new ParseError("'local' must be an object"));
            return null;
        }

        if (!dict.TryGetValue("variable", out var varObj) || varObj is not string variable)
        {
            errors.Add(new ParseError("'local' requires 'variable' field"));
            return null;
        }

        if (!dict.TryGetValue("value", out var valObj))
        {
            errors.Add(new ParseError("'local' requires 'value' field"));
            return null;
        }

        // Convert value to string (expression)
        var valueStr = valObj?.ToString() ?? "null";

        return new LocalAction(variable, valueStr);
    }

    private GlobalAction? ParseGlobalAction(object? value, List<ParseError> errors)
    {
        if (value is not Dictionary<object, object> dict)
        {
            errors.Add(new ParseError("'global' must be an object"));
            return null;
        }

        if (!dict.TryGetValue("variable", out var varObj) || varObj is not string variable)
        {
            errors.Add(new ParseError("'global' requires 'variable' field"));
            return null;
        }

        if (!dict.TryGetValue("value", out var valObj))
        {
            errors.Add(new ParseError("'global' requires 'value' field"));
            return null;
        }

        // Convert value to string (expression)
        var valueStr = valObj?.ToString() ?? "null";

        return new GlobalAction(variable, valueStr);
    }

    private IncrementAction? ParseIncrementAction(object? value, List<ParseError> errors)
    {
        if (value is not Dictionary<object, object> dict)
        {
            errors.Add(new ParseError("'increment' must be an object"));
            return null;
        }

        if (!dict.TryGetValue("variable", out var varObj) || varObj is not string variable)
        {
            errors.Add(new ParseError("'increment' requires 'variable' field"));
            return null;
        }

        var by = 1;
        if (dict.TryGetValue("by", out var byObj))
        {
            by = byObj switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 1
            };
        }

        return new IncrementAction(variable, by);
    }

    private DecrementAction? ParseDecrementAction(object? value, List<ParseError> errors)
    {
        if (value is not Dictionary<object, object> dict)
        {
            errors.Add(new ParseError("'decrement' must be an object"));
            return null;
        }

        if (!dict.TryGetValue("variable", out var varObj) || varObj is not string variable)
        {
            errors.Add(new ParseError("'decrement' requires 'variable' field"));
            return null;
        }

        var by = 1;
        if (dict.TryGetValue("by", out var byObj))
        {
            by = byObj switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 1
            };
        }

        return new DecrementAction(variable, by);
    }

    private ClearAction? ParseClearAction(object? value, List<ParseError> errors)
    {
        string? variable = null;

        if (value is string str)
        {
            variable = str;
        }
        else if (value is Dictionary<object, object> dict &&
                dict.TryGetValue("variable", out var varObj) && varObj is string v)
        {
            variable = v;
        }

        if (string.IsNullOrEmpty(variable))
        {
            errors.Add(new ParseError("'clear' requires 'variable' field"));
            return null;
        }

        return new ClearAction(variable);
    }

    private LogAction? ParseLogAction(object? value, List<ParseError> errors)
    {
        string? message = null;
        var level = "info";

        if (value is string str)
        {
            message = str;
        }
        else if (value is Dictionary<object, object> dict)
        {
            if (dict.TryGetValue("message", out var msgObj) && msgObj is string msg)
            {
                message = msg;
            }
            if (dict.TryGetValue("level", out var lvlObj) && lvlObj is string lvl)
            {
                level = lvl;
            }
        }

        if (string.IsNullOrEmpty(message))
        {
            errors.Add(new ParseError("'log' requires 'message' field"));
            return null;
        }

        return new LogAction(message, level);
    }

    private EmitAction? ParseEmitAction(object? value, List<ParseError> errors)
    {
        string? signal = null;
        string? payload = null;

        if (value is string str)
        {
            signal = str;
        }
        else if (value is Dictionary<object, object> dict)
        {
            if (dict.TryGetValue("signal", out var sigObj) && sigObj is string sig)
            {
                signal = sig;
            }
            if (dict.TryGetValue("payload", out var payObj) && payObj is string pay)
            {
                payload = pay;
            }
        }

        if (string.IsNullOrEmpty(signal))
        {
            errors.Add(new ParseError("'emit' requires 'signal' field"));
            return null;
        }

        return new EmitAction(signal, payload);
    }

    private WaitForAction? ParseWaitForAction(object? value, List<ParseError> errors)
    {
        string? signal = null;

        if (value is string str)
        {
            signal = str;
        }
        else if (value is Dictionary<object, object> dict &&
                dict.TryGetValue("signal", out var sigObj) && sigObj is string sig)
        {
            signal = sig;
        }

        if (string.IsNullOrEmpty(signal))
        {
            errors.Add(new ParseError("'wait_for' requires 'signal' field"));
            return null;
        }

        return new WaitForAction(signal);
    }

    private SyncAction? ParseSyncAction(object? value, List<ParseError> errors)
    {
        string? point = null;

        if (value is string str)
        {
            point = str;
        }
        else if (value is Dictionary<object, object> dict &&
                dict.TryGetValue("point", out var ptObj) && ptObj is string pt)
        {
            point = pt;
        }

        if (string.IsNullOrEmpty(point))
        {
            errors.Add(new ParseError("'sync' requires 'point' field"));
            return null;
        }

        return new SyncAction(point);
    }

    private ContinuationPointAction? ParseContinuationPointAction(object? value, List<ParseError> errors)
    {
        if (value is not Dictionary<object, object> dict)
        {
            errors.Add(new ParseError("'continuation_point' must be an object"));
            return null;
        }

        if (!dict.TryGetValue("name", out var nameObj) || nameObj is not string name)
        {
            errors.Add(new ParseError("'continuation_point' requires 'name' field"));
            return null;
        }

        if (!dict.TryGetValue("timeout", out var timeoutObj) || timeoutObj is not string timeout)
        {
            errors.Add(new ParseError("'continuation_point' requires 'timeout' field"));
            return null;
        }

        if (!dict.TryGetValue("default_flow", out var defaultFlowObj) || defaultFlowObj is not string defaultFlow)
        {
            errors.Add(new ParseError("'continuation_point' requires 'default_flow' field"));
            return null;
        }

        return new ContinuationPointAction(name, timeout, defaultFlow);
    }

    private EmitIntentAction? ParseEmitIntentAction(object? value, List<ParseError> errors)
    {
        if (value is not Dictionary<object, object> dict)
        {
            errors.Add(new ParseError("'emit_intent' must be an object"));
            return null;
        }

        if (!dict.TryGetValue("action", out var actionObj) || actionObj is not string action)
        {
            errors.Add(new ParseError("'emit_intent' requires 'action' field"));
            return null;
        }

        // Optional fields with defaults
        var channel = "action";
        if (dict.TryGetValue("channel", out var channelObj) && channelObj is string ch)
        {
            channel = ch;
        }

        var urgency = "1.0";
        if (dict.TryGetValue("urgency", out var urgencyObj))
        {
            urgency = urgencyObj?.ToString() ?? "1.0";
        }

        string? target = null;
        if (dict.TryGetValue("target", out var targetObj) && targetObj is string tgt)
        {
            target = tgt;
        }

        return new EmitIntentAction(action, channel, urgency, target);
    }

    private DomainAction ParseDomainAction(string name, object? value, List<ParseError> errors)
    {
        var parameters = new Dictionary<string, object?>();
        IReadOnlyList<ActionNode>? onError = null;

        if (value is Dictionary<object, object> dict)
        {
            foreach (var (k, v) in dict)
            {
                if (k is string key)
                {
                    if (key == "on_error")
                    {
                        // Parse action-level on_error handlers
                        onError = ParseActions(v, errors);
                    }
                    else
                    {
                        parameters[key] = v;
                    }
                }
            }
        }
        else if (value != null)
        {
            // Single value, treat as default parameter
            parameters["value"] = value;
        }

        return new DomainAction(name, parameters, onError);
    }

    private static IReadOnlyList<string> ParseStringList(object? value)
    {
        if (value is IList<object> list)
        {
            return list.OfType<string>().ToList();
        }
        return [];
    }
}
