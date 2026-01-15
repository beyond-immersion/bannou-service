// =============================================================================
// Cognition Template Registry Tests
// Tests for cognition template registration and loading.
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Cognition;
using BeyondImmersion.BannouService.Behavior;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Cognition;

/// <summary>
/// Tests for <see cref="CognitionTemplateRegistry"/>.
/// </summary>
public sealed class CognitionTemplateRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CognitionTemplateRegistry _registry;

    public CognitionTemplateRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cognition_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _registry = new CognitionTemplateRegistry(loadEmbeddedDefaults: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // =========================================================================
    // EMBEDDED DEFAULTS TESTS
    // =========================================================================

    [Fact]
    public void Constructor_LoadsEmbeddedDefaults()
    {
        // Assert - Default templates are loaded
        Assert.True(_registry.HasTemplate(CognitionTemplates.HumanoidBase));
        Assert.True(_registry.HasTemplate(CognitionTemplates.CreatureBase));
        Assert.True(_registry.HasTemplate(CognitionTemplates.ObjectBase));
    }

    [Fact]
    public void Constructor_WithoutDefaults_NoTemplatesLoaded()
    {
        // Arrange
        var registry = new CognitionTemplateRegistry(loadEmbeddedDefaults: false);

        // Assert
        Assert.False(registry.HasTemplate(CognitionTemplates.HumanoidBase));
        Assert.Empty(registry.GetTemplateIds());
    }

    [Fact]
    public void GetTemplate_HumanoidBase_ReturnsValidTemplate()
    {
        // Act
        var template = _registry.GetTemplate(CognitionTemplates.HumanoidBase);

        // Assert
        Assert.NotNull(template);
        Assert.Equal(CognitionTemplates.HumanoidBase, template.Id);
        Assert.Equal(5, template.Stages.Count); // All 5 cognition stages
        Assert.Contains(template.Stages, s => s.Name == CognitionStages.Filter);
        Assert.Contains(template.Stages, s => s.Name == CognitionStages.MemoryQuery);
        Assert.Contains(template.Stages, s => s.Name == CognitionStages.Significance);
        Assert.Contains(template.Stages, s => s.Name == CognitionStages.Storage);
        Assert.Contains(template.Stages, s => s.Name == CognitionStages.Intention);
    }

    [Fact]
    public void GetTemplate_CreatureBase_HasSimplifiedPipeline()
    {
        // Act
        var template = _registry.GetTemplate(CognitionTemplates.CreatureBase);

        // Assert
        Assert.NotNull(template);
        Assert.Equal(3, template.Stages.Count); // Fewer stages for creatures
        Assert.Contains(template.Stages, s => s.Name == CognitionStages.Filter);
        Assert.Contains(template.Stages, s => s.Name == CognitionStages.MemoryQuery);
        Assert.Contains(template.Stages, s => s.Name == CognitionStages.Intention);
        // Creatures skip significance and storage stages
    }

    [Fact]
    public void GetTemplate_ObjectBase_HasMinimalPipeline()
    {
        // Act
        var template = _registry.GetTemplate(CognitionTemplates.ObjectBase);

        // Assert
        Assert.NotNull(template);
        Assert.Equal(2, template.Stages.Count); // Minimal for objects
        Assert.Contains(template.Stages, s => s.Name == CognitionStages.Filter);
        Assert.Contains(template.Stages, s => s.Name == CognitionStages.Intention);
    }

    [Fact]
    public void GetTemplate_Unknown_ReturnsNull()
    {
        // Act
        var template = _registry.GetTemplate("unknown-template");

        // Assert
        Assert.Null(template);
    }

    [Fact]
    public void GetTemplateIds_ReturnsAllRegistered()
    {
        // Act
        var ids = _registry.GetTemplateIds();

        // Assert
        Assert.Contains(CognitionTemplates.HumanoidBase, ids);
        Assert.Contains(CognitionTemplates.CreatureBase, ids);
        Assert.Contains(CognitionTemplates.ObjectBase, ids);
        Assert.Equal(3, ids.Count);
    }

    // =========================================================================
    // REGISTRATION TESTS
    // =========================================================================

    [Fact]
    public void RegisterTemplate_AddsNewTemplate()
    {
        // Arrange
        var template = new CognitionTemplate
        {
            Id = "custom-template",
            Description = "Custom test template",
            Stages = []
        };

        // Act
        _registry.RegisterTemplate(template);

        // Assert
        Assert.True(_registry.HasTemplate("custom-template"));
        Assert.Same(template, _registry.GetTemplate("custom-template"));
    }

    [Fact]
    public void RegisterTemplate_OverridesExisting()
    {
        // Arrange
        var template1 = new CognitionTemplate { Id = "test", Stages = [] };
        var template2 = new CognitionTemplate { Id = "test", Description = "Updated", Stages = [] };

        // Act
        _registry.RegisterTemplate(template1);
        _registry.RegisterTemplate(template2);

        // Assert
        var retrieved = _registry.GetTemplate("test");
        Assert.Equal("Updated", retrieved?.Description);
    }

    [Fact]
    public void RegisterTemplate_EmptyId_Throws()
    {
        // Arrange
        var template = new CognitionTemplate { Id = "", Stages = [] };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _registry.RegisterTemplate(template));
    }

    // =========================================================================
    // YAML LOADING TESTS
    // =========================================================================

    [Fact]
    public void LoadFromDirectory_LoadsYamlFiles()
    {
        // Arrange
        File.WriteAllText(
            Path.Combine(_tempDir, "test.yaml"),
            CognitionTestFixtures.Load("template_test"));

        // Act
        var count = _registry.LoadFromDirectory(_tempDir);

        // Assert
        Assert.Equal(1, count);
        Assert.True(_registry.HasTemplate("test-template"));

        var template = _registry.GetTemplate("test-template");
        Assert.NotNull(template);
        Assert.Equal("Test template from YAML", template.Description);
        Assert.Single(template.Stages);
    }

    [Fact]
    public void LoadFromDirectory_LoadsYmlExtension()
    {
        // Arrange
        File.WriteAllText(
            Path.Combine(_tempDir, "test.yml"),
            CognitionTestFixtures.Load("template_yml_extension"));

        // Act
        var count = _registry.LoadFromDirectory(_tempDir);

        // Assert
        Assert.Equal(1, count);
        Assert.True(_registry.HasTemplate("yml-template"));
    }

    [Fact]
    public void LoadFromDirectory_Recursive_LoadsSubdirectories()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "subdirectory");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(
            Path.Combine(_tempDir, "root.yaml"),
            CognitionTestFixtures.Load("template_root"));
        File.WriteAllText(
            Path.Combine(subDir, "nested.yaml"),
            CognitionTestFixtures.Load("template_nested"));

        // Act
        var count = _registry.LoadFromDirectory(_tempDir, recursive: true);

        // Assert
        Assert.Equal(2, count);
        Assert.True(_registry.HasTemplate("root-template"));
        Assert.True(_registry.HasTemplate("nested-template"));
    }

    [Fact]
    public void LoadFromDirectory_NonRecursive_OnlyTopLevel()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "subdirectory");
        Directory.CreateDirectory(subDir);

        File.WriteAllText(
            Path.Combine(_tempDir, "root.yaml"),
            CognitionTestFixtures.Load("template_root"));
        File.WriteAllText(
            Path.Combine(subDir, "nested.yaml"),
            CognitionTestFixtures.Load("template_nested"));

        // Act
        var count = _registry.LoadFromDirectory(_tempDir, recursive: false);

        // Assert
        Assert.Equal(1, count);
        Assert.True(_registry.HasTemplate("root-template"));
        Assert.False(_registry.HasTemplate("nested-template"));
    }

    [Fact]
    public void LoadFromDirectory_InvalidYaml_SkipsAndContinues()
    {
        // Arrange
        File.WriteAllText(
            Path.Combine(_tempDir, "valid.yaml"),
            CognitionTestFixtures.Load("template_valid"));
        File.WriteAllText(
            Path.Combine(_tempDir, "invalid.yaml"),
            "this is not valid yaml: [unclosed bracket");

        // Act
        var count = _registry.LoadFromDirectory(_tempDir);

        // Assert
        Assert.Equal(1, count); // Only valid template loaded
        Assert.True(_registry.HasTemplate("valid-template"));
    }

    [Fact]
    public void LoadFromDirectory_NonexistentDirectory_ReturnsZero()
    {
        // Act
        var count = _registry.LoadFromDirectory("/nonexistent/path");

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public void ParseYaml_ValidTemplate_ReturnsTemplate()
    {
        // Arrange
        var yaml = CognitionTestFixtures.Load("template_parsed");

        // Act
        var template = _registry.ParseYaml(yaml);

        // Assert
        Assert.NotNull(template);
        Assert.Equal("parsed-template", template.Id);
        Assert.Equal("Parsed from string", template.Description);
        Assert.Equal("2.0", template.Version);
        Assert.Equal(2, template.Stages.Count);

        var filterStage = template.Stages.First(s => s.Name == "filter");
        Assert.Single(filterStage.Handlers);
        Assert.Equal("handler1", filterStage.Handlers[0].Id);
        Assert.Equal("filter_attention", filterStage.Handlers[0].HandlerName);
        Assert.Equal(50, Convert.ToInt32(filterStage.Handlers[0].Parameters["max"]));
    }

    [Fact]
    public void ParseYaml_MetadataFormat_ReturnsTemplate()
    {
        // Arrange - Alternative format with nested metadata
        var yaml = CognitionTestFixtures.Load("template_metadata");

        // Act
        var template = _registry.ParseYaml(yaml);

        // Assert
        Assert.NotNull(template);
        Assert.Equal("metadata-template", template.Id);
        Assert.Equal("Uses metadata format", template.Description);
    }

    [Fact]
    public void ParseYaml_EmptyString_ReturnsNull()
    {
        // Act
        var template = _registry.ParseYaml("");

        // Assert
        Assert.Null(template);
    }

    [Fact]
    public void ParseYaml_MissingId_ReturnsNull()
    {
        // Arrange
        var yaml = CognitionTestFixtures.Load("template_no_id");

        // Act
        var template = _registry.ParseYaml(yaml);

        // Assert
        Assert.Null(template);
    }

    // =========================================================================
    // HANDLER DEFINITION TESTS
    // =========================================================================

    [Fact]
    public void HumanoidBase_FilterStage_HasCorrectParameters()
    {
        // Arrange
        var template = _registry.GetTemplate(CognitionTemplates.HumanoidBase);
        Assert.NotNull(template);

        // Act
        var filterStage = template.Stages.First(s => s.Name == CognitionStages.Filter);
        var attentionHandler = filterStage.Handlers.First(h => h.Id == "attention_filter");

        // Assert
        Assert.Equal("filter_attention", attentionHandler.HandlerName);
        Assert.True(attentionHandler.Enabled);

        // Check default parameters
        Assert.Equal(100f, attentionHandler.Parameters["attention_budget"]);
        Assert.Equal(10, attentionHandler.Parameters["max_perceptions"]);

        var weights = (Dictionary<string, object>)attentionHandler.Parameters["priority_weights"];
        Assert.Equal(10.0f, weights["threat"]);
        Assert.Equal(5.0f, weights["novelty"]);
    }

    [Fact]
    public void HumanoidBase_IntentionStage_HasMultipleHandlers()
    {
        // Arrange
        var template = _registry.GetTemplate(CognitionTemplates.HumanoidBase);
        Assert.NotNull(template);

        // Act
        var intentionStage = template.Stages.First(s => s.Name == CognitionStages.Intention);

        // Assert
        Assert.Equal(2, intentionStage.Handlers.Count);
        Assert.Equal("goal_impact", intentionStage.Handlers[0].Id);
        Assert.Equal("goap_replan", intentionStage.Handlers[1].Id);
    }
}
