// =============================================================================
// Dialogue Resolver Tests
// Tests for the three-step dialogue resolution pipeline.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Dialogue;
using BeyondImmersion.BannouService.Behavior;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Dialogue;

/// <summary>
/// Tests for <see cref="DialogueResolver"/>.
/// </summary>
public sealed class DialogueResolverTests
{
    private readonly Mock<IExternalDialogueLoader> _loaderMock;
    private readonly DialogueResolver _resolver;
    private readonly LocalizationContext _englishLocale;
    private readonly IDialogueExpressionContext _nullContext;

    public DialogueResolverTests()
    {
        _loaderMock = new Mock<IExternalDialogueLoader>();
        _resolver = new DialogueResolver(_loaderMock.Object);
        _englishLocale = LocalizationContext.English;
        _nullContext = NullDialogueExpressionContext.Instance;
    }

    // =========================================================================
    // INLINE TEXT TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_WithNoExternalRef_ReturnsInlineText()
    {
        // Arrange
        var reference = DialogueReference.Inline("Hello, world!");

        // Act
        var result = await _resolver.ResolveAsync(reference, _englishLocale, _nullContext);

        // Assert
        Assert.Equal("Hello, world!", result.Text);
        Assert.Equal(DialogueSource.Inline, result.Source);
    }

    [Fact]
    public async Task ResolveAsync_WithExternalRefNotFound_ReturnsInlineText()
    {
        // Arrange
        var reference = DialogueReference.WithExternal("Default text", "nonexistent/ref");
        _loaderMock
            .Setup(l => l.LoadAsync("nonexistent/ref", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalDialogueFile?)null);

        // Act
        var result = await _resolver.ResolveAsync(reference, _englishLocale, _nullContext);

        // Assert
        Assert.Equal("Default text", result.Text);
        Assert.Equal(DialogueSource.Inline, result.Source);
    }

    // =========================================================================
    // LOCALIZATION TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_WithLocalization_ReturnsLocalizedText()
    {
        // Arrange
        var reference = DialogueReference.WithExternal("Default text", "dialogue/test");
        var externalFile = new ExternalDialogueFile
        {
            Reference = "dialogue/test",
            FilePath = "/test/dialogue/test.yaml",
            Localizations = new Dictionary<string, string>
            {
                ["en"] = "Hello from localization!"
            }
        };
        _loaderMock
            .Setup(l => l.LoadAsync("dialogue/test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalFile);

        // Act
        var result = await _resolver.ResolveAsync(reference, _englishLocale, _nullContext);

        // Assert
        Assert.Equal("Hello from localization!", result.Text);
        Assert.Equal(DialogueSource.Localization, result.Source);
        Assert.Equal("en", result.ResolvedLocale);
    }

    [Fact]
    public async Task ResolveAsync_WithLocaleFallback_UsesLanguageCode()
    {
        // Arrange
        var reference = DialogueReference.WithExternal("Default text", "dialogue/test");
        var externalFile = new ExternalDialogueFile
        {
            Reference = "dialogue/test",
            FilePath = "/test/dialogue/test.yaml",
            Localizations = new Dictionary<string, string>
            {
                ["en"] = "English text"
            }
        };
        _loaderMock
            .Setup(l => l.LoadAsync("dialogue/test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalFile);

        var usLocale = LocalizationContext.ForLocale("en-US");

        // Act
        var result = await _resolver.ResolveAsync(reference, usLocale, _nullContext);

        // Assert
        Assert.Equal("English text", result.Text);
        Assert.Equal("en", result.ResolvedLocale);
    }

    [Fact]
    public async Task ResolveAsync_WithMissingLocale_FallsBackToInline()
    {
        // Arrange
        var reference = DialogueReference.WithExternal("Default text", "dialogue/test");
        var externalFile = new ExternalDialogueFile
        {
            Reference = "dialogue/test",
            FilePath = "/test/dialogue/test.yaml",
            Localizations = new Dictionary<string, string>
            {
                ["de"] = "German text only"
            }
        };
        _loaderMock
            .Setup(l => l.LoadAsync("dialogue/test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalFile);

        // Act
        var result = await _resolver.ResolveAsync(reference, _englishLocale, _nullContext);

        // Assert
        Assert.Equal("Default text", result.Text);
        Assert.Equal(DialogueSource.Inline, result.Source);
    }

    // =========================================================================
    // OVERRIDE TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_WithMatchingOverride_ReturnsOverrideText()
    {
        // Arrange
        var reference = DialogueReference.WithExternal("Default text", "dialogue/test");
        var externalFile = new ExternalDialogueFile
        {
            Reference = "dialogue/test",
            FilePath = "/test/dialogue/test.yaml",
            Localizations = new Dictionary<string, string>
            {
                ["en"] = "Normal text"
            },
            Overrides =
            [
                new DialogueOverride
                {
                    Condition = "${is_vip}",
                    Text = "VIP special text!",
                    Priority = 10
                }
            ]
        };
        _loaderMock
            .Setup(l => l.LoadAsync("dialogue/test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalFile);

        // Create a mock context that returns true for the condition
        var contextMock = new Mock<IDialogueExpressionContext>();
        contextMock.Setup(c => c.EvaluateCondition("${is_vip}")).Returns(true);
        contextMock.Setup(c => c.EvaluateTemplate(It.IsAny<string>())).Returns<string>(s => s);

        // Act
        var result = await _resolver.ResolveAsync(reference, _englishLocale, contextMock.Object);

        // Assert
        Assert.Equal("VIP special text!", result.Text);
        Assert.Equal(DialogueSource.Override, result.Source);
        Assert.Equal("${is_vip}", result.MatchedCondition);
    }

    [Fact]
    public async Task ResolveAsync_WithNonMatchingOverride_ReturnsLocalization()
    {
        // Arrange
        var reference = DialogueReference.WithExternal("Default text", "dialogue/test");
        var externalFile = new ExternalDialogueFile
        {
            Reference = "dialogue/test",
            FilePath = "/test/dialogue/test.yaml",
            Localizations = new Dictionary<string, string>
            {
                ["en"] = "Normal text"
            },
            Overrides =
            [
                new DialogueOverride
                {
                    Condition = "${is_vip}",
                    Text = "VIP special text!",
                    Priority = 10
                }
            ]
        };
        _loaderMock
            .Setup(l => l.LoadAsync("dialogue/test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalFile);

        // Create a mock context that returns false for the condition
        var contextMock = new Mock<IDialogueExpressionContext>();
        contextMock.Setup(c => c.EvaluateCondition("${is_vip}")).Returns(false);
        contextMock.Setup(c => c.EvaluateTemplate(It.IsAny<string>())).Returns<string>(s => s);

        // Act
        var result = await _resolver.ResolveAsync(reference, _englishLocale, contextMock.Object);

        // Assert
        Assert.Equal("Normal text", result.Text);
        Assert.Equal(DialogueSource.Localization, result.Source);
    }

    [Fact]
    public async Task ResolveAsync_WithMultipleOverrides_UsesHighestPriority()
    {
        // Arrange
        var reference = DialogueReference.WithExternal("Default text", "dialogue/test");
        var externalFile = new ExternalDialogueFile
        {
            Reference = "dialogue/test",
            FilePath = "/test/dialogue/test.yaml",
            Overrides =
            [
                new DialogueOverride
                {
                    Condition = "${condition_a}",
                    Text = "Low priority text",
                    Priority = 5
                },
                new DialogueOverride
                {
                    Condition = "${condition_b}",
                    Text = "High priority text",
                    Priority = 10
                }
            ]
        };
        _loaderMock
            .Setup(l => l.LoadAsync("dialogue/test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalFile);

        // Both conditions match
        var contextMock = new Mock<IDialogueExpressionContext>();
        contextMock.Setup(c => c.EvaluateCondition(It.IsAny<string>())).Returns(true);
        contextMock.Setup(c => c.EvaluateTemplate(It.IsAny<string>())).Returns<string>(s => s);

        // Act
        var result = await _resolver.ResolveAsync(reference, _englishLocale, contextMock.Object);

        // Assert - Overrides are sorted by priority (highest first)
        Assert.Equal("High priority text", result.Text);
        Assert.Equal(DialogueSource.Override, result.Source);
    }

    [Fact]
    public async Task ResolveAsync_WithLocaleRestrictedOverride_OnlyMatchesCorrectLocale()
    {
        // Arrange
        var reference = DialogueReference.WithExternal("Default text", "dialogue/test");
        var externalFile = new ExternalDialogueFile
        {
            Reference = "dialogue/test",
            FilePath = "/test/dialogue/test.yaml",
            Localizations = new Dictionary<string, string>
            {
                ["en"] = "English default",
                ["es"] = "Spanish default"
            },
            Overrides =
            [
                new DialogueOverride
                {
                    Condition = "${always_true}",
                    Text = "Spanish override only",
                    Priority = 10,
                    Locale = "es"
                }
            ]
        };
        _loaderMock
            .Setup(l => l.LoadAsync("dialogue/test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalFile);

        var contextMock = new Mock<IDialogueExpressionContext>();
        contextMock.Setup(c => c.EvaluateCondition(It.IsAny<string>())).Returns(true);
        contextMock.Setup(c => c.EvaluateTemplate(It.IsAny<string>())).Returns<string>(s => s);

        // Act - English locale should NOT match Spanish-only override
        var result = await _resolver.ResolveAsync(reference, _englishLocale, contextMock.Object);

        // Assert
        Assert.Equal("English default", result.Text);
        Assert.Equal(DialogueSource.Localization, result.Source);
    }

    // =========================================================================
    // TEMPLATE EVALUATION TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_EvaluatesTemplateVariables()
    {
        // Arrange
        var reference = DialogueReference.Inline("Hello, {{ player.name }}!");
        var contextMock = new Mock<IDialogueExpressionContext>();
        contextMock
            .Setup(c => c.EvaluateTemplate("Hello, {{ player.name }}!"))
            .Returns("Hello, Alice!");

        // Act
        var result = await _resolver.ResolveAsync(reference, _englishLocale, contextMock.Object);

        // Assert
        Assert.Equal("Hello, Alice!", result.Text);
    }

    // =========================================================================
    // OPTIONS RESOLUTION TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveOptionsAsync_ResolvesMultipleOptions()
    {
        // Arrange
        var options = new List<DialogueOptionReference>
        {
            new()
            {
                Value = "option_a",
                InlineLabel = "Option A"
            },
            new()
            {
                Value = "option_b",
                InlineLabel = "Option B",
                IsDefault = true
            }
        };

        // Act
        var results = await _resolver.ResolveOptionsAsync(options, _englishLocale, _nullContext);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("option_a", results[0].Value);
        Assert.Equal("Option A", results[0].Label);
        Assert.False(results[0].IsDefault);
        Assert.Equal("option_b", results[1].Value);
        Assert.Equal("Option B", results[1].Label);
        Assert.True(results[1].IsDefault);
    }

    [Fact]
    public async Task ResolveOptionsAsync_EvaluatesConditions()
    {
        // Arrange
        var options = new List<DialogueOptionReference>
        {
            new()
            {
                Value = "option_a",
                InlineLabel = "Option A",
                Condition = "${has_permission}"
            }
        };

        var contextMock = new Mock<IDialogueExpressionContext>();
        contextMock.Setup(c => c.EvaluateCondition("${has_permission}")).Returns(false);
        contextMock.Setup(c => c.EvaluateTemplate(It.IsAny<string>())).Returns<string>(s => s);

        // Act
        var results = await _resolver.ResolveOptionsAsync(options, _englishLocale, contextMock.Object);

        // Assert
        Assert.Single(results);
        Assert.False(results[0].IsAvailable);
    }

    // =========================================================================
    // SPEAKER AND METADATA TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveAsync_PreservesSpeakerAndEmotion()
    {
        // Arrange
        var reference = new DialogueReference
        {
            InlineText = "Hello!",
            Speaker = "merchant_01",
            Emotion = "happy"
        };

        // Act
        var result = await _resolver.ResolveAsync(reference, _englishLocale, _nullContext);

        // Assert
        Assert.Equal("merchant_01", result.Speaker);
        Assert.Equal("happy", result.Emotion);
    }
}
