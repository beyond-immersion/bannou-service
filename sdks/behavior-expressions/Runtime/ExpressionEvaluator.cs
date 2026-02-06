// ═══════════════════════════════════════════════════════════════════════════
// ABML Expression Evaluator Implementation
// Concrete implementation of the expression evaluator.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Compiler;
using BeyondImmersion.Bannou.BehaviorCompiler.Exceptions;
using BeyondImmersion.Bannou.BehaviorExpressions.Compiler;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.Bannou.BehaviorExpressions.Functions;

namespace BeyondImmersion.Bannou.BehaviorExpressions.Runtime;

/// <summary>
/// Concrete implementation of <see cref="IExpressionEvaluator"/>.
/// Thread-safe for evaluation of cached expressions.
/// </summary>
public sealed class ExpressionEvaluator : IExpressionEvaluator
{
    private readonly ExpressionCache _cache;
    private readonly ExpressionCompiler _compiler;
    private readonly IFunctionRegistry _functions;

    // Thread-local VM instances for thread safety
    private readonly ThreadLocal<ExpressionVm> _vm;

    /// <summary>
    /// Creates a new expression evaluator with default settings.
    /// </summary>
    public ExpressionEvaluator()
        : this(FunctionRegistry.CreateWithBuiltins(), VmConfig.DefaultCacheSize)
    {
    }

    /// <summary>
    /// Creates a new expression evaluator with the specified function registry.
    /// </summary>
    /// <param name="functions">The function registry.</param>
    /// <param name="cacheSize">Expression cache size.</param>
    public ExpressionEvaluator(
        IFunctionRegistry functions,
        int cacheSize = VmConfig.DefaultCacheSize)
    {
        _functions = functions ?? throw new ArgumentNullException(nameof(functions));
        _cache = new ExpressionCache(cacheSize);
        _compiler = new ExpressionCompiler();
        _vm = new ThreadLocal<ExpressionVm>(() => new ExpressionVm(_functions));
    }

    /// <inheritdoc/>
    public object? Evaluate(string expression, IVariableScope scope)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(scope);

        var compiled = _cache.GetOrCompile(expression, _compiler.Compile);
        return _vm.Value?.Execute(compiled, scope);
    }

    /// <inheritdoc/>
    public T? Evaluate<T>(string expression, IVariableScope scope)
    {
        var result = Evaluate(expression, scope);

        if (result is null)
        {
            return default;
        }

        if (result is T typedResult)
        {
            return typedResult;
        }

        // Try to convert
        try
        {
            return (T)Convert.ChangeType(result, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    /// <inheritdoc/>
    public bool EvaluateCondition(string expression, IVariableScope scope)
    {
        var result = Evaluate(expression, scope);
        return AbmlTypeCoercion.IsTrue(result);
    }

    /// <inheritdoc/>
    public CompiledExpression Compile(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return _cache.GetOrCompile(expression, _compiler.Compile);
    }

    /// <inheritdoc/>
    public object? Execute(CompiledExpression compiled, IVariableScope scope)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(scope);

        return _vm.Value?.Execute(compiled, scope);
    }

    /// <inheritdoc/>
    public bool TryEvaluate(string expression, IVariableScope scope, out object? result)
    {
        try
        {
            result = Evaluate(expression, scope);
            return true;
        }
        catch (AbmlException)
        {
            result = null;
            return false;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public CacheStatistics GetCacheStatistics()
    {
        return _cache.GetStatistics();
    }

    /// <inheritdoc/>
    public void ClearCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Creates a default expression evaluator with built-in functions.
    /// </summary>
    /// <returns>A new expression evaluator.</returns>
    public static ExpressionEvaluator CreateDefault()
    {
        return new ExpressionEvaluator();
    }

    /// <summary>
    /// Creates an expression evaluator with custom functions.
    /// </summary>
    /// <param name="customFunctions">Action to register custom functions.</param>
    /// <returns>A new expression evaluator.</returns>
    public static ExpressionEvaluator CreateWithCustomFunctions(Action<IFunctionRegistry> customFunctions)
    {
        var registry = FunctionRegistry.CreateWithBuiltins();
        customFunctions(registry);
        return new ExpressionEvaluator(registry);
    }
}

/// <summary>
/// Extension methods for expression evaluation.
/// </summary>
public static class ExpressionEvaluatorExtensions
{
    /// <summary>
    /// Evaluates an expression with a dictionary of variables.
    /// </summary>
    /// <param name="evaluator">The expression evaluator.</param>
    /// <param name="expression">The expression string.</param>
    /// <param name="variables">The variables to use.</param>
    /// <returns>The result of the expression evaluation.</returns>
    public static object? Evaluate(
        this IExpressionEvaluator evaluator,
        string expression,
        IDictionary<string, object?> variables)
    {
        var scope = new VariableScope();
        scope.SetVariables(variables);
        return evaluator.Evaluate(expression, scope);
    }

    /// <summary>
    /// Evaluates an expression with a dictionary of variables and returns a typed result.
    /// </summary>
    /// <typeparam name="T">The expected result type.</typeparam>
    /// <param name="evaluator">The expression evaluator.</param>
    /// <param name="expression">The expression string.</param>
    /// <param name="variables">The variables to use.</param>
    /// <returns>The typed result of the expression evaluation.</returns>
    public static T? Evaluate<T>(
        this IExpressionEvaluator evaluator,
        string expression,
        IDictionary<string, object?> variables)
    {
        var scope = new VariableScope();
        scope.SetVariables(variables);
        return evaluator.Evaluate<T>(expression, scope);
    }

    /// <summary>
    /// Evaluates a condition expression with a dictionary of variables.
    /// </summary>
    /// <param name="evaluator">The expression evaluator.</param>
    /// <param name="expression">The expression string.</param>
    /// <param name="variables">The variables to use.</param>
    /// <returns>The truthiness of the expression result.</returns>
    public static bool EvaluateCondition(
        this IExpressionEvaluator evaluator,
        string expression,
        IDictionary<string, object?> variables)
    {
        var scope = new VariableScope();
        scope.SetVariables(variables);
        return evaluator.EvaluateCondition(expression, scope);
    }
}
