// =============================================================================
// ABML Document Executor Tests
// Tests for document execution.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Parser;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Abml.Execution;
using Xunit;

namespace BeyondImmersion.BannouService.Tests.Abml;

/// <summary>
/// Tests for DocumentExecutor.
/// </summary>
public class DocumentExecutorTests
{
    private readonly DocumentParser _parser = new();
    private readonly DocumentExecutor _executor = new();

    // =========================================================================
    // BASIC EXECUTION TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_SimpleLog_Succeeds()
    {
        var yaml = TestFixtures.Load("executor_simple_log");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Hello World", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_MultipleActions_ExecutesInOrder()
    {
        var yaml = TestFixtures.Load("executor_multi_action");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Logs.Count);
        Assert.Equal("First", result.Logs[0].Message);
        Assert.Equal("Second", result.Logs[1].Message);
        Assert.Equal("Third", result.Logs[2].Message);
    }

    [Fact]
    public async Task Execute_NonExistentFlow_Fails()
    {
        var yaml = TestFixtures.Load("executor_missing_flow");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "nonexistent");

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error);
    }

    // =========================================================================
    // VARIABLE TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_SetVariable_StoresValue()
    {
        var yaml = TestFixtures.Load("executor_set_var");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Hello Alice", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_IncrementVariable_IncreasesValue()
    {
        var yaml = TestFixtures.Load("executor_increment");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Contains("Counter: 8", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_DecrementVariable_DecreasesValue()
    {
        var yaml = TestFixtures.Load("executor_decrement");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Contains("Lives: 2", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_ClearVariable_SetsToNull()
    {
        var yaml = TestFixtures.Load("executor_clear");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Contains("Temp: null", result.Logs[0].Message);
    }

    // =========================================================================
    // CONDITIONAL TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_CondTrue_ExecutesThenBranch()
    {
        var yaml = TestFixtures.Load("executor_cond_true");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("High", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_CondFalse_ExecutesElseBranch()
    {
        var yaml = TestFixtures.Load("executor_cond_false");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Low", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_CondMultipleBranches_ExecutesFirstMatch()
    {
        var yaml = TestFixtures.Load("executor_cond_multi");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("High", result.Logs[0].Message);
    }

    // =========================================================================
    // LOOP TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_Repeat_ExecutesCorrectTimes()
    {
        var yaml = TestFixtures.Load("executor_repeat");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Contains("Count: 3", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_ForEach_IteratesCollection()
    {
        var yaml = TestFixtures.Load("executor_foreach");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        // Create scope with items
        var scope = new VariableScope();
        scope.SetValue("items", new List<string> { "apple", "banana", "cherry" });

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start", scope);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Logs.Count);
        Assert.Equal("Item: apple", result.Logs[0].Message);
        Assert.Equal("Item: banana", result.Logs[1].Message);
        Assert.Equal("Item: cherry", result.Logs[2].Message);
    }

    [Fact]
    public async Task Execute_ForEach_NullCollection_NoIterations()
    {
        var yaml = TestFixtures.Load("executor_foreach_null");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Done", result.Logs[0].Message);
    }

    // =========================================================================
    // FLOW CONTROL TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_Goto_TransfersControl()
    {
        var yaml = TestFixtures.Load("executor_goto");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Logs.Count);
        Assert.Equal("Start", result.Logs[0].Message);
        Assert.Equal("Other", result.Logs[1].Message);
    }

    [Fact]
    public async Task Execute_GotoWithArgs_PassesArguments()
    {
        var yaml = TestFixtures.Load("executor_goto_args");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Value: 42", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_Call_ExecutesAndReturns()
    {
        var yaml = TestFixtures.Load("executor_call");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Logs.Count);
        Assert.Equal("Before call", result.Logs[0].Message);
        Assert.Equal("In subroutine", result.Logs[1].Message);
        Assert.Equal("After call", result.Logs[2].Message);
    }

    [Fact]
    public async Task Execute_Return_ExitsFlow()
    {
        var yaml = TestFixtures.Load("executor_return");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Equal("done", result.Value);
        Assert.Single(result.Logs);
        Assert.Equal("Before return", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_CallWithReturn_ReturnsValue()
    {
        var yaml = TestFixtures.Load("executor_call_return");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Result: 5", result.Logs[0].Message);
    }

    // =========================================================================
    // INITIAL SCOPE TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_WithInitialScope_UsesProvidedVariables()
    {
        var yaml = TestFixtures.Load("executor_initial_scope");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scope = new VariableScope();
        scope.SetValue("player_name", "Hero");
        scope.SetValue("player_level", 10);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start", scope);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Name: Hero, Level: 10", result.Logs[0].Message);
    }

    // =========================================================================
    // CANCELLATION TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_WithCancellation_Cancels()
    {
        var yaml = TestFixtures.Load("executor_cancel");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start", ct: cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("cancelled", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // COMPLEX SCENARIO TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_ComplexBehavior_ExecutesCorrectly()
    {
        var yaml = TestFixtures.Load("executor_complex");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Logs.Count);
        Assert.Equal("High energy", result.Logs[0].Message);
        Assert.Equal("Final energy: 70", result.Logs[1].Message);
    }

    [Fact]
    public async Task Execute_NestedLoopsAndConditions_ExecutesCorrectly()
    {
        var yaml = TestFixtures.Load("executor_nested");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scope = new VariableScope();
        scope.SetValue("rows", new List<int> { 1, 2, 3 });
        scope.SetValue("cols", new List<int> { 1, 2, 3 });

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start", scope);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Diagonal count: 3", result.Logs[0].Message);
    }
}
