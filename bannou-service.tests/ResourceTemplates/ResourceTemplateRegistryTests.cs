using BeyondImmersion.Bannou.BehaviorCompiler.Templates;
using BeyondImmersion.BannouService.ResourceTemplates;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Tests.ResourceTemplates;

/// <summary>
/// Tests for the ResourceTemplateRegistry singleton.
/// </summary>
public class ResourceTemplateRegistryTests
{
    private readonly IResourceTemplateRegistry _registry;

    public ResourceTemplateRegistryTests()
    {
        var logger = new Mock<ILogger<ResourceTemplateRegistry>>();
        _registry = new ResourceTemplateRegistry(logger.Object);
    }

    [Fact]
    public void Register_ValidTemplate_CanRetrieveBySourceType()
    {
        // Arrange
        var template = new TestResourceTemplate("test-source", "test");

        // Act
        _registry.Register(template);

        // Assert
        var retrieved = _registry.GetBySourceType("test-source");
        Assert.NotNull(retrieved);
        Assert.Equal("test-source", retrieved.SourceType);
        Assert.Equal("test", retrieved.Namespace);
    }

    [Fact]
    public void Register_ValidTemplate_CanRetrieveByNamespace()
    {
        // Arrange
        var template = new TestResourceTemplate("character-personality", "personality");

        // Act
        _registry.Register(template);

        // Assert
        var retrieved = _registry.GetByNamespace("personality");
        Assert.NotNull(retrieved);
        Assert.Equal("character-personality", retrieved.SourceType);
    }

    [Fact]
    public void Register_DuplicateSourceType_ThrowsException()
    {
        // Arrange
        var template1 = new TestResourceTemplate("test-source", "test1");
        var template2 = new TestResourceTemplate("test-source", "test2");

        _registry.Register(template1);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _registry.Register(template2));
        Assert.Contains("test-source", ex.Message);
        Assert.Contains("already registered", ex.Message);
    }

    [Fact]
    public void Register_DuplicateNamespace_ThrowsExceptionAndRollsBack()
    {
        // Arrange
        var template1 = new TestResourceTemplate("source1", "shared-namespace");
        var template2 = new TestResourceTemplate("source2", "shared-namespace");

        _registry.Register(template1);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => _registry.Register(template2));
        Assert.Contains("shared-namespace", ex.Message);
        Assert.Contains("conflicts", ex.Message);

        // Verify rollback: source2 should not be registered
        Assert.Null(_registry.GetBySourceType("source2"));
    }

    [Fact]
    public void Register_NullTemplate_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _registry.Register(null!));
    }

    [Fact]
    public void GetBySourceType_UnknownSourceType_ReturnsNull()
    {
        // Act
        var result = _registry.GetBySourceType("unknown");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetByNamespace_UnknownNamespace_ReturnsNull()
    {
        // Act
        var result = _registry.GetByNamespace("unknown");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void HasTemplate_RegisteredTemplate_ReturnsTrue()
    {
        // Arrange
        var template = new TestResourceTemplate("test-source", "test");
        _registry.Register(template);

        // Act & Assert
        Assert.True(_registry.HasTemplate("test-source"));
    }

    [Fact]
    public void HasTemplate_UnknownTemplate_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(_registry.HasTemplate("unknown"));
    }

    [Fact]
    public void GetAllTemplates_ReturnsAllRegistered()
    {
        // Arrange
        var template1 = new TestResourceTemplate("source1", "ns1");
        var template2 = new TestResourceTemplate("source2", "ns2");
        _registry.Register(template1);
        _registry.Register(template2);

        // Act
        var all = _registry.GetAllTemplates().ToList();

        // Assert
        Assert.Equal(2, all.Count);
        Assert.Contains(all, t => t.SourceType == "source1");
        Assert.Contains(all, t => t.SourceType == "source2");
    }

    [Fact]
    public void GetBySourceType_CaseInsensitive()
    {
        // Arrange
        var template = new TestResourceTemplate("Test-Source", "test");
        _registry.Register(template);

        // Act
        var retrieved = _registry.GetBySourceType("test-source");

        // Assert
        Assert.NotNull(retrieved);
    }

    [Fact]
    public void GetByNamespace_CaseInsensitive()
    {
        // Arrange
        var template = new TestResourceTemplate("test-source", "Test");
        _registry.Register(template);

        // Act
        var retrieved = _registry.GetByNamespace("test");

        // Assert
        Assert.NotNull(retrieved);
    }

    /// <summary>
    /// Simple test template for registry tests.
    /// </summary>
    private sealed class TestResourceTemplate : ResourceTemplateBase
    {
        public override string SourceType { get; }
        public override string Namespace { get; }
        public override IReadOnlyDictionary<string, Type> ValidPaths { get; }

        public TestResourceTemplate(string sourceType, string ns)
        {
            SourceType = sourceType;
            Namespace = ns;
            ValidPaths = new Dictionary<string, Type>
            {
                [""] = typeof(object),
                ["id"] = typeof(Guid),
                ["name"] = typeof(string),
            };
        }
    }
}
