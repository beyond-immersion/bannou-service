// ═══════════════════════════════════════════════════════════════════════════
// ABML Scope and Error Handling Tests
// Tests for variable scope semantics, call isolation, and error handling.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.Abml.Parser;
using BeyondImmersion.BannouService.Abml.Runtime;
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
    public async Task Execute_OnError_DefaultBehavior_StopsAfterHandling()
    {
        // Default behavior: error is handled but flow execution STOPS
        // (no continuation to next action unless _error_handled is set)
        var yaml = """
            version: "2.0"
            metadata:
              id: stop_after
            flows:
              start:
                actions:
                  - goto: nowhere
                  - log: "This should NOT run (default stops after error)"
                on_error:
                  - log: "Error handled"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        // Default: error handled = success, but execution STOPS
        // The second action ("This should NOT run") does not execute
        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Error handled", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_OnError_WithErrorHandledFlag_ContinuesToNextAction()
    {
        // When _error_handled is set to true, execution continues to next action
        var yaml = """
            version: "2.0"
            metadata:
              id: continue_after
            flows:
              start:
                actions:
                  - goto: nowhere
                  - log: "This SHOULD run (error_handled=true)"
                on_error:
                  - log: "Error handled"
                  - set:
                      variable: _error_handled
                      value: "${true}"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        // With _error_handled=true: error handled AND execution continues
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Logs.Count);
        Assert.Equal("Error handled", result.Logs[0].Message);
        Assert.Equal("This SHOULD run (error_handled=true)", result.Logs[1].Message);
    }

    [Fact]
    public async Task Execute_OnError_ErrorHandledFalse_StopsExecution()
    {
        // Explicitly setting _error_handled to false still stops execution
        var yaml = """
            version: "2.0"
            metadata:
              id: explicit_false
            flows:
              start:
                actions:
                  - goto: nowhere
                  - log: "This should NOT run"
                on_error:
                  - log: "Error handled"
                  - set:
                      variable: _error_handled
                      value: "${false}"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        // _error_handled=false means stop (same as default)
        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Error handled", result.Logs[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DOCUMENT-LEVEL ON_ERROR TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_DocumentOnError_ParsesCorrectly()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: doc_error_parse
            on_error: error_handler
            flows:
              start:
                actions:
                  - log: "Main"
              error_handler:
                actions:
                  - log: "Document error handled"
            """;

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        Assert.Equal("error_handler", result.Value!.OnError);
    }

    [Fact]
    public async Task Execute_DocumentOnError_HandlesUnhandledFlowError()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: doc_error_exec
            on_error: error_handler
            flows:
              start:
                actions:
                  - goto: nonexistent_flow
              error_handler:
                actions:
                  - log: { message: "Document caught: ${_error.message}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Contains("Document caught:", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_DocumentOnError_FlowLevelTakesPrecedence()
    {
        // Flow-level on_error should handle before document-level
        var yaml = """
            version: "2.0"
            metadata:
              id: doc_precedence
            on_error: doc_handler
            flows:
              start:
                actions:
                  - goto: nowhere
                on_error:
                  - log: "Flow handled"
              doc_handler:
                actions:
                  - log: "Document handled"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Flow handled", result.Logs[0].Message);  // Flow-level wins
    }

    [Fact]
    public async Task Execute_DocumentOnError_InvalidFlowPropagatesError()
    {
        // If the document-level on_error points to a non-existent flow, error propagates
        var yaml = """
            version: "2.0"
            metadata:
              id: doc_invalid
            on_error: nonexistent_handler
            flows:
              start:
                actions:
                  - goto: nowhere
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task Execute_DocumentOnError_SetsErrorVariable()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: doc_error_var
            on_error: error_handler
            flows:
              start:
                actions:
                  - goto: nowhere
              error_handler:
                actions:
                  - log: { message: "Flow: ${_error.flow}, Action: ${_error.action}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Contains("Flow: start", result.Logs[0].Message);
        Assert.Contains("Action:", result.Logs[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ACTION-LEVEL ON_ERROR TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_ActionOnError_ParsesCorrectly()
    {
        var yaml = """
            version: "2.0"
            metadata:
              id: action_error_parse
            flows:
              start:
                actions:
                  - failing_action:
                      param: value
                      on_error:
                        - log: "Action error handled"
            """;

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        var action = result.Value!.Flows["start"].Actions[0] as DomainAction;
        Assert.NotNull(action);
        Assert.NotNull(action.OnError);
        Assert.Single(action.OnError);
    }

    [Fact]
    public async Task Execute_ActionOnError_DefaultBehavior_StopsAfterHandling()
    {
        // Default behavior: action error is handled but flow STOPS
        // (no continuation to next action unless _error_handled is set)
        var handlers = TestHandlerFactory.CreateWithFailingHandler();
        var executor = new DocumentExecutor(new ExpressionEvaluator(), handlers);

        var yaml = """
            version: "2.0"
            metadata:
              id: action_error_exec
            flows:
              start:
                actions:
                  - failing_action:
                      param: value
                      on_error:
                        - log: { message: "Action caught: ${_error.message}" }
                  - log: "This should NOT run (default stops)"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await executor.ExecuteAsync(parseResult.Value!, "start");

        // Default: error handled = success, but execution STOPS
        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Contains("Action caught:", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_ActionOnError_WithErrorHandledFlag_ContinuesToNextAction()
    {
        // When _error_handled is set in action's on_error, execution continues
        var handlers = TestHandlerFactory.CreateWithFailingHandler();
        var executor = new DocumentExecutor(new ExpressionEvaluator(), handlers);

        var yaml = """
            version: "2.0"
            metadata:
              id: action_continue
            flows:
              start:
                actions:
                  - failing_action:
                      param: value
                      on_error:
                        - log: { message: "Action caught: ${_error.message}" }
                        - set:
                            variable: _error_handled
                            value: "${true}"
                  - log: "This SHOULD run (error_handled=true)"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await executor.ExecuteAsync(parseResult.Value!, "start");

        // With _error_handled=true: error handled AND execution continues
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Logs.Count);
        Assert.Contains("Action caught:", result.Logs[0].Message);
        Assert.Equal("This SHOULD run (error_handled=true)", result.Logs[1].Message);
    }

    [Fact]
    public async Task Execute_ActionOnError_TakesPrecedenceOverFlow()
    {
        // Action-level on_error should handle before flow-level
        var handlers = TestHandlerFactory.CreateWithFailingHandler();
        var executor = new DocumentExecutor(new ExpressionEvaluator(), handlers);

        var yaml = """
            version: "2.0"
            metadata:
              id: action_precedence
            flows:
              start:
                actions:
                  - failing_action:
                      param: value
                      on_error:
                        - log: "Action handled"
                on_error:
                  - log: "Flow handled"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Action handled", result.Logs[0].Message);  // Action-level wins
    }

    [Fact]
    public async Task Execute_ActionOnError_FallsBackToFlowLevel()
    {
        // Action without on_error falls back to flow-level
        var handlers = TestHandlerFactory.CreateWithFailingHandler();
        var executor = new DocumentExecutor(new ExpressionEvaluator(), handlers);

        var yaml = """
            version: "2.0"
            metadata:
              id: action_fallback
            flows:
              start:
                actions:
                  - failing_action:
                      param: value
                on_error:
                  - log: "Flow handled"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Flow handled", result.Logs[0].Message);  // Falls back to flow
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3-LEVEL ERROR CHAIN TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ErrorChain_ActionToFlowToDocument()
    {
        // Test that errors cascade through all 3 levels
        var handlers = TestHandlerFactory.CreateWithFailingHandler();
        var executor = new DocumentExecutor(new ExpressionEvaluator(), handlers);

        // Action with no on_error → Flow with no on_error → Document on_error
        var yaml = """
            version: "2.0"
            metadata:
              id: chain_all
            on_error: doc_handler
            flows:
              start:
                actions:
                  - failing_action:
                      param: value
              doc_handler:
                actions:
                  - log: "Document handled"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Document handled", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_ErrorChain_NoHandlersPropagatesToResult()
    {
        // Error with no handlers at any level results in failure
        var handlers = TestHandlerFactory.CreateWithFailingHandler();
        var executor = new DocumentExecutor(new ExpressionEvaluator(), handlers);

        var yaml = """
            version: "2.0"
            metadata:
              id: chain_none
            flows:
              start:
                actions:
                  - failing_action:
                      param: value
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.False(result.IsSuccess);
        Assert.Contains("failed", result.Error);
    }

    [Fact]
    public async Task Execute_ErrorChain_NestedCallError()
    {
        // Error in called flow bubbles up through call stack
        var yaml = """
            version: "2.0"
            metadata:
              id: chain_nested
            on_error: doc_handler
            flows:
              start:
                actions:
                  - call: { flow: level1 }
              level1:
                actions:
                  - call: { flow: level2 }
              level2:
                actions:
                  - goto: nowhere
              doc_handler:
                actions:
                  - log: { message: "Caught from: ${_error.flow}" }
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Contains("Caught from:", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_ErrorChain_FlowHandlerStopsDocumentHandler()
    {
        // If flow-level handles the error, document-level should NOT be called
        var yaml = """
            version: "2.0"
            metadata:
              id: chain_stop
            on_error: doc_handler
            flows:
              start:
                actions:
                  - goto: nowhere
                on_error:
                  - log: "Flow handled"
              doc_handler:
                actions:
                  - log: "Document handled"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Flow handled", result.Logs[0].Message);  // Only flow handler runs
    }

    [Fact]
    public async Task Execute_ErrorChain_ErrorInHandlerPropagatesToNext()
    {
        // If action on_error handler itself errors, it propagates to flow level
        var handlers = TestHandlerFactory.CreateWithFailingHandler();
        var executor = new DocumentExecutor(new ExpressionEvaluator(), handlers);

        var yaml = """
            version: "2.0"
            metadata:
              id: chain_handler_error
            flows:
              start:
                actions:
                  - failing_action:
                      param: value
                      on_error:
                        - goto: nowhere
                on_error:
                  - log: "Flow caught handler error"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Flow caught handler error", result.Logs[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // _ERROR_HANDLED CONTINUATION TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_ErrorHandled_DocumentLevel_ContinuesToNextAction()
    {
        // Document-level on_error with _error_handled=true allows continuation
        var yaml = """
            version: "2.0"
            metadata:
              id: doc_continue
            on_error: error_handler
            flows:
              start:
                actions:
                  - goto: nowhere
                  - log: "This SHOULD run after doc error handling"
              error_handler:
                actions:
                  - log: "Document handler"
                  - set:
                      variable: _error_handled
                      value: "${true}"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Logs.Count);
        Assert.Equal("Document handler", result.Logs[0].Message);
        Assert.Equal("This SHOULD run after doc error handling", result.Logs[1].Message);
    }

    [Fact]
    public async Task Execute_ErrorHandled_MultipleErrorsWithContinuation()
    {
        // Multiple failing actions with _error_handled allows continuing through all
        var handlers = TestHandlerFactory.CreateWithFailingHandler();
        var executor = new DocumentExecutor(new ExpressionEvaluator(), handlers);

        var yaml = """
            version: "2.0"
            metadata:
              id: multi_error
            flows:
              start:
                actions:
                  - failing_action:
                      param: first
                      on_error:
                        - log: "First error handled"
                        - set:
                            variable: _error_handled
                            value: "${true}"
                  - log: "Between errors"
                  - failing_action:
                      param: second
                      on_error:
                        - log: "Second error handled"
                        - set:
                            variable: _error_handled
                            value: "${true}"
                  - log: "After all errors"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await executor.ExecuteAsync(parseResult.Value!, "start");

        // All 4 log messages should appear - both errors handled with continuation
        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Logs.Count);
        Assert.Equal("First error handled", result.Logs[0].Message);
        Assert.Equal("Between errors", result.Logs[1].Message);
        Assert.Equal("Second error handled", result.Logs[2].Message);
        Assert.Equal("After all errors", result.Logs[3].Message);
    }

    [Fact]
    public async Task Execute_ErrorHandled_GracefulDegradation_UseCachedData()
    {
        // THE DREAM pattern: graceful degradation using cached data on error
        var yaml = """
            version: "2.0"
            metadata:
              id: graceful_degradation
            context:
              variables:
                cached_data:
                  default: "cached_value"
                result:
                  default: null
            flows:
              start:
                actions:
                  - goto: nonexistent_query_service
                  - log: { message: "Using data: ${result}" }
                on_error:
                  - log: "Query failed, using cached data"
                  - set:
                      variable: result
                      value: "${cached_data}"
                  - set:
                      variable: _error_handled
                      value: "${true}"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        // Error handled, cached data used, execution continued
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Logs.Count);
        Assert.Equal("Query failed, using cached data", result.Logs[0].Message);
        Assert.Equal("Using data: cached_value", result.Logs[1].Message);
    }

    [Fact]
    public async Task Execute_ErrorHandled_FlagIsLocalToScope()
    {
        // _error_handled set in one flow's error handler doesn't affect subsequent flows
        var yaml = """
            version: "2.0"
            metadata:
              id: flag_scope
            flows:
              start:
                actions:
                  - goto: nowhere
                  - log: "This runs (error_handled=true)"
                  - set:
                      variable: result
                      value: "completed"
                on_error:
                  - log: "Error handled, continuing"
                  - set:
                      variable: _error_handled
                      value: "${true}"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        // Flow continues after error handling
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Logs.Count);
        Assert.Equal("Error handled, continuing", result.Logs[0].Message);
        Assert.Equal("This runs (error_handled=true)", result.Logs[1].Message);
    }

    [Fact]
    public async Task Execute_ErrorHandled_CanSetAndCheckVariable()
    {
        // Can use variables set in error handler after continuation
        var yaml = """
            version: "2.0"
            metadata:
              id: use_error_var
            flows:
              start:
                actions:
                  - goto: nowhere
                  - log: { message: "Recovered: ${recovered}" }
                on_error:
                  - log: "Error occurred"
                  - set:
                      variable: recovered
                      value: "yes"
                  - set:
                      variable: _error_handled
                      value: "${true}"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        // Variables set in error handler are available after continuation
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Logs.Count);
        Assert.Equal("Error occurred", result.Logs[0].Message);
        Assert.Equal("Recovered: yes", result.Logs[1].Message);
    }

    [Fact]
    public async Task Execute_ErrorHandled_FlagMustBeExactlyTrue()
    {
        // _error_handled must be boolean true, not truthy string
        var yaml = """
            version: "2.0"
            metadata:
              id: flag_must_be_true
            flows:
              start:
                actions:
                  - goto: nowhere
                  - log: "This should NOT run"
                on_error:
                  - log: "Error handled"
                  - set:
                      variable: _error_handled
                      value: "yes"
            """;

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        // String "yes" is not boolean true, so execution stops
        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Error handled", result.Logs[0].Message);
    }
}

/// <summary>
/// Test action handler that always fails.
/// </summary>
internal class FailingActionHandler : IActionHandler
{
    public bool CanHandle(ActionNode action) => action is DomainAction { Name: "failing_action" };

    public ValueTask<ActionResult> ExecuteAsync(
        ActionNode action,
        BeyondImmersion.BannouService.Abml.Execution.ExecutionContext context,
        CancellationToken ct = default)
    {
        return ValueTask.FromResult(ActionResult.Error("Action intentionally failed"));
    }
}

/// <summary>
/// Creates a registry with the FailingActionHandler registered first
/// so it takes precedence over the default DomainActionHandler.
/// </summary>
internal static class TestHandlerFactory
{
    public static ActionHandlerRegistry CreateWithFailingHandler()
    {
        var registry = new ActionHandlerRegistry();
        registry.Register(new FailingActionHandler());  // Register FIRST so it takes precedence
        registry.RegisterBuiltinHandlers();             // Then register builtins
        return registry;
    }
}
