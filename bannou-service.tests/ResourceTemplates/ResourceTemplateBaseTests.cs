using BeyondImmersion.Bannou.BehaviorCompiler.Templates;
using BeyondImmersion.BannouService.ResourceTemplates;

namespace BeyondImmersion.BannouService.Tests.ResourceTemplates;

/// <summary>
/// Tests for ResourceTemplateBase path validation logic.
/// </summary>
public class ResourceTemplateBaseTests
{
    private readonly TestTemplate _template = new();

    [Fact]
    public void ValidatePath_EmptyPath_ReturnsRootType()
    {
        // Act
        var result = _template.ValidatePath("");

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(typeof(TestArchive), result.ExpectedType);
    }

    [Fact]
    public void ValidatePath_NullPath_ReturnsRootType()
    {
        // Act
        var result = _template.ValidatePath(null!);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(typeof(TestArchive), result.ExpectedType);
    }

    [Fact]
    public void ValidatePath_DirectMatch_ReturnsType()
    {
        // Act
        var result = _template.ValidatePath("characterId");

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(typeof(Guid), result.ExpectedType);
    }

    [Fact]
    public void ValidatePath_NestedPath_ReturnsType()
    {
        // Act
        var result = _template.ValidatePath("personality.archetypeHint");

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(typeof(string), result.ExpectedType);
    }

    [Fact]
    public void ValidatePath_IntermediatePath_ReturnsObjectType()
    {
        // Act - "personality" is a prefix of "personality.archetypeHint"
        var result = _template.ValidatePath("personality");

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(typeof(object), result.ExpectedType); // Intermediate nodes return object
    }

    [Fact]
    public void ValidatePath_InvalidPath_ReturnsError()
    {
        // Act
        var result = _template.ValidatePath("nonexistent");

        // Assert
        Assert.False(result.IsValid);
        Assert.Null(result.ExpectedType);
        Assert.Contains("nonexistent", result.ErrorMessage);
    }

    [Fact]
    public void ValidatePath_InvalidNestedPath_ReturnsError()
    {
        // Act
        var result = _template.ValidatePath("personality.nonexistent");

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidatePath_CaseSensitive()
    {
        // Act - paths are case-sensitive per the dictionary
        var result = _template.ValidatePath("CharacterId");

        // Assert - should not find it (case mismatch)
        Assert.False(result.IsValid);
    }

    /// <summary>
    /// Test template with a realistic path structure.
    /// </summary>
    private sealed class TestTemplate : ResourceTemplateBase
    {
        public override string SourceType => "test";
        public override string Namespace => "test";
        public override IReadOnlyDictionary<string, Type> ValidPaths { get; } = new Dictionary<string, Type>
        {
            [""] = typeof(TestArchive),
            ["characterId"] = typeof(Guid),
            ["hasPersonality"] = typeof(bool),
            ["personality.archetypeHint"] = typeof(string),
            ["personality.version"] = typeof(int),
            ["personality.traits"] = typeof(ICollection<TraitValue>),
        };
    }

    // Dummy types for testing
    private sealed class TestArchive { }
    private sealed class TraitValue { }
}
