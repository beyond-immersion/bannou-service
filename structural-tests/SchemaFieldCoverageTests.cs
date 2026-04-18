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
/// source as any of four lexical forms: <c>.PropertyName</c> (dot access),
/// <c>PropertyName =</c> (initializer assignment, excluding <c>==</c>),
/// <c>&lt;PropertyName&gt;</c> (generic type parameter), or <c>new PropertyName</c>
/// (constructor or allocation expression). If no file in the plugin contains a match, the
/// property is flagged as an unused schema field. The same four patterns populate the
/// "covered" set used by passthrough detection for embedded sub-types.
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
/// <b>x-sdk-type extension:</b> When a model declares <c>x-sdk-type:
/// BeyondImmersion.Bannou.{SdkName}.{TypeName}</c>, its implementation lives in the named SDK
/// rather than in the owning plugin — the plugin typically passes the whole object opaquely to
/// a helper in <c>sdks/{sdk-name-kebab}/</c> that reads the individual fields. The coverage
/// check honors this by ALSO scanning the SDK source tree — properties that appear in SDK
/// source count as "used" for <c>x-sdk-type</c> models. PascalCase SDK names are converted to
/// kebab-case (<c>Core</c> → <c>core</c>, <c>MusicTheory</c> → <c>music-theory</c>). This
/// preserves the ability to catch fields declared with <c>x-sdk-type</c> but still not read
/// anywhere: the annotation promises the SDK will read the field, and if neither the plugin
/// nor the SDK does, it is still flagged.
/// </para>
/// <para>
/// <b>x-library-type extension:</b> When a model declares <c>x-library-type:
/// "Namespace.TypeName"</c> (e.g. <c>"Json.Patch.JsonPatch"</c>), its field reads live in an
/// external library (typically a NuGet package) whose source is not in this repository. The
/// schema declares which library owns field-level access; the test verifies the declaration
/// by scanning plugin source for either a <c>using Namespace;</c> directive or a qualified
/// reference to the full type name. When the verification succeeds, the per-field check is
/// skipped — the library is trusted to read the fields correctly. When the plugin does NOT
/// reference the declared library (misdeclared annotation, or library never actually
/// adopted), the check falls through to the normal per-field walk and dead fields are still
/// flagged. This mirrors the <c>x-sdk-type</c> trust-by-association pattern for external
/// dependencies, with the key safeguard that the scan confirms the plugin actually imports
/// the library before granting passthrough. The schema annotation alone is not sufficient;
/// the code must match the claim.
/// </para>
/// <para>
/// <b>Passthrough detection:</b> When a model is <c>$ref</c>'d by some other property in the
/// schema (i.e., it is an embedded sub-type rather than a top-level request/response/event)
/// AND the model's PascalCase type name appears anywhere in plugin source (e.g.,
/// <c>body.DeviceInfo</c>, <c>new DeviceInfo()</c>, or <c>session.DeviceInfo = ...</c>), the
/// per-field check is skipped for that model. The plugin is treating the type as a
/// passthrough unit — the whole object is forwarded to consumers (other services via
/// events, clients via responses, internal storage) without per-field inspection. Top-level
/// entry-point models (request/response/top-level event types) are NEVER marked passthrough
/// because they are not <c>$ref</c>'d by anything else; their per-field checks always run,
/// which catches the case of an entry-point model receiving a field the service never reads.
/// The two-condition rule (referenced AND named) prevents name collisions on top-level types
/// from silently hiding real gaps — a top-level model like <c>LoginRequest</c> would never
/// be marked passthrough even if some unrelated source line contained the literal token
/// <c>LoginRequest</c>, because <c>LoginRequest</c> is not <c>$ref</c>'d by anything.
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
///   <item><b>$ref navigation is partial</b> — passthrough detection (see above) skips a
///     model's per-field check when the model is <c>$ref</c>'d by another property AND its
///     type name appears in plugin source. Models <c>$ref</c>'d but whose type name does
///     NOT appear in source are still walked in isolation; if only the parent's other
///     properties are read, the sub-type's fields are still flagged.</item>
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
    private static readonly string SdksDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "sdks");

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
    /// TEMPORARY: Pre-implementation plugins where every endpoint throws
    /// <c>NotImplementedException</c>. Their schemas are designed and code scaffolding
    /// exists, but no business logic has been written yet. Each pre-implementation
    /// plugin contributes hundreds of unused fields (craft: 303, divine: 157), drowning
    /// out signal from operational plugins where individual gaps are actionable.
    /// </summary>
    /// <remarks>
    /// This list deliberately violates the "no exclusions" rule documented in this class's
    /// XML summary. It is a temporary signal-to-noise concession until each listed plugin
    /// has its business logic implemented. <b>When a plugin's endpoints stop throwing
    /// <c>NotImplementedException</c>, remove it from this list immediately</b> — the
    /// remaining unused fields will then be real implementation gaps that the test is
    /// meant to catch.
    /// </remarks>
    private static readonly HashSet<string> PreImplementationPlugins = new(StringComparer.Ordinal)
    {
        "arbitration",
        "craft",
        "divine",
        "environment",
        "website",
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

    /// <summary>
    /// Matches a PascalCase identifier appearing as a generic type parameter
    /// (<c>List&lt;Foo&gt;</c>, <c>Dictionary&lt;K, Foo&gt;</c>, <c>Foo&lt;Bar?&gt;</c>).
    /// Captures type names the dot-access and initializer-assignment patterns miss because
    /// C# generic-parameter syntax uses angle brackets rather than dots or equals signs.
    /// Required for passthrough detection to recognize that a plugin is aware of an embedded
    /// sub-type via its collection/dictionary usage (e.g. <c>List&lt;ContractClauseDefinition&gt;</c>
    /// on an internal model tells the scan that the plugin is forwarding the whole object).
    /// </summary>
    private static readonly Regex GenericParameterPattern = new(
        @"[<,]\s*([A-Z][a-zA-Z0-9_]*)(?=\s*[,>?])",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches a PascalCase identifier immediately after the <c>new</c> keyword
    /// (<c>new Foo()</c>, <c>new Foo { }</c>, <c>new Foo&lt;T&gt;()</c>, <c>new Foo[]</c>).
    /// Captures type names used in constructor and allocation expressions that the
    /// dot-access and initializer-assignment patterns miss. This is the form the
    /// passthrough detection XML doc already documents (<c>new DeviceInfo()</c>) — the
    /// existing two regexes did not actually match that shape prior to this pattern
    /// being added.
    /// </summary>
    private static readonly Regex ConstructorPattern = new(
        @"\bnew\s+([A-Z][a-zA-Z0-9_]*)",
        RegexOptions.Compiled);

    [Fact]
    public void SchemaFields_MustBeReadByPluginCode()
    {
        if (!Directory.Exists(SchemasDir) || !Directory.Exists(PluginsDir))
            return;

        // Cache plugin source scans — one dir may host multiple schema files (-api, -configuration,
        // -service-events, -client-events) and we don't want to re-scan source files four times.
        var pluginReferenceCache = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Cache SDK source scans separately — a single SDK (e.g. sdks/core/) is referenced by
        // x-sdk-type declarations in many schemas, so we scan each SDK directory once.
        var sdkReferenceCache = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Cache raw plugin source content for x-library-type namespace/import scanning.
        // This is distinct from pluginReferenceCache (which holds extracted PascalCase
        // identifiers) because library references appear in positions — `using Namespace;`,
        // dotted type names — that the identifier regexes do not capture.
        var libraryReferenceCache = new Dictionary<string, string>(StringComparer.Ordinal);

        var violations = new List<Violation>();

        foreach (var schemaFile in Directory.EnumerateFiles(SchemasDir, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(schemaFile);
            if (ExcludedSchemaFiles.Contains(fileName))
                continue;

            var serviceName = DeriveServiceName(fileName);
            if (serviceName == null)
                continue; // Filename doesn't match any known suffix pattern — out of scope.

            // TEMPORARY: Skip pre-implementation plugins (see PreImplementationPlugins above).
            if (PreImplementationPlugins.Contains(serviceName))
                continue;

            var pluginDir = Path.Combine(PluginsDir, $"lib-{serviceName}");
            if (!Directory.Exists(pluginDir))
                continue; // Pre-implementation stub or shared schema — skip silently.

            if (!pluginReferenceCache.TryGetValue(pluginDir, out var coveredProperties))
            {
                coveredProperties = CollectSourcePropertyReferences(pluginDir);
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

            // Pre-pass: collect the set of model names that are referenced by some other
            // property in this schema file via $ref (direct, allOf composition, or array
            // items). These are the candidates for passthrough detection — top-level
            // entry-point models (requests, responses, top-level events) are not in this
            // set because nothing $ref's them. See "Passthrough detection" in class XML.
            var modelsReferencedByOthers = CollectReferencedModelNames(schemas);

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

                // PASSTHROUGH DETECTION: If this model is $ref'd by another property in the
                // schema (so it's an embedded sub-type, not a top-level entry point) AND its
                // type name appears in plugin source, the plugin is forwarding the whole
                // object to consumers without inspecting individual fields. Skip the per-field
                // check — downstream consumers (events, responses, services) read the fields.
                // Both conditions are required: the $ref check ensures top-level types like
                // request/response models are never silently hidden by name collisions.
                if (modelsReferencedByOthers.Contains(modelName)
                    && coveredProperties.Contains(modelName))
                {
                    continue;
                }

                // When a model declares x-sdk-type, its implementation lives in the named SDK
                // rather than in the owning plugin. Collect the SDK's property references once
                // (cached) and include them in the coverage check for this model's fields.
                HashSet<string>? sdkProperties = null;
                var sdkTypeFullName = GetSdkTypeFullName(modelMapping);
                if (sdkTypeFullName != null)
                {
                    var sdkDir = ResolveSdkSourceDir(sdkTypeFullName);
                    if (sdkDir != null)
                    {
                        if (!sdkReferenceCache.TryGetValue(sdkDir, out sdkProperties))
                        {
                            sdkProperties = CollectSourcePropertyReferences(sdkDir);
                            sdkReferenceCache[sdkDir] = sdkProperties;
                        }
                    }
                }

                // When a model declares x-library-type, its field reads live in an external
                // library (e.g., a NuGet package like JsonPatch.Net) rather than in plugin or
                // SDK source. If the plugin actually references the declared library — via a
                // `using` directive on the containing namespace or a qualified type reference —
                // the annotation is verified and the entire per-field check is skipped. If the
                // plugin does NOT reference the library (misdeclared annotation), the check
                // falls through to the normal per-field walk and dead fields are still flagged.
                var libraryTypeFullName = GetLibraryTypeFullName(modelMapping);
                if (libraryTypeFullName != null
                    && PluginSourceReferencesLibrary(pluginDir, libraryTypeFullName, libraryReferenceCache))
                {
                    continue;
                }

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
                    // Accept reads from the declared SDK source tree when x-sdk-type is set.
                    if (sdkProperties != null && sdkProperties.Contains(pascalName))
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
    /// Walks every non-Generated, non-test <c>.cs</c> file in a source directory and collects
    /// every PascalCase identifier that appears after a dot or immediately before a simple
    /// <c>=</c> assignment. The resulting set is the "used property" bag against which schema
    /// property names are checked. Used for both plugin directories (<c>plugins/lib-{service}/</c>)
    /// and SDK directories (<c>sdks/{sdk-name}/</c>, reached via <c>x-sdk-type</c> on a model).
    /// </summary>
    /// <remarks>
    /// This is inherently a lexical approximation. Common property names (<c>Name</c>,
    /// <c>Count</c>, <c>Id</c>, <c>Type</c>, <c>Value</c>) will almost always be present because
    /// they appear in unrelated code. This causes the test to under-report violations for fields
    /// sharing those names — a known limitation documented on the test class.
    /// </remarks>
    private static HashSet<string> CollectSourcePropertyReferences(string sourceDir)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);

        foreach (var csFile in Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, csFile);
            if (relative.Contains("Generated", StringComparison.OrdinalIgnoreCase))
                continue;
            // Defensive: skip any .tests project content inside the source dir. By convention
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
            foreach (Match match in GenericParameterPattern.Matches(content))
                references.Add(match.Groups[1].Value);
            foreach (Match match in ConstructorPattern.Matches(content))
                references.Add(match.Groups[1].Value);
        }

        return references;
    }

    /// <summary>
    /// Extracts the <c>x-sdk-type</c> value from a model mapping, if declared. When a schema
    /// model carries <c>x-sdk-type: BeyondImmersion.Bannou.{SdkName}.{TypeName}</c>, its
    /// property reads live in the named SDK rather than in the owning plugin. The coverage
    /// check honors this by ALSO scanning the SDK source tree — a field declared with
    /// <c>x-sdk-type</c> is still flagged if it isn't read in either the plugin OR the SDK.
    /// </summary>
    private static string? GetSdkTypeFullName(YamlMappingNode modelMapping)
    {
        if (modelMapping.Children.TryGetValue(new YamlScalarNode("x-sdk-type"), out var sdkTypeNode)
            && sdkTypeNode is YamlScalarNode scalar
            && !string.IsNullOrEmpty(scalar.Value))
        {
            return scalar.Value;
        }
        return null;
    }

    /// <summary>
    /// Extracts the <c>x-library-type</c> value from a model mapping, if declared. When a
    /// schema model carries <c>x-library-type: "Namespace.TypeName"</c>, its field reads
    /// live in an external library (e.g., a NuGet package) rather than in plugin or SDK
    /// source. The test verifies the declaration by scanning plugin source for either a
    /// <c>using</c> directive against the containing namespace or a qualified reference to
    /// the type. If the schema claims an external library owner but the plugin doesn't
    /// actually reference it, passthrough does NOT activate and the fields are walked
    /// individually — protecting against misdeclared annotations.
    /// </summary>
    private static string? GetLibraryTypeFullName(YamlMappingNode modelMapping)
    {
        if (modelMapping.Children.TryGetValue(new YamlScalarNode("x-library-type"), out var libraryTypeNode)
            && libraryTypeNode is YamlScalarNode scalar
            && !string.IsNullOrEmpty(scalar.Value))
        {
            return scalar.Value;
        }
        return null;
    }

    /// <summary>
    /// Verifies that a plugin's source references the declared library — either via a
    /// <c>using Namespace;</c> directive against the containing namespace, or via a
    /// qualified reference to the full type name (e.g., <c>Json.Patch.JsonPatch</c>).
    /// The match is a simple substring search over the cached plugin source content; the
    /// scan intentionally tolerates trailing punctuation and whitespace that would appear
    /// in real usage (semicolons after usings, angle brackets before generic parameters,
    /// parentheses before constructor invocations).
    /// </summary>
    /// <remarks>
    /// The cache in <paramref name="libraryReferenceCache"/> stores per-plugin raw
    /// concatenated source content, not the PascalCase-identifier set used by the property
    /// coverage check. That separate cache is necessary because library references appear
    /// in non-identifier positions (inside <c>using</c> directives, in fully-qualified type
    /// references with dots embedded) that the identifier-extraction regexes do not capture.
    /// </remarks>
    private static bool PluginSourceReferencesLibrary(
        string pluginDir,
        string libraryFullTypeName,
        Dictionary<string, string> libraryReferenceCache)
    {
        if (!libraryReferenceCache.TryGetValue(pluginDir, out var combinedSource))
        {
            combinedSource = ReadAllPluginSource(pluginDir);
            libraryReferenceCache[pluginDir] = combinedSource;
        }

        // Extract containing namespace: "Json.Patch.JsonPatch" -> "Json.Patch"
        var lastDot = libraryFullTypeName.LastIndexOf('.');
        var libraryNamespace = lastDot > 0 ? libraryFullTypeName[..lastDot] : libraryFullTypeName;

        // Accept either:
        //   (1) `using Namespace;` or `using Namespace.Deeper;` — captures top-level imports
        //   (2) A fully-qualified type reference `Namespace.TypeName` anywhere in source
        //       (e.g., `Json.Patch.JsonPatch` inside a method body)
        return combinedSource.Contains($"using {libraryNamespace};", StringComparison.Ordinal)
            || combinedSource.Contains($"using {libraryNamespace}.", StringComparison.Ordinal)
            || combinedSource.Contains(libraryFullTypeName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads every non-Generated, non-test <c>.cs</c> file in a plugin directory and
    /// concatenates the raw content into a single string. Used for library-import scanning
    /// where the matching target (e.g., <c>using Json.Patch;</c>) appears in a position
    /// that the PascalCase-identifier extraction regexes cannot capture.
    /// </summary>
    private static string ReadAllPluginSource(string sourceDir)
    {
        var sb = new StringBuilder();
        foreach (var csFile in Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, csFile);
            if (relative.Contains("Generated", StringComparison.OrdinalIgnoreCase))
                continue;
            if (relative.Contains(".tests", StringComparison.OrdinalIgnoreCase))
                continue;
            if (relative.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;

            try
            {
                sb.AppendLine(File.ReadAllText(csFile));
            }
            catch
            {
                // Skip unreadable files — same tolerance as the identifier-extraction scan.
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Maps an <c>x-sdk-type</c> full name (e.g. <c>BeyondImmersion.Bannou.Core.ResponseTransformation</c>)
    /// to the corresponding SDK source directory (<c>sdks/core/</c>). Returns null if the full
    /// name does not match the expected <c>BeyondImmersion.Bannou.{SdkName}.*</c> pattern, or if
    /// the resolved directory does not exist on disk. PascalCase SDK names are converted to
    /// kebab-case (e.g. <c>MusicTheory</c> → <c>music-theory</c>).
    /// </summary>
    private static string? ResolveSdkSourceDir(string sdkTypeFullName)
    {
        const string prefix = "BeyondImmersion.Bannou.";
        if (!sdkTypeFullName.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        var remainder = sdkTypeFullName[prefix.Length..];
        var dotIdx = remainder.IndexOf('.');
        if (dotIdx <= 0)
            return null;
        var sdkName = remainder[..dotIdx];
        var kebabName = PascalToKebab(sdkName);
        var dir = Path.Combine(SdksDir, kebabName);
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>
    /// Converts a PascalCase identifier to kebab-case by lowercasing each character and
    /// inserting a hyphen before each uppercase character (except the first).
    /// <c>Core</c> → <c>core</c>, <c>MusicTheory</c> → <c>music-theory</c>.
    /// </summary>
    private static string PascalToKebab(string pascal)
    {
        if (string.IsNullOrEmpty(pascal)) return pascal;
        var sb = new StringBuilder();
        for (int i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (i > 0 && char.IsUpper(c)) sb.Append('-');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
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

    /// <summary>
    /// Walks every model in <paramref name="schemas"/> and collects the set of model names
    /// that are <c>$ref</c>'d by some other property in this schema file. Used by passthrough
    /// detection to distinguish embedded sub-types (legitimate passthrough candidates) from
    /// top-level entry-point models (request/response/event types that are not <c>$ref</c>'d
    /// by anything else and must always have their fields checked individually).
    /// </summary>
    /// <remarks>
    /// Cross-file <c>$ref</c>s (e.g., <c>common-api.yaml#/components/schemas/Foo</c>) are
    /// captured by their last path segment, which is the same identifier the target file uses
    /// for the type. This means a model defined in file A and <c>$ref</c>'d from file B is
    /// recognized as referenced when file A is the current scan target. <c>common-*.yaml</c>
    /// files are excluded wholesale by the test, so cross-file targets pointing into common
    /// schemas don't appear in violation reports either way.
    /// </remarks>
    private static HashSet<string> CollectReferencedModelNames(YamlMappingNode schemas)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var modelEntry in schemas.Children)
        {
            if (modelEntry.Value is not YamlMappingNode parentMapping)
                continue;
            if (!parentMapping.Children.TryGetValue(new YamlScalarNode("properties"), out var pNode))
                continue;
            if (pNode is not YamlMappingNode pMapping)
                continue;

            foreach (var propEntry in pMapping.Children)
            {
                if (propEntry.Value is not YamlMappingNode propMapping)
                    continue;

                var targetType = ExtractRefTarget(propMapping);
                if (targetType != null)
                    referenced.Add(targetType);
            }
        }

        return referenced;
    }

    /// <summary>
    /// Extracts a <c>$ref</c> target type name from a property's schema mapping. Handles three
    /// shapes: direct <c>$ref</c>, <c>allOf</c> composition (used to attach <c>nullable: true</c>
    /// to a referenced type in OpenAPI 3.0.x), and array <c>items.$ref</c>. Returns the bare
    /// type name (e.g., <c>"DeviceInfo"</c>) regardless of whether the source path is local
    /// (<c>#/components/schemas/Foo</c>) or cross-file (<c>common-api.yaml#/components/schemas/Foo</c>).
    /// Returns <c>null</c> if no <c>$ref</c> is found in any of the recognized shapes.
    /// </summary>
    private static string? ExtractRefTarget(YamlMappingNode propMapping)
    {
        // Direct: { $ref: '#/components/schemas/Foo' }
        if (propMapping.Children.TryGetValue(new YamlScalarNode("$ref"), out var refNode)
            && refNode is YamlScalarNode refScalar)
        {
            return ParseRefTargetName(refScalar.Value);
        }

        // allOf composition: { allOf: [{ $ref: '#/components/schemas/Foo' }] }
        // Used by OpenAPI 3.0.x to attach `nullable: true` and similar siblings to a $ref.
        if (propMapping.Children.TryGetValue(new YamlScalarNode("allOf"), out var allOfNode)
            && allOfNode is YamlSequenceNode allOfSeq)
        {
            foreach (var item in allOfSeq)
            {
                if (item is YamlMappingNode itemMapping
                    && itemMapping.Children.TryGetValue(new YamlScalarNode("$ref"), out var inner)
                    && inner is YamlScalarNode innerScalar)
                {
                    return ParseRefTargetName(innerScalar.Value);
                }
            }
        }

        // Array items: { type: array, items: { $ref: '#/components/schemas/Foo' } }
        // Recurse so items.allOf.[$ref] also works if the item itself is a composition.
        if (propMapping.Children.TryGetValue(new YamlScalarNode("items"), out var itemsNode)
            && itemsNode is YamlMappingNode itemsMapping)
        {
            return ExtractRefTarget(itemsMapping);
        }

        return null;
    }

    /// <summary>
    /// Parses a <c>$ref</c> string into the bare type name (the last path segment).
    /// <c>"#/components/schemas/Foo"</c> → <c>"Foo"</c>.
    /// <c>"common-api.yaml#/components/schemas/Foo"</c> → <c>"Foo"</c>.
    /// </summary>
    private static string? ParseRefTargetName(string? refValue)
    {
        if (string.IsNullOrEmpty(refValue))
            return null;
        var idx = refValue.LastIndexOf('/');
        return idx >= 0 ? refValue[(idx + 1)..] : refValue;
    }
}
