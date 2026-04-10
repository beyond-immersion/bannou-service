using System.Text.RegularExpressions;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Validates that every nullable field on an x-lifecycle entity has corresponding
/// ChangeFields coverage in its plugin's source code — i.e., the plugin contains
/// a call to <c>IsFieldSet("fieldName")</c> somewhere.
///
/// Rationale: any nullable field on a lifecycle entity represents a 3-state update
/// surface (absent / null / value). Per GitHub Issue #722, all such fields must be
/// handled via ChangeFields.IsFieldSet to distinguish "field absent" from "explicit
/// null clear". This test mechanically enforces that every nullable lifecycle field
/// has IsFieldSet coverage somewhere in the owning plugin.
///
/// Auto-injected fields (createdAt, updatedAt, isDeprecated, deprecatedAt,
/// deprecationReason) are excluded — they are managed by infrastructure, not by
/// Update request methods.
///
/// This test is NOT informational — it fails as a normal test and must be fixed
/// by implementing the missing IsFieldSet coverage in the affected plugin code.
/// There is no exceptions list; every violation must be resolved.
/// </summary>
public class ChangeFieldsCoverageTests
{
    private static readonly string SchemasDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "schemas");
    private static readonly string PluginsDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins");

    /// <summary>
    /// The canonical pattern to search for in plugin source:
    ///   .ChangeFields.IsFieldSet("fieldName")   — extension method call on ChangeFields
    ///   IsFieldSet("fieldName")                 — direct method call (less common)
    /// Uses a simple substring match on <c>IsFieldSet("fieldName")</c> which catches both.
    /// </summary>
    private static readonly Regex IsFieldSetCallPattern = new(
        @"IsFieldSet\(\s*""([a-zA-Z_][a-zA-Z0-9_]*)""\s*\)",
        RegexOptions.Compiled);

    [Fact]
    public void LifecycleNullableFields_HaveIsFieldSetCoverageInPlugin()
    {
        var violations = new List<string>();

        foreach (var eventsFile in Directory.EnumerateFiles(SchemasDir, "*-service-events.yaml"))
        {
            var fileName = Path.GetFileName(eventsFile);
            var serviceName = DeriveServiceName(eventsFile);

            // Find the plugin directory. If missing, skip (some services may not have plugins yet).
            var pluginDir = Path.Combine(PluginsDir, $"lib-{serviceName}");
            if (!Directory.Exists(pluginDir))
                continue;

            // Collect all IsFieldSet("...") calls from plugin source (excluding Generated/)
            var coveredFields = CollectIsFieldSetCalls(pluginDir);

            // Parse the x-lifecycle block and check every nullable field
            var lifecycleEntries = ParseNullableLifecycleFields(eventsFile);

            foreach (var (entityName, fieldName) in lifecycleEntries)
            {
                if (!coveredFields.Contains(fieldName))
                {
                    violations.Add($"{serviceName}: {entityName}.{fieldName} (in {fileName})");
                }
            }
        }

        if (violations.Count == 0)
            return;

        // Group by service for readability
        var grouped = violations
            .GroupBy(v => v.Split(':')[0])
            .OrderBy(g => g.Key)
            .ToList();

        var report = string.Join("\n", grouped.Select(g =>
            $"\n  [{g.Key}] ({g.Count()} uncovered field(s)):\n" +
            string.Join("\n", g.Select(v => $"    - {v}"))));

        Assert.Fail(
            $"Found {violations.Count} nullable lifecycle field(s) without IsFieldSet coverage " +
            $"across {grouped.Count} service(s). Every nullable field on an x-lifecycle entity " +
            $"must have a corresponding ChangeFields.IsFieldSet(\"fieldName\") call somewhere in " +
            $"the owning plugin's source code (see GitHub Issue #722 and " +
            $"HELPERS-AND-COMMON-PATTERNS.md § ChangeFields Pattern).\n" +
            $"{report}");
    }

    /// <summary>
    /// Derives the service name from an events schema file path.
    /// Example: "character-service-events.yaml" → "character".
    /// </summary>
    private static string DeriveServiceName(string eventsFilePath)
    {
        var stem = Path.GetFileNameWithoutExtension(eventsFilePath);
        // Strip trailing "-service-events"
        const string suffix = "-service-events";
        if (stem.EndsWith(suffix, StringComparison.Ordinal))
            return stem[..^suffix.Length];
        // Fallback: strip "-events" if only that is present
        const string altSuffix = "-events";
        if (stem.EndsWith(altSuffix, StringComparison.Ordinal))
            return stem[..^altSuffix.Length];
        return stem;
    }

    /// <summary>
    /// Walks every .cs file in the plugin directory (excluding Generated/) and collects
    /// the set of field names referenced in <c>IsFieldSet("fieldName")</c> calls.
    /// </summary>
    private static HashSet<string> CollectIsFieldSetCalls(string pluginDir)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);

        foreach (var csFile in Directory.EnumerateFiles(pluginDir, "*.cs", SearchOption.AllDirectories))
        {
            // Skip Generated subdirectory
            var relative = Path.GetRelativePath(pluginDir, csFile);
            if (relative.Contains("Generated", StringComparison.OrdinalIgnoreCase))
                continue;

            string content;
            try
            {
                content = File.ReadAllText(csFile);
            }
            catch
            {
                continue;
            }

            foreach (Match match in IsFieldSetCallPattern.Matches(content))
            {
                var fieldName = match.Groups[1].Value;
                fields.Add(fieldName);
            }
        }

        return fields;
    }

    /// <summary>
    /// Parses the x-lifecycle block of an events schema and yields (EntityName, FieldName)
    /// pairs for every nullable field — that is, fields in the model block that do NOT
    /// have <c>required: true</c> AND are not auto-injected lifecycle or deprecation fields.
    /// </summary>
    private static IEnumerable<(string EntityName, string FieldName)> ParseNullableLifecycleFields(string eventsFilePath)
    {
        var root = SchemaParser.ParseYamlFile(eventsFilePath);
        if (root == null)
            yield break;

        if (!root.Children.TryGetValue(new YamlScalarNode("x-lifecycle"), out var lifecycleNode))
            yield break;

        if (lifecycleNode is not YamlMappingNode lifecycleMapping)
            yield break;

        // Configuration keys under x-lifecycle (not entities)
        var configKeys = new HashSet<string>(StringComparer.Ordinal) { "topic_prefix" };

        foreach (var entry in lifecycleMapping.Children)
        {
            var entityName = ((YamlScalarNode)entry.Key).Value;
            if (entityName == null || configKeys.Contains(entityName))
                continue;

            if (entry.Value is not YamlMappingNode entityMapping)
                continue;

            // Detect deprecation: true — if set, exclude deprecation auto-injected fields
            var hasDeprecation = false;
            if (entityMapping.Children.TryGetValue(new YamlScalarNode("deprecation"), out var deprNode)
                && deprNode is YamlScalarNode deprScalar
                && string.Equals(deprScalar.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                hasDeprecation = true;
            }

            if (!entityMapping.Children.TryGetValue(new YamlScalarNode("model"), out var modelNode))
                continue;

            if (modelNode is not YamlMappingNode modelMapping)
                continue;

            foreach (var field in modelMapping.Children)
            {
                var fieldName = ((YamlScalarNode)field.Key).Value;
                if (fieldName == null)
                    continue;

                // Exclude auto-injected fields (always present, managed by infrastructure)
                if (SchemaParser.LifecycleAutoInjectedFields.Contains(fieldName))
                    continue;
                if (hasDeprecation && SchemaParser.DeprecationAutoInjectedFields.Contains(fieldName))
                    continue;

                if (field.Value is not YamlMappingNode fieldMapping)
                    continue;

                // Non-nullable: has required: true OR primary: true (primary keys are always required)
                var isRequired = HasTrueFlag(fieldMapping, "required") || HasTrueFlag(fieldMapping, "primary");
                if (isRequired)
                    continue;

                // This is a nullable lifecycle field — it must have IsFieldSet coverage
                yield return (entityName, fieldName);
            }
        }
    }

    /// <summary>
    /// Returns true if the field mapping has a scalar entry with the given key set to "true".
    /// </summary>
    private static bool HasTrueFlag(YamlMappingNode mapping, string key)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node))
            return false;

        return node is YamlScalarNode scalar &&
                string.Equals(scalar.Value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
