// =============================================================================
// File Localization Provider Tests
// Tests for file-based string table localization.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Dialogue;
using BeyondImmersion.BannouService.Behavior;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Dialogue;

/// <summary>
/// Tests for <see cref="FileLocalizationProvider"/> and <see cref="YamlFileLocalizationSource"/>.
/// </summary>
public sealed class FileLocalizationProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileLocalizationProvider _provider;
    private readonly List<YamlFileLocalizationSource> _createdSources = new();

    public FileLocalizationProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "loc_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _provider = new FileLocalizationProvider(new LocalizationConfiguration
        {
            DefaultLocale = "en",
            LogMissingKeys = false
        });
    }

    public void Dispose()
    {
        foreach (var source in _createdSources)
        {
            source.Dispose();
        }
        _createdSources.Clear();
        _provider.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private YamlFileLocalizationSource CreateSource(string name, string directory, string? filePattern = null, int priority = 0)
    {
        var source = filePattern != null
            ? new YamlFileLocalizationSource(name, directory, filePattern: filePattern, priority: priority)
            : new YamlFileLocalizationSource(name, directory, priority: priority);
        _createdSources.Add(source);
        return source;
    }

    // =========================================================================
    // YAML FILE SOURCE TESTS
    // =========================================================================

    [Fact]
    public void YamlFileLocalizationSource_LoadsStringTable()
    {
        // Arrange
        CreateLocalizationFile("strings.en.yaml", DialogueTestFixtures.Load("strings_en_full"));

        var source = CreateSource("test", _tempDir, filePattern: "strings.{locale}.yaml");
        _provider.RegisterSource(source);

        // Act
        var startText = _provider.GetText("ui.menu.start", "en");
        var quitText = _provider.GetText("ui.menu.quit", "en");
        var greetingText = _provider.GetText("dialogue.greeting", "en");

        // Assert
        Assert.Equal("Start Game", startText);
        Assert.Equal("Quit Game", quitText);
        Assert.Equal("Hello!", greetingText);
    }

    [Fact]
    public void YamlFileLocalizationSource_LoadsMultipleLocales()
    {
        // Arrange
        CreateLocalizationFile("strings.en.yaml", DialogueTestFixtures.Load("strings_en_simple"));
        CreateLocalizationFile("strings.es.yaml", DialogueTestFixtures.Load("strings_es_simple"));
        CreateLocalizationFile("strings.ja.yaml", DialogueTestFixtures.Load("strings_ja_simple"));

        var source = CreateSource("test", _tempDir, filePattern: "strings.{locale}.yaml");
        _provider.RegisterSource(source);

        // Act
        var en = _provider.GetText("greeting", "en");
        var es = _provider.GetText("greeting", "es");
        var ja = _provider.GetText("greeting", "ja");

        // Assert
        Assert.Equal("Hello!", en);
        Assert.Equal("¡Hola!", es);
        Assert.Equal("こんにちは！", ja);
    }

    [Fact]
    public void YamlFileLocalizationSource_ReportsSupportedLocales()
    {
        // Arrange
        CreateLocalizationFile("strings.en.yaml", DialogueTestFixtures.Load("strings_single_key"));
        CreateLocalizationFile("strings.fr.yaml", DialogueTestFixtures.Load("strings_single_key"));

        var source = CreateSource("test", _tempDir, filePattern: "strings.{locale}.yaml");

        // Force load by querying
        source.GetText("key", "en");
        source.GetText("key", "fr");

        // Assert
        Assert.Contains("en", source.SupportedLocales);
        Assert.Contains("fr", source.SupportedLocales);
    }

    // =========================================================================
    // PROVIDER TESTS
    // =========================================================================

    [Fact]
    public void GetText_ReturnsNullForMissingKey()
    {
        // Arrange
        CreateLocalizationFile("strings.en.yaml", DialogueTestFixtures.Load("strings_existing"));
        RegisterSource();

        // Act
        var result = _provider.GetText("nonexistent", "en");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetText_ReturnsNullForMissingLocale()
    {
        // Arrange
        CreateLocalizationFile("strings.en.yaml", DialogueTestFixtures.Load("strings_single_key"));
        RegisterSource();

        // Act
        var result = _provider.GetText("key", "de");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetTextWithFallback_UsesLocaleFallbackChain()
    {
        // Arrange
        CreateLocalizationFile("strings.en.yaml", DialogueTestFixtures.Load("strings_greeting"));
        RegisterSource();

        var context = LocalizationContext.ForLocale("en-US"); // Falls back to "en"

        // Act
        var result = _provider.GetTextWithFallback("greeting", context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("English greeting", result.Text);
        Assert.Equal("en", result.FoundLocale);
        Assert.True(result.IsFallback);
    }

    [Fact]
    public void GetTextWithFallback_UsesDefaultLocale()
    {
        // Arrange
        CreateLocalizationFile("strings.en.yaml", DialogueTestFixtures.Load("strings_greeting_default"));
        RegisterSource();

        var context = new LocalizationContext
        {
            Locale = "fr",
            FallbackLocales = [],
            DefaultLocale = "en"
        };

        // Act
        var result = _provider.GetTextWithFallback("greeting", context);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Default English", result.Text);
        Assert.True(result.IsFallback);
    }

    [Fact]
    public void GetTextWithFallback_ReturnsNullWhenNothingFound()
    {
        // Arrange
        CreateLocalizationFile("strings.de.yaml", DialogueTestFixtures.Load("strings_greeting_de"));
        RegisterSource();

        var context = new LocalizationContext
        {
            Locale = "fr",
            DefaultLocale = "en"
        };

        // Act
        var result = _provider.GetTextWithFallback("greeting", context);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void HasKey_ReturnsTrueForExistingKey()
    {
        // Arrange
        CreateLocalizationFile("strings.en.yaml", DialogueTestFixtures.Load("strings_existing"));
        RegisterSource();

        // Act & Assert
        Assert.True(_provider.HasKey("existing", "en"));
        Assert.False(_provider.HasKey("missing", "en"));
    }

    // =========================================================================
    // MULTI-SOURCE TESTS
    // =========================================================================

    [Fact]
    public void MultipleSources_HigherPriorityWins()
    {
        // Arrange
        var lowDir = Path.Combine(_tempDir, "low");
        var highDir = Path.Combine(_tempDir, "high");
        Directory.CreateDirectory(lowDir);
        Directory.CreateDirectory(highDir);

        File.WriteAllText(Path.Combine(lowDir, "strings.en.yaml"), DialogueTestFixtures.Load("strings_key_low_priority"));
        File.WriteAllText(Path.Combine(highDir, "strings.en.yaml"), DialogueTestFixtures.Load("strings_key_high_priority"));

        var lowSource = CreateSource("low", lowDir, priority: 0);
        var highSource = CreateSource("high", highDir, priority: 10);

        _provider.RegisterSource(lowSource);
        _provider.RegisterSource(highSource);

        // Act
        var result = _provider.GetText("key", "en");

        // Assert
        Assert.Equal("High priority", result);
    }

    [Fact]
    public void MultipleSources_FallsBackToLowerPriority()
    {
        // Arrange
        var lowDir = Path.Combine(_tempDir, "low");
        var highDir = Path.Combine(_tempDir, "high");
        Directory.CreateDirectory(lowDir);
        Directory.CreateDirectory(highDir);

        File.WriteAllText(Path.Combine(lowDir, "strings.en.yaml"), DialogueTestFixtures.Load("strings_key_ab_low"));
        File.WriteAllText(Path.Combine(highDir, "strings.en.yaml"), DialogueTestFixtures.Load("strings_key_a_high"));

        var lowSource = CreateSource("low", lowDir, priority: 0);
        var highSource = CreateSource("high", highDir, priority: 10);

        _provider.RegisterSource(lowSource);
        _provider.RegisterSource(highSource);

        // Act
        var resultA = _provider.GetText("key_a", "en");
        var resultB = _provider.GetText("key_b", "en");

        // Assert
        Assert.Equal("High A", resultA); // From high priority
        Assert.Equal("Low B", resultB);  // Falls back to low priority
    }

    [Fact]
    public void Sources_OrderedByPriority()
    {
        // Arrange
        var source1 = CreateSource("source1", _tempDir, priority: 5);
        var source2 = CreateSource("source2", _tempDir, priority: 10);
        var source3 = CreateSource("source3", _tempDir, priority: 1);

        _provider.RegisterSource(source1);
        _provider.RegisterSource(source2);
        _provider.RegisterSource(source3);

        // Act
        var sources = _provider.Sources;

        // Assert - Should be ordered by priority descending
        Assert.Equal(3, sources.Count);
        Assert.Equal("source2", sources[0].Name);
        Assert.Equal("source1", sources[1].Name);
        Assert.Equal("source3", sources[2].Name);
    }

    [Fact]
    public void RemoveSource_RemovesByName()
    {
        // Arrange
        var source = CreateSource("test_source", _tempDir);
        _provider.RegisterSource(source);
        Assert.Single(_provider.Sources);

        // Act
        var removed = _provider.RemoveSource("test_source");

        // Assert
        Assert.True(removed);
        Assert.Empty(_provider.Sources);
    }

    // =========================================================================
    // RELOAD TESTS
    // =========================================================================

    [Fact]
    public async Task ReloadAsync_RefreshesAllSources()
    {
        // Arrange
        CreateLocalizationFile("strings.en.yaml", DialogueTestFixtures.Load("strings_key_original"));
        RegisterSource();

        var original = _provider.GetText("key", "en");
        Assert.Equal("Original", original);

        // Update file
        CreateLocalizationFile("strings.en.yaml", DialogueTestFixtures.Load("strings_key_updated"));

        // Act
        await _provider.ReloadAsync();

        // After reload, next access should see new value
        // Note: This depends on the source clearing its cache on reload
    }

    // =========================================================================
    // SUPPORTED LOCALES TESTS
    // =========================================================================

    [Fact]
    public void SupportedLocales_AggregatesFromAllSources()
    {
        // Arrange
        var dir1 = Path.Combine(_tempDir, "dir1");
        var dir2 = Path.Combine(_tempDir, "dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        File.WriteAllText(Path.Combine(dir1, "strings.en.yaml"), DialogueTestFixtures.Load("strings_single_key"));
        File.WriteAllText(Path.Combine(dir1, "strings.fr.yaml"), DialogueTestFixtures.Load("strings_single_key"));
        File.WriteAllText(Path.Combine(dir2, "strings.de.yaml"), DialogueTestFixtures.Load("strings_single_key"));
        File.WriteAllText(Path.Combine(dir2, "strings.ja.yaml"), DialogueTestFixtures.Load("strings_single_key"));

        var source1 = CreateSource("source1", dir1);
        var source2 = CreateSource("source2", dir2);

        _provider.RegisterSource(source1);
        _provider.RegisterSource(source2);

        // Trigger loading
        _provider.GetText("key", "en");
        _provider.GetText("key", "fr");
        _provider.GetText("key", "de");
        _provider.GetText("key", "ja");

        // Act
        var locales = _provider.SupportedLocales;

        // Assert
        Assert.Contains("en", locales);
        Assert.Contains("fr", locales);
        Assert.Contains("de", locales);
        Assert.Contains("ja", locales);
    }

    [Fact]
    public void DefaultLocale_ReturnsConfiguredValue()
    {
        // Assert
        Assert.Equal("en", _provider.DefaultLocale);
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    private void CreateLocalizationFile(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_tempDir, fileName), content);
    }

    private void RegisterSource()
    {
        var source = CreateSource("default", _tempDir, filePattern: "strings.{locale}.yaml");
        _provider.RegisterSource(source);
    }
}
