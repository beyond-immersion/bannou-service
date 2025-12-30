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

namespace BeyondImmersion.BannouService.UnitTests.Abml;

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
        var yaml = @"
version: ""2.0""
metadata:
  id: simple

flows:
  start:
    actions:
    - log: { message: ""Hello"" }
  helper:
    actions:
    - log: { message: ""Helper"" }
";
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
        var mainYaml = @"
version: ""2.0""
metadata:
  id: main

imports:
  - file: ""lib.yml""
    as: ""lib""

flows:
  start:
    actions:
    - log: { message: ""Main start"" }
    - call: { flow: ""lib.greet"" }
";
        var libYaml = @"
version: ""2.0""
metadata:
  id: lib

flows:
  greet:
    actions:
    - log: { message: ""Hello from lib"" }
  farewell:
    actions:
    - log: { message: ""Goodbye from lib"" }
";
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
        var aYaml = @"
version: ""2.0""
metadata:
  id: a

imports:
  - file: ""b.yml""
    as: ""b""

flows:
  start:
    actions:
    - log: { message: ""In A"" }
";
        var bYaml = @"
version: ""2.0""
metadata:
  id: b

imports:
  - file: ""c.yml""
    as: ""c""

flows:
  helper:
    actions:
    - log: { message: ""In B"" }
";
        var cYaml = @"
version: ""2.0""
metadata:
  id: c

flows:
  deep:
    actions:
    - log: { message: ""In C"" }
";
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
        var mainYaml = @"
version: ""2.0""
metadata:
  id: main

flows:
  start:
    actions:
    - call: { flow: ""helper"" }
  helper:
    actions:
    - log: { message: ""Helper"" }
";
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
        var aYaml = @"
version: ""2.0""
metadata:
  id: a

imports:
  - file: ""b.yml""
    as: ""b""

flows:
  start:
    actions:
    - call: { flow: ""b.entry"" }
";
        var bYaml = @"
version: ""2.0""
metadata:
  id: b

imports:
  - file: ""c.yml""
    as: ""c""

flows:
  entry:
    actions:
    - log: { message: ""In B"" }
    - call: { flow: ""c.work"" }
";
        var cYaml = @"
version: ""2.0""
metadata:
  id: c

flows:
  work:
    actions:
    - log: { message: ""In C"" }
";
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
        var mainYaml = @"
version: ""2.0""
metadata:
  id: main

imports:
  - file: ""lib.yml""
    as: ""lib""

flows:
  start:
    actions:
    - goto: { flow: ""lib.target"" }
";
        var libYaml = @"
version: ""2.0""
metadata:
  id: lib

flows:
  target:
    actions:
    - log: { message: ""Arrived"" }
";
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
        var mainYaml = @"
version: ""2.0""
metadata:
  id: main

imports:
  - file: ""lib.yml""
    as: ""lib""

flows:
  start:
    actions:
    - goto:
        flow: ""lib.target""
        args:
            x: ""${value}""
            y: ""42""
";
        var libYaml = @"
version: ""2.0""
metadata:
  id: lib

flows:
  target:
    actions:
    - log: { message: ""Got x=${x}, y=${y}"" }
";
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
        var mainYaml = @"
version: ""2.0""
metadata:
  id: main

imports:
  - file: ""lib.yml""
    as: ""lib""

flows:
  start:
    actions:
    - cond:
        - when: ""${flag}""
            then:
            - call: { flow: ""lib.yes"" }
        - else:
            - call: { flow: ""lib.no"" }
";
        var libYaml = @"
version: ""2.0""
metadata:
  id: lib

flows:
  yes:
    actions:
    - log: { message: ""Yes"" }
  no:
    actions:
    - log: { message: ""No"" }
";
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
        var mainYaml = @"
version: ""2.0""
metadata:
  id: main

imports:
  - file: ""lib.yml""
    as: ""lib""

flows:
  start:
    actions:
    - for_each:
        variable: item
        collection: ""${items}""
        do:
            - call: { flow: ""lib.process"" }
";
        var libYaml = @"
version: ""2.0""
metadata:
  id: lib

flows:
  process:
    actions:
    - log: { message: ""Processing"" }
";
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
        var mainYaml = @"
version: ""2.0""
metadata:
  id: main

imports:
  - file: ""lib.yml""
    as: ""lib""

flows:
  start:
    actions:
    - repeat:
        times: 3
        do:
            - call: { flow: ""lib.tick"" }
";
        var libYaml = @"
version: ""2.0""
metadata:
  id: lib

flows:
  tick:
    actions:
    - log: { message: ""Tick"" }
";
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
        var mainYaml = @"
version: ""2.0""
metadata:
  id: main

context:
  variables:
    main_var:
    type: string
    default: ""main_value""

imports:
  - file: ""lib.yml""
    as: ""lib""

flows:
  start:
    actions:
    - log: { message: ""Hello"" }
";
        var libYaml = @"
version: ""2.0""
metadata:
  id: lib

context:
  variables:
    lib_var:
    type: int
    default: 42

flows:
  work:
    actions:
    - log: { message: ""Working"" }
";
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
        var mainYaml = @"
version: ""2.0""
metadata:
  id: main

imports:
  - file: ""lib.yml""
    as: ""lib""

flows:
  start:
    actions:
    - log: { message: ""Start"" }
    - call: { flow: ""lib.greet"" }
    - log: { message: ""End"" }
";
        var libYaml = @"
version: ""2.0""
metadata:
  id: lib

flows:
  greet:
    actions:
    - log: { message: ""Hello from lib"" }
";
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
        var aYaml = @"
version: ""2.0""
metadata:
  id: a

imports:
  - file: ""b.yml""
    as: ""b""

flows:
  start:
    actions:
    - log: { message: ""A start"" }
    - call: { flow: ""b.entry"" }
    - log: { message: ""A end"" }
";
        var bYaml = @"
version: ""2.0""
metadata:
  id: b

imports:
  - file: ""c.yml""
    as: ""c""

flows:
  entry:
    actions:
    - log: { message: ""B entry"" }
    - call: { flow: ""c.work"" }
    - log: { message: ""B exit"" }
";
        var cYaml = @"
version: ""2.0""
metadata:
  id: c

flows:
  work:
    actions:
    - log: { message: ""C work"" }
";
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
        var mainYaml = @"
version: ""2.0""
metadata:
  id: main

imports:
  - file: ""lib.yml""
    as: ""lib""

flows:
  start:
    actions:
    - log: { message: ""Main"" }
";
        var libYaml = @"
version: ""2.0""
metadata:
  id: lib

flows:
  standalone:
    actions:
    - log: { message: ""Lib standalone"" }
";
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
        var mainYaml = @"
version: ""2.0""
metadata:
  id: main

imports:
  - file: ""ai.yml""
    as: ""ai""

goals:
  survive:
    priority: 100
    conditions:
    health: ""> 0""

flows:
  start:
    actions:
    - log: { message: ""Hello"" }
";
        var aiYaml = @"
version: ""2.0""
metadata:
  id: ai

goals:
  eat:
    priority: 50
    conditions:
    hunger: ""<= 0.3""
  rest:
    priority: 30
    conditions:
    energy: "">= 0.8""

flows:
  idle:
    actions:
    - log: { message: ""Idling"" }
";
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
        var mainYaml = @"
version: ""2.0""
metadata:
  id: main

imports:
  - file: ""lib.yml""
    as: ""lib""

flows:
  start:
    actions:
    - log: { message: ""Working"" }
    on_error:
    - call: { flow: ""lib.handle_error"" }
";
        var libYaml = @"
version: ""2.0""
metadata:
  id: lib

flows:
  handle_error:
    actions:
    - log: { message: ""Handling error"" }
";
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
        var yaml = @"
version: ""2.0""
metadata:
  id: my-document
  type: behavior
  description: ""A test document""
  tags: [""test"", ""example""]

flows:
  start:
    actions:
    - log: { message: ""Hello"" }
";
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
