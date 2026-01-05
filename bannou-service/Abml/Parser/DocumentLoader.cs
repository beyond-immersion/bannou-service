// =============================================================================
// ABML Document Loader
// Loads ABML documents with import resolution.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Documents;

namespace BeyondImmersion.BannouService.Abml.Parser;

/// <summary>
/// Loads ABML documents with full import resolution.
/// </summary>
public sealed class DocumentLoader
{
    private readonly IDocumentResolver _resolver;
    private readonly DocumentParser _parser;

    /// <summary>
    /// Creates a new document loader.
    /// </summary>
    /// <param name="resolver">Document resolver for imports.</param>
    /// <param name="parser">Parser for YAML content.</param>
    public DocumentLoader(IDocumentResolver resolver, DocumentParser parser)
    {
        _resolver = resolver;
        _parser = parser;
    }

    /// <summary>
    /// Loads a document from YAML content with all imports resolved.
    /// </summary>
    /// <param name="yaml">The YAML content.</param>
    /// <param name="sourcePath">Source path for relative import resolution.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded document with resolved imports.</returns>
    public async ValueTask<LoadedDocument> LoadAsync(string yaml, string? sourcePath, CancellationToken ct)
    {
        var parseResult = _parser.Parse(yaml);

        if (!parseResult.IsSuccess || parseResult.Value == null)
        {
            throw new DocumentLoadException(
                $"Failed to parse document: {string.Join(", ", parseResult.Errors.Select(e => e.Message))}",
                sourcePath);
        }

        return await LoadWithImportsAsync(parseResult.Value, sourcePath, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ct);
    }

    /// <summary>
    /// Loads a pre-parsed document with all imports resolved.
    /// </summary>
    /// <param name="document">The parsed document.</param>
    /// <param name="sourcePath">Source path for relative import resolution.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded document with resolved imports.</returns>
    public async ValueTask<LoadedDocument> LoadAsync(AbmlDocument document, string? sourcePath, CancellationToken ct)
    {
        return await LoadWithImportsAsync(document, sourcePath, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ct);
    }

    private async ValueTask<LoadedDocument> LoadWithImportsAsync(
        AbmlDocument document,
        string? sourcePath,
        HashSet<string> loadingStack,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Check for circular imports
        var normalizedPath = NormalizePath(sourcePath ?? document.Metadata?.Id ?? "unknown");
        if (!loadingStack.Add(normalizedPath))
        {
            throw new CircularImportException(normalizedPath, loadingStack);
        }

        try
        {
            var imports = new Dictionary<string, LoadedDocument>(StringComparer.OrdinalIgnoreCase);

            // Handle documents with no imports section (Imports may be null)
            if (document.Imports == null || document.Imports.Count == 0)
            {
                return new LoadedDocument(document, sourcePath, imports);
            }

            foreach (var import in document.Imports)
            {
                // Skip schema-only imports (type validation, not document imports)
                if (string.IsNullOrEmpty(import.File))
                {
                    continue;
                }

                var alias = import.As ?? Path.GetFileNameWithoutExtension(import.File);
                if (string.IsNullOrEmpty(alias))
                {
                    throw new DocumentLoadException(
                        $"Import '{import.File}' must have an 'as' alias or a valid filename",
                        sourcePath);
                }

                if (imports.ContainsKey(alias))
                {
                    throw new DocumentLoadException(
                        $"Duplicate import alias '{alias}'",
                        sourcePath);
                }

                var resolved = await _resolver.ResolveAsync(import.File, sourcePath, ct) ?? throw new DocumentLoadException(
                        $"Could not resolve import '{import.File}'",
                        sourcePath);

                // Recursively load imports
                var loadedImport = await LoadWithImportsAsync(
                    resolved.Document,
                    resolved.SourcePath,
                    loadingStack,
                    ct);

                imports[alias] = loadedImport;
            }

            return new LoadedDocument(document, sourcePath, imports);
        }
        finally
        {
            loadingStack.Remove(normalizedPath);
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').ToLowerInvariant();
    }
}

/// <summary>
/// A fully loaded ABML document with resolved imports.
/// </summary>
public sealed class LoadedDocument
{
    /// <summary>
    /// The main document.
    /// </summary>
    public AbmlDocument Document { get; }

    /// <summary>
    /// Source path of this document.
    /// </summary>
    public string? SourcePath { get; }

    /// <summary>
    /// Imported documents keyed by alias.
    /// </summary>
    public IReadOnlyDictionary<string, LoadedDocument> Imports { get; }

    /// <summary>
    /// Creates a new loaded document.
    /// </summary>
    public LoadedDocument(
        AbmlDocument document,
        string? sourcePath,
        IReadOnlyDictionary<string, LoadedDocument> imports)
    {
        Document = document;
        SourcePath = sourcePath;
        Imports = imports;
    }

    /// <summary>
    /// Creates a loaded document with no imports.
    /// </summary>
    public LoadedDocument(AbmlDocument document, string? sourcePath = null)
        : this(document, sourcePath, new Dictionary<string, LoadedDocument>())
    {
    }

    /// <summary>
    /// Tries to resolve a potentially namespaced flow reference.
    /// </summary>
    /// <param name="flowRef">Flow reference (e.g., "my_flow" or "common.my_flow").</param>
    /// <param name="flow">The resolved flow.</param>
    /// <param name="resolvedDocument">The document containing the flow.</param>
    /// <returns>True if the flow was found.</returns>
    public bool TryResolveFlow(string flowRef, out Flow? flow, out LoadedDocument? resolvedDocument)
    {
        var dotIndex = flowRef.IndexOf('.');
        if (dotIndex < 0)
        {
            // Local flow reference
            if (Document.Flows.TryGetValue(flowRef, out flow))
            {
                resolvedDocument = this;
                return true;
            }

            flow = null;
            resolvedDocument = null;
            return false;
        }

        // Namespaced reference: "alias.flow_name"
        var alias = flowRef[..dotIndex];
        var flowName = flowRef[(dotIndex + 1)..];

        if (!Imports.TryGetValue(alias, out var importedDoc))
        {
            flow = null;
            resolvedDocument = null;
            return false;
        }

        // Recursively resolve in imported document (supports nested namespaces)
        return importedDoc.TryResolveFlow(flowName, out flow, out resolvedDocument);
    }
}

/// <summary>
/// Exception thrown when a document fails to load.
/// </summary>
public class DocumentLoadException : Exception
{
    /// <summary>
    /// The source path that failed to load.
    /// </summary>
    public string? SourcePath { get; }

    /// <summary>
    /// Creates a new document load exception.
    /// </summary>
    public DocumentLoadException(string message, string? sourcePath)
        : base(message)
    {
        SourcePath = sourcePath;
    }
}

/// <summary>
/// Exception thrown when a circular import is detected.
/// </summary>
public sealed class CircularImportException : DocumentLoadException
{
    /// <summary>
    /// The path that caused the circular reference.
    /// </summary>
    public string CircularPath { get; }

    /// <summary>
    /// The import stack that led to the circular reference.
    /// </summary>
    public IReadOnlyCollection<string> ImportStack { get; }

    /// <summary>
    /// Creates a new circular import exception.
    /// </summary>
    public CircularImportException(string circularPath, IEnumerable<string> importStack)
        : base($"Circular import detected: '{circularPath}' is already being loaded", circularPath)
    {
        CircularPath = circularPath;
        ImportStack = importStack.ToList();
    }
}
