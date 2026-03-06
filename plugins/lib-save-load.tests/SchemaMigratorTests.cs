using BeyondImmersion.BannouService.SaveLoad.Migration;
using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.Services;
using System.Linq.Expressions;
using System.Text;

namespace BeyondImmersion.BannouService.SaveLoad.Tests;

/// <summary>
/// Tests for the SchemaMigrator class.
/// </summary>
public class SchemaMigratorTests
{
    private readonly Mock<ILogger<SchemaMigrator>> _loggerMock;
    private readonly Mock<IQueryableStateStore<SaveSchemaDefinition>> _schemaStoreMock;
    private readonly Mock<ITelemetryProvider> _telemetryProviderMock;

    public SchemaMigratorTests()
    {
        _loggerMock = new Mock<ILogger<SchemaMigrator>>();
        _schemaStoreMock = new Mock<IQueryableStateStore<SaveSchemaDefinition>>();
        _telemetryProviderMock = new Mock<ITelemetryProvider>();
    }

    #region Constructor Tests

    [Fact]
    public void SchemaMigrator_CanBeInstantiated()
    {
        // Arrange & Act
        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Assert
        Assert.NotNull(migrator);
    }

    #endregion

    #region FindMigrationPath Tests

    [Fact]
    public async Task FindMigrationPath_SameVersion_ReturnsSingleElementPath()
    {
        // Arrange
        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Act
        var path = await migrator.FindMigrationPathAsync("game", "v1.0", "v1.0", CancellationToken.None);

        // Assert
        Assert.NotNull(path);
        Assert.Single(path);
        Assert.Equal("v1.0", path[0]);
    }

    [Fact]
    public async Task FindMigrationPath_NoSchemasInNamespace_ReturnsNull()
    {
        // Arrange
        _schemaStoreMock.Setup(s => s.QueryAsync(
                It.IsAny<Expression<Func<SaveSchemaDefinition, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SaveSchemaDefinition>());

        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Act
        var path = await migrator.FindMigrationPathAsync("game", "v1.0", "v2.0", CancellationToken.None);

        // Assert
        Assert.Null(path);
    }

    [Fact]
    public async Task FindMigrationPath_DirectPath_ReturnsCorrectPath()
    {
        // Arrange - v1.0 -> v2.0 direct path
        var schemas = new[]
        {
            new SaveSchemaDefinition
            {
                Namespace = "game",
                SchemaVersion = "v1.0",
                PreviousVersion = null,
                SchemaJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SaveSchemaDefinition
            {
                Namespace = "game",
                SchemaVersion = "v2.0",
                PreviousVersion = "v1.0",
                SchemaJson = "{}",
                MigrationPatchJson = """[{"op":"add","path":"/newField","value":"default"}]""",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        _schemaStoreMock.Setup(s => s.QueryAsync(
                It.IsAny<Expression<Func<SaveSchemaDefinition, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(schemas);

        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Act
        var path = await migrator.FindMigrationPathAsync("game", "v1.0", "v2.0", CancellationToken.None);

        // Assert
        Assert.NotNull(path);
        Assert.Equal(2, path.Count);
        Assert.Equal("v1.0", path[0]);
        Assert.Equal("v2.0", path[1]);
    }

    [Fact]
    public async Task FindMigrationPath_MultiStepPath_ReturnsCorrectPath()
    {
        // Arrange - v1.0 -> v2.0 -> v3.0 chain
        var schemas = new[]
        {
            new SaveSchemaDefinition
            {
                Namespace = "game",
                SchemaVersion = "v1.0",
                PreviousVersion = null,
                SchemaJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SaveSchemaDefinition
            {
                Namespace = "game",
                SchemaVersion = "v2.0",
                PreviousVersion = "v1.0",
                SchemaJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SaveSchemaDefinition
            {
                Namespace = "game",
                SchemaVersion = "v3.0",
                PreviousVersion = "v2.0",
                SchemaJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        _schemaStoreMock.Setup(s => s.QueryAsync(
                It.IsAny<Expression<Func<SaveSchemaDefinition, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(schemas);

        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Act
        var path = await migrator.FindMigrationPathAsync("game", "v1.0", "v3.0", CancellationToken.None);

        // Assert
        Assert.NotNull(path);
        Assert.Equal(3, path.Count);
        Assert.Equal("v1.0", path[0]);
        Assert.Equal("v2.0", path[1]);
        Assert.Equal("v3.0", path[2]);
    }

    [Fact]
    public async Task FindMigrationPath_NoPathExists_ReturnsNull()
    {
        // Arrange - disconnected versions
        var schemas = new[]
        {
            new SaveSchemaDefinition
            {
                Namespace = "game",
                SchemaVersion = "v1.0",
                PreviousVersion = null,
                SchemaJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new SaveSchemaDefinition
            {
                Namespace = "game",
                SchemaVersion = "v3.0",
                PreviousVersion = "v2.0", // v2.0 doesn't exist
                SchemaJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };

        _schemaStoreMock.Setup(s => s.QueryAsync(
                It.IsAny<Expression<Func<SaveSchemaDefinition, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(schemas);

        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Act
        var path = await migrator.FindMigrationPathAsync("game", "v1.0", "v3.0", CancellationToken.None);

        // Assert
        Assert.Null(path);
    }

    [Fact]
    public async Task FindMigrationPath_ExceedsMaxSteps_ReturnsNull()
    {
        // Arrange - create path longer than max steps
        var schemas = Enumerable.Range(1, 15).Select(i => new SaveSchemaDefinition
        {
            Namespace = "game",
            SchemaVersion = $"v{i}.0",
            PreviousVersion = i > 1 ? $"v{i - 1}.0" : null,
            SchemaJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        }).ToArray();

        _schemaStoreMock.Setup(s => s.QueryAsync(
                It.IsAny<Expression<Func<SaveSchemaDefinition, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(schemas);

        // Use low max steps
        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object, maxMigrationSteps: 5);

        // Act
        var path = await migrator.FindMigrationPathAsync("game", "v1.0", "v15.0", CancellationToken.None);

        // Assert
        Assert.Null(path);
    }

    #endregion

    #region ApplyMigrationPath Tests

    [Fact]
    public async Task ApplyMigrationPath_SingleElementPath_ReturnsOriginalData()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("""{"field":"value"}""");
        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Act
        var result = await migrator.ApplyMigrationPathAsync(
            "game", data, new List<string> { "v1.0" }, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(data, result.Data);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task ApplyMigrationPath_WithValidPatch_AppliesPatch()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("""{"name":"Alice"}""");
        var schema = new SaveSchemaDefinition
        {
            Namespace = "game",
            SchemaVersion = "v2.0",
            PreviousVersion = "v1.0",
            SchemaJson = "{}",
            MigrationPatchJson = """[{"op":"add","path":"/age","value":25}]""",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _schemaStoreMock.Setup(s => s.GetAsync(
                SaveSchemaDefinition.GetStateKey("game", "v2.0"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(schema);

        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Act
        var result = await migrator.ApplyMigrationPathAsync(
            "game", data, new List<string> { "v1.0", "v2.0" }, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        var resultJson = Encoding.UTF8.GetString(result.Data);
        Assert.Contains("Alice", resultJson);
        Assert.Contains("25", resultJson);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task ApplyMigrationPath_SchemaNotFound_ReturnsNull()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("""{"field":"value"}""");

        _schemaStoreMock.Setup(s => s.GetAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SaveSchemaDefinition?)null);

        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Act
        var result = await migrator.ApplyMigrationPathAsync(
            "game", data, new List<string> { "v1.0", "v2.0" }, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ApplyMigrationPath_NoPatchInSchema_AddsWarning()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("""{"field":"value"}""");
        var schema = new SaveSchemaDefinition
        {
            Namespace = "game",
            SchemaVersion = "v2.0",
            PreviousVersion = "v1.0",
            SchemaJson = "{}",
            MigrationPatchJson = null, // No patch
            CreatedAt = DateTimeOffset.UtcNow
        };

        _schemaStoreMock.Setup(s => s.GetAsync(
                SaveSchemaDefinition.GetStateKey("game", "v2.0"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(schema);

        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Act
        var result = await migrator.ApplyMigrationPathAsync(
            "game", data, new List<string> { "v1.0", "v2.0" }, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Warnings);
        Assert.Contains("No migration patch", result.Warnings[0]);
    }

    #endregion

    #region ValidateAgainstSchema Tests

    [Fact]
    public void ValidateAgainstSchema_ValidJson_ReturnsTrue()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("""{"field":"value","number":42}""");
        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Act
        var result = migrator.ValidateAgainstSchema(data, "{}");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateAgainstSchema_InvalidJson_ReturnsFalse()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("not valid json");
        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Act
        var result = migrator.ValidateAgainstSchema(data, "{}");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateAgainstSchema_EmptyJsonObject_ReturnsTrue()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("{}");
        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Act
        var result = migrator.ValidateAgainstSchema(data, "{}");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateAgainstSchema_JsonArray_ReturnsTrue()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("""[1,2,3]""");
        var migrator = new SchemaMigrator(_loggerMock.Object, _schemaStoreMock.Object, _telemetryProviderMock.Object);

        // Act
        var result = migrator.ValidateAgainstSchema(data, "{}");

        // Assert
        Assert.True(result);
    }

    #endregion
}
