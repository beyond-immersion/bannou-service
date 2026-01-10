using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeyondImmersion.BannouService.Documentation.Services;

/// <summary>
/// Service for transforming git repository content into documentation format.
/// Handles YAML frontmatter parsing using YamlDotNet, slug generation, and category mapping.
/// </summary>
public partial class ContentTransformService : IContentTransformService
{
    private readonly ILogger<ContentTransformService> _logger;
    private readonly IDeserializer _yamlDeserializer;

    // Regex to match YAML frontmatter (---\n...\n---)
    [GeneratedRegex(@"^---\s*\r?\n(.*?)\r?\n---\s*\r?\n?", RegexOptions.Singleline)]
    private static partial Regex FrontmatterRegex();

    // Regex for slug generation - matches non-alphanumeric characters
    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex NonSlugCharRegex();

    // Regex to collapse multiple hyphens
    [GeneratedRegex(@"-+")]
    private static partial Regex MultipleHyphensRegex();

    /// <summary>
    /// Creates a new instance of the ContentTransformService.
    /// </summary>
    public ContentTransformService(ILogger<ContentTransformService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <inheritdoc />
    public DocumentFrontmatter? ParseFrontmatter(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var match = FrontmatterRegex().Match(content);
        if (!match.Success)
        {
            return null;
        }

        try
        {
            var yamlContent = match.Groups[1].Value;
            var frontmatter = _yamlDeserializer.Deserialize<DocumentFrontmatter>(yamlContent);
            return frontmatter;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse YAML frontmatter");
            return null;
        }
    }

    /// <inheritdoc />
    public string ExtractContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var match = FrontmatterRegex().Match(content);
        if (!match.Success)
        {
            return content;
        }

        return content[match.Length..].TrimStart();
    }

    /// <inheritdoc />
    public string GenerateSlug(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        // Remove file extension
        var slug = Path.GetFileNameWithoutExtension(filePath);

        // Include directory path for uniqueness (e.g., "guides/getting-started")
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            // Normalize directory separators and combine
            directory = directory.Replace('\\', '/').Trim('/');
            slug = $"{directory}/{slug}";
        }

        // Convert to lowercase
        slug = slug.ToLowerInvariant();

        // Replace spaces and underscores with hyphens
        slug = slug.Replace(' ', '-').Replace('_', '-');

        // Remove special characters (keep alphanumeric, hyphens, and forward slashes)
        slug = Regex.Replace(slug, @"[^a-z0-9\-/]", "");

        // Collapse multiple hyphens
        slug = MultipleHyphensRegex().Replace(slug, "-");

        // Remove leading/trailing hyphens from each segment
        var segments = slug.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = segments[i].Trim('-');
        }
        slug = string.Join("/", segments.Where(s => !string.IsNullOrEmpty(s)));

        return slug;
    }

    /// <inheritdoc />
    public string DetermineCategory(
        string filePath,
        DocumentFrontmatter? frontmatter,
        IDictionary<string, string>? categoryMapping,
        string defaultCategory)
    {
        // Priority 1: Frontmatter category
        if (!string.IsNullOrWhiteSpace(frontmatter?.Category))
        {
            return frontmatter.Category;
        }

        // Priority 2: Path prefix matching via category mapping
        if (categoryMapping != null && categoryMapping.Count > 0)
        {
            var normalizedPath = filePath.Replace('\\', '/').ToLowerInvariant();

            // Find longest matching prefix
            var matchedCategory = categoryMapping
                .Where(kvp => normalizedPath.StartsWith(kvp.Key.ToLowerInvariant(), StringComparison.Ordinal))
                .OrderByDescending(kvp => kvp.Key.Length)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(matchedCategory.Value))
            {
                return matchedCategory.Value;
            }
        }

        // Priority 3: Infer from directory structure
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            var firstDir = directory.Replace('\\', '/').Split('/').FirstOrDefault();
            if (!string.IsNullOrEmpty(firstDir))
            {
                // Try to map common directory names to categories
                var inferredCategory = InferCategoryFromDirectory(firstDir);
                if (!string.IsNullOrEmpty(inferredCategory))
                {
                    return inferredCategory;
                }
            }
        }

        // Fallback: Default category
        return defaultCategory;
    }

    /// <inheritdoc />
    public TransformedDocument TransformFile(
        string filePath,
        string content,
        IDictionary<string, string>? categoryMapping,
        string defaultCategory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var frontmatter = ParseFrontmatter(content);
        var markdownContent = ExtractContent(content);

        // Determine slug (frontmatter override or generated)
        var slug = !string.IsNullOrWhiteSpace(frontmatter?.Slug)
            ? frontmatter.Slug
            : GenerateSlug(filePath);

        // Determine title (frontmatter or extract from content)
        var title = !string.IsNullOrWhiteSpace(frontmatter?.Title)
            ? frontmatter.Title
            : ExtractTitleFromContent(markdownContent, filePath);

        // Determine category
        var category = DetermineCategory(filePath, frontmatter, categoryMapping, defaultCategory);

        // Determine summary
        var summary = !string.IsNullOrWhiteSpace(frontmatter?.Summary)
            ? frontmatter.Summary
            : ExtractSummaryFromContent(markdownContent);

        // Determine voice summary
        var voiceSummary = !string.IsNullOrWhiteSpace(frontmatter?.VoiceSummary)
            ? frontmatter.VoiceSummary
            : GenerateVoiceSummary(markdownContent);

        return new TransformedDocument
        {
            Slug = slug,
            Title = title,
            Category = category,
            Content = markdownContent,
            Summary = summary,
            VoiceSummary = voiceSummary,
            Tags = frontmatter?.Tags ?? [],
            Metadata = frontmatter?.Metadata ?? [],
            Related = frontmatter?.Related ?? [],
            IsDraft = frontmatter?.Draft ?? false,
            SourcePath = filePath
        };
    }

    /// <inheritdoc />
    public string GenerateVoiceSummary(string content, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        // Remove markdown formatting
        var text = StripMarkdown(content);

        // Get first paragraph or sentences
        var firstParagraph = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? text;

        // Truncate to max length at word boundary
        if (firstParagraph.Length <= maxLength)
        {
            return firstParagraph.Trim();
        }

        var truncated = firstParagraph[..maxLength];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
        {
            truncated = truncated[..lastSpace];
        }

        return truncated.Trim().TrimEnd('.', ',', ';', ':') + "...";
    }

    #region Private Methods

    private static string ExtractTitleFromContent(string content, string filePath)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            // Fallback to filename
            return Path.GetFileNameWithoutExtension(filePath)
                .Replace('-', ' ')
                .Replace('_', ' ');
        }

        // Look for first H1 heading
        var lines = content.Split('\n');
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("# ", StringComparison.Ordinal))
            {
                return trimmedLine[2..].Trim();
            }
        }

        // Fallback to filename
        return Path.GetFileNameWithoutExtension(filePath)
            .Replace('-', ' ')
            .Replace('_', ' ');
    }

    private static string? ExtractSummaryFromContent(string content, int maxLength = 300)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        // Skip the title (first H1) and find first paragraph
        var lines = content.Split('\n');
        var foundTitle = false;
        var summaryBuilder = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // Skip empty lines at start
            if (summaryBuilder.Length == 0 && string.IsNullOrEmpty(trimmedLine))
            {
                continue;
            }

            // Skip the first H1 heading
            if (!foundTitle && trimmedLine.StartsWith("# ", StringComparison.Ordinal))
            {
                foundTitle = true;
                continue;
            }

            // Skip other headings
            if (trimmedLine.StartsWith('#'))
            {
                if (summaryBuilder.Length > 0)
                {
                    break; // Stop at next heading
                }
                continue;
            }

            // End of paragraph
            if (string.IsNullOrEmpty(trimmedLine) && summaryBuilder.Length > 0)
            {
                break;
            }

            // Accumulate text
            if (!string.IsNullOrEmpty(trimmedLine))
            {
                if (summaryBuilder.Length > 0)
                {
                    summaryBuilder.Append(' ');
                }
                summaryBuilder.Append(trimmedLine);
            }

            if (summaryBuilder.Length >= maxLength)
            {
                break;
            }
        }

        var summary = StripMarkdown(summaryBuilder.ToString().Trim());
        if (summary.Length > maxLength)
        {
            var lastSpace = summary.LastIndexOf(' ', maxLength);
            if (lastSpace > maxLength / 2)
            {
                summary = summary[..lastSpace] + "...";
            }
            else
            {
                summary = summary[..maxLength] + "...";
            }
        }

        return string.IsNullOrEmpty(summary) ? null : summary;
    }

    private static string StripMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var text = markdown;

        // Remove code blocks
        text = Regex.Replace(text, @"```[\s\S]*?```", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"`[^`]+`", "");

        // Remove images
        text = Regex.Replace(text, @"!\[[^\]]*\]\([^)]+\)", "");

        // Convert links to text
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");

        // Remove bold/italic markers
        text = Regex.Replace(text, @"\*\*([^*]+)\*\*", "$1");
        text = Regex.Replace(text, @"\*([^*]+)\*", "$1");
        text = Regex.Replace(text, @"__([^_]+)__", "$1");
        text = Regex.Replace(text, @"_([^_]+)_", "$1");

        // Remove headings markers
        text = Regex.Replace(text, @"^#+\s*", "", RegexOptions.Multiline);

        // Remove blockquotes
        text = Regex.Replace(text, @"^>\s*", "", RegexOptions.Multiline);

        // Remove horizontal rules
        text = Regex.Replace(text, @"^[-*_]{3,}$", "", RegexOptions.Multiline);

        // Remove list markers
        text = Regex.Replace(text, @"^\s*[-*+]\s+", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"^\s*\d+\.\s+", "", RegexOptions.Multiline);

        // Collapse whitespace
        text = Regex.Replace(text, @"\s+", " ");

        return text.Trim();
    }

    private static string? InferCategoryFromDirectory(string directoryName)
    {
        // Map common directory names to categories
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "guides", "Guide" },
            { "guide", "Guide" },
            { "tutorials", "Tutorial" },
            { "tutorial", "Tutorial" },
            { "api", "Reference" },
            { "reference", "Reference" },
            { "concepts", "Concept" },
            { "concept", "Concept" },
            { "examples", "Example" },
            { "example", "Example" },
            { "faq", "Troubleshooting" },
            { "troubleshooting", "Troubleshooting" },
            { "architecture", "Architecture" },
            { "design", "Architecture" },
            { "getting-started", "Guide" },
            { "quickstart", "Guide" },
            { "overview", "Concept" },
            { "howto", "Guide" },
            { "how-to", "Guide" },
            { "recipes", "Example" },
            { "cookbook", "Example" }
        };

        return mappings.TryGetValue(directoryName, out var category) ? category : null;
    }

    #endregion
}
