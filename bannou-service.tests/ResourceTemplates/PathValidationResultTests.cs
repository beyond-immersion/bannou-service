using BeyondImmersion.Bannou.BehaviorCompiler.Templates;

namespace BeyondImmersion.BannouService.Tests.ResourceTemplates;

/// <summary>
/// Tests for PathValidationResult factory methods and suggestions.
/// </summary>
public class PathValidationResultTests
{
    [Fact]
    public void Valid_ReturnsValidResult()
    {
        // Act
        var result = PathValidationResult.Valid(typeof(string));

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(typeof(string), result.ExpectedType);
        Assert.Null(result.ErrorMessage);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void InvalidPath_ReturnsInvalidResult()
    {
        // Arrange
        var validPaths = new[] { "name", "id", "status" };

        // Act
        var result = PathValidationResult.InvalidPath("nam", validPaths);

        // Assert
        Assert.False(result.IsValid);
        Assert.Null(result.ExpectedType);
        Assert.Contains("nam", result.ErrorMessage);
        Assert.NotEmpty(result.Suggestions);
    }

    [Fact]
    public void InvalidPath_SuggestsSimilarPaths_WithPrefixMatch()
    {
        // Arrange - paths that start with input prefix
        var validPaths = new[] { "character.id", "character.name", "status", "personality" };

        // Act - "character" prefix matches paths starting with "character"
        var result = PathValidationResult.InvalidPath("character", validPaths);

        // Assert - both paths starting with "character" should be suggested
        Assert.Contains("character.id", result.Suggestions);
        Assert.Contains("character.name", result.Suggestions);
    }

    [Fact]
    public void UnknownTemplate_ReturnsInvalidResult()
    {
        // Act
        var result = PathValidationResult.UnknownTemplate("nonexistent");

        // Assert
        Assert.False(result.IsValid);
        Assert.Null(result.ExpectedType);
        Assert.Contains("nonexistent", result.ErrorMessage);
        Assert.Contains("Unknown resource template", result.ErrorMessage);
    }

    [Fact]
    public void FindSimilarPaths_WithPrefixMatch_FindsMatch()
    {
        // Arrange
        var validPaths = new[] { "personality", "history", "character" };

        // Act - use a test path with matching prefix
        var result = PathValidationResult.InvalidPath("person", validPaths);

        // Assert - should suggest "personality" (starts with "person")
        Assert.Contains("personality", result.Suggestions);
    }

    [Fact]
    public void FindSimilarPaths_NoMatchingPrefix_ReturnsEmpty()
    {
        // Arrange
        var validPaths = new[] { "abc", "def", "ghi" };

        // Act
        var result = PathValidationResult.InvalidPath("xyz", validPaths);

        // Assert - no paths start with "xyz"
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public void FindSimilarPaths_LimitedToThree()
    {
        // Arrange - many paths with matching prefix
        var validPaths = new[]
        {
            "characterId", "characterName", "characterStatus",
            "characterAge", "characterRealm", "characterSpecies"
        };

        // Act
        var result = PathValidationResult.InvalidPath("character", validPaths);

        // Assert - at most 3 suggestions
        Assert.True(result.Suggestions.Count <= 3);
    }
}
