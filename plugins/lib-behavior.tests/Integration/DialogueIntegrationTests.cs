// =============================================================================
// Dialogue Integration Tests
// Tests end-to-end dialogue resolution flows with external files, localization,
// overrides, and expression context integration.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Dialogue;
using BeyondImmersion.BannouService.Behavior;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Integration;

/// <summary>
/// Integration tests for the dialogue resolution system covering the three-step
/// resolution pipeline, localization, overrides, and expression context.
/// </summary>
public sealed class DialogueIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ExternalDialogueLoader _loader;
    private readonly DialogueResolver _resolver;

    public DialogueIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dialogue_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _loader = new ExternalDialogueLoader(new ExternalDialogueLoaderOptions
        {
            EnableCaching = true,
            LogFileLoads = false
        });
        _loader.RegisterDirectory(_tempDir, priority: 0);

        _resolver = new DialogueResolver(_loader);
    }

    public void Dispose()
    {
        _loader.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // =========================================================================
    // INLINE RESOLUTION TESTS
    // =========================================================================

    [Fact]
    public async Task Resolve_InlineOnly_ReturnsInlineText()
    {
        // Arrange
        var reference = DialogueReference.Inline("Welcome to my shop!");

        // Act
        var result = await _resolver.ResolveAsync(
            reference,
            LocalizationContext.English,
            NullDialogueExpressionContext.Instance);

        // Assert
        Assert.Equal("Welcome to my shop!", result.Text);
        Assert.Equal(DialogueSource.Inline, result.Source);
        Assert.Null(result.MatchedCondition);
    }

    [Fact]
    public async Task Resolve_InlineWithSpeakerAndEmotion_PreservesMetadata()
    {
        // Arrange
        var reference = new DialogueReference
        {
            InlineText = "I'm feeling great!",
            Speaker = "npc_merchant",
            Emotion = "happy"
        };

        // Act
        var result = await _resolver.ResolveAsync(
            reference,
            LocalizationContext.English,
            NullDialogueExpressionContext.Instance);

        // Assert
        Assert.Equal("I'm feeling great!", result.Text);
        Assert.Equal("npc_merchant", result.Speaker);
        Assert.Equal("happy", result.Emotion);
    }

    // =========================================================================
    // LOCALIZATION TESTS
    // =========================================================================

    [Fact]
    public async Task Resolve_WithLocalization_ReturnsLocalizedText()
    {
        // Arrange
        CreateDialogueFile("greet", IntegrationTestFixtures.Load("dialogue_greet_localized"));

        var reference = DialogueReference.WithExternal("Hello!", "greet");

        // Act - Request Japanese
        var result = await _resolver.ResolveAsync(
            reference,
            LocalizationContext.ForLocale("ja"),
            NullDialogueExpressionContext.Instance);

        // Assert
        Assert.Equal("こんにちは！", result.Text);
        Assert.Equal(DialogueSource.Localization, result.Source);
        Assert.Equal("ja", result.ResolvedLocale);
    }

    [Fact]
    public async Task Resolve_LocaleFallback_FallsToParentLocale()
    {
        // Arrange
        CreateDialogueFile("greet", IntegrationTestFixtures.Load("dialogue_greet_en_es"));

        var reference = DialogueReference.WithExternal("Default greeting", "greet");

        // Act - Request en-US which falls back to en
        var result = await _resolver.ResolveAsync(
            reference,
            LocalizationContext.ForLocale("en-US"),
            NullDialogueExpressionContext.Instance);

        // Assert
        Assert.Equal("Hello!", result.Text);
        Assert.Equal(DialogueSource.Localization, result.Source);
        Assert.Equal("en", result.ResolvedLocale);
    }

    [Fact]
    public async Task Resolve_NoMatchingLocale_FallsToInline()
    {
        // Arrange
        CreateDialogueFile("greet", IntegrationTestFixtures.Load("dialogue_greet_es_only"));

        var reference = DialogueReference.WithExternal("Hello (inline)!", "greet");

        // Act - Request French (not available)
        var result = await _resolver.ResolveAsync(
            reference,
            LocalizationContext.ForLocale("fr"),
            NullDialogueExpressionContext.Instance);

        // Assert
        Assert.Equal("Hello (inline)!", result.Text);
        Assert.Equal(DialogueSource.Inline, result.Source);
    }

    // =========================================================================
    // OVERRIDE TESTS
    // =========================================================================

    [Fact]
    public async Task Resolve_WithMatchingOverride_ReturnsOverrideText()
    {
        // Arrange
        CreateDialogueFile("greet", IntegrationTestFixtures.Load("dialogue_override_basic"));

        var reference = DialogueReference.WithExternal("Inline greeting", "greet");
        var context = new TestExpressionContext(conditionResult: true);

        // Act
        var result = await _resolver.ResolveAsync(
            reference,
            LocalizationContext.English,
            context);

        // Assert
        Assert.Equal("Special greeting!", result.Text);
        Assert.Equal(DialogueSource.Override, result.Source);
        Assert.Equal("true", result.MatchedCondition);
    }

    [Fact]
    public async Task Resolve_OverridePriority_HighestPriorityWins()
    {
        // Arrange
        CreateDialogueFile("greet", IntegrationTestFixtures.Load("dialogue_override_multiple"));

        var reference = DialogueReference.WithExternal("Inline", "greet");
        var context = new TestExpressionContext(conditionResult: true); // All conditions match

        // Act
        var result = await _resolver.ResolveAsync(
            reference,
            LocalizationContext.English,
            context);

        // Assert
        Assert.Equal("High priority", result.Text);
        Assert.Equal("high", result.MatchedCondition);
    }

    [Fact]
    public async Task Resolve_OverrideConditionFalse_SkipsToNextStep()
    {
        // Arrange
        CreateDialogueFile("greet", IntegrationTestFixtures.Load("dialogue_override_condition_false"));

        var reference = DialogueReference.WithExternal("Inline", "greet");
        var context = new TestExpressionContext(conditionResult: false);

        // Act
        var result = await _resolver.ResolveAsync(
            reference,
            LocalizationContext.English,
            context);

        // Assert
        Assert.Equal("Localized text", result.Text);
        Assert.Equal(DialogueSource.Localization, result.Source);
    }

    // =========================================================================
    // TEMPLATE EVALUATION TESTS
    // =========================================================================

    [Fact]
    public async Task Resolve_TemplateVariables_EvaluatesInText()
    {
        // Arrange
        var reference = DialogueReference.Inline("Hello, {{ player.name }}!");
        var context = new TestExpressionContext(
            conditionResult: false,
            templateEvaluator: text => text.Replace("{{ player.name }}", "Adventurer"));

        // Act
        var result = await _resolver.ResolveAsync(
            reference,
            LocalizationContext.English,
            context);

        // Assert
        Assert.Equal("Hello, Adventurer!", result.Text);
    }

    // =========================================================================
    // OPTION RESOLUTION TESTS
    // =========================================================================

    [Fact]
    public async Task ResolveOptions_BasicOptions_ResolvesAllLabels()
    {
        // Arrange
        var options = new List<DialogueOptionReference>
        {
            new() { Value = "accept", InlineLabel = "I'll help you!" },
            new() { Value = "decline", InlineLabel = "Sorry, I'm busy." },
            new() { Value = "question", InlineLabel = "Tell me more." }
        };

        // Act
        var result = await _resolver.ResolveOptionsAsync(
            options,
            LocalizationContext.English,
            NullDialogueExpressionContext.Instance);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("accept", result[0].Value);
        Assert.Equal("I'll help you!", result[0].Label);
        Assert.True(result[0].IsAvailable);
    }

    [Fact]
    public async Task ResolveOptions_WithConditions_FiltersAvailability()
    {
        // Arrange
        var options = new List<DialogueOptionReference>
        {
            new() { Value = "normal", InlineLabel = "Normal option" },
            new() { Value = "locked", InlineLabel = "Locked option", Condition = "player.level >= 10" }
        };

        var context = new TestExpressionContext(conditionResult: false);

        // Act
        var result = await _resolver.ResolveOptionsAsync(
            options,
            LocalizationContext.English,
            context);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.True(result[0].IsAvailable);
        Assert.False(result[1].IsAvailable);
    }

    [Fact]
    public async Task ResolveOptions_DefaultOption_MarkedCorrectly()
    {
        // Arrange
        var options = new List<DialogueOptionReference>
        {
            new() { Value = "opt1", InlineLabel = "Option 1" },
            new() { Value = "opt2", InlineLabel = "Option 2", IsDefault = true },
            new() { Value = "opt3", InlineLabel = "Option 3" }
        };

        // Act
        var result = await _resolver.ResolveOptionsAsync(
            options,
            LocalizationContext.English,
            NullDialogueExpressionContext.Instance);

        // Assert
        Assert.False(result[0].IsDefault);
        Assert.True(result[1].IsDefault);
        Assert.False(result[2].IsDefault);
    }

    // =========================================================================
    // EXTERNAL FILE LOADING TESTS
    // =========================================================================

    [Fact]
    public async Task Resolve_MissingExternalFile_FallsToInline()
    {
        // Arrange
        var reference = DialogueReference.WithExternal(
            "Inline default",
            "nonexistent/dialogue");

        // Act
        var result = await _resolver.ResolveAsync(
            reference,
            LocalizationContext.English,
            NullDialogueExpressionContext.Instance);

        // Assert
        Assert.Equal("Inline default", result.Text);
        Assert.Equal(DialogueSource.Inline, result.Source);
    }

    [Fact]
    public async Task Loader_DirectoryPriority_HigherPriorityWins()
    {
        // Arrange
        var lowPriorityDir = Path.Combine(_tempDir, "low");
        var highPriorityDir = Path.Combine(_tempDir, "high");
        Directory.CreateDirectory(lowPriorityDir);
        Directory.CreateDirectory(highPriorityDir);

        File.WriteAllText(
            Path.Combine(lowPriorityDir, "test.yaml"),
            IntegrationTestFixtures.Load("dialogue_dir_low_priority"));
        File.WriteAllText(
            Path.Combine(highPriorityDir, "test.yaml"),
            IntegrationTestFixtures.Load("dialogue_dir_high_priority"));

        _loader.RegisterDirectory(lowPriorityDir, priority: 10);
        _loader.RegisterDirectory(highPriorityDir, priority: 100);

        var reference = DialogueReference.WithExternal("Inline", "test");

        // Act
        var result = await _resolver.ResolveAsync(
            reference,
            LocalizationContext.English,
            NullDialogueExpressionContext.Instance);

        // Assert
        Assert.Equal("High priority content", result.Text);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private void CreateDialogueFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name + ".yaml");
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// Test implementation of expression context for testing.
    /// </summary>
    private sealed class TestExpressionContext : IDialogueExpressionContext
    {
        private readonly bool _conditionResult;
        private readonly Func<string, string>? _templateEvaluator;

        public TestExpressionContext(
            bool conditionResult,
            Func<string, string>? templateEvaluator = null)
        {
            _conditionResult = conditionResult;
            _templateEvaluator = templateEvaluator;
        }

        public bool EvaluateCondition(string condition) => _conditionResult;

        public string EvaluateTemplate(string text) =>
            _templateEvaluator?.Invoke(text) ?? text;

        public object? GetVariable(string name) => null;
    }
}
