namespace BeyondImmersion.BannouService.Documentation.Services;

/// <summary>
/// Interface for transforming git repository content into documentation format.
/// Handles YAML frontmatter parsing, slug generation, and category mapping.
/// </summary>
public interface IContentTransformService
{
    /// <summary>
    /// Parses YAML frontmatter from markdown content.
    /// </summary>
    /// <param name="content">The raw markdown content with optional frontmatter.</param>
    /// <returns>The parsed frontmatter, or null if no frontmatter is present.</returns>
    DocumentFrontmatter? ParseFrontmatter(string content);

    /// <summary>
    /// Extracts the markdown content without frontmatter.
    /// </summary>
    /// <param name="content">The raw markdown content with optional frontmatter.</param>
    /// <returns>The markdown content without the frontmatter block.</returns>
    string ExtractContent(string content);

    /// <summary>
    /// Generates a URL-friendly slug from a file path.
    /// </summary>
    /// <param name="filePath">The file path relative to repository root.</param>
    /// <returns>A URL-safe slug.</returns>
    string GenerateSlug(string filePath);

    /// <summary>
    /// Determines the document category from frontmatter, path, or mapping.
    /// Priority: frontmatter > path prefix match > default.
    /// </summary>
    /// <param name="filePath">The file path relative to repository root.</param>
    /// <param name="frontmatter">The parsed frontmatter (may be null).</param>
    /// <param name="categoryMapping">Optional path-to-category mapping.</param>
    /// <param name="defaultCategory">The default category if no match is found.</param>
    /// <returns>The determined category as a string.</returns>
    string DetermineCategory(
        string filePath,
        DocumentFrontmatter? frontmatter,
        IDictionary<string, string>? categoryMapping,
        string defaultCategory);

    /// <summary>
    /// Transforms a markdown file into a document import model.
    /// </summary>
    /// <param name="filePath">The file path relative to repository root.</param>
    /// <param name="content">The raw file content.</param>
    /// <param name="categoryMapping">Optional path-to-category mapping.</param>
    /// <param name="defaultCategory">The default category.</param>
    /// <returns>The transformed document import model.</returns>
    TransformedDocument TransformFile(
        string filePath,
        string content,
        IDictionary<string, string>? categoryMapping,
        string defaultCategory);

    /// <summary>
    /// Generates a voice-friendly summary from content.
    /// </summary>
    /// <param name="content">The markdown content.</param>
    /// <param name="maxLength">Maximum summary length.</param>
    /// <returns>A concise, voice-friendly summary.</returns>
    string GenerateVoiceSummary(string content, int maxLength = 200);
}

/// <summary>
/// Represents parsed YAML frontmatter from a markdown document.
/// </summary>
public class DocumentFrontmatter
{
    /// <summary>Gets or sets the document title.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the document category.</summary>
    public string? Category { get; set; }

    /// <summary>Gets or sets the document summary/description.</summary>
    public string? Summary { get; set; }

    /// <summary>Gets or sets the document tags.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Gets or sets the voice summary for audio output.</summary>
    public string? VoiceSummary { get; set; }

    /// <summary>Gets or sets custom metadata.</summary>
    public Dictionary<string, object> Metadata { get; set; } = [];

    /// <summary>Gets or sets the document slug override.</summary>
    public string? Slug { get; set; }

    /// <summary>Gets or sets related document slugs or IDs.</summary>
    public List<string> Related { get; set; } = [];

    /// <summary>Gets or sets whether the document is a draft (should be skipped).</summary>
    public bool Draft { get; set; }

    /// <summary>Gets or sets the document order within its category.</summary>
    public int Order { get; set; }
}

/// <summary>
/// Represents a document transformed from a repository file.
/// </summary>
public class TransformedDocument
{
    /// <summary>Gets or sets the document slug.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Gets or sets the document title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the document category.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Gets or sets the markdown content.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Gets or sets the document summary.</summary>
    public string? Summary { get; set; }

    /// <summary>Gets or sets the voice summary.</summary>
    public string? VoiceSummary { get; set; }

    /// <summary>Gets or sets the document tags.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Gets or sets custom metadata.</summary>
    public Dictionary<string, object> Metadata { get; set; } = [];

    /// <summary>Gets or sets related document references.</summary>
    public List<string> Related { get; set; } = [];

    /// <summary>Gets or sets whether the document is a draft.</summary>
    public bool IsDraft { get; set; }

    /// <summary>Gets or sets the original file path.</summary>
    public string SourcePath { get; set; } = string.Empty;
}
