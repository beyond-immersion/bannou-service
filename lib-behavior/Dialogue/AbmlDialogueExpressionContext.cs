// =============================================================================
// ABML Dialogue Expression Context Adapter
// Bridges ABML expression evaluation with the dialogue resolver.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Runtime;
using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.Bannou.Behavior.Dialogue;

/// <summary>
/// Adapter that bridges ABML's expression context with IDialogueExpressionContext.
/// </summary>
/// <remarks>
/// <para>
/// This adapter allows the dialogue resolver to evaluate conditions and templates
/// using ABML's expression evaluation infrastructure, without directly depending
/// on ABML internals.
/// </para>
/// </remarks>
public sealed class AbmlDialogueExpressionContext : IDialogueExpressionContext
{
    private readonly IVariableScope _scope;
    private readonly IExpressionEvaluator? _evaluator;

    /// <summary>
    /// Creates an adapter from an ABML variable scope.
    /// </summary>
    /// <param name="scope">The ABML variable scope.</param>
    /// <param name="evaluator">Optional expression evaluator for conditions.</param>
    public AbmlDialogueExpressionContext(
        IVariableScope scope,
        IExpressionEvaluator? evaluator = null)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _evaluator = evaluator;
    }

    /// <summary>
    /// Creates an adapter from an ABML execution context.
    /// </summary>
    /// <param name="context">The ABML execution context.</param>
    /// <returns>A dialogue expression context.</returns>
    public static AbmlDialogueExpressionContext FromExecutionContext(
        BeyondImmersion.BannouService.Abml.Execution.ExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var scope = context.CallStack.Current?.Scope ?? context.RootScope;
        return new AbmlDialogueExpressionContext(scope, context.Evaluator);
    }

    /// <inheritdoc/>
    public bool EvaluateCondition(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return false;
        }

        try
        {
            // If we have an evaluator, use it
            if (_evaluator != null)
            {
                var result = _evaluator.Evaluate(condition, _scope);
                return ConvertToBool(result);
            }

            // Fall back to simple variable lookup for simple conditions
            // Handle ${variable} format
            if (condition.StartsWith("${") && condition.EndsWith("}"))
            {
                var varName = condition[2..^1].Trim();
                var value = _scope.GetValue(varName);
                return ConvertToBool(value);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public string EvaluateTemplate(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        try
        {
            // Handle {{ variable }} mustache-style templates
            return TemplateEvaluator.Evaluate(text, _scope);
        }
        catch
        {
            return text;
        }
    }

    /// <inheritdoc/>
    public object? GetVariable(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        return _scope.GetValue(name);
    }

    private static bool ConvertToBool(object? value)
    {
        return value switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            double d => d != 0,
            float f => f != 0,
            string s => !string.IsNullOrEmpty(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
            null => false,
            _ => true
        };
    }
}

/// <summary>
/// Simple template evaluator for {{ variable }} syntax.
/// </summary>
internal static class TemplateEvaluator
{
    /// <summary>
    /// Evaluates a template string, replacing {{ variable }} with values from scope.
    /// </summary>
    /// <param name="template">The template string.</param>
    /// <param name="scope">The variable scope.</param>
    /// <returns>The evaluated string.</returns>
    public static string Evaluate(string template, IVariableScope scope)
    {
        if (!template.Contains("{{"))
        {
            return template;
        }

        var result = new System.Text.StringBuilder(template.Length);
        var index = 0;

        while (index < template.Length)
        {
            var start = template.IndexOf("{{", index, StringComparison.Ordinal);
            if (start < 0)
            {
                // No more templates
                result.Append(template.AsSpan(index));
                break;
            }

            // Append text before template
            result.Append(template.AsSpan(index, start - index));

            var end = template.IndexOf("}}", start + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                // Unclosed template, append rest as-is
                result.Append(template.AsSpan(start));
                break;
            }

            // Extract and evaluate variable
            var varName = template.Substring(start + 2, end - start - 2).Trim();
            var value = EvaluateVariable(varName, scope);
            result.Append(value);

            index = end + 2;
        }

        return result.ToString();
    }

    private static string EvaluateVariable(string expression, IVariableScope scope)
    {
        // Handle simple variable reference
        var value = scope.GetValue(expression);
        if (value != null)
        {
            return FormatValue(value);
        }

        // Handle dotted paths (e.g., player.name)
        var parts = expression.Split('.');
        if (parts.Length > 1)
        {
            value = scope.GetValue(parts[0]);
            for (var i = 1; i < parts.Length && value != null; i++)
            {
                value = GetProperty(value, parts[i]);
            }

            if (value != null)
            {
                return FormatValue(value);
            }
        }

        // Return empty for unknown variables
        return string.Empty;
    }

    private static object? GetProperty(object obj, string propertyName)
    {
        if (obj is IReadOnlyDictionary<string, object?> dict)
        {
            return dict.TryGetValue(propertyName, out var value) ? value : null;
        }

        if (obj is IDictionary<string, object> mutableDict)
        {
            return mutableDict.TryGetValue(propertyName, out var value) ? value : null;
        }

        // Try reflection for objects
        var property = obj.GetType().GetProperty(propertyName);
        return property?.GetValue(obj);
    }

    private static string FormatValue(object value)
    {
        return value switch
        {
            string s => s,
            double d => d.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            float f => f.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }
}
