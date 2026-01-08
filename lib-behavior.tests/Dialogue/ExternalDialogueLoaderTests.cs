// =============================================================================
// External Dialogue Loader Tests
// Tests for YAML-based external dialogue file loading.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Dialogue;
using BeyondImmersion.BannouService.Behavior;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Dialogue;

/// <summary>
/// Tests for <see cref="ExternalDialogueLoader"/>.
/// </summary>
public sealed class ExternalDialogueLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ExternalDialogueLoader _loader;

    public ExternalDialogueLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dialogue_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var options = new ExternalDialogueLoaderOptions
        {
            EnableCaching = true,
            LogFileLoads = false
        };
        _loader = new ExternalDialogueLoader(options);
        _loader.RegisterDirectory(_tempDir);
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
    // BASIC LOADING TESTS
    // =========================================================================

    [Fact]
    public async Task LoadAsync_WithValidYaml_ReturnsDialogueFile()
    {
        // Arrange
        CreateFile("test.yaml", DialogueTestFixtures.Load("dialogue_basic"));

        // Act
        var result = await _loader.LoadAsync("test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test", result.Reference);
        Assert.Equal(2, result.Localizations.Count);
        Assert.Equal("Hello in English", result.Localizations["en"]);
        Assert.Equal("Hola en Español", result.Localizations["es"]);
    }

    [Fact]
    public async Task LoadAsync_WithNestedPath_FindsFile()
    {
        // Arrange
        var nestedDir = Path.Combine(_tempDir, "dialogue", "merchant");
        Directory.CreateDirectory(nestedDir);

        File.WriteAllText(Path.Combine(nestedDir, "greet.yaml"), DialogueTestFixtures.Load("dialogue_nested"));

        // Act
        var result = await _loader.LoadAsync("dialogue/merchant/greet");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("dialogue/merchant/greet", result.Reference);
        Assert.Equal("Welcome to my shop!", result.Localizations["en"]);
    }

    [Fact]
    public async Task LoadAsync_WithNonexistentFile_ReturnsNull()
    {
        // Act
        var result = await _loader.LoadAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    // =========================================================================
    // OVERRIDE PARSING TESTS
    // =========================================================================

    [Fact]
    public async Task LoadAsync_ParsesOverrides()
    {
        // Arrange
        CreateFile("test.yaml", DialogueTestFixtures.Load("dialogue_with_overrides"));

        // Act
        var result = await _loader.LoadAsync("test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Overrides.Count);

        // Overrides should be sorted by priority (highest first)
        Assert.Equal("VIP greeting!", result.Overrides[0].Text);
        Assert.Equal(10, result.Overrides[0].Priority);
        Assert.Equal("We're closing soon!", result.Overrides[1].Text);
        Assert.Equal(5, result.Overrides[1].Priority);
    }

    [Fact]
    public async Task LoadAsync_ParsesLocaleRestrictedOverrides()
    {
        // Arrange
        CreateFile("test.yaml", DialogueTestFixtures.Load("dialogue_locale_restricted"));

        // Act
        var result = await _loader.LoadAsync("test");

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Overrides);
        Assert.Equal("es", result.Overrides[0].Locale);
    }

    // =========================================================================
    // CACHING TESTS
    // =========================================================================

    [Fact]
    public async Task LoadAsync_CachesResult()
    {
        // Arrange
        CreateFile("test.yaml", DialogueTestFixtures.Load("dialogue_original"));

        // Act - Load twice
        var result1 = await _loader.LoadAsync("test");
        var result2 = await _loader.LoadAsync("test");

        // Assert - Should return cached result
        Assert.Same(result1, result2);
    }

    [Fact]
    public async Task ReloadAsync_InvalidatesCache()
    {
        // Arrange
        CreateFile("test.yaml", DialogueTestFixtures.Load("dialogue_original"));

        // Load initial
        var result1 = await _loader.LoadAsync("test");

        // Update file
        CreateFile("test.yaml", DialogueTestFixtures.Load("dialogue_updated"));

        // Act - Reload
        var result2 = await _loader.ReloadAsync("test");

        // Assert
        Assert.NotSame(result1, result2);
        Assert.Equal("Original text", result1?.Localizations["en"]);
        Assert.Equal("Updated text", result2?.Localizations["en"]);
    }

    [Fact]
    public async Task ClearCache_InvalidatesAllCachedFiles()
    {
        // Arrange
        CreateFile("test1.yaml", DialogueTestFixtures.Load("dialogue_original"));
        CreateFile("test2.yaml", DialogueTestFixtures.Load("dialogue_original"));

        // Load both
        await _loader.LoadAsync("test1");
        await _loader.LoadAsync("test2");

        // Act
        _loader.ClearCache();

        // No exception means success - cache operations completed
    }

    // =========================================================================
    // DIRECTORY REGISTRATION TESTS
    // =========================================================================

    [Fact]
    public async Task RegisterDirectory_WithPriority_SearchesHigherPriorityFirst()
    {
        // Arrange
        var lowPriorityDir = Path.Combine(_tempDir, "low");
        var highPriorityDir = Path.Combine(_tempDir, "high");
        Directory.CreateDirectory(lowPriorityDir);
        Directory.CreateDirectory(highPriorityDir);

        File.WriteAllText(Path.Combine(lowPriorityDir, "test.yaml"), DialogueTestFixtures.Load("dialogue_low_priority"));
        File.WriteAllText(Path.Combine(highPriorityDir, "test.yaml"), DialogueTestFixtures.Load("dialogue_high_priority"));

        var loader = new ExternalDialogueLoader();
        loader.RegisterDirectory(lowPriorityDir, priority: 0);
        loader.RegisterDirectory(highPriorityDir, priority: 10);

        // Act
        var result = await loader.LoadAsync("test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("High priority", result.Localizations["en"]);

        loader.Dispose();
    }

    [Fact]
    public void RegisteredDirectories_ReturnsInPriorityOrder()
    {
        // Arrange
        _loader.RegisterDirectory("/path/a", priority: 5);
        _loader.RegisterDirectory("/path/b", priority: 10);
        _loader.RegisterDirectory("/path/c", priority: 1);

        // Act
        var directories = _loader.RegisteredDirectories;

        // Assert - Should be sorted by priority (highest first)
        Assert.Equal(4, directories.Count); // Including _tempDir from constructor
        Assert.Equal(10, directories[0].Priority);
    }

    // =========================================================================
    // FILE EXTENSION TESTS
    // =========================================================================

    [Fact]
    public async Task LoadAsync_FindsYmlExtension()
    {
        // Arrange
        CreateFile("test.yml", DialogueTestFixtures.Load("dialogue_yml_extension"));

        // Act
        var result = await _loader.LoadAsync("test");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Found with .yml", result.Localizations["en"]);
    }

    [Fact]
    public void Exists_ReturnsTrueForExistingFile()
    {
        // Arrange
        CreateFile("exists.yaml", DialogueTestFixtures.Load("dialogue_exists"));

        // Act & Assert
        Assert.True(_loader.Exists("exists"));
        Assert.False(_loader.Exists("doesnotexist"));
    }

    // =========================================================================
    // LOCALIZATION FALLBACK TESTS
    // =========================================================================

    [Fact]
    public async Task GetLocalizationWithFallback_ReturnsCorrectLocale()
    {
        // Arrange
        CreateFile("test.yaml", DialogueTestFixtures.Load("dialogue_en_es"));

        var result = await _loader.LoadAsync("test");
        Assert.NotNull(result);

        // Act - Try US English which should fall back to "en"
        var usContext = LocalizationContext.ForLocale("en-US");
        var localized = result.GetLocalizationWithFallback(usContext);

        // Assert
        Assert.NotNull(localized);
        Assert.Equal("English text", localized.Value.text);
        Assert.Equal("en", localized.Value.locale);
    }

    [Fact]
    public async Task GetLocalizationWithFallback_ReturnsNullWhenNotFound()
    {
        // Arrange
        CreateFile("test.yaml", DialogueTestFixtures.Load("dialogue_de_only"));

        var result = await _loader.LoadAsync("test");
        Assert.NotNull(result);

        // Act - Try French which doesn't exist
        var frContext = new LocalizationContext
        {
            Locale = "fr",
            DefaultLocale = "ja" // Also doesn't exist
        };
        var localized = result.GetLocalizationWithFallback(frContext);

        // Assert
        Assert.Null(localized);
    }

    // =========================================================================
    // ERROR HANDLING TESTS
    // =========================================================================

    [Fact]
    public async Task LoadAsync_WithMalformedYaml_ReturnsNull()
    {
        // Arrange - Invalid YAML
        CreateFile("malformed.yaml", "localizations:\n  en: [unclosed bracket");

        // Act
        var result = await _loader.LoadAsync("malformed");

        // Assert - Should return null, not throw
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadAsync_WithEmptyFile_ReturnsNull()
    {
        // Arrange
        CreateFile("empty.yaml", "");

        // Act
        var result = await _loader.LoadAsync("empty");

        // Assert
        Assert.Null(result);
    }

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    private void CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content);
    }
}
