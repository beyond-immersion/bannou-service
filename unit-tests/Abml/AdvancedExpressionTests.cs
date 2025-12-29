// ═══════════════════════════════════════════════════════════════════════════
// ABML Advanced Expression Tests
// Tests for short-circuit evaluation, cache behavior, thread safety, and edge cases.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Compiler;
using BeyondImmersion.BannouService.Abml.Exceptions;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Functions;
using BeyondImmersion.BannouService.Abml.Runtime;
using Xunit;

namespace BeyondImmersion.BannouService.UnitTests.Abml;

/// <summary>
/// Advanced tests for expression evaluation edge cases and behaviors.
/// </summary>
public class AdvancedExpressionTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // SHORT-CIRCUIT SIDE-EFFECT VERIFICATION
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ShortCircuit_And_DoesNotEvaluateRightWhenLeftIsFalse()
    {
        var callCount = 0;
        var registry = FunctionRegistry.CreateWithBuiltins();
        registry.Register("side_effect", args =>
        {
            callCount++;
            return true;
        }, minArgs: 0, maxArgs: 0);

        var evaluator = new ExpressionEvaluator(registry);
        var scope = new VariableScope();

        var result = evaluator.Evaluate("false && side_effect()", scope);

        Assert.Equal(false, result);
        Assert.Equal(0, callCount); // side_effect should NOT have been called
    }

    [Fact]
    public void ShortCircuit_And_EvaluatesRightWhenLeftIsTrue()
    {
        var callCount = 0;
        var registry = FunctionRegistry.CreateWithBuiltins();
        registry.Register("side_effect", args =>
        {
            callCount++;
            return true;
        }, minArgs: 0, maxArgs: 0);

        var evaluator = new ExpressionEvaluator(registry);
        var scope = new VariableScope();

        var result = evaluator.Evaluate("true && side_effect()", scope);

        Assert.Equal(true, result);
        Assert.Equal(1, callCount); // side_effect SHOULD have been called
    }

    [Fact]
    public void ShortCircuit_Or_DoesNotEvaluateRightWhenLeftIsTrue()
    {
        var callCount = 0;
        var registry = FunctionRegistry.CreateWithBuiltins();
        registry.Register("side_effect", args =>
        {
            callCount++;
            return false;
        }, minArgs: 0, maxArgs: 0);

        var evaluator = new ExpressionEvaluator(registry);
        var scope = new VariableScope();

        var result = evaluator.Evaluate("true || side_effect()", scope);

        Assert.Equal(true, result);
        Assert.Equal(0, callCount); // side_effect should NOT have been called
    }

    [Fact]
    public void ShortCircuit_Or_EvaluatesRightWhenLeftIsFalse()
    {
        var callCount = 0;
        var registry = FunctionRegistry.CreateWithBuiltins();
        registry.Register("side_effect", args =>
        {
            callCount++;
            return true;
        }, minArgs: 0, maxArgs: 0);

        var evaluator = new ExpressionEvaluator(registry);
        var scope = new VariableScope();

        var result = evaluator.Evaluate("false || side_effect()", scope);

        Assert.Equal(true, result);
        Assert.Equal(1, callCount); // side_effect SHOULD have been called
    }

    [Fact]
    public void ShortCircuit_And_ChainedDoesNotEvaluateLaterTerms()
    {
        var callCounts = new int[3];
        var registry = FunctionRegistry.CreateWithBuiltins();

        registry.Register("effect1", args => { callCounts[0]++; return true; }, 0, 0);
        registry.Register("effect2", args => { callCounts[1]++; return false; }, 0, 0);
        registry.Register("effect3", args => { callCounts[2]++; return true; }, 0, 0);

        var evaluator = new ExpressionEvaluator(registry);
        var scope = new VariableScope();

        // effect2 returns false, so effect3 should not be evaluated
        var result = evaluator.Evaluate("effect1() && effect2() && effect3()", scope);

        Assert.Equal(false, result);
        Assert.Equal(1, callCounts[0]); // effect1 called
        Assert.Equal(1, callCounts[1]); // effect2 called
        Assert.Equal(0, callCounts[2]); // effect3 NOT called (short-circuited)
    }

    [Fact]
    public void ShortCircuit_Ternary_OnlyEvaluatesSelectedBranch()
    {
        var trueBranchCalled = false;
        var falseBranchCalled = false;
        var registry = FunctionRegistry.CreateWithBuiltins();

        registry.Register("true_branch", args => { trueBranchCalled = true; return "yes"; }, 0, 0);
        registry.Register("false_branch", args => { falseBranchCalled = true; return "no"; }, 0, 0);

        var evaluator = new ExpressionEvaluator(registry);
        var scope = new VariableScope();

        // Condition is true, so only true_branch should be evaluated
        var result = evaluator.Evaluate("true ? true_branch() : false_branch()", scope);

        Assert.Equal("yes", result);
        Assert.True(trueBranchCalled);
        Assert.False(falseBranchCalled);
    }

    [Fact]
    public void ShortCircuit_NullCoalesce_DoesNotEvaluateRightWhenLeftIsNotNull()
    {
        var callCount = 0;
        var registry = FunctionRegistry.CreateWithBuiltins();
        registry.Register("fallback", args =>
        {
            callCount++;
            return "fallback";
        }, minArgs: 0, maxArgs: 0);

        var evaluator = new ExpressionEvaluator(registry);
        var scope = new VariableScope();
        scope.SetValue("value", "exists");

        var result = evaluator.Evaluate("value ?? fallback()", scope);

        Assert.Equal("exists", result);
        Assert.Equal(0, callCount); // fallback should NOT have been called
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CACHE EVICTION LRU BEHAVIOR
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Cache_EvictsLeastRecentlyUsedWhenFull()
    {
        var cache = new ExpressionCache(maxSize: 5);
        var compiler = new ExpressionCompiler();

        // Fill the cache with 5 expressions
        for (var i = 0; i < 5; i++)
        {
            cache.GetOrCompile($"expr{i}", compiler.Compile);
        }

        Assert.Equal(5, cache.Count);
        Assert.True(cache.Contains("expr0"));

        // Access expr1-4 to make expr0 the least recently used
        cache.TryGet("expr1", out _);
        cache.TryGet("expr2", out _);
        cache.TryGet("expr3", out _);
        cache.TryGet("expr4", out _);

        // Add a new expression, should trigger eviction
        cache.GetOrCompile("expr5", compiler.Compile);

        // After eviction, cache should be at or below max size
        Assert.True(cache.Count <= 5);

        // expr0 should be evicted as LRU
        Assert.False(cache.Contains("expr0"));
    }

    [Fact]
    public void Cache_MaintainsSizeLimit()
    {
        var cache = new ExpressionCache(maxSize: 10);
        var compiler = new ExpressionCompiler();

        // Add more expressions than the limit
        for (var i = 0; i < 20; i++)
        {
            cache.GetOrCompile($"1 + {i}", compiler.Compile);
        }

        // Cache should not exceed max size
        Assert.True(cache.Count <= 10);
    }

    [Fact]
    public void Cache_TracksHitsAndMisses()
    {
        var cache = new ExpressionCache(maxSize: 10);
        var compiler = new ExpressionCompiler();

        // First access - miss
        cache.GetOrCompile("1 + 2", compiler.Compile);
        Assert.Equal(1, cache.MissCount);
        Assert.Equal(0, cache.HitCount);

        // Second access - hit
        cache.GetOrCompile("1 + 2", compiler.Compile);
        Assert.Equal(1, cache.MissCount);
        Assert.Equal(1, cache.HitCount);

        // Third access - hit
        cache.GetOrCompile("1 + 2", compiler.Compile);
        Assert.Equal(1, cache.MissCount);
        Assert.Equal(2, cache.HitCount);

        Assert.True(cache.HitRatio > 0.6); // 2 hits / 3 total
    }

    [Fact]
    public void Cache_ClearResetsStatistics()
    {
        var cache = new ExpressionCache(maxSize: 10);
        var compiler = new ExpressionCompiler();

        cache.GetOrCompile("1 + 2", compiler.Compile);
        cache.GetOrCompile("1 + 2", compiler.Compile);

        Assert.True(cache.HitCount > 0 || cache.MissCount > 0);

        cache.Clear();

        Assert.Equal(0, cache.HitCount);
        Assert.Equal(0, cache.MissCount);
        Assert.Equal(0, cache.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // THREAD SAFETY CONCURRENT EVALUATION
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Concurrent_EvaluationIsThreadSafe()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var exceptions = new List<Exception>();
        var successCount = 0;

        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            try
            {
                var scope = new VariableScope();
                scope.SetValue("x", i);

                var result = evaluator.Evaluate("x * 2", scope);
                Assert.Equal((double)(i * 2), result);
                Interlocked.Increment(ref successCount);
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        }));

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
        Assert.Equal(100, successCount);
    }

    [Fact]
    public async Task Concurrent_CacheAccessIsThreadSafe()
    {
        var cache = new ExpressionCache(maxSize: 50);
        var compiler = new ExpressionCompiler();
        var exceptions = new List<Exception>();

        var tasks = Enumerable.Range(0, 200).Select(i => Task.Run(() =>
        {
            try
            {
                // Access shared expressions and unique expressions
                var expr = i % 10 == 0 ? "shared" : $"unique_{i}";
                cache.GetOrCompile(expr, compiler.Compile);
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        }));

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
        Assert.True(cache.Count <= 50);
    }

    [Fact]
    public async Task Concurrent_FunctionCallsAreIsolated()
    {
        var callCount = 0;
        var registry = FunctionRegistry.CreateWithBuiltins();
        registry.Register("increment", args =>
        {
            return Interlocked.Increment(ref callCount);
        }, minArgs: 0, maxArgs: 0);

        var evaluator = new ExpressionEvaluator(registry);

        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            var scope = new VariableScope();
            return evaluator.Evaluate("increment()", scope);
        }));

        var results = await Task.WhenAll(tasks);

        // All results should be unique (1 through 50)
        Assert.Equal(50, results.Distinct().Count());
        Assert.Equal(50, callCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FUNCTION ARITY ERROR HANDLING
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Function_TooFewArguments_ThrowsRuntimeException()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // min expects exactly 2 arguments
        var ex = Assert.Throws<AbmlRuntimeException>(() =>
            evaluator.Evaluate("min(5)", scope));

        Assert.Contains("min", ex.Message);
        Assert.Contains("argument", ex.Message.ToLower());
    }

    [Fact]
    public void Function_TooManyArguments_ThrowsRuntimeException()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // length expects exactly 1 argument
        var ex = Assert.Throws<AbmlRuntimeException>(() =>
            evaluator.Evaluate("length('a', 'b')", scope));

        Assert.Contains("length", ex.Message);
        Assert.Contains("argument", ex.Message.ToLower());
    }

    [Fact]
    public void Function_ZeroArgsWhenRequired_ThrowsRuntimeException()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // abs expects exactly 1 argument
        var ex = Assert.Throws<AbmlRuntimeException>(() =>
            evaluator.Evaluate("abs()", scope));

        Assert.Contains("abs", ex.Message);
    }

    [Fact]
    public void Function_UnknownFunction_ThrowsRuntimeException()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        var ex = Assert.Throws<AbmlRuntimeException>(() =>
            evaluator.Evaluate("nonexistent_function()", scope));

        Assert.Contains("nonexistent_function", ex.Message);
        Assert.Contains("Unknown", ex.Message);
    }

    [Fact]
    public void Function_VariableArgsAcceptsValidCounts()
    {
        var registry = FunctionRegistry.CreateWithBuiltins();
        registry.Register("varargs", args => args.Length, minArgs: 1, maxArgs: 5);

        var evaluator = new ExpressionEvaluator(registry);
        var scope = new VariableScope();

        Assert.Equal(1, evaluator.Evaluate("varargs(1)", scope));
        Assert.Equal(3, evaluator.Evaluate("varargs(1, 2, 3)", scope));
        Assert.Equal(5, evaluator.Evaluate("varargs(1, 2, 3, 4, 5)", scope));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EDGE CASES: DEEP NESTING AND LONG EXPRESSIONS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EdgeCase_DeeplyNestedParentheses()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // 20 levels of nesting
        var expr = "((((((((((((((((((((1 + 2))))))))))))))))))))";
        var result = evaluator.Evaluate(expr, scope);

        Assert.Equal(3.0, result);
    }

    [Fact]
    public void EdgeCase_DeeplyNestedPropertyAccess()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // Create nested object structure
        var level5 = new Dictionary<string, object?> { ["value"] = 42 };
        var level4 = new Dictionary<string, object?> { ["d"] = level5 };
        var level3 = new Dictionary<string, object?> { ["c"] = level4 };
        var level2 = new Dictionary<string, object?> { ["b"] = level3 };
        var level1 = new Dictionary<string, object?> { ["a"] = level2 };
        scope.SetValue("obj", level1);

        var result = evaluator.Evaluate("obj.a.b.c.d.value", scope);

        Assert.Equal(42, result);
    }

    [Fact]
    public void EdgeCase_LongArithmeticChain()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // Long chain of additions: 1 + 2 + 3 + ... + 50
        var expr = string.Join(" + ", Enumerable.Range(1, 50));
        var result = evaluator.Evaluate(expr, scope);

        // Sum of 1 to 50 = 50 * 51 / 2 = 1275
        Assert.Equal(1275.0, result);
    }

    [Fact]
    public void EdgeCase_LongLogicalChain()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // Long chain of ANDs: true && true && ... && true (20 times)
        var expr = string.Join(" && ", Enumerable.Repeat("true", 20));
        var result = evaluator.Evaluate(expr, scope);

        Assert.Equal(true, result);
    }

    [Fact]
    public void EdgeCase_ComplexNestedTernary()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();
        scope.SetValue("x", 5);

        // Nested ternary: x < 3 ? 'small' : x < 7 ? 'medium' : 'large'
        var result = evaluator.Evaluate("x < 3 ? 'small' : x < 7 ? 'medium' : 'large'", scope);

        Assert.Equal("medium", result);
    }

    [Fact]
    public void EdgeCase_ManyFunctionArguments()
    {
        var registry = FunctionRegistry.CreateWithBuiltins();
        registry.Register("sum_all", args =>
        {
            return args.Sum(a => Convert.ToDouble(a));
        }, minArgs: 1, maxArgs: 20);

        var evaluator = new ExpressionEvaluator(registry);
        var scope = new VariableScope();

        var result = evaluator.Evaluate("sum_all(1, 2, 3, 4, 5, 6, 7, 8, 9, 10)", scope);

        Assert.Equal(55.0, result);
    }

    [Fact]
    public void EdgeCase_NestedFunctionCalls()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // abs(min(max(-10, -5), 3))
        var result = evaluator.Evaluate("abs(min(max(-10, -5), 3))", scope);

        // max(-10, -5) = -5
        // min(-5, 3) = -5
        // abs(-5) = 5
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void EdgeCase_VeryLongStringLiteral()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        var longString = new string('a', 1000);
        var result = evaluator.Evaluate($"length('{longString}')", scope);

        Assert.Equal(1000, result);
    }

    [Fact]
    public void EdgeCase_MixedOperatorPrecedence()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // Complex precedence: 1 + 2 * 3 - 4 / 2 + 5 % 3
        // = 1 + 6 - 2 + 2 = 7
        var result = evaluator.Evaluate("1 + 2 * 3 - 4 / 2 + 5 % 3", scope);

        Assert.Equal(7.0, result);
    }

    [Fact]
    public void EdgeCase_NullSafeChainWithMixedNulls()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        var obj = new Dictionary<string, object?>
        {
            ["a"] = new Dictionary<string, object?>
            {
                ["b"] = null // Middle of chain is null
            }
        };
        scope.SetValue("obj", obj);

        // obj.a.b?.c should return null without error
        var result = evaluator.Evaluate("obj.a.b?.c", scope);

        Assert.Null(result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NESTED FUNCTION CALL TESTS (Correction 4 verification)
    // These tests verify the CallArgs + Call opcode pattern works correctly
    // when function arguments are nested function calls.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NestedFunctions_SingleLevel()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // min(max(5, 10), 8) = min(10, 8) = 8
        var result = evaluator.Evaluate("min(max(5, 10), 8)", scope);
        Assert.Equal(8.0, result);
    }

    [Fact]
    public void NestedFunctions_BothArgsNested()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // max(min(1, 2), min(3, 4)) = max(1, 3) = 3
        var result = evaluator.Evaluate("max(min(1, 2), min(3, 4))", scope);
        Assert.Equal(3.0, result);
    }

    [Fact]
    public void NestedFunctions_ThreeLevelsDeep()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // abs(min(max(-10, -5), floor(3.7)))
        // = abs(min(-5, 3))
        // = abs(-5)
        // = 5
        var result = evaluator.Evaluate("abs(min(max(-10, -5), floor(3.7)))", scope);
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void NestedFunctions_WithArithmeticInArgs()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();
        scope.SetValue("x", 7);

        // min(x + 3, max(x - 2, 4))
        // = min(10, max(5, 4))
        // = min(10, 5)
        // = 5
        var result = evaluator.Evaluate("min(x + 3, max(x - 2, 4))", scope);
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void NestedFunctions_StringFunctions()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // length(upper("hello")) = length("HELLO") = 5
        var result = evaluator.Evaluate("length(upper('hello'))", scope);
        Assert.Equal(5, result);
    }

    [Fact]
    public void NestedFunctions_MixedTypes()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // round(max(1.5, min(2.5, 3.5))) = round(max(1.5, 2.5)) = round(2.5) = 3
        var result = evaluator.Evaluate("round(max(1.5, min(2.5, 3.5)))", scope);
        Assert.Equal(3.0, result);  // Standard rounding: 2.5 rounds to 3
    }

    [Fact]
    public void NestedFunctions_InTernary()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();
        scope.SetValue("val", 10);

        // val > 5 ? max(val, 20) : min(val, 0)
        // = 10 > 5 ? max(10, 20) : min(10, 0)
        // = true ? 20 : min(10, 0)
        // = 20
        var result = evaluator.Evaluate("val > 5 ? max(val, 20) : min(val, 0)", scope);
        Assert.Equal(20.0, result);
    }

    [Fact]
    public void NestedFunctions_CustomFunctionWithNested()
    {
        var registry = FunctionRegistry.CreateWithBuiltins();
        registry.Register("double", args => Convert.ToDouble(args[0]) * 2, minArgs: 1, maxArgs: 1);
        registry.Register("square", args => Math.Pow(Convert.ToDouble(args[0]), 2), minArgs: 1, maxArgs: 1);

        var evaluator = new ExpressionEvaluator(registry);
        var scope = new VariableScope();

        // double(square(3)) = double(9) = 18
        var result = evaluator.Evaluate("double(square(3))", scope);
        Assert.Equal(18.0, result);
    }

    [Fact]
    public void NestedFunctions_MultipleArgsAllNested()
    {
        var registry = FunctionRegistry.CreateWithBuiltins();
        registry.Register("add3", args =>
        {
            return Convert.ToDouble(args[0]) + Convert.ToDouble(args[1]) + Convert.ToDouble(args[2]);
        }, minArgs: 3, maxArgs: 3);

        var evaluator = new ExpressionEvaluator(registry);
        var scope = new VariableScope();

        // add3(min(5, 2), max(3, 1), abs(-4))
        // = add3(2, 3, 4)
        // = 9
        var result = evaluator.Evaluate("add3(min(5, 2), max(3, 1), abs(-4))", scope);
        Assert.Equal(9.0, result);
    }

    [Fact]
    public void NestedFunctions_DeepNesting()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();

        // max(min(abs(floor(-2.7)), ceil(1.2)), round(2.4))
        // = max(min(abs(-3), 2), 2)
        // = max(min(3, 2), 2)
        // = max(2, 2)
        // = 2
        var result = evaluator.Evaluate("max(min(abs(floor(-2.7)), ceil(1.2)), round(2.4))", scope);
        Assert.Equal(2.0, result);
    }

    [Fact]
    public void NestedFunctions_WithVariables()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();
        scope.SetValue("a", 5);
        scope.SetValue("b", -3);
        scope.SetValue("c", 10);

        // min(max(a, b), c) = min(max(5, -3), 10) = min(5, 10) = 5
        var result = evaluator.Evaluate("min(max(a, b), c)", scope);
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void NestedFunctions_ComplexExpression()
    {
        var evaluator = ExpressionEvaluator.CreateDefault();
        var scope = new VariableScope();
        // Use doubles to get floating-point division (int/int = integer division in ABML)
        scope.SetValue("health", 45.0);
        scope.SetValue("maxHealth", 100.0);

        // Calculate clamped health percentage
        // min(max(health / maxHealth * 100, 0), 100)
        var result = evaluator.Evaluate("min(max(health / maxHealth * 100, 0), 100)", scope);
        Assert.Equal(45.0, result);
    }
}
