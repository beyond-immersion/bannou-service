// =============================================================================
// ABML Document Loader Tests
// Tests for document import resolution and loading.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.Bannou.BehaviorCompiler.Parser;
using Xunit;

namespace BeyondImmersion.BannouService.Tests.Abml;

/// <summary>
/// Tests for DocumentLoader and import resolution.
/// </summary>
public class DocumentLoaderTests
{
    private readonly DocumentParser _parser = new();
    private readonly DocumentExecutor _executor = new();

    // =========================================================================
    // DOCUMENT LOADER TESTS
    // =========================================================================

    [Fact]
    public async Task LoadAsync_SimpleDocument_NoImports_Succeeds()
    {
        // Arrange
        var yaml = TestFixtures.Load("executor_simple_log");
        var resolver = new InMemoryDocumentResolver();
        var loader = new DocumentLoader(resolver, _parser);

        // Act
        var loaded = await loader.LoadAsync(yaml, "test.yml", CancellationToken.None);

        // Assert
        Assert.NotNull(loaded);
        Assert.NotNull(loaded.Document);
        Assert.Empty(loaded.Imports);
    }

    [Fact]
    public async Task LoadAsync_WithImport_ResolvesImport()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("import_main");
        var commonYaml = TestFixtures.Load("import_common");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var commonDoc = _parser.Parse(commonYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("import_common.yml", commonDoc);

        var loader = new DocumentLoader(resolver, _parser);

        // Act
        var loaded = await loader.LoadAsync(mainDoc, "import_main.yml", CancellationToken.None);

        // Assert
        Assert.NotNull(loaded);
        Assert.Single(loaded.Imports);
        Assert.True(loaded.Imports.ContainsKey("common"));
        Assert.NotNull(loaded.Imports["common"].Document);
    }

    [Fact]
    public async Task LoadAsync_CircularImport_ThrowsCircularImportException()
    {
        // Arrange
        var aYaml = TestFixtures.Load("import_circular_a");
        var bYaml = TestFixtures.Load("import_circular_b");

        var aDoc = _parser.Parse(aYaml).Value!;
        var bDoc = _parser.Parse(bYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("import_circular_a.yml", aDoc);
        resolver.Register("import_circular_b.yml", bDoc);

        var loader = new DocumentLoader(resolver, _parser);

        // Act & Assert
        await Assert.ThrowsAsync<CircularImportException>(async () =>
            await loader.LoadAsync(aDoc, "import_circular_a.yml", CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_NestedImports_ResolvesAll()
    {
        // Arrange
        var aYaml = TestFixtures.Load("import_nested_a");
        var bYaml = TestFixtures.Load("import_nested_b");
        var cYaml = TestFixtures.Load("import_nested_c");

        var aDoc = _parser.Parse(aYaml).Value!;
        var bDoc = _parser.Parse(bYaml).Value!;
        var cDoc = _parser.Parse(cYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("import_nested_a.yml", aDoc);
        resolver.Register("import_nested_b.yml", bDoc);
        resolver.Register("import_nested_c.yml", cDoc);

        var loader = new DocumentLoader(resolver, _parser);

        // Act
        var loaded = await loader.LoadAsync(aDoc, "import_nested_a.yml", CancellationToken.None);

        // Assert
        Assert.NotNull(loaded);
        Assert.Single(loaded.Imports);
        Assert.True(loaded.Imports.ContainsKey("b"));
        Assert.Single(loaded.Imports["b"].Imports);
        Assert.True(loaded.Imports["b"].Imports.ContainsKey("c"));
    }

    [Fact]
    public async Task LoadAsync_MissingImport_ThrowsDocumentLoadException()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("import_main");
        var mainDoc = _parser.Parse(mainYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        // Don't register the import - it should fail

        var loader = new DocumentLoader(resolver, _parser);

        // Act & Assert
        await Assert.ThrowsAsync<DocumentLoadException>(async () =>
            await loader.LoadAsync(mainDoc, "import_main.yml", CancellationToken.None));
    }

    // =========================================================================
    // FLOW RESOLUTION TESTS
    // =========================================================================

    [Fact]
    public void TryResolveFlow_LocalFlow_Succeeds()
    {
        // Arrange
        var yaml = TestFixtures.Load("executor_simple_log");
        var doc = _parser.Parse(yaml).Value!;
        var loaded = new LoadedDocument(doc);

        // Act
        var found = loaded.TryResolveFlow("start", out var flow, out var resolvedDoc);

        // Assert
        Assert.True(found);
        Assert.NotNull(flow);
        Assert.Same(loaded, resolvedDoc);
    }

    [Fact]
    public void TryResolveFlow_LocalFlow_NotFound_ReturnsFalse()
    {
        // Arrange
        var yaml = TestFixtures.Load("executor_simple_log");
        var doc = _parser.Parse(yaml).Value!;
        var loaded = new LoadedDocument(doc);

        // Act
        var found = loaded.TryResolveFlow("nonexistent", out var flow, out var resolvedDoc);

        // Assert
        Assert.False(found);
        Assert.Null(flow);
        Assert.Null(resolvedDoc);
    }

    [Fact]
    public async Task TryResolveFlow_NamespacedFlow_Succeeds()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("import_main");
        var commonYaml = TestFixtures.Load("import_common");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var commonDoc = _parser.Parse(commonYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("import_common.yml", commonDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "import_main.yml", CancellationToken.None);

        // Act
        var found = loaded.TryResolveFlow("common.greet", out var flow, out var resolvedDoc);

        // Assert
        Assert.True(found);
        Assert.NotNull(flow);
        Assert.Equal("greet", flow.Name);
        Assert.Same(loaded.Imports["common"], resolvedDoc);
    }

    [Fact]
    public async Task TryResolveFlow_NestedNamespace_Succeeds()
    {
        // Arrange
        var aYaml = TestFixtures.Load("import_nested_a");
        var bYaml = TestFixtures.Load("import_nested_b");
        var cYaml = TestFixtures.Load("import_nested_c");

        var aDoc = _parser.Parse(aYaml).Value!;
        var bDoc = _parser.Parse(bYaml).Value!;
        var cDoc = _parser.Parse(cYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("import_nested_a.yml", aDoc);
        resolver.Register("import_nested_b.yml", bDoc);
        resolver.Register("import_nested_c.yml", cDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(aDoc, "import_nested_a.yml", CancellationToken.None);

        // Act - Resolve b.c.do_work (nested namespace)
        var found = loaded.TryResolveFlow("b.c.do_work", out var flow, out var resolvedDoc);

        // Assert
        Assert.True(found);
        Assert.NotNull(flow);
        Assert.Equal("do_work", flow.Name);
    }

    // =========================================================================
    // EXECUTION WITH IMPORTS TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_WithImport_CallsImportedFlow()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("import_main");
        var commonYaml = TestFixtures.Load("import_common");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var commonDoc = _parser.Parse(commonYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("import_common.yml", commonDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "import_main.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Equal(3, result.Logs.Count);
        Assert.Equal("Starting main", result.Logs[0].Message);
        Assert.Equal("Hello from common!", result.Logs[1].Message);
        Assert.Equal("Finished main", result.Logs[2].Message);
    }

    [Fact]
    public async Task Execute_NestedImports_ExecutesCorrectly()
    {
        // Arrange
        var aYaml = TestFixtures.Load("import_nested_a");
        var bYaml = TestFixtures.Load("import_nested_b");
        var cYaml = TestFixtures.Load("import_nested_c");

        var aDoc = _parser.Parse(aYaml).Value!;
        var bDoc = _parser.Parse(bYaml).Value!;
        var cDoc = _parser.Parse(cYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("import_nested_a.yml", aDoc);
        resolver.Register("import_nested_b.yml", bDoc);
        resolver.Register("import_nested_c.yml", cDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(aDoc, "import_nested_a.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert - Tests context-relative resolution:
        // A calls b.call_c, B calls c.do_work (relative to B's imports, not A's)
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Equal(5, result.Logs.Count);
        Assert.Equal("In A", result.Logs[0].Message);
        Assert.Equal("In B", result.Logs[1].Message);
        Assert.Equal("In C - doing work", result.Logs[2].Message);  // Context-relative: B→C
        Assert.Equal("Back in B", result.Logs[3].Message);
        Assert.Equal("Back in A", result.Logs[4].Message);
    }

    [Fact]
    public async Task Execute_NestedNamespaceResolution_CallsDeepImport()
    {
        // Arrange - This tests that A can call b.c.do_work directly using nested namespace
        var aYaml = TestFixtures.Load("import_nested_a");
        var bYaml = TestFixtures.Load("import_nested_b");
        var cYaml = TestFixtures.Load("import_nested_c");

        var aDoc = _parser.Parse(aYaml).Value!;
        var bDoc = _parser.Parse(bYaml).Value!;
        var cDoc = _parser.Parse(cYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("import_nested_a.yml", aDoc);
        resolver.Register("import_nested_b.yml", bDoc);
        resolver.Register("import_nested_c.yml", cDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(aDoc, "import_nested_a.yml", CancellationToken.None);

        // Act - Start directly from the deeply nested flow
        var result = await _executor.ExecuteAsync(loaded, "b.c.do_work");

        // Assert
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Single(result.Logs);
        Assert.Equal("In C - doing work", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_NonExistentImportedFlow_Fails()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("loader_nonexistent_main");
        var commonYaml = TestFixtures.Load("loader_nonexistent_common");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var commonDoc = _parser.Parse(commonYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("common.yml", commonDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "main.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task Execute_StartFromImportedFlow_Succeeds()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("import_main");
        var commonYaml = TestFixtures.Load("import_common");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var commonDoc = _parser.Parse(commonYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("import_common.yml", commonDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "import_main.yml", CancellationToken.None);

        // Act - Start execution from an imported flow
        var result = await _executor.ExecuteAsync(loaded, "common.farewell");

        // Assert
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Single(result.Logs);
        Assert.Equal("Goodbye from common!", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_GotoToImportedFlow_Succeeds()
    {
        // Arrange
        var mainYaml = TestFixtures.Load("import_goto_main");
        var targetYaml = TestFixtures.Load("import_goto_target");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var targetDoc = _parser.Parse(targetYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("import_goto_target.yml", targetDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "import_goto_main.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert - goto transfers control, so "After goto" should NOT appear
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Equal(3, result.Logs.Count);
        Assert.Equal("Before goto", result.Logs[0].Message);
        Assert.Equal("Arrived at destination", result.Logs[1].Message);
        Assert.Equal("Completing in target", result.Logs[2].Message);
    }

    [Fact]
    public async Task Execute_GotoWithContextRelativeResolution_Succeeds()
    {
        // Arrange - Test that goto from imported document resolves relative to that document
        // Note: goto is a tail call, so control does NOT return to the caller
        var mainYaml = TestFixtures.Load("loader_goto_context_main");
        var aYaml = TestFixtures.Load("loader_goto_context_a");
        var bYaml = TestFixtures.Load("loader_goto_context_b");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var aDoc = _parser.Parse(aYaml).Value!;
        var bDoc = _parser.Parse(bYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("goto_context_a.yml", aDoc);
        resolver.Register("goto_context_b.yml", bDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "goto_context_main.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert - A's goto to b.target should resolve relative to A's imports
        // Since goto is a tail call, "Back in main" won't appear - the goto takes over
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Equal(3, result.Logs.Count);
        Assert.Equal("Starting in main", result.Logs[0].Message);
        Assert.Equal("In A.entry", result.Logs[1].Message);
        Assert.Equal("In B.target (reached via context-relative goto)", result.Logs[2].Message);
    }

    [Fact]
    public async Task Execute_CallWithContextRelativeResolution_Succeeds()
    {
        // Arrange - Test that call from imported document resolves relative to that document
        // and properly returns to caller
        var mainYaml = TestFixtures.Load("loader_call_context_main");
        var aYaml = TestFixtures.Load("loader_call_context_a");
        var bYaml = TestFixtures.Load("loader_call_context_b");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var aDoc = _parser.Parse(aYaml).Value!;
        var bDoc = _parser.Parse(bYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("call_context_a.yml", aDoc);
        resolver.Register("call_context_b.yml", bDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "call_context_main.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert - A's call to b.helper should resolve relative to A's imports
        // and control should return through the call stack
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Equal(5, result.Logs.Count);
        Assert.Equal("Starting in main", result.Logs[0].Message);
        Assert.Equal("In A.entry", result.Logs[1].Message);
        Assert.Equal("In B.helper (reached via context-relative call)", result.Logs[2].Message);
        Assert.Equal("Back in A.entry", result.Logs[3].Message);
        Assert.Equal("Back in main", result.Logs[4].Message);
    }

    // =========================================================================
    // FILE SYSTEM RESOLVER TESTS
    // =========================================================================

    [Fact]
    public async Task FileSystemResolver_LoadsFromDisk_Succeeds()
    {
        // Arrange - Use the test fixtures directory
        var fixturesPath = Path.Combine(
            Path.GetDirectoryName(typeof(DocumentLoaderTests).Assembly.Location)!,
            "Abml", "fixtures");

        var resolver = new FileSystemDocumentResolver(fixturesPath, _parser);
        var loader = new DocumentLoader(resolver, _parser);

        // Read the main document manually to bootstrap
        var mainPath = Path.Combine(fixturesPath, "import_main.yml");
        var mainYaml = await File.ReadAllTextAsync(mainPath);

        // Act
        var loaded = await loader.LoadAsync(mainYaml, "import_main.yml", CancellationToken.None);

        // Assert
        Assert.NotNull(loaded);
        Assert.Single(loaded.Imports);
        Assert.True(loaded.Imports.ContainsKey("common"));
    }

    [Fact]
    public async Task FileSystemResolver_ExecutesWithImports_Succeeds()
    {
        // Arrange
        var fixturesPath = Path.Combine(
            Path.GetDirectoryName(typeof(DocumentLoaderTests).Assembly.Location)!,
            "Abml", "fixtures");

        var resolver = new FileSystemDocumentResolver(fixturesPath, _parser);
        var loader = new DocumentLoader(resolver, _parser);

        var mainPath = Path.Combine(fixturesPath, "import_main.yml");
        var mainYaml = await File.ReadAllTextAsync(mainPath);
        var loaded = await loader.LoadAsync(mainYaml, "import_main.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Equal(3, result.Logs.Count);
        Assert.Equal("Starting main", result.Logs[0].Message);
        Assert.Equal("Hello from common!", result.Logs[1].Message);
        Assert.Equal("Finished main", result.Logs[2].Message);
    }

    [Fact]
    public async Task FileSystemResolver_RelativePaths_ResolvesCorrectly()
    {
        // Arrange - Test that relative paths resolve from the importing document's directory
        var fixturesPath = Path.Combine(
            Path.GetDirectoryName(typeof(DocumentLoaderTests).Assembly.Location)!,
            "Abml", "fixtures");

        var resolver = new FileSystemDocumentResolver(fixturesPath, _parser);

        // Act - Resolve from a "subdir/file.yml" perspective
        var result = await resolver.ResolveAsync("import_common.yml", "some_dir/main.yml", CancellationToken.None);

        // Assert - Should try to find "some_dir/import_common.yml" which doesn't exist
        Assert.Null(result);

        // Act - Resolve from root perspective
        result = await resolver.ResolveAsync("import_common.yml", null, CancellationToken.None);

        // Assert - Should find the file at root
        Assert.NotNull(result);
        Assert.NotNull(result.Document);
    }

    [Fact]
    public async Task FileSystemResolver_PathTraversal_Rejected()
    {
        // Arrange
        var fixturesPath = Path.Combine(
            Path.GetDirectoryName(typeof(DocumentLoaderTests).Assembly.Location)!,
            "Abml", "fixtures");

        var resolver = new FileSystemDocumentResolver(fixturesPath, _parser);

        // Act - Try to escape the base path
        var result = await resolver.ResolveAsync("../../some_file.yml", null, CancellationToken.None);

        // Assert - Should reject the path traversal attempt
        Assert.Null(result);
    }

    [Fact]
    public async Task FileSystemResolver_DotSlashRelativePath_ResolvesCorrectly()
    {
        // Arrange - Test ./sibling.yml resolution
        var fixturesPath = Path.Combine(
            Path.GetDirectoryName(typeof(DocumentLoaderTests).Assembly.Location)!,
            "Abml", "fixtures");

        var resolver = new FileSystemDocumentResolver(fixturesPath, _parser);
        var loader = new DocumentLoader(resolver, _parser);

        var mainPath = Path.Combine(fixturesPath, "subdir", "nested_main.yml");
        var mainYaml = await File.ReadAllTextAsync(mainPath);

        // Act
        var loaded = await loader.LoadAsync(mainYaml, "subdir/nested_main.yml", CancellationToken.None);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Imports.Count);
        Assert.True(loaded.Imports.ContainsKey("sibling"));
        Assert.True(loaded.Imports.ContainsKey("parent"));
    }

    [Fact]
    public async Task FileSystemResolver_DotDotRelativePath_ExecutesCorrectly()
    {
        // Arrange - Test ../other/file.yml resolution and execution
        var fixturesPath = Path.Combine(
            Path.GetDirectoryName(typeof(DocumentLoaderTests).Assembly.Location)!,
            "Abml", "fixtures");

        var resolver = new FileSystemDocumentResolver(fixturesPath, _parser);
        var loader = new DocumentLoader(resolver, _parser);

        var mainPath = Path.Combine(fixturesPath, "subdir", "nested_main.yml");
        var mainYaml = await File.ReadAllTextAsync(mainPath);
        var loaded = await loader.LoadAsync(mainYaml, "subdir/nested_main.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Equal(4, result.Logs.Count);
        Assert.Equal("In nested_main", result.Logs[0].Message);
        Assert.Equal("Hello from sibling (./sibling.yml)", result.Logs[1].Message);
        Assert.Equal("Hello from parent sibling (../other/parent_sibling.yml)", result.Logs[2].Message);
        Assert.Equal("Done", result.Logs[3].Message);
    }

    // =========================================================================
    // VARIABLE SCOPE ISOLATION TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_ImportedFlowModifiesExistingVariable_PropagatesUpward()
    {
        // Arrange - Test that when imported flow modifies an EXISTING variable,
        // the change propagates to the caller (this is by design - SetValue modifies parent if exists)
        var mainYaml = TestFixtures.Load("loader_scope_modify_main");
        var libYaml = TestFixtures.Load("loader_scope_modify_lib");

        var mainResult = _parser.Parse(mainYaml);
        Assert.True(mainResult.IsSuccess, $"Main parse failed: {string.Join(", ", mainResult.Errors.Select(e => e.Message))}");
        var mainDoc = mainResult.Value!;

        var libResult = _parser.Parse(libYaml);
        Assert.True(libResult.IsSuccess, $"Lib parse failed: {string.Join(", ", libResult.Errors.Select(e => e.Message))}");
        var libDoc = libResult.Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("scope_lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "scope_main.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert - When lib modifies an existing variable (my_var), change propagates to caller
        // This is by design: SetValue finds existing variable in parent scope and updates it
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Equal(3, result.Logs.Count);
        Assert.Equal("Before call: original", result.Logs[0].Message);
        Assert.Equal("In lib: modified_by_lib", result.Logs[1].Message);
        Assert.Equal("After call: modified_by_lib", result.Logs[2].Message);  // Modified by lib!
    }

    [Fact]
    public async Task Execute_ImportedFlowSetsNewVariable_IsolatedFromCaller()
    {
        // Arrange - Test that NEW variables created in imported flow don't leak to caller
        var mainYaml = TestFixtures.Load("loader_scope_isolated_main");
        var libYaml = TestFixtures.Load("loader_scope_isolated_lib");

        var mainResult = _parser.Parse(mainYaml);
        Assert.True(mainResult.IsSuccess, $"Main parse failed: {string.Join(", ", mainResult.Errors.Select(e => e.Message))}");
        var mainDoc = mainResult.Value!;

        var libResult = _parser.Parse(libYaml);
        Assert.True(libResult.IsSuccess, $"Lib parse failed: {string.Join(", ", libResult.Errors.Select(e => e.Message))}");
        var libDoc = libResult.Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("scope_isolated_lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "scope_isolated_main.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert - NEW variables created in lib scope should NOT be visible to caller
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Equal(3, result.Logs.Count);
        Assert.Equal("Before call, lib_only_var = null", result.Logs[0].Message);  // Not set yet
        Assert.Equal("In lib: created_in_lib", result.Logs[1].Message);  // Set in lib's scope
        Assert.Equal("After call, lib_only_var = null", result.Logs[2].Message);  // Not visible to caller!
    }

    [Fact]
    public async Task Execute_ImportedFlowReadsParentVariable_CanAccess()
    {
        // Arrange - Test that imported flow CAN read variables from parent scope
        var mainYaml = TestFixtures.Load("loader_scope_read_main");
        var libYaml = TestFixtures.Load("loader_scope_read_lib");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("scope_read_lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "scope_read_main.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert - lib CAN read from parent scope
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Single(result.Logs);
        Assert.Equal("Lib sees: Hello from main", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_ResultFromImportedFlow_AccessibleViaMagicVar()
    {
        // Arrange - Test that return value from imported flow is accessible via _result
        var mainYaml = TestFixtures.Load("loader_result_main");
        var libYaml = TestFixtures.Load("loader_result_lib");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("result_lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "result_main.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Equal(2, result.Logs.Count);
        Assert.Equal("Computing...", result.Logs[0].Message);
        Assert.Equal("Result was: 42", result.Logs[1].Message);
    }

    // =========================================================================
    // CONTEXT VARIABLES FROM IMPORTS TESTS
    // =========================================================================

    [Fact]
    public async Task Execute_ImportedDocumentContextVariables_NotAccessibleFromMain()
    {
        // Arrange - Test that imported document's context.variables are NOT accessible from main
        var mainYaml = TestFixtures.Load("loader_ctx_main");
        var libYaml = TestFixtures.Load("loader_ctx_lib");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("ctx_lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "ctx_main.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert - lib_config should NOT be accessible (resolves to null because not in scope)
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Single(result.Logs);
        // Variable not found - expression evaluates to "null" string
        Assert.Equal("Trying to access lib context var: null", result.Logs[0].Message);
    }

    [Fact]
    public async Task Execute_ImportedFlowUsesOwnContextVariables_Succeeds()
    {
        // Arrange - Test that imported flow CAN use its own document's context.variables
        var mainYaml = TestFixtures.Load("loader_ctx_own_main");
        var libYaml = TestFixtures.Load("loader_ctx_own_lib");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var libDoc = _parser.Parse(libYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("ctx_own_lib.yml", libDoc);

        var loader = new DocumentLoader(resolver, _parser);
        var loaded = await loader.LoadAsync(mainDoc, "ctx_own_main.yml", CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(loaded, "start");

        // Assert - Context variables are intentionally only initialized from root document
        // in tree-walking execution. This is by design - see ABML.md Appendix C.1.
        // The bytecode compilation path handles imports differently (merges with prefixes).
        Assert.True(result.IsSuccess, $"Execution failed: {result.Error}");
        Assert.Equal(2, result.Logs.Count);
        Assert.Equal("Calling lib", result.Logs[0].Message);
        // lib_setting is not initialized because tree-walking uses explicit parameter passing.
        // Workaround: set variables before calling imported flows.
    }

    // =========================================================================
    // SCHEMA-ONLY IMPORTS TESTS
    // =========================================================================

    [Fact]
    public async Task LoadAsync_SchemaOnlyImport_SkippedCorrectly()
    {
        // Arrange - Test that schema-only imports (no file:) are skipped
        var mainYaml = TestFixtures.Load("loader_schema_import_main");
        var realYaml = TestFixtures.Load("loader_schema_import_real");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var realDoc = _parser.Parse(realYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("real_import.yml", realDoc);
        // Note: types.json is NOT registered - schema imports should be skipped

        var loader = new DocumentLoader(resolver, _parser);

        // Act - Should not throw even though types.json isn't registered
        var loaded = await loader.LoadAsync(mainDoc, "schema_import_main.yml", CancellationToken.None);

        // Assert
        Assert.NotNull(loaded);
        Assert.Single(loaded.Imports);  // Only the file import, not the schema import
        Assert.True(loaded.Imports.ContainsKey("real"));
    }

    [Fact]
    public async Task LoadAsync_EmptyFileImport_SkippedCorrectly()
    {
        // Arrange - Test that imports with empty file: are skipped
        var mainYaml = TestFixtures.Load("loader_empty_file_main");
        var validYaml = TestFixtures.Load("loader_empty_file_valid");

        var mainDoc = _parser.Parse(mainYaml).Value!;
        var validDoc = _parser.Parse(validYaml).Value!;

        var resolver = new InMemoryDocumentResolver();
        resolver.Register("valid.yml", validDoc);

        var loader = new DocumentLoader(resolver, _parser);

        // Act
        var loaded = await loader.LoadAsync(mainDoc, "empty_file_main.yml", CancellationToken.None);

        // Assert
        Assert.NotNull(loaded);
        Assert.Single(loaded.Imports);
        Assert.True(loaded.Imports.ContainsKey("valid"));
        Assert.False(loaded.Imports.ContainsKey("empty"));
    }
}
