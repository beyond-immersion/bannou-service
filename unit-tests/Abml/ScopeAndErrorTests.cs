// ═══════════════════════════════════════════════════════════════════════════
// ABML Scope and Error Handling Tests
// Tests for variable scope semantics, call isolation, and error handling.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Parser;
using Xunit;

namespace BeyondImmersion.BannouService.UnitTests.Abml;

/// <summary>
/// Tests for variable scope semantics, call isolation, and error handling.
/// </summary>
public class ScopeAndErrorTests
{
    private readonly DocumentParser _parser = new();
    private readonly DocumentExecutor _executor = new();

    // ═══════════════════════════════════════════════════════════════════════
    // LOCAL ACTION TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_LocalAction_ParsesCorrectly()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: local_parse
            flows:
              start:
                actions:
                  - local:
                      variable: temp
                      value: "42"
            """;

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as LocalAction;
        Assert.NotNull(action);
        Assert.Equal("temp", action.Variable);
        Assert.Equal("42", action.Value);
    }

    [Fact]
    public async Task Execute_LocalAction_ShadowsParentVariable()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: local_shadow
            flows:
              start:
                actions:
                  - set:
                      variable: x
                      value: "outer"
                  - call: { flow: inner }
                  - log: { message: "After call: ${x}" }
              inner:
                actions:
                  - local:
                      variable: x
                      value: "inner"
                  - log: { message: "In inner: ${x}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Logs.Count);
        Assert.Equal("In inner: inner", result.Logs[0].Message);
        Assert.Equal("After call: outer", result.Logs[1].Message);  // x unchanged
    }

    [Fact]
    public async Task Execute_ForEach_LoopVariableShadowsOuter()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: foreach_shadow
            flows:
              start:
                actions:
                  - set:
                      variable: item
                      value: "original"
                  - for_each:
                      variable: item
                      collection: "${items}"
                      do:
                        - log: { message: "Loop: ${item}" }
                  - log: { message: "After loop: ${item}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var scope = new VariableScope();
        scope.SetValue("items", new List<string> { "a", "b" });

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start", scope);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Logs.Count);
        Assert.Equal("Loop: a", result.Logs[0].Message);
        Assert.Equal("Loop: b", result.Logs[1].Message);
        Assert.Equal("After loop: original", result.Logs[2].Message);  // Restored
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GLOBAL ACTION TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_GlobalAction_ParsesCorrectly()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: global_parse
            flows:
              start:
                actions:
                  - global:
                      variable: shared
                      value: "value"
            """;

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as GlobalAction;
        Assert.NotNull(action);
        Assert.Equal("shared", action.Variable);
        Assert.Equal("value", action.Value);
    }

    [Fact]
    public async Task Execute_GlobalAction_SetsInRootScope()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: global_set
            flows:
              start:
                actions:
                  - call: { flow: nested }
                  - log: { message: "Root: ${result}" }
              nested:
                actions:
                  - call: { flow: deep }
              deep:
                actions:
                  - global:
                      variable: result
                      value: "from_deep"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Root: from_deep", result.Logs[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SET VS LOCAL BEHAVIOR TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_SetAction_ModifiesExistingInParent()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: set_parent
            flows:
              start:
                actions:
                  - set:
                      variable: counter
                      value: "0"
                  - call: { flow: increment }
                  - log: { message: "Final: ${counter}" }
              increment:
                actions:
                  - set:
                      variable: counter
                      value: "${counter + 1}"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Final: 1", result.Logs[0].Message);  // Counter was modified
    }

    [Fact]
    public async Task Execute_SetAction_CreatesLocalIfNotExists()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: set_new
            flows:
              start:
                actions:
                  - call: { flow: creator }
                  - log: { message: "Outer: ${new_var ?? 'not found'}" }
              creator:
                actions:
                  - set:
                      variable: new_var
                      value: "created"
                  - log: { message: "Inner: ${new_var}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Logs.Count);
        Assert.Equal("Inner: created", result.Logs[0].Message);
        Assert.Equal("Outer: not found", result.Logs[1].Message);  // Not visible in parent
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CALL HANDLER ISOLATION TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_Call_CalledFlowHasOwnScope()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: call_isolation
            flows:
              start:
                actions:
                  - set:
                      variable: x
                      value: "10"
                  - call: { flow: modify }
                  - log: { message: "x = ${x}" }
              modify:
                actions:
                  - local:
                      variable: x
                      value: "99"
                  - local:
                      variable: y
                      value: "new"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("x = 10", result.Logs[0].Message);  // x unchanged, y not visible
    }

    [Fact]
    public async Task Execute_Call_CanReadParentVariables()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: call_read
            flows:
              start:
                actions:
                  - set:
                      variable: message
                      value: "hello"
                  - call: { flow: reader }
              reader:
                actions:
                  - log: { message: "Read: ${message}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Read: hello", result.Logs[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ERROR HANDLING TESTS (on_error)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_OnError_ParsesCorrectly()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: on_error_parse
            flows:
              start:
                actions:
                  - log: "Try"
                on_error:
                  - log: "Error handled"
            """;

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value!.Flows["start"].OnError);
        Assert.Single(result.Value.Flows["start"].OnError);
    }

    [Fact]
    public async Task Execute_OnError_HandlesActionError()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: on_error_exec
            flows:
              start:
                actions:
                  - goto: nonexistent_flow
                on_error:
                  - log: { message: "Caught: ${_error.message}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Contains("Caught:", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_OnError_SetsErrorVariable()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: error_var
            flows:
              start:
                actions:
                  - call: { flow: failing }
                on_error:
                  - log: { message: "Flow: ${_error.flow}" }
                  - log: { message: "Action: ${_error.action}" }
              failing:
                actions:
                  - goto: nowhere
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        // The error from failing flow propagates - but we can check the flow name
        // Since calling flow's on_error should trigger
        Assert.Equal(2, result.Logs.Count);
        Assert.Contains("Flow:", result.Logs[0].Message);
        Assert.Contains("Action:", result.Logs[1].Message);
    }

    [Fact]
    public async Task Execute_NoOnError_PropagatesError()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: no_handler
            flows:
              start:
                actions:
                  - goto: nonexistent
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task Execute_OnError_ContinuesAfterHandling()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: continue_after
            flows:
              start:
                actions:
                  - goto: nowhere
                  - log: "This should not run"
                on_error:
                  - log: "Error handled"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        // After handling error, execution continues with next action
        // But since the error was in the first action and we handled it,
        // we should see the "Error handled" log and then "This should not run" should NOT appear
        // because goto would have exited the flow if it succeeded
        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Error handled", result.Logs[0].Message);
    }
}
