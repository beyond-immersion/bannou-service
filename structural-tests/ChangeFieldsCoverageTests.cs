using System.Text.RegularExpressions;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Validates ChangeFields / IsFieldSet coverage for the <c>x-detect-if-set</c> opt-in pattern.
///
/// Two tests:
/// <list type="bullet">
///   <item><c>DetectIfSetProperties_HaveIsFieldSetCoverageInPlugin</c> — enforcing (always runs).
///     Every property marked <c>x-detect-if-set: true</c> in a service API schema must have a
///     matching <c>IsFieldSet("fieldName")</c> call in the owning plugin.</item>
///   <item><c>DetectIfSet_MigrationCandidates</c> — informational (SkipUnless gated).
///     Scans x-lifecycle nullable fields, cross-references against Update/Modify/Set request
///     models, and reports fields that ARE on a request but do NOT yet have <c>x-detect-if-set</c>.
///     These are migration candidates for future ChangeFields adoption.</item>
/// </list>
///
/// See GitHub Issue #722 and HELPERS-AND-COMMON-PATTERNS.md § ChangeFields Pattern.
/// </summary>
public class ChangeFieldsCoverageTests
{
    private static readonly string SchemasDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "schemas");
    private static readonly string PluginsDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins");

    private static readonly Regex IsFieldSetCallPattern = new(
        @"IsFieldSet\(\s*""([a-zA-Z_][a-zA-Z0-9_]*)""\s*\)",
        RegexOptions.Compiled);

    /// <summary>
    /// Request class name patterns eligible for ChangeFields post-processing.
    /// </summary>
    private static readonly Regex TargetClassPattern = new(
        @"^(?:Update|Modify|BulkUpdate)\w*Request$|^Set[A-Z]\w*Request$",
        RegexOptions.Compiled);

    #region Enforcing Test

    /// <summary>
    /// Enforcing test: every property marked with <c>x-detect-if-set: true</c> in a service's
    /// API schema MUST have a corresponding <c>IsFieldSet("fieldName")</c> call somewhere in
    /// the owning plugin's source code (excluding Generated/).
    /// </summary>
    [Fact]
    public void DetectIfSetProperties_HaveIsFieldSetCoverageInPlugin()
    {
        var violations = new List<string>();

        foreach (var apiFile in Directory.EnumerateFiles(SchemasDir, "*-api.yaml"))
        {
            var relativePath = Path.GetRelativePath(SchemasDir, apiFile);
            if (relativePath.StartsWith("Generated", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Path.GetFileName(apiFile) == "common-api.yaml")
                continue;

            var fileName = Path.GetFileName(apiFile);
            var serviceName = DeriveServiceNameFromApi(apiFile);

            var pluginDir = Path.Combine(PluginsDir, $"lib-{serviceName}");
            if (!Directory.Exists(pluginDir))
                continue;

            var optInFields = ParseDetectIfSetProperties(apiFile);
            if (optInFields.Count == 0)
                continue;

            var coveredFields = CollectIsFieldSetCalls(pluginDir);

            foreach (var (className, fieldName) in optInFields)
            {
                if (!coveredFields.Contains(fieldName))
                {
                    violations.Add($"{serviceName}: {className}.{fieldName} (in {fileName})");
                }
            }
        }

        if (violations.Count == 0)
            return;

        var grouped = violations
            .GroupBy(v => v.Split(':')[0])
            .OrderBy(g => g.Key)
            .ToList();

        var report = string.Join("\n", grouped.Select(g =>
            $"\n  [{g.Key}] ({g.Count()} uncovered field(s)):\n" +
            string.Join("\n", g.Select(v => $"    - {v}"))));

        Assert.Fail(
            $"Found {violations.Count} x-detect-if-set property/properties without IsFieldSet coverage " +
            $"across {grouped.Count} service(s). Every property marked with x-detect-if-set: true in " +
            $"an API schema must have a corresponding ChangeFields.IsFieldSet(\"fieldName\") call " +
            $"somewhere in the owning plugin's source code (see GitHub Issue #722 and " +
            $"HELPERS-AND-COMMON-PATTERNS.md § ChangeFields Pattern).\n" +
            $"{report}");
    }

    #endregion

    #region Informational Test — Migration Candidates

    /// <summary>
    /// Informational audit: identifies lifecycle nullable fields that appear on
    /// Update/Modify/Set request models but do NOT yet have <c>x-detect-if-set: true</c>.
    ///
    /// These are migration candidates — fields where a developer should evaluate whether
    /// 3-state clear-to-null semantics are needed and, if so, add the opt-in attribute.
    ///
    /// Filters out three categories of false positives:
    /// <list type="bullet">
    ///   <item><b>Sensitive fields</b> — in the x-lifecycle <c>sensitive:</c> list, excluded
    ///     from events, never user-updatable.</item>
    ///   <item><b>Renamed wire fields</b> — lifecycle name has a <c>new*</c> variant on the
    ///     request (e.g., <c>containerId</c> → <c>newContainerId</c>). The 3-state semantics
    ///     exist under the renamed field.</item>
    ///   <item><b>No update surface</b> — lifecycle field does not appear on any
    ///     Update/Modify/Set request model. System-driven, set only by event handlers
    ///     or lifecycle transitions.</item>
    /// </list>
    ///
    /// Run with: <c>BANNOU_RUN_INFORMATIONAL_TESTS=true dotnet test --filter-method "*MigrationCandidates*"</c>
    /// </summary>
    [Fact]
    public void DetectIfSet_MigrationCandidates()
    {
        SkipUnless.InformationalTest(
            "Produces audit list of lifecycle nullable fields on request models missing x-detect-if-set");

        // 1. Load all lifecycle nullable fields (same scan as the original test)
        var lifecycleFields = ParseAllLifecycleNullableFields();

        // 2. Load all request-model fields and which are already opted-in
        var (requestFieldsByService, optedInByService) = LoadRequestFieldsAndOptIns();

        // 3. Classify each lifecycle field
        var candidates = new List<string>();
        var alreadyOptedIn = 0;
        var filtered = 0;

        foreach (var (svc, fields) in lifecycleFields.OrderBy(kv => kv.Key))
        {
            var svcRequestFields = requestFieldsByService.GetValueOrDefault(svc);
            var svcOptedIn = optedInByService.GetValueOrDefault(svc);

            foreach (var (entity, field, isSensitive) in fields)
            {
                // Filter: sensitive
                if (isSensitive) { filtered++; continue; }

                // Filter: already opted in
                if (svcOptedIn != null && svcOptedIn.Contains(field)) { alreadyOptedIn++; continue; }

                // Filter: renamed wire field (new* variant exists on request)
                var renamedVariant = $"new{char.ToUpperInvariant(field[0])}{field[1..]}";
                if (svcRequestFields != null && svcRequestFields.Contains(renamedVariant)) { filtered++; continue; }

                // Filter: no update surface
                if (svcRequestFields == null || !svcRequestFields.Contains(field)) { filtered++; continue; }

                // This field IS on a request model but NOT opted-in → candidate
                candidates.Add($"{svc}: {entity}.{field}");
            }
        }

        if (candidates.Count == 0)
        {
            // All lifecycle fields on request models are opted-in — migration complete
            return;
        }

        var grouped = candidates
            .GroupBy(v => v.Split(':')[0])
            .OrderBy(g => g.Key)
            .ToList();

        var report = string.Join("\n", grouped.Select(g =>
            $"\n  [{g.Key}] ({g.Count()} candidate(s)):\n" +
            string.Join("\n", g.Select(v => $"    - {v}"))));

        Assert.Fail(
            $"Found {candidates.Count} lifecycle nullable field(s) on Update/Modify/Set request models " +
            $"that do not yet have x-detect-if-set: true ({grouped.Count} service(s)). " +
            $"These are migration candidates — evaluate whether each field needs 3-state " +
            $"clear-to-null semantics and add x-detect-if-set: true if so.\n" +
            $"\n  Already opted in: {alreadyOptedIn}" +
            $"\n  Filtered (sensitive / renamed / no update surface): {filtered}" +
            $"\n  Candidates: {candidates.Count}\n" +
            $"{report}");
    }

    #endregion

    #region Shared Helpers

    private static string DeriveServiceNameFromApi(string apiFilePath)
    {
        var stem = Path.GetFileNameWithoutExtension(apiFilePath);
        const string suffix = "-api";
        return stem.EndsWith(suffix, StringComparison.Ordinal) ? stem[..^suffix.Length] : stem;
    }

    private static string DeriveServiceNameFromEvents(string eventsFilePath)
    {
        var stem = Path.GetFileNameWithoutExtension(eventsFilePath);
        const string suffix = "-service-events";
        if (stem.EndsWith(suffix, StringComparison.Ordinal))
            return stem[..^suffix.Length];
        const string altSuffix = "-events";
        if (stem.EndsWith(altSuffix, StringComparison.Ordinal))
            return stem[..^altSuffix.Length];
        return stem;
    }

    private static HashSet<string> CollectIsFieldSetCalls(string pluginDir)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);

        foreach (var csFile in Directory.EnumerateFiles(pluginDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(pluginDir, csFile);
            if (relative.Contains("Generated", StringComparison.OrdinalIgnoreCase))
                continue;

            string content;
            try { content = File.ReadAllText(csFile); }
            catch { continue; }

            foreach (Match match in IsFieldSetCallPattern.Matches(content))
                fields.Add(match.Groups[1].Value);
        }

        return fields;
    }

    /// <summary>
    /// Parses an API schema for properties with <c>x-detect-if-set: true</c>.
    /// </summary>
    private static List<(string ClassName, string FieldName)> ParseDetectIfSetProperties(string apiFilePath)
    {
        var results = new List<(string, string)>();
        var root = SchemaParser.ParseYamlFile(apiFilePath);
        if (root == null) return results;

        var schemasMapping = GetSchemasMapping(root);
        if (schemasMapping == null) return results;

        foreach (var schemaEntry in schemasMapping.Children)
        {
            var className = ((YamlScalarNode)schemaEntry.Key).Value;
            if (className == null || schemaEntry.Value is not YamlMappingNode classDef)
                continue;
            if (!classDef.Children.TryGetValue(new YamlScalarNode("properties"), out var propsNode)
                || propsNode is not YamlMappingNode propsMapping)
                continue;

            foreach (var propEntry in propsMapping.Children)
            {
                var propName = ((YamlScalarNode)propEntry.Key).Value;
                if (propName == null || propEntry.Value is not YamlMappingNode propDef)
                    continue;
                if (propDef.Children.TryGetValue(new YamlScalarNode("x-detect-if-set"), out var detectNode)
                    && detectNode is YamlScalarNode detectScalar
                    && string.Equals(detectScalar.Value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    results.Add((className, propName));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Parses all x-lifecycle blocks across all event schemas, returning nullable non-required
    /// fields (excluding auto-injected). Matches the original test's scan surface.
    /// </summary>
    private static Dictionary<string, List<(string Entity, string Field, bool IsSensitive)>> ParseAllLifecycleNullableFields()
    {
        var results = new Dictionary<string, List<(string, string, bool)>>();

        foreach (var eventsFile in Directory.EnumerateFiles(SchemasDir, "*-service-events.yaml"))
        {
            var relativePath = Path.GetRelativePath(SchemasDir, eventsFile);
            if (relativePath.StartsWith("Generated", StringComparison.OrdinalIgnoreCase))
                continue;

            var serviceName = DeriveServiceNameFromEvents(eventsFile);
            var root = SchemaParser.ParseYamlFile(eventsFile);
            if (root == null) continue;

            if (!root.Children.TryGetValue(new YamlScalarNode("x-lifecycle"), out var lifecycleNode)
                || lifecycleNode is not YamlMappingNode lifecycleMapping)
                continue;

            var configKeys = new HashSet<string>(StringComparer.Ordinal) { "topic_prefix" };
            var serviceFields = new List<(string, string, bool)>();

            foreach (var entry in lifecycleMapping.Children)
            {
                var entityName = ((YamlScalarNode)entry.Key).Value;
                if (entityName == null || configKeys.Contains(entityName))
                    continue;
                if (entry.Value is not YamlMappingNode entityMapping)
                    continue;

                var hasDeprecation = HasTrueFlag(entityMapping, "deprecation");
                var sensitive = new HashSet<string>(StringComparer.Ordinal);
                if (entityMapping.Children.TryGetValue(new YamlScalarNode("sensitive"), out var sensNode)
                    && sensNode is YamlSequenceNode sensSeq)
                {
                    foreach (var s in sensSeq)
                        if (s is YamlScalarNode ss && ss.Value != null)
                            sensitive.Add(ss.Value);
                }

                if (!entityMapping.Children.TryGetValue(new YamlScalarNode("model"), out var modelNode)
                    || modelNode is not YamlMappingNode modelMapping)
                    continue;

                foreach (var field in modelMapping.Children)
                {
                    var fieldName = ((YamlScalarNode)field.Key).Value;
                    if (fieldName == null) continue;
                    if (SchemaParser.LifecycleAutoInjectedFields.Contains(fieldName)) continue;
                    if (hasDeprecation && SchemaParser.DeprecationAutoInjectedFields.Contains(fieldName)) continue;
                    if (field.Value is not YamlMappingNode fieldMapping) continue;
                    if (HasTrueFlag(fieldMapping, "required") || HasTrueFlag(fieldMapping, "primary")) continue;

                    serviceFields.Add((entityName, fieldName, sensitive.Contains(fieldName)));
                }
            }

            if (serviceFields.Count > 0)
                results[serviceName] = serviceFields;
        }

        return results;
    }

    /// <summary>
    /// Loads all request-model field names and x-detect-if-set opt-ins from API schemas.
    /// </summary>
    private static (Dictionary<string, HashSet<string>> RequestFields, Dictionary<string, HashSet<string>> OptedIn)
        LoadRequestFieldsAndOptIns()
    {
        var requestFields = new Dictionary<string, HashSet<string>>();
        var optedIn = new Dictionary<string, HashSet<string>>();

        foreach (var apiFile in Directory.EnumerateFiles(SchemasDir, "*-api.yaml"))
        {
            var relativePath = Path.GetRelativePath(SchemasDir, apiFile);
            if (relativePath.StartsWith("Generated", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Path.GetFileName(apiFile) == "common-api.yaml")
                continue;

            var serviceName = DeriveServiceNameFromApi(apiFile);
            var root = SchemaParser.ParseYamlFile(apiFile);
            if (root == null) continue;

            var schemasMapping = GetSchemasMapping(root);
            if (schemasMapping == null) continue;

            var svcReqFields = new HashSet<string>(StringComparer.Ordinal);
            var svcOptedIn = new HashSet<string>(StringComparer.Ordinal);

            foreach (var schemaEntry in schemasMapping.Children)
            {
                var className = ((YamlScalarNode)schemaEntry.Key).Value;
                if (className == null || !TargetClassPattern.IsMatch(className))
                    continue;
                if (schemaEntry.Value is not YamlMappingNode classDef)
                    continue;
                if (!classDef.Children.TryGetValue(new YamlScalarNode("properties"), out var propsNode)
                    || propsNode is not YamlMappingNode propsMapping)
                    continue;

                foreach (var propEntry in propsMapping.Children)
                {
                    var propName = ((YamlScalarNode)propEntry.Key).Value;
                    if (propName == null) continue;
                    svcReqFields.Add(propName);

                    if (propEntry.Value is YamlMappingNode propDef
                        && propDef.Children.TryGetValue(new YamlScalarNode("x-detect-if-set"), out var detectNode)
                        && detectNode is YamlScalarNode detectScalar
                        && string.Equals(detectScalar.Value, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        svcOptedIn.Add(propName);
                    }
                }
            }

            if (svcReqFields.Count > 0)
                requestFields[serviceName] = svcReqFields;
            if (svcOptedIn.Count > 0)
                optedIn[serviceName] = svcOptedIn;
        }

        return (requestFields, optedIn);
    }

    private static YamlMappingNode? GetSchemasMapping(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("components"), out var componentsNode)
            || componentsNode is not YamlMappingNode componentsMapping)
            return null;
        if (!componentsMapping.Children.TryGetValue(new YamlScalarNode("schemas"), out var schemasNode)
            || schemasNode is not YamlMappingNode schemasMapping)
            return null;
        return schemasMapping;
    }

    private static bool HasTrueFlag(YamlMappingNode mapping, string key)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node))
            return false;
        return node is YamlScalarNode scalar &&
                string.Equals(scalar.Value, "true", StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
