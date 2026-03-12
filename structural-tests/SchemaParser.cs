using YamlDotNet.RepresentationModel;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Structured YAML parser for OpenAPI schema files. Provides semantic access
/// to x-lifecycle definitions, component schemas, and allOf references for
/// schema validation tests.
/// </summary>
internal static class SchemaParser
{
    private static readonly string SchemasDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "schemas");

    /// <summary>
    /// Fields that are auto-injected by generate-lifecycle-events.py for all lifecycle entities.
    /// These should NOT be manually defined in x-lifecycle model blocks.
    /// </summary>
    internal static readonly HashSet<string> LifecycleAutoInjectedFields = new(StringComparer.Ordinal)
    {
        "createdAt",
        "updatedAt"
    };

    /// <summary>
    /// Fields that are auto-injected by generate-lifecycle-events.py when deprecation: true.
    /// These should NOT be manually defined in x-lifecycle model blocks when deprecation is enabled.
    /// </summary>
    internal static readonly HashSet<string> DeprecationAutoInjectedFields = new(StringComparer.Ordinal)
    {
        "isDeprecated",
        "deprecatedAt",
        "deprecationReason"
    };

    /// <summary>
    /// Gets all non-generated event schema YAML files.
    /// </summary>
    internal static IEnumerable<string> GetEventSchemaFiles()
    {
        if (!Directory.Exists(SchemasDir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(SchemasDir, "*-events.yaml"))
        {
            var relativePath = Path.GetRelativePath(SchemasDir, file);
            if (relativePath.StartsWith("Generated", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return file;
        }
    }

    /// <summary>
    /// Parses a YAML file and returns the root mapping node.
    /// Returns null if the file is empty or not a valid YAML mapping.
    /// </summary>
    internal static YamlMappingNode? ParseYamlFile(string filePath)
    {
        using var reader = new StreamReader(filePath);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0)
            return null;

        return yaml.Documents[0].RootNode as YamlMappingNode;
    }

    /// <summary>
    /// Extracts x-lifecycle entity definitions from an events schema file.
    /// Returns entity name, model field names, and whether deprecation is enabled.
    /// </summary>
    internal static IEnumerable<LifecycleEntity> GetLifecycleEntities(string eventsFilePath)
    {
        var root = ParseYamlFile(eventsFilePath);
        if (root == null)
            yield break;

        if (!root.Children.TryGetValue(new YamlScalarNode("x-lifecycle"), out var lifecycleNode))
            yield break;

        if (lifecycleNode is not YamlMappingNode lifecycleMapping)
            yield break;

        // Track top-level lifecycle config (topic_prefix is a config key, not an entity)
        var configKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "topic_prefix"
        };

        foreach (var entry in lifecycleMapping.Children)
        {
            var key = ((YamlScalarNode)entry.Key).Value;
            if (key == null || configKeys.Contains(key))
                continue;

            if (entry.Value is not YamlMappingNode entityMapping)
                continue;

            var entityName = key;
            var modelFields = new List<string>();
            var hasDeprecation = false;
            string? instanceEntity = null;

            // Check deprecation flag
            if (entityMapping.Children.TryGetValue(new YamlScalarNode("deprecation"), out var deprecationNode))
            {
                hasDeprecation = deprecationNode is YamlScalarNode scalar &&
                                string.Equals(scalar.Value, "true", StringComparison.OrdinalIgnoreCase);
            }

            // Check instanceEntity (Category B: names the lifecycle entity representing instances)
            if (entityMapping.Children.TryGetValue(new YamlScalarNode("instanceEntity"), out var instanceNode))
            {
                instanceEntity = (instanceNode as YamlScalarNode)?.Value;
            }

            // Extract model field names
            if (entityMapping.Children.TryGetValue(new YamlScalarNode("model"), out var modelNode) &&
                modelNode is YamlMappingNode modelMapping)
            {
                foreach (var field in modelMapping.Children)
                {
                    var fieldName = ((YamlScalarNode)field.Key).Value;
                    if (fieldName != null)
                        modelFields.Add(fieldName);
                }
            }

            yield return new LifecycleEntity(entityName, modelFields, hasDeprecation, instanceEntity);
        }
    }

    /// <summary>
    /// Represents a parsed x-lifecycle entity definition.
    /// </summary>
    /// <param name="EntityName">PascalCase entity name from x-lifecycle key</param>
    /// <param name="ModelFields">Field names defined in the model block</param>
    /// <param name="HasDeprecation">Whether deprecation: true is set</param>
    /// <param name="InstanceEntity">Name of the lifecycle entity representing instances of this template (Category B only)</param>
    internal sealed record LifecycleEntity(
        string EntityName,
        List<string> ModelFields,
        bool HasDeprecation,
        string? InstanceEntity);
}
