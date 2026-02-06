// =============================================================================
// ABML Scope and Error Handling Tests
// Tests for variable scope semantics, call isolation, and error handling.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.Bannou.BehaviorCompiler.Parser;
using BeyondImmersion.BannouService.Abml.Runtime;
using Xunit;

namespace BeyondImmersion.BannouService.Tests.Abml;

/// <summary>
/// Tests for variable scope semantics, call isolation, and error handling.
/// </summary>
public class ScopeAndErrorTests
{
    private readonly DocumentParser _parser = new();
    private readonly DocumentExecutor _executor = new();

    // =========================================================================
    // LOCAL ACTION TESTS
    // =========================================================================

    [Fact]
    public void Parse_LocalAction_ParsesCorrectly()
    {
        var yaml = TestFixtures.Load("scope_local_parse");

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
        var yaml = TestFixtures.Load("scope_local_shadow");

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
        var yaml = TestFixtures.Load("scope_foreach_shadow");

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

    // =========================================================================
    // GLOBAL ACTION TESTS
    // =========================================================================

    [Fact]
    public void Parse_GlobalAction_ParsesCorrectly()
    {
        var yaml = TestFixtures.Load("scope_global_parse");

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
        var yaml = TestFixtures.Load("scope_global_set");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Root: from_deep", result.Logs[0].Message);
    }

    // =========================================================================
    // SET VS LOCAL BEHAVIOR TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_SetAction_ModifiesExistingInParent()
    {
        var yaml = TestFixtures.Load("scope_set_parent");

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
        var yaml = TestFixtures.Load("scope_set_new");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Logs.Count);
        Assert.Equal("Inner: created", result.Logs[0].Message);
        Assert.Equal("Outer: not found", result.Logs[1].Message);  // Not visible in parent
    }

    // =========================================================================
    // CALL HANDLER ISOLATION TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_Call_CalledFlowHasOwnScope()
    {
        var yaml = TestFixtures.Load("scope_call_isolation");

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
        var yaml = TestFixtures.Load("scope_call_read");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Read: hello", result.Logs[0].Message);
    }

    // =========================================================================
    // ERROR HANDLING TESTS (on_error)
    // =========================================================================

    [Fact]
    public void Parse_OnError_ParsesCorrectly()
    {
        var yaml = TestFixtures.Load("scope_on_error_parse");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value!.Flows["start"].OnError);
        Assert.Single(result.Value.Flows["start"].OnError);
    }

    [Fact]
    public async Task Execute_OnError_HandlesActionError()
    {
        var yaml = TestFixtures.Load("scope_on_error_exec");

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
        var yaml = TestFixtures.Load("scope_error_var");

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
        var yaml = TestFixtures.Load("scope_no_handler");

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
        var yaml = TestFixtures.Load("scope_stop_after");

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
        var yaml = TestFixtures.Load("scope_continue_after");

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
        var yaml = TestFixtures.Load("scope_explicit_false");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        // _error_handled=false means stop (same as default)
        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Error handled", result.Logs[0].Message);
    }

    // =========================================================================
    // DOCUMENT-LEVEL ON_ERROR TESTS
    // =========================================================================

    [Fact]
    public void Parse_DocumentOnError_ParsesCorrectly()
    {
        var yaml = TestFixtures.Load("scope_doc_error_parse");

        var result = _parser.Parse(yaml);

        Assert.True(result.IsSuccess);
        Assert.Equal("error_handler", result.Value!.OnError);
    }

    [Fact]
    public async Task Execute_DocumentOnError_HandlesUnhandledFlowError()
    {
        var yaml = TestFixtures.Load("scope_doc_error_exec");

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
        var yaml = TestFixtures.Load("scope_doc_precedence");

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
        var yaml = TestFixtures.Load("scope_doc_invalid");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task Execute_DocumentOnError_SetsErrorVariable()
    {
        var yaml = TestFixtures.Load("scope_doc_error_var");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await _executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Contains("Flow: start", result.Logs[0].Message);
        Assert.Contains("Action:", result.Logs[0].Message);
    }

    // =========================================================================
    // ACTION-LEVEL ON_ERROR TESTS
    // =========================================================================

    [Fact]
    public void Parse_ActionOnError_ParsesCorrectly()
    {
        var yaml = TestFixtures.Load("scope_action_error_parse");

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

        var yaml = TestFixtures.Load("scope_action_error_exec");

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

        var yaml = TestFixtures.Load("scope_action_continue");

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

        var yaml = TestFixtures.Load("scope_action_precedence");

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

        var yaml = TestFixtures.Load("scope_action_fallback");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Flow handled", result.Logs[0].Message);  // Falls back to flow
    }

    // =========================================================================
    // 3-LEVEL ERROR CHAIN TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_ErrorChain_ActionToFlowToDocument()
    {
        // Test that errors cascade through all 3 levels
        var handlers = TestHandlerFactory.CreateWithFailingHandler();
        var executor = new DocumentExecutor(new ExpressionEvaluator(), handlers);

        // Action with no on_error -> Flow with no on_error -> Document on_error
        var yaml = TestFixtures.Load("scope_chain_all");

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

        var yaml = TestFixtures.Load("scope_chain_none");

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
        var yaml = TestFixtures.Load("scope_chain_nested");

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
        var yaml = TestFixtures.Load("scope_chain_stop");

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

        var yaml = TestFixtures.Load("scope_chain_handler_error");

        var parseResult = _parser.Parse(yaml);
        Assert.True(parseResult.IsSuccess);

        var result = await executor.ExecuteAsync(parseResult.Value!, "start");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Logs);
        Assert.Equal("Flow caught handler error", result.Logs[0].Message);
    }

    // =========================================================================
    // _ERROR_HANDLED CONTINUATION TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_ErrorHandled_DocumentLevel_ContinuesToNextAction()
    {
        // Document-level on_error with _error_handled=true allows continuation
        var yaml = TestFixtures.Load("scope_doc_continue");

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

        var yaml = TestFixtures.Load("scope_multi_error");

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
        var yaml = TestFixtures.Load("scope_graceful_degradation");

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
        var yaml = TestFixtures.Load("scope_flag_scope");

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
        var yaml = TestFixtures.Load("scope_use_error_var");

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
        var yaml = TestFixtures.Load("scope_flag_must_be_true");

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
