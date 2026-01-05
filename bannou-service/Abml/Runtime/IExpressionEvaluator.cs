// ═══════════════════════════════════════════════════════════════════════════
// ABML Expression Evaluator Interface
// Top-level API for evaluating ABML expressions.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Compiler;
using BeyondImmersion.BannouService.Abml.Expressions;

namespace BeyondImmersion.BannouService.Abml.Runtime;

/// <summary>
/// Top-level API for evaluating ABML expressions.
/// </summary>
public interface IExpressionEvaluator
{
    /// <summary>
    /// Evaluates an expression string with the given variable scope.
    /// </summary>
    /// <param name="expression">The expression string (e.g., "${health > 0.5}").</param>
    /// <param name="scope">The variable scope for variable resolution.</param>
    /// <returns>The result of the expression evaluation.</returns>
    object? Evaluate(string expression, IVariableScope scope);

    /// <summary>
    /// Evaluates an expression string and converts the result to the specified type.
    /// </summary>
    /// <typeparam name="T">The expected result type.</typeparam>
    /// <param name="expression">The expression string.</param>
    /// <param name="scope">The variable scope for variable resolution.</param>
    /// <returns>The result converted to type T, or default if conversion fails.</returns>
    T? Evaluate<T>(string expression, IVariableScope scope);

    /// <summary>
    /// Evaluates an expression string as a boolean condition.
    /// </summary>
    /// <param name="expression">The expression string.</param>
    /// <param name="scope">The variable scope for variable resolution.</param>
    /// <returns>The truthiness of the expression result.</returns>
    bool EvaluateCondition(string expression, IVariableScope scope);

    /// <summary>
    /// Compiles an expression string to bytecode without executing it.
    /// </summary>
    /// <param name="expression">The expression string.</param>
    /// <returns>The compiled expression.</returns>
    CompiledExpression Compile(string expression);

    /// <summary>
    /// Executes a pre-compiled expression with the given variable scope.
    /// </summary>
    /// <param name="compiled">The compiled expression.</param>
    /// <param name="scope">The variable scope for variable resolution.</param>
    /// <returns>The result of the expression execution.</returns>
    object? Execute(CompiledExpression compiled, IVariableScope scope);

    /// <summary>
    /// Tries to evaluate an expression, returning false on failure.
    /// </summary>
    /// <param name="expression">The expression string.</param>
    /// <param name="scope">The variable scope.</param>
    /// <param name="result">The result if successful.</param>
    /// <returns>True if evaluation succeeded.</returns>
    bool TryEvaluate(string expression, IVariableScope scope, out object? result);

    /// <summary>
    /// Gets statistics about the expression cache.
    /// </summary>
    /// <returns>Cache statistics.</returns>
    CacheStatistics GetCacheStatistics();

    /// <summary>
    /// Clears the expression cache.
    /// </summary>
    void ClearCache();
}
