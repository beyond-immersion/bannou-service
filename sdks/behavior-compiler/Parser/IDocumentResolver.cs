// =============================================================================
// ABML Document Resolver Interface
// Resolves import paths to parsed ABML documents.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;

namespace BeyondImmersion.Bannou.BehaviorCompiler.Parser;

/// <summary>
/// Resolves ABML document paths to parsed documents.
/// Used for handling imports in ABML documents.
/// </summary>
public interface IDocumentResolver
{
    /// <summary>
    /// Resolves a document path relative to a base path.
    /// </summary>
    /// <param name="importPath">The import path from the ABML document.</param>
    /// <param name="basePath">The path of the importing document (for relative resolution).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved and parsed document, or null if not found.</returns>
    ValueTask<ResolvedDocument?> ResolveAsync(string importPath, string? basePath, CancellationToken ct);
}

/// <summary>
/// A resolved ABML document with its source path.
/// </summary>
/// <param name="Document">The parsed ABML document.</param>
/// <param name="SourcePath">The resolved source path (for nested import resolution).</param>
public sealed record ResolvedDocument(AbmlDocument Document, string SourcePath);

/// <summary>
/// In-memory document resolver for testing.
/// Maps paths directly to documents without file system access.
/// </summary>
public sealed class InMemoryDocumentResolver : IDocumentResolver
{
    private readonly Dictionary<string, AbmlDocument> _documents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a document at a given path.
    /// </summary>
    public void Register(string path, AbmlDocument document)
    {
        _documents[NormalizePath(path)] = document;
    }

    /// <inheritdoc/>
    public ValueTask<ResolvedDocument?> ResolveAsync(string importPath, string? basePath, CancellationToken ct)
    {
        var resolvedPath = ResolvePath(importPath, basePath);
        var normalizedPath = NormalizePath(resolvedPath);

        if (_documents.TryGetValue(normalizedPath, out var document))
        {
            return ValueTask.FromResult<ResolvedDocument?>(new ResolvedDocument(document, normalizedPath));
        }

        return ValueTask.FromResult<ResolvedDocument?>(null);
    }

    private static string ResolvePath(string importPath, string? basePath)
    {
        if (string.IsNullOrEmpty(basePath) || Path.IsPathRooted(importPath))
        {
            return importPath;
        }

        var baseDir = Path.GetDirectoryName(basePath) ?? "";
        return Path.Combine(baseDir, importPath);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}

/// <summary>
/// File system document resolver for production use.
/// Resolves import paths to files on disk and parses them.
/// </summary>
public sealed class FileSystemDocumentResolver : IDocumentResolver
{
    private readonly string _basePath;
    private readonly DocumentParser _parser;
    private readonly Dictionary<string, AbmlDocument> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new file system document resolver.
    /// </summary>
    /// <param name="basePath">Base directory for resolving relative paths.</param>
    /// <param name="parser">Parser for YAML content.</param>
    public FileSystemDocumentResolver(string basePath, DocumentParser parser)
    {
        _basePath = Path.GetFullPath(basePath);
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    /// <inheritdoc/>
    public async ValueTask<ResolvedDocument?> ResolveAsync(string importPath, string? basePath, CancellationToken ct)
    {
        var resolvedPath = ResolvePath(importPath, basePath);
        var normalizedPath = NormalizePath(resolvedPath);

        // Check cache first
        if (_cache.TryGetValue(normalizedPath, out var cachedDoc))
        {
            return new ResolvedDocument(cachedDoc, normalizedPath);
        }

        // Construct full file path
        var fullPath = Path.IsPathRooted(resolvedPath)
            ? resolvedPath
            : Path.GetFullPath(Path.Combine(_basePath, resolvedPath));

        // Validate file is within base path (security check)
        if (!fullPath.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            return null;  // Path traversal attempt - reject silently
        }

        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            var yaml = await File.ReadAllTextAsync(fullPath, ct);
            var parseResult = _parser.Parse(yaml);

            if (!parseResult.IsSuccess || parseResult.Value == null)
            {
                return null;
            }

            // Cache the result
            _cache[normalizedPath] = parseResult.Value;
            return new ResolvedDocument(parseResult.Value, normalizedPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string ResolvePath(string importPath, string? basePath)
    {
        if (string.IsNullOrEmpty(basePath) || Path.IsPathRooted(importPath))
        {
            return importPath;
        }

        // Resolve relative to the importing document's directory
        var baseDir = Path.GetDirectoryName(basePath) ?? "";
        return Path.Combine(baseDir, importPath);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }
}
