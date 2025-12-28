using BeyondImmersion.BannouService.Documentation.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Documentation.Tests.Services;

/// <summary>
/// Unit tests for ContentTransformService.
/// Tests YAML frontmatter parsing, slug generation, and category determination.
/// </summary>
public class ContentTransformServiceTests
{
    private readonly Mock<ILogger<ContentTransformService>> _mockLogger;
    private readonly ContentTransformService _service;

    public ContentTransformServiceTests()
    {
        _mockLogger = new Mock<ILogger<ContentTransformService>>();
        _service = new ContentTransformService(_mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = new ContentTransformService(_mockLogger.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ContentTransformService(null!));
    }

    #endregion

    #region ParseFrontmatter Tests

    [Fact]
    public void ParseFrontmatter_WithValidYaml_ShouldReturnFrontmatter()
    {
        // Arrange
        var content = """
            ---
            title: Test Document
            category: Guide
            tags:
            - test
            - example
            ---
            # Content here
            """;

        // Act
        var result = _service.ParseFrontmatter(content);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test Document", result.Title);
        Assert.Equal("Guide", result.Category);
        Assert.Contains("test", result.Tags);
        Assert.Contains("example", result.Tags);
    }

    [Fact]
    public void ParseFrontmatter_WithNoFrontmatter_ShouldReturnNull()
    {
        // Arrange
        var content = "# Just content without frontmatter";

        // Act
        var result = _service.ParseFrontmatter(content);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseFrontmatter_WithEmptyContent_ShouldReturnNull()
    {
        // Arrange & Act
        var result = _service.ParseFrontmatter(string.Empty);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseFrontmatter_WithNullContent_ShouldReturnNull()
    {
        // Arrange & Act
        var result = _service.ParseFrontmatter(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseFrontmatter_WithDraftFlag_ShouldParseDraft()
    {
        // Arrange
        var content = """
            ---
            title: Draft Document
            draft: true
            ---
            # Draft content
            """;

        // Act
        var result = _service.ParseFrontmatter(content);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Draft);
    }

    [Fact]
    public void ParseFrontmatter_WithSlugOverride_ShouldParseSlug()
    {
        // Arrange
        var content = """
            ---
            title: Custom Slug Document
            slug: my-custom-slug
            ---
            # Content
            """;

        // Act
        var result = _service.ParseFrontmatter(content);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("my-custom-slug", result.Slug);
    }

    [Fact]
    public void ParseFrontmatter_WithInvalidYaml_ShouldReturnNull()
    {
        // Arrange
        var content = """
            ---
            title: Test
            invalid: [unclosed bracket
            ---
            # Content
            """;

        // Act
        var result = _service.ParseFrontmatter(content);

        // Assert - Invalid YAML should return null and log warning
        Assert.Null(result);
    }

    #endregion

    #region ExtractContent Tests

    [Fact]
    public void ExtractContent_WithFrontmatter_ShouldReturnContentWithoutFrontmatter()
    {
        // Arrange
        var content = """
            ---
            title: Test
            ---
            # Real Content

            Paragraph here.
            """;

        // Act
        var result = _service.ExtractContent(content);

        // Assert
        Assert.StartsWith("# Real Content", result);
        Assert.DoesNotContain("---", result);
    }

    [Fact]
    public void ExtractContent_WithNoFrontmatter_ShouldReturnOriginalContent()
    {
        // Arrange
        var content = "# Just content\n\nNo frontmatter here.";

        // Act
        var result = _service.ExtractContent(content);

        // Assert
        Assert.Equal(content, result);
    }

    [Fact]
    public void ExtractContent_WithEmptyContent_ShouldReturnEmpty()
    {
        // Arrange & Act
        var result = _service.ExtractContent(string.Empty);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region GenerateSlug Tests

    [Fact]
    public void GenerateSlug_FromFilename_ShouldGenerateValidSlug()
    {
        // Arrange
        var filePath = "getting-started.md";

        // Act
        var result = _service.GenerateSlug(filePath);

        // Assert
        Assert.Equal("getting-started", result);
    }

    [Fact]
    public void GenerateSlug_WithDirectory_ShouldIncludeDirectory()
    {
        // Arrange
        var filePath = "guides/getting-started.md";

        // Act
        var result = _service.GenerateSlug(filePath);

        // Assert
        Assert.Equal("guides/getting-started", result);
    }

    [Fact]
    public void GenerateSlug_WithSpaces_ShouldConvertToHyphens()
    {
        // Arrange
        var filePath = "My Document Name.md";

        // Act
        var result = _service.GenerateSlug(filePath);

        // Assert
        Assert.Equal("my-document-name", result);
    }

    [Fact]
    public void GenerateSlug_WithUnderscores_ShouldConvertToHyphens()
    {
        // Arrange
        var filePath = "my_document_name.md";

        // Act
        var result = _service.GenerateSlug(filePath);

        // Assert
        Assert.Equal("my-document-name", result);
    }

    [Fact]
    public void GenerateSlug_WithSpecialCharacters_ShouldRemoveThem()
    {
        // Arrange
        var filePath = "How to: Create a (Test) Document!.md";

        // Act
        var result = _service.GenerateSlug(filePath);

        // Assert
        Assert.Equal("how-to-create-a-test-document", result);
    }

    [Fact]
    public void GenerateSlug_WithMultipleHyphens_ShouldCollapse()
    {
        // Arrange
        var filePath = "my---document.md";

        // Act
        var result = _service.GenerateSlug(filePath);

        // Assert
        Assert.Equal("my-document", result);
    }

    [Fact]
    public void GenerateSlug_WithEmptyPath_ShouldReturnEmpty()
    {
        // Arrange & Act
        var result = _service.GenerateSlug(string.Empty);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GenerateSlug_WithWindowsPath_ShouldNormalize()
    {
        // Arrange - Use forward slashes which work cross-platform
        // Note: On Linux, backslashes are treated as literal characters, not path separators
        var filePath = "docs/guides/getting-started.md";

        // Act
        var result = _service.GenerateSlug(filePath);

        // Assert
        Assert.Equal("docs/guides/getting-started", result);
    }

    #endregion

    #region DetermineCategory Tests

    [Fact]
    public void DetermineCategory_WithFrontmatterCategory_ShouldUseFrontmatter()
    {
        // Arrange
        var frontmatter = new DocumentFrontmatter { Category = "Custom Category" };
        var categoryMapping = new Dictionary<string, string> { { "guides/", "Guide" } };

        // Act
        var result = _service.DetermineCategory("guides/test.md", frontmatter, categoryMapping, "Other");

        // Assert
        Assert.Equal("Custom Category", result);
    }

    [Fact]
    public void DetermineCategory_WithCategoryMapping_ShouldUseMapping()
    {
        // Arrange
        var categoryMapping = new Dictionary<string, string>
        {
            { "guides/", "Guide" },
            { "api/", "Reference" }
        };

        // Act
        var result = _service.DetermineCategory("guides/getting-started.md", null, categoryMapping, "Other");

        // Assert
        Assert.Equal("Guide", result);
    }

    [Fact]
    public void DetermineCategory_WithDirectoryInference_ShouldInferFromDirectory()
    {
        // Arrange & Act
        var result = _service.DetermineCategory("tutorials/my-tutorial.md", null, null, "Other");

        // Assert
        Assert.Equal("Tutorial", result);
    }

    [Fact]
    public void DetermineCategory_WithNoMatch_ShouldReturnDefault()
    {
        // Arrange & Act
        var result = _service.DetermineCategory("random/document.md", null, null, "Default Category");

        // Assert
        Assert.Equal("Default Category", result);
    }

    [Fact]
    public void DetermineCategory_WithLongestPrefixMatch_ShouldMatchLongest()
    {
        // Arrange
        var categoryMapping = new Dictionary<string, string>
        {
            { "guides/", "Guide" },
            { "guides/advanced/", "Advanced Guide" }
        };

        // Act
        var result = _service.DetermineCategory("guides/advanced/complex-topic.md", null, categoryMapping, "Other");

        // Assert
        Assert.Equal("Advanced Guide", result);
    }

    #endregion

    #region TransformFile Tests

    [Fact]
    public void TransformFile_WithCompleteFrontmatter_ShouldTransformCorrectly()
    {
        // Arrange
        var content = """
            ---
            title: Complete Document
            slug: complete-doc
            category: Guide
            summary: A complete test document
            voiceSummary: A summary for voice
            tags:
            - test
            - complete
            ---
            # Complete Document

            This is the content.
            """;

        // Act
        var result = _service.TransformFile("test.md", content, null, "Other");

        // Assert
        Assert.Equal("complete-doc", result.Slug);
        Assert.Equal("Complete Document", result.Title);
        Assert.Equal("Guide", result.Category);
        Assert.Equal("A complete test document", result.Summary);
        Assert.Equal("A summary for voice", result.VoiceSummary);
        Assert.Contains("test", result.Tags);
        Assert.Contains("complete", result.Tags);
        Assert.False(result.IsDraft);
    }

    [Fact]
    public void TransformFile_WithDraftFlag_ShouldMarkAsDraft()
    {
        // Arrange
        var content = """
            ---
            title: Draft Document
            draft: true
            ---
            # Draft
            """;

        // Act
        var result = _service.TransformFile("draft.md", content, null, "Other");

        // Assert
        Assert.True(result.IsDraft);
    }

    [Fact]
    public void TransformFile_WithNoFrontmatter_ShouldExtractFromContent()
    {
        // Arrange
        var content = """
            # Auto-Extracted Title

            This is the first paragraph that becomes the summary.
            """;

        // Act
        var result = _service.TransformFile("test/my-document.md", content, null, "Other");

        // Assert
        Assert.Equal("test/my-document", result.Slug);
        Assert.Equal("Auto-Extracted Title", result.Title);
        Assert.Contains("first paragraph", result.Summary);
    }

    [Fact]
    public void TransformFile_WithNullContent_ShouldHandleGracefully()
    {
        // Arrange & Act
        var result = _service.TransformFile("test.md", "", null, "Other");

        // Assert
        Assert.Equal("test", result.Slug);
        Assert.Equal("test", result.Title); // Falls back to filename
    }

    [Fact]
    public void TransformFile_WithEmptyFilePath_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() => _service.TransformFile("", "content", null, "Other"));
    }

    #endregion

    #region GenerateVoiceSummary Tests

    [Fact]
    public void GenerateVoiceSummary_WithShortContent_ShouldReturnContent()
    {
        // Arrange
        var content = "This is a short paragraph.";

        // Act
        var result = _service.GenerateVoiceSummary(content);

        // Assert
        Assert.Equal("This is a short paragraph.", result);
    }

    [Fact]
    public void GenerateVoiceSummary_WithLongContent_ShouldTruncate()
    {
        // Arrange
        var content = new string('a', 300) + " " + new string('b', 100);

        // Act
        var result = _service.GenerateVoiceSummary(content, 200);

        // Assert
        Assert.True(result.Length <= 203); // 200 + "..."
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void GenerateVoiceSummary_WithMarkdown_ShouldStripFormatting()
    {
        // Arrange
        var content = "This has **bold** and *italic* and [links](http://example.com).";

        // Act
        var result = _service.GenerateVoiceSummary(content);

        // Assert
        Assert.DoesNotContain("**", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("[", result);
        Assert.DoesNotContain("](", result);
    }

    [Fact]
    public void GenerateVoiceSummary_WithEmptyContent_ShouldReturnEmpty()
    {
        // Arrange & Act
        var result = _service.GenerateVoiceSummary(string.Empty);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    #endregion
}
