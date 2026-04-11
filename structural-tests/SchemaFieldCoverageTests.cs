using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Structural enforcement of schema field coverage — the broader class of dead-schema-field bugs
/// exemplified by item #1 of issue #720's follow-up.
/// </summary>
/// <remarks>
/// <para>
/// Every property defined in a plugin's OpenAPI schema should be read by the plugin's own source
/// code somewhere. A property that is never referenced is either a dead field (stale schema that
/// should be removed) or a missing implementation (the field exists in the contract but the code
/// that reads or writes it was never written). Either situation is a latent bug: the schema
/// advertises a surface that the service does not actually honor.
/// </para>
/// <para>
/// <b>Item #1 — honest limitation:</b> The specific case that motivated this gate is
/// <c>GenesisTemplateModel.Awakening.InitialPersonalityTraits</c>, a template field preserved
/// through template parsing but never applied to the created character. This gate does NOT catch
/// that specific bug because the field IS referenced in source — in a <c>LogWarning</c> call.
/// The lexical scan cannot distinguish "read for business logic" from "read for diagnostic
/// logging," so any textual occurrence of the PascalCase property name counts as "used." What
/// this gate does catch is the broader class of fields whose PascalCase name appears NOWHERE in
/// the owning plugin's source code — the strictly-dead cases, which are still plentiful and
/// worth surfacing.
/// </para>
/// <para>
/// <b>Rule:</b> For every OpenAPI schema file in <c>schemas/</c> (excluding shared/global files
/// and generated outputs), walk every <c>components/schemas/</c> model with a <c>properties:</c>
/// block. For each property name, compute the PascalCase C# form (upper-case first character,
/// preserve the rest) and check whether that name appears anywhere in the owning plugin's C#
/// source as either <c>.PropertyName</c> (dot access) or <c>PropertyName =</c> (initializer
/// assignment, excluding <c>==</c> comparisons). If no file in the plugin contains a match, the
/// property is flagged as an unused schema field.
/// </para>
/// <para>
/// <b>Plugin directory resolution:</b> The owning plugin is derived from the schema filename by
/// stripping one of four known suffixes: <c>-service-events</c>, <c>-client-events</c>,
/// <c>-configuration</c>, <c>-api</c>. The remaining stem is the service name, mapped to
/// <c>plugins/lib-{service}/</c>. Hyphenated service names (<c>character-lifecycle</c>,
/// <c>game-session</c>, <c>save-load</c>) preserve hyphens. Schemas whose filenames do not match
/// any known suffix, or whose plugin directory does not exist on disk, are silently skipped.
/// </para>
/// <para>
/// <b>Excluded schema files</b> (skipped wholesale):
/// </para>
/// <list type="bullet">
///   <item><c>common-api.yaml</c>, <c>common-events.yaml</c>, <c>common-client-events.yaml</c>
///     — shared types referenced by many services; the "owning" plugin is ambiguous</item>
///   <item><c>state-stores.yaml</c>, <c>telemetry-metrics.yaml</c>, <c>variable-providers.yaml</c>,
///     <c>localization-categories.yaml</c>, <c>archetype-definitions.yaml</c> — global config
///     and cross-cutting definitions, not per-service schemas</item>
///   <item>Anything under <c>schemas/Generated/</c> — regenerated outputs, never edited manually</item>
/// </list>
/// <para>
/// <b>Excluded model types</b> (inside per-service schemas, skipped during the walk):
/// </para>
/// <list type="bullet">
///   <item>Models whose name starts with <c>Base</c> — abstract types meant to be inherited via
///     <c>allOf</c>; their properties are carried into derived types which are checked separately</item>
///   <item>Models whose name ends with <c>Error</c> — typically generic ErrorResponse shapes that
///     mirror a shared error template, not checked to avoid noise</item>
/// </list>
/// <para>
/// <b>Always-used fields</b> (never flagged even when absent from source):
/// </para>
/// <list type="bullet">
///   <item>Auto-injected lifecycle fields: <c>createdAt</c>, <c>updatedAt</c></item>
///   <item>Auto-injected deprecation fields: <c>isDeprecated</c>, <c>deprecatedAt</c>, <c>deprecationReason</c></item>
///   <item>Event envelope fields: <c>eventId</c>, <c>timestamp</c>, <c>eventName</c> —
///     inherited from <c>BaseServiceEvent</c> and managed by the event publisher infrastructure</item>
///   <item>Per-file: fields listed under <c>x-lifecycle.{Entity}.sensitive</c> in events schemas
///     — intentionally stripped from lifecycle events and not expected to appear in handler code</item>
/// </list>
/// <para>
/// <b>Known limitations</b> (documented so the signal isn't mistaken for precision):
/// </para>
/// <list type="bullet">
///   <item><b>Read vs write is not distinguished</b> — any textual occurrence of
///     <c>.PropertyName</c> or <c>PropertyName =</c> counts as "used." A property that is logged
///     but never acted on passes the gate (this is exactly why the Genesis
///     <c>InitialPersonalityTraits</c> case slips through).</item>
///   <item><b>Semantic usage is not verified</b> — <c>LogWarning(..., template.Foo.Count)</c>
///     counts as a read of <c>Foo</c> even though the code only logs it.</item>
///   <item><b>$ref navigation is not followed</b> — if model A references model B via
///     <c>$ref</c> and only A's properties are read, B's properties are still walked in isolation.</item>
///   <item><b>Substring match may false-positive across models sharing a property name</b> —
///     if two models define a <c>Name</c> field and only one is actually used, both pass because
///     the lexical scan cannot tell them apart.</item>
///   <item><b>Common property names are near-universally "used"</b> — fields named <c>Name</c>,
///     <c>Count</c>, <c>Id</c>, <c>Type</c>, <c>Value</c> will almost always match somewhere in
///     any reasonably-sized plugin source tree.</item>
///   <item><b>Object initializer false positives in the reference set</b> — the assignment
///     pattern <c>PropertyName =</c> also matches local variable declarations and enum member
///     initializers, inflating the "used" set. This is the safer failure mode: false positives
///     in the set translate to false negatives in violations (under-reporting), never to false
///     positives in violations (over-reporting).</item>
/// </list>
/// <para>
/// <b>This test is always-on and expected to be noisy.</b> There is no
/// <c>SkipUnless.InformationalTest()</c> gate and no allowlist. The codebase has accumulated dead
/// schema fields over time and surfacing them is the entire point. Expect several hundred
/// violations at first run. Each one is a real data point the developer should evaluate — either
/// remove the field from the schema or implement the code that reads it. Do not "fix" the test
/// by adding exclusions.
/// </para>
/// </remarks>
public class SchemaFieldCoverageTests
{
    private static readonly string SchemasDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "schemas");
    private static readonly string PluginsDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins");

    /// <summary>
    /// Schema filenames that are not per-service schemas and are skipped wholesale. The
    /// <c>common-*</c> files define shared types used via <c>$ref</c> by many plugins and have
    /// no single owning plugin. The global config files (<c>state-stores.yaml</c>,
    /// <c>telemetry-metrics.yaml</c>, <c>variable-providers.yaml</c>,
    /// <c>localization-categories.yaml</c>, <c>archetype-definitions.yaml</c>) are cross-cutting
    /// definitions that drive code generation across the entire platform, not per-service
    /// contracts.
    /// </summary>
    private static readonly HashSet<string> ExcludedSchemaFiles = new(StringComparer.Ordinal)
    {
        "common-api.yaml",
        "common-client-events.yaml",
        "common-events.yaml",
        "state-stores.yaml",
        "telemetry-metrics.yaml",
        "variable-providers.yaml",
        "localization-categories.yaml",
        "archetype-definitions.yaml",
    };

    /// <summary>
    /// Fields that are always considered "used" regardless of whether they appear in plugin
    /// source. These are auto-injected by the code generator (lifecycle timestamps, deprecation
    /// tracking) or inherited from base event envelopes and managed by infrastructure rather
    /// than business logic.
    /// </summary>
    private static readonly HashSet<string> AlwaysUsedFields = new(StringComparer.Ordinal)
    {
        // Auto-injected lifecycle
        "createdAt",
        "updatedAt",
        // Auto-injected deprecation (when deprecation: true)
        "isDeprecated",
        "deprecatedAt",
        "deprecationReason",
        // Event envelope (BaseServiceEvent / BaseClientEvent inheritance)
        "eventId",
        "timestamp",
        "eventName",
    };

    /// <summary>
    /// Schema filename suffixes recognized as per-service schemas. Order matters: the longer
    /// suffix <c>-service-events</c> must be tested before <c>-events</c> so that
    /// <c>foo-service-events.yaml</c> does not strip only the trailing <c>-events</c>, leaving
    /// a bogus <c>foo-service</c> stem.
    /// </summary>
    private static readonly string[] SchemaSuffixes = new[]
    {
        "-service-events",
        "-client-events",
        "-configuration",
        "-api",
        "-events", // Legacy fallback (none in the current repo, but harmless)
    };

    /// <summary>
    /// Matches a dot followed by a PascalCase identifier — the typical shape of a C# property
    /// or method access (<c>obj.PropertyName</c>, <c>Type.StaticMember</c>, <c>nameof(X.Y)</c>).
    /// </summary>
    private static readonly Regex DotAccessPattern = new(
        @"\.([A-Z][a-zA-Z0-9_]*)",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches a PascalCase identifier followed by a simple <c>=</c> assignment (not <c>==</c>).
    /// Catches C# object initializer syntax (<c>new Foo { Bar = 1 }</c>) which the dot-access
    /// pattern misses. Also false-matches local variable declarations and similar — those false
    /// positives inflate the "used" set harmlessly (they only cause under-reporting of
    /// violations, never over-reporting).
    /// </summary>
    private static readonly Regex InitializerAssignmentPattern = new(
        @"\b([A-Z][a-zA-Z0-9_]*)\s*=(?!=)",
        RegexOptions.Compiled);

    [Fact]
    public void SchemaFields_MustBeReadByPluginCode()
    {
        if (!Directory.Exists(SchemasDir) || !Directory.Exists(PluginsDir))
            return;

        // Cache plugin source scans — one dir may host multiple schema files (-api, -configuration,
        // -service-events, -client-events) and we don't want to re-scan source files four times.
        var pluginReferenceCache = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        var violations = new List<Violation>();

        foreach (var schemaFile in Directory.EnumerateFiles(SchemasDir, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(schemaFile);
            if (ExcludedSchemaFiles.Contains(fileName))
                continue;

            var serviceName = DeriveServiceName(fileName);
            if (serviceName == null)
                continue; // Filename doesn't match any known suffix pattern — out of scope.

            var pluginDir = Path.Combine(PluginsDir, $"lib-{serviceName}");
            if (!Directory.Exists(pluginDir))
                continue; // Pre-implementation stub or shared schema — skip silently.

            if (!pluginReferenceCache.TryGetValue(pluginDir, out var coveredProperties))
            {
                coveredProperties = CollectPluginPropertyReferences(pluginDir);
                pluginReferenceCache[pluginDir] = coveredProperties;
            }

            var root = SchemaParser.ParseYamlFile(schemaFile);
            if (root == null)
                continue;

            // Sensitive fields are listed per-entity in x-lifecycle and are intentionally
            // stripped from lifecycle events — they need not appear in handler code.
            var sensitiveFields = CollectSensitiveLifecycleFields(root);

            var schemas = GetComponentsSchemas(root);
            if (schemas == null)
                continue;

            foreach (var modelEntry in schemas.Children)
            {
                var modelName = (modelEntry.Key as YamlScalarNode)?.Value;
                if (modelName == null)
                    continue;

                // Skip abstract base types (inherited via allOf; derived types are checked separately)
                if (modelName.StartsWith("Base", StringComparison.Ordinal))
                    continue;

                // Skip generic error response shapes — these mirror a shared template
                if (modelName.EndsWith("Error", StringComparison.Ordinal))
                    continue;

                if (modelEntry.Value is not YamlMappingNode modelMapping)
                    continue;

                // Navigate to the properties block. Models without a properties block
                // (enum-only schemas, pure $ref wrappers, arrays) have nothing to scan.
                if (!modelMapping.Children.TryGetValue(new YamlScalarNode("properties"), out var propsNode))
                    continue;
                if (propsNode is not YamlMappingNode propsMapping)
                    continue;

                foreach (var propEntry in propsMapping.Children)
                {
                    var propName = (propEntry.Key as YamlScalarNode)?.Value;
                    if (propName == null)
                        continue;

                    if (AlwaysUsedFields.Contains(propName))
                        continue;
                    if (sensitiveFields.Contains(propName))
                        continue;

                    var pascalName = ToPascalCase(propName);
                    if (coveredProperties.Contains(pascalName))
                        continue;

                    violations.Add(new Violation(serviceName, fileName, modelName, propName));
                }
            }
        }

        if (violations.Count == 0)
            return;

        // Group violations by service, ordering services by descending violation count so the
        // largest offenders appear first in the report.
        var grouped = violations
            .GroupBy(v => v.Service)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var report = new StringBuilder();
        foreach (var group in grouped)
        {
            report.AppendLine();
            report.AppendLine($"  [{group.Key}] ({group.Count()} unused field(s)):");
            foreach (var v in group
                .OrderBy(v => v.SchemaFile, StringComparer.Ordinal)
                .ThenBy(v => v.Model, StringComparer.Ordinal)
                .ThenBy(v => v.Field, StringComparer.Ordinal))
            {
                report.AppendLine($"    - {v.Model}.{v.Field} (in {v.SchemaFile})");
            }
        }

        Assert.Fail(
            $"Found {violations.Count} schema field(s) across {grouped.Count} plugin(s) that are " +
            $"defined in OpenAPI schemas but appear NOWHERE in the owning plugin's C# source code. " +
            $"A schema field with no code reference is either dead (remove from the schema) or " +
            $"missing an implementation (add the code that reads or writes it). See the test's " +
            $"XML documentation for the full set of known limitations.\n" +
            $"{report}");
    }

    /// <summary>
    /// A single schema field coverage violation. Tracked as a record for grouping and ordering.
    /// </summary>
    private sealed record Violation(string Service, string SchemaFile, string Model, string Field);

    /// <summary>
    /// Derives the service name from a schema filename by stripping the first matching suffix
    /// from <see cref="SchemaSuffixes"/>. Returns null if the filename does not end with any
    /// known per-service suffix.
    /// </summary>
    private static string? DeriveServiceName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        foreach (var suffix in SchemaSuffixes)
        {
            if (stem.EndsWith(suffix, StringComparison.Ordinal))
                return stem[..^suffix.Length];
        }
        return null;
    }

    /// <summary>
    /// Converts a camelCase property name to PascalCase by upper-casing the first character.
    /// Preserves the rest verbatim to handle acronyms like <c>accountId</c> → <c>AccountId</c>
    /// and <c>realmHistoryEvents</c> → <c>RealmHistoryEvents</c>.
    /// </summary>
    private static string ToPascalCase(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase))
            return camelCase;

        if (char.IsUpper(camelCase[0]))
            return camelCase;

        return char.ToUpperInvariant(camelCase[0]) + camelCase[1..];
    }

    /// <summary>
    /// Walks every non-Generated, non-test <c>.cs</c> file in the plugin directory and collects
    /// every PascalCase identifier that appears after a dot or immediately before a simple
    /// <c>=</c> assignment. The resulting set is the "used property" bag against which schema
    /// property names are checked.
    /// </summary>
    /// <remarks>
    /// This is inherently a lexical approximation. Common property names (<c>Name</c>,
    /// <c>Count</c>, <c>Id</c>, <c>Type</c>, <c>Value</c>) will almost always be present because
    /// they appear in unrelated code. This causes the test to under-report violations for fields
    /// sharing those names — a known limitation documented on the test class.
    /// </remarks>
    private static HashSet<string> CollectPluginPropertyReferences(string pluginDir)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);

        foreach (var csFile in Directory.EnumerateFiles(pluginDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(pluginDir, csFile);
            if (relative.Contains("Generated", StringComparison.OrdinalIgnoreCase))
                continue;
            // Defensive: skip any .tests project content inside the plugin dir. By convention
            // lib-*.tests is a sibling directory, not nested under lib-*, but this keeps the
            // collection honest if someone ever nests test sources.
            if (relative.Contains(".tests", StringComparison.OrdinalIgnoreCase))
                continue;
            if (relative.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
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

            foreach (Match match in DotAccessPattern.Matches(content))
                references.Add(match.Groups[1].Value);
            foreach (Match match in InitializerAssignmentPattern.Matches(content))
                references.Add(match.Groups[1].Value);
        }

        return references;
    }

    /// <summary>
    /// Collects field names listed under <c>x-lifecycle.{Entity}.sensitive</c> for every entity
    /// in the schema's <c>x-lifecycle</c> block. These fields are intentionally stripped from
    /// generated lifecycle events and are not expected to appear in handler code that deals with
    /// those events.
    /// </summary>
    private static HashSet<string> CollectSensitiveLifecycleFields(YamlMappingNode root)
    {
        var sensitive = new HashSet<string>(StringComparer.Ordinal);

        if (!root.Children.TryGetValue(new YamlScalarNode("x-lifecycle"), out var lifecycleNode))
            return sensitive;
        if (lifecycleNode is not YamlMappingNode lifecycleMapping)
            return sensitive;

        foreach (var entityEntry in lifecycleMapping.Children)
        {
            if (entityEntry.Value is not YamlMappingNode entityMapping)
                continue;

            if (!entityMapping.Children.TryGetValue(new YamlScalarNode("sensitive"), out var sensitiveNode))
                continue;

            if (sensitiveNode is not YamlSequenceNode sensitiveSequence)
                continue;

            foreach (var item in sensitiveSequence)
            {
                if (item is YamlScalarNode scalar && scalar.Value != null)
                    sensitive.Add(scalar.Value);
            }
        }

        return sensitive;
    }

    /// <summary>
    /// Navigates from the root mapping to <c>components.schemas</c>. Returns null if either
    /// intermediate key is missing or not a mapping (e.g., state-stores.yaml has no
    /// <c>components</c> section).
    /// </summary>
    private static YamlMappingNode? GetComponentsSchemas(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("components"), out var componentsNode))
            return null;
        if (componentsNode is not YamlMappingNode componentsMapping)
            return null;

        if (!componentsMapping.Children.TryGetValue(new YamlScalarNode("schemas"), out var schemasNode))
            return null;

        return schemasNode as YamlMappingNode;
    }
}
