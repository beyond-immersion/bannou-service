// =============================================================================
// Behavior Compiler
// Main entry point for compiling ABML documents to behavior bytecode.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Compiler.Actions;
using BeyondImmersion.BannouService.Abml.Bytecode;
using BeyondImmersion.BannouService.Abml.Documents;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Parser;

namespace BeyondImmersion.Bannou.Behavior.Compiler;

/// <summary>
/// Compiles ABML documents to behavior model bytecode.
/// </summary>
public sealed class BehaviorCompiler
{
    private readonly DocumentParser _parser = new();
    private readonly SemanticAnalyzer _analyzer = new();

    /// <summary>
    /// Compiles an ABML YAML string to bytecode.
    /// </summary>
    /// <param name="yaml">The ABML YAML source.</param>
    /// <param name="options">Compilation options.</param>
    /// <returns>The compilation result.</returns>
    public CompilationResult CompileYaml(string yaml, CompilationOptions? options = null)
    {
        var parseResult = _parser.Parse(yaml);

        if (!parseResult.IsSuccess || parseResult.Value == null)
        {
            return new CompilationResult(
                false,
                null,
                parseResult.Errors.Select(e => new CompilationError(e.Message)).ToList());
        }

        return Compile(parseResult.Value, options);
    }

    /// <summary>
    /// Compiles a parsed ABML document to bytecode.
    /// </summary>
    /// <param name="document">The parsed ABML document.</param>
    /// <param name="options">Compilation options.</param>
    /// <returns>The compilation result.</returns>
    public CompilationResult Compile(AbmlDocument document, CompilationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var context = new CompilationContext(options);

        // Set model ID if provided
        if (options?.ModelId.HasValue == true)
        {
            context.ModelBuilder.WithModelId(options.ModelId.Value);
        }

        // Set source path for debug info
        context.SourcePath = document.Metadata.Id;

        try
        {
            // Phase 0: Semantic analysis (optional but recommended)
            if (options?.SkipSemanticAnalysis != true)
            {
                var analysisResult = _analyzer.Analyze(document);
                if (!analysisResult.IsValid)
                {
                    foreach (var error in analysisResult.Errors)
                    {
                        context.AddError(error.Message);
                    }
                    return new CompilationResult(false, null, context.Errors);
                }
            }

            // Phase 1: Analyze and register variables
            AnalyzeDocument(document, context);

            // Phase 2: Compile flows
            CompileFlows(document, context);

            // Phase 3: Finalize
            if (context.HasErrors)
            {
                return new CompilationResult(false, null, context.Errors);
            }

            // Add halt at end
            context.Emitter.Emit(BehaviorOpcode.Halt);

            // Build final output
            var bytecode = context.Finalize();

            return new CompilationResult(true, bytecode, context.Errors);
        }
        catch (Exception ex)
        {
            context.AddError($"Compilation failed: {ex.Message}");
            return new CompilationResult(false, null, context.Errors);
        }
    }

    private static void AnalyzeDocument(AbmlDocument document, CompilationContext context)
    {
        // Register variables from context
        if (document.Context?.Variables != null)
        {
            foreach (var (name, def) in document.Context.Variables)
            {
                var defaultValue = def.Default switch
                {
                    double d => d,
                    int i => (double)i,
                    long l => (double)l,
                    float f => (double)f,
                    bool b => b ? 1.0 : 0.0,
                    string s when double.TryParse(s, out var parsed) => parsed,
                    _ => 0.0
                };

                // Register as input (can be read from game state)
                context.RegisterInput(name, defaultValue);
            }
        }
    }

    private static void CompileFlows(AbmlDocument document, CompilationContext context)
    {
        var registry = new ActionCompilerRegistry(context);

        // Compile main flow first (if exists)
        if (document.Flows.TryGetValue("main", out var mainFlow))
        {
            CompileFlow("main", mainFlow, registry, context);
        }

        // Compile other flows
        foreach (var (name, flow) in document.Flows)
        {
            if (name != "main")
            {
                CompileFlow(name, flow, registry, context);
            }
        }

        // Patch all flow labels
        foreach (var (flowName, labelId) in context.Labels.GetFlowLabels())
        {
            if (!context.Labels.TryGetFlowOffset(flowName, out _))
            {
                context.AddError($"Undefined flow: {flowName}");
            }
        }
    }

    private static void CompileFlow(
        string name,
        Flow flow,
        ActionCompilerRegistry registry,
        CompilationContext context)
    {
        // Record flow offset
        var offset = context.Emitter.CurrentOffset;
        context.Labels.RegisterFlowOffset(name, offset);

        // Define flow label
        var labelId = context.Labels.GetOrAllocateFlowLabel(name);
        context.Emitter.DefineLabel(labelId);

        // Compile flow actions
        registry.CompileActions(flow.Actions, context);
    }
}

/// <summary>
/// Result of compiling an ABML document.
/// </summary>
public sealed class CompilationResult
{
    /// <summary>
    /// Whether compilation succeeded.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// The compiled bytecode (null if compilation failed).
    /// </summary>
    public byte[]? Bytecode { get; }

    /// <summary>
    /// Compilation errors and warnings.
    /// </summary>
    public IReadOnlyList<CompilationError> Errors { get; }

    /// <summary>
    /// Creates a new compilation result.
    /// </summary>
    public CompilationResult(bool success, byte[]? bytecode, IReadOnlyList<CompilationError> errors)
    {
        Success = success;
        Bytecode = bytecode;
        Errors = errors;
    }
}
