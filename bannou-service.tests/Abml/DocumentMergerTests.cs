// =============================================================================
// ABML Document Merger Tests
// Tests for merging LoadedDocument trees into flat AbmlDocuments.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Compiler;
using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Parser;
using Xunit;

namespace BeyondImmersion.BannouService.Tests.Abml;

/// <summary>
/// Tests for DocumentMerger - flattening LoadedDocument trees for bytecode compilation.
/// </summary>
public class DocumentMergerTests
{
    private readonly DocumentParser _parser = new();
    private readonly DocumentMerger _merger = new();
    private readonly DocumentExecutor _executor = new();

    // =========================================================================
    // BASIC MERGING TESTS
    // =========================================================================

    [Fact]
    public void Merge_SimpleDocument_NoImports_ReturnsUnchanged()
    {
        // Arrange
        var yaml = TestFixtures.Load("merger_simple");
        var doc = _parser.Parse(yaml).Value!;
        var loaded = new LoadedDocument(doc);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert
        Assert.Equal(2, merged.Flows.Count);
        Assert.True(merged.Flows.ContainsKey("start"));
        Assert.True(merged.Flows.ContainsKey("helper"));
        Assert.Empty(merged.Imports);
    }

    [Fact]
    public async Task Merge_WithOneImport_FlattenFlowsWithPrefix()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("merger_import_main");
        var libYaml = TestFixtures.Load("merger_import_lib");
        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "main.yml", CancellationToken.None);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert - Should have 3 flows: start, lib.greet, lib.farewell
        Assert.Equal(3, merged.Flows.Count);
        Assert.True(merged.Flows.ContainsKey("start"));
        Assert.True(merged.Flows.ContainsKey("lib.greet"));
        Assert.True(merged.Flows.ContainsKey("lib.farewell"));
        Assert.Empty(merged.Imports);

        // The call in 'start' should now reference 'lib.greet' (already correct)
        var startFlow = merged.Flows["start"];
        var callAction = startFlow.Actions[1] as CallAction;
        Assert.NotNull(callAction);
        Assert.Equal("lib.greet", callAction.Flow);
    }

    [Fact]
    public async Task Merge_NestedImports_FlattensAllLevels()
    {
        // Arrange - A imports B, B imports C
        var aYaml = TestFixtures.Load("merger_nested_a");
        var bYaml = TestFixtures.Load("merger_nested_b");
        var cYaml = TestFixtures.Load("merger_nested_c");
        var aDoc = _parser.Parse(aYaml).Value!;
        var bDoc = _parser.Parse(bYaml).Value!;
        var cDoc = _parser.Parse(cYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("b.yml", bDoc);
        resolver.Register("c.yml", cDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(aDoc, "a.yml", CancellationToken.None);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert - Should have: start, b.helper, b.c.deep
        Assert.Equal(3, merged.Flows.Count);
        Assert.True(merged.Flows.ContainsKey("start"));
        Assert.True(merged.Flows.ContainsKey("b.helper"));
        Assert.True(merged.Flows.ContainsKey("b.c.deep"));
    }

    // =========================================================================
    // FLOW REFERENCE REWRITING TESTS
    // =========================================================================

    [Fact]
    public void Merge_CallToLocalFlow_PreservesReference()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("merger_local_call_main");
        var mainDoc = _parser.Parse(mainYaml).Value!;
        var loaded = new LoadedDocument(mainDoc);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert
        var startFlow = merged.Flows["start"];
        var callAction = startFlow.Actions[0] as CallAction;
        Assert.NotNull(callAction);
        Assert.Equal("helper", callAction.Flow);
    }

    [Fact]
    public async Task Merge_CallFromImportedFlow_RewritesToFullyQualified()
    {
        // Arrange - B has a call to C, both imported by A
        var aYaml = TestFixtures.Load("merger_rewrite_a");
        var bYaml = TestFixtures.Load("merger_rewrite_b");
        var cYaml = TestFixtures.Load("merger_rewrite_c");
        var aDoc = _parser.Parse(aYaml).Value!;
        var bDoc = _parser.Parse(bYaml).Value!;
        var cDoc = _parser.Parse(cYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("b.yml", bDoc);
        resolver.Register("c.yml", cDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(aDoc, "a.yml", CancellationToken.None);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert - B's call to c.work should become b.c.work
        var bEntryFlow = merged.Flows["b.entry"];
        var callAction = bEntryFlow.Actions[1] as CallAction;
        Assert.NotNull(callAction);
        Assert.Equal("b.c.work", callAction.Flow);
    }

    [Fact]
    public async Task Merge_GotoAction_RewritesToFullyQualified()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("merger_goto_main");
        var libYaml = TestFixtures.Load("merger_goto_lib");
        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "main.yml", CancellationToken.None);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert
        var startFlow = merged.Flows["start"];
        var gotoAction = startFlow.Actions[0] as GotoAction;
        Assert.NotNull(gotoAction);
        Assert.Equal("lib.target", gotoAction.Flow);
    }

    [Fact]
    public async Task Merge_GotoWithArgs_PreservesArgs()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("merger_goto_args_main");
        var libYaml = TestFixtures.Load("merger_goto_args_lib");
        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "main.yml", CancellationToken.None);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert
        var startFlow = merged.Flows["start"];
        var gotoAction = startFlow.Actions[0] as GotoAction;
        Assert.NotNull(gotoAction);
        Assert.Equal("lib.target", gotoAction.Flow);
        Assert.NotNull(gotoAction.Args);
        Assert.Equal(2, gotoAction.Args.Count);
        Assert.Equal("${value}", gotoAction.Args["x"]);
        Assert.Equal("42", gotoAction.Args["y"]);
    }

    // =========================================================================
    // NESTED ACTION REWRITING TESTS
    // =========================================================================

    [Fact]
    public async Task Merge_CallInsideCond_RewritesCorrectly()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("merger_cond_call_main");
        var libYaml = TestFixtures.Load("merger_cond_call_lib");
        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "main.yml", CancellationToken.None);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert
        var startFlow = merged.Flows["start"];
        var condAction = startFlow.Actions[0] as CondAction;
        Assert.NotNull(condAction);

        var thenCall = condAction.Branches[0].Then[0] as CallAction;
        Assert.NotNull(thenCall);
        Assert.Equal("lib.yes", thenCall.Flow);

        var elseCall = condAction.ElseBranch![0] as CallAction;
        Assert.NotNull(elseCall);
        Assert.Equal("lib.no", elseCall.Flow);
    }

    [Fact]
    public async Task Merge_CallInsideForEach_RewritesCorrectly()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("merger_foreach_main");
        var libYaml = TestFixtures.Load("merger_foreach_lib");
        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "main.yml", CancellationToken.None);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert
        var startFlow = merged.Flows["start"];
        var forEachAction = startFlow.Actions[0] as ForEachAction;
        Assert.NotNull(forEachAction);

        var innerCall = forEachAction.Do[0] as CallAction;
        Assert.NotNull(innerCall);
        Assert.Equal("lib.process", innerCall.Flow);
    }

    [Fact]
    public async Task Merge_CallInsideRepeat_RewritesCorrectly()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("merger_repeat_main");
        var libYaml = TestFixtures.Load("merger_repeat_lib");
        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "main.yml", CancellationToken.None);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert
        var startFlow = merged.Flows["start"];
        var repeatAction = startFlow.Actions[0] as RepeatAction;
        Assert.NotNull(repeatAction);

        var innerCall = repeatAction.Do[0] as CallAction;
        Assert.NotNull(innerCall);
        Assert.Equal("lib.tick", innerCall.Flow);
    }

    // =========================================================================
    // CONTEXT MERGING TESTS
    // =========================================================================

    [Fact]
    public async Task Merge_ContextVariables_MergesWithPrefix()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("merger_context_main");
        var libYaml = TestFixtures.Load("merger_context_lib");
        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "main.yml", CancellationToken.None);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert
        Assert.NotNull(merged.Context);
        Assert.Equal(2, merged.Context.Variables.Count);
        Assert.True(merged.Context.Variables.ContainsKey("main_var"));
        Assert.True(merged.Context.Variables.ContainsKey("lib.lib_var"));
        Assert.Equal("main_value", merged.Context.Variables["main_var"].Default);
        Assert.Equal(42, Convert.ToInt32(merged.Context.Variables["lib.lib_var"].Default));
    }

    // =========================================================================
    // EXECUTION AFTER MERGE TESTS
    // =========================================================================

    [Fact]
    public async Task Merge_ExecuteMergedDocument_SameResultAsOriginal()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("merger_exec_main");
        var libYaml = TestFixtures.Load("merger_exec_lib");
        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "main.yml", CancellationToken.None);

        // Execute original (with imports)
        var originalResult = await _executor.ExecuteAsync(loaded, "start");

        // Merge and execute
        var merged = _merger.Merge(loaded);
        var mergedResult = await _executor.ExecuteAsync(merged, "start");

        // Assert - Both should produce the same logs
        Assert.True(originalResult.IsSuccess);
        Assert.True(mergedResult.IsSuccess);
        Assert.Equal(originalResult.Logs.Count, mergedResult.Logs.Count);
        for (int i = 0; i < originalResult.Logs.Count; i++)
        {
            Assert.Equal(originalResult.Logs[i].Message, mergedResult.Logs[i].Message);
        }
    }

    [Fact]
    public async Task Merge_ExecuteNestedImports_SameResultAsOriginal()
    {
        // Arrange
        var aYaml = TestFixtures.Load("merger_exec_nested_a");
        var bYaml = TestFixtures.Load("merger_exec_nested_b");
        var cYaml = TestFixtures.Load("merger_exec_nested_c");
        var aDoc = _parser.Parse(aYaml).Value!;
        var bDoc = _parser.Parse(bYaml).Value!;
        var cDoc = _parser.Parse(cYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("b.yml", bDoc);
        resolver.Register("c.yml", cDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(aDoc, "a.yml", CancellationToken.None);

        // Execute original
        var originalResult = await _executor.ExecuteAsync(loaded, "start");

        // Merge and execute
        var merged = _merger.Merge(loaded);
        var mergedResult = await _executor.ExecuteAsync(merged, "start");

        // Assert
        Assert.True(originalResult.IsSuccess, $"Original failed: {originalResult.Error}");
        Assert.True(mergedResult.IsSuccess, $"Merged failed: {mergedResult.Error}");
        Assert.Equal(5, originalResult.Logs.Count);
        Assert.Equal(5, mergedResult.Logs.Count);

        var expectedLogs = new[] { "A start", "B entry", "C work", "B exit", "A end" };
        for (int i = 0; i < expectedLogs.Length; i++)
        {
            Assert.Equal(expectedLogs[i], originalResult.Logs[i].Message);
            Assert.Equal(expectedLogs[i], mergedResult.Logs[i].Message);
        }
    }

    [Fact]
    public async Task Merge_StartFromMergedImportedFlow_Succeeds()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("merger_standalone_main");
        var libYaml = TestFixtures.Load("merger_standalone_lib");
        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "main.yml", CancellationToken.None);

        // Merge
        var merged = _merger.Merge(loaded);

        // Act - Start from the merged imported flow
        var result = await _executor.ExecuteAsync(merged, "lib.standalone");

        // Assert
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Single(result.Logs);
        Assert.Equal("Lib standalone", result.Logs[0].Message);
    }

    // =========================================================================
    // GOAP GOALS MERGING TESTS
    // =========================================================================

    [Fact]
    public async Task Merge_GoapGoals_MergesWithPrefix()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("merger_goals_main");
        var aiYaml = TestFixtures.Load("merger_goals_ai");
        var mainDoc = _parser.Parse(mainYaml).Value!;
        var aiDoc = _parser.Parse(aiYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("ai.yml", aiDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "main.yml", CancellationToken.None);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert - Should have 3 goals: survive, ai.eat, ai.rest
        Assert.Equal(3, merged.Goals.Count);
        Assert.True(merged.Goals.ContainsKey("survive"));
        Assert.True(merged.Goals.ContainsKey("ai.eat"));
        Assert.True(merged.Goals.ContainsKey("ai.rest"));

        Assert.Equal(100, merged.Goals["survive"].Priority);
        Assert.Equal(50, merged.Goals["ai.eat"].Priority);
        Assert.Equal(30, merged.Goals["ai.rest"].Priority);
    }

    // =========================================================================
    // FLOW ON_ERROR REWRITING TESTS
    // =========================================================================

    [Fact]
    public async Task Merge_FlowOnError_RewritesCallsCorrectly()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("merger_onerror_main");
        var libYaml = TestFixtures.Load("merger_onerror_lib");
        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "main.yml", CancellationToken.None);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert
        var startFlow = merged.Flows["start"];
        Assert.Single(startFlow.OnError);
        var errorCall = startFlow.OnError[0] as CallAction;
        Assert.NotNull(errorCall);
        Assert.Equal("lib.handle_error", errorCall.Flow);
    }

    // =========================================================================
    // METADATA PRESERVATION TESTS
    // =========================================================================

    [Fact]
    public void Merge_PreservesRootMetadata()
    {
        // Arrange
        var yaml = TestFixtures.Load("merger_metadata");
        var doc = _parser.Parse(yaml).Value!;
        var loaded = new LoadedDocument(doc);

        // Act
        var merged = _merger.Merge(loaded);

        // Assert
        Assert.Equal("2.0", merged.Version);
        Assert.Equal("my-document", merged.Metadata.Id);
        Assert.Equal("behavior", merged.Metadata.Type);
        Assert.Equal("A test document", merged.Metadata.Description);
        Assert.Equal(2, merged.Metadata.Tags.Count);
        Assert.Contains("test", merged.Metadata.Tags);
        Assert.Contains("example", merged.Metadata.Tags);
    }
}
