// ═══════════════════════════════════════════════════════════════════════════
// ABML Document Executor Tests
// Tests for document execution.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Parser;
using Xunit;

namespace BeyondImmersion.BannouService.UnitTests.Abml;

/// <summary>
/// Tests for DocumentExecutor.
/// </summary>
public class DocumentExecutorTests
{
    private readonly DocumentParser _parser = new();
    private readonly DocumentExecutor _executor = new();

    // ═══════════════════════════════════════════════════════════════════════
    // BASIC EXECUTION TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_SimpleLog_Succeeds()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: simple_log
            flows:
              start:
                actions:
                  - log: "Hello World"
            """;

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
        var yaml = """
            version: "2.0"
            metadata:
              id: multi_action
            flows:
              start:
                actions:
                  - log: "First"
                  - log: "Second"
                  - log: "Third"
            """;

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
        var yaml = """
            version: "2.0"
            metadata:
              id: missing_flow
            flows:
              start:
                actions:
                  - log: "Hello"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "nonexistent");

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VARIABLE TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_SetVariable_StoresValue()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: set_var
            flows:
              start:
                actions:
                  - set:
                      variable: name
                      value: "Alice"
                  - log: { message: "Hello ${name}" }
            """;

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
        var yaml = """
            version: "2.0"
            metadata:
              id: increment
            flows:
              start:
                actions:
                  - set:
                      variable: counter
                      value: "5"
                  - increment:
                      variable: counter
                      by: 3
                  - log: { message: "Counter: ${counter}" }
            """;

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
        var yaml = """
            version: "2.0"
            metadata:
              id: decrement
            flows:
              start:
                actions:
                  - set:
                      variable: lives
                      value: "3"
                  - decrement:
                      variable: lives
                  - log: { message: "Lives: ${lives}" }
            """;

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
        var yaml = """
            version: "2.0"
            metadata:
              id: clear
            flows:
              start:
                actions:
                  - set:
                      variable: temp
                      value: "data"
                  - clear: temp
                  - log: { message: "Temp: ${temp}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Contains("Temp: null", result.Logs[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CONDITIONAL TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_CondTrue_ExecutesThenBranch()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: cond_true
            flows:
              start:
                actions:
                  - set:
                      variable: x
                      value: "10"
                  - cond:
                      - when: "${x > 5}"
                        then:
                          - log: "High"
                      - else:
                          - log: "Low"
            """;

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
        var yaml = """
            version: "2.0"
            metadata:
              id: cond_false
            flows:
              start:
                actions:
                  - set:
                      variable: x
                      value: "2"
                  - cond:
                      - when: "${x > 5}"
                        then:
                          - log: "High"
                      - else:
                          - log: "Low"
            """;

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
        var yaml = """
            version: "2.0"
            metadata:
              id: cond_multi
            flows:
              start:
                actions:
                  - set:
                      variable: x
                      value: "7"
                  - cond:
                      - when: "${x > 10}"
                        then:
                          - log: "Very High"
                      - when: "${x > 5}"
                        then:
                          - log: "High"
                      - when: "${x > 0}"
                        then:
                          - log: "Low"
                      - else:
                          - log: "Zero or Negative"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("High", result.Logs[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // LOOP TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_Repeat_ExecutesCorrectTimes()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: repeat_test
            flows:
              start:
                actions:
                  - set:
                      variable: count
                      value: "0"
                  - repeat:
                      times: 3
                      do:
                        - increment:
                            variable: count
                  - log: { message: "Count: ${count}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Contains("Count: 3", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_ForEach_IteratesCollection()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: foreach_test
            flows:
              start:
                actions:
                  - for_each:
                      variable: item
                      collection: "${items}"
                      do:
                        - log: { message: "Item: ${item}" }
            """;

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
        var yaml = """
            version: "2.0"
            metadata:
              id: foreach_null
            flows:
              start:
                actions:
                  - for_each:
                      variable: item
                      collection: "${items}"
                      do:
                        - log: { message: "Item: ${item}" }
                  - log: "Done"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Done", result.Logs[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FLOW CONTROL TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_Goto_TransfersControl()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: goto_test
            flows:
              start:
                actions:
                  - log: "Start"
                  - goto: other
              other:
                actions:
                  - log: "Other"
            """;

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
        var yaml = """
            version: "2.0"
            metadata:
              id: goto_args
            flows:
              start:
                actions:
                  - set:
                      variable: x
                      value: "42"
                  - goto:
                      flow: process
                      args:
                        value: "${x}"
              process:
                actions:
                  - log: { message: "Value: ${value}" }
            """;

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
        var yaml = """
            version: "2.0"
            metadata:
              id: call_test
            flows:
              start:
                actions:
                  - log: "Before call"
                  - call: { flow: subroutine }
                  - log: "After call"
              subroutine:
                actions:
                  - log: "In subroutine"
            """;

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
        var yaml = """
            version: "2.0"
            metadata:
              id: return_test
            flows:
              start:
                actions:
                  - log: "Before return"
                  - return: { value: "done" }
                  - log: "After return"
            """;

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
        var yaml = """
            version: "2.0"
            metadata:
              id: call_return
            flows:
              start:
                actions:
                  - call: { flow: calculate }
                  - log: { message: "Result: ${_result}" }
              calculate:
                actions:
                  - set:
                      variable: sum
                      value: "${2 + 3}"
                  - return: { value: "${sum}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Result: 5", result.Logs[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // INITIAL SCOPE TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WithInitialScope_UsesProvidedVariables()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: initial_scope
            flows:
              start:
                actions:
                  - log: { message: "Name: ${player_name}, Level: ${player_level}" }
            """;

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

    // ═══════════════════════════════════════════════════════════════════════
    // CANCELLATION TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_WithCancellation_Cancels()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: cancel_test
            flows:
              start:
                actions:
                  - repeat:
                      times: 1000
                      do:
                        - log: "Loop"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start", ct: cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("cancelled", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // COMPLEX SCENARIO TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ComplexBehavior_ExecutesCorrectly()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: complex_behavior
            flows:
              start:
                actions:
                  - set:
                      variable: energy
                      value: "100"
                  - call: { flow: check_status }
                  - log: { message: "Final energy: ${energy}" }

              check_status:
                actions:
                  - cond:
                      - when: "${energy > 80}"
                        then:
                          - log: "High energy"
                          - decrement:
                              variable: energy
                              by: 30
                      - when: "${energy > 50}"
                        then:
                          - log: "Medium energy"
                      - else:
                          - log: "Low energy"
            """;

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
        var yaml = """
            version: "2.0"
            metadata:
              id: nested
            flows:
              start:
                actions:
                  - set:
                      variable: total
                      value: "0"
                  - for_each:
                      variable: row
                      collection: "${rows}"
                      do:
                        - for_each:
                            variable: col
                            collection: "${cols}"
                            do:
                              - cond:
                                  - when: "${row == col}"
                                    then:
                                      - increment:
                                          variable: total
                  - log: { message: "Diagonal count: ${total}" }
            """;

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
