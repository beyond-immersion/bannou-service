using BeyondImmersion.BannouService.Attributes;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Structural enforcement of T25 type safety at inter-service boundaries.
/// </summary>
/// <remarks>
/// <para>
/// This test detects plugin code calling <c>Guid.TryParse</c>, <c>int.TryParse</c>,
/// or similar string-to-primitive conversion methods on a property of a generated
/// response or event model. Each occurrence means the owning schema declares the
/// field as <c>string</c> while a consumer operates on it as a stronger primitive
/// type (<c>Guid</c>, <c>int</c>, etc.). The detection is structural; the cause
/// requires investigation — possible causes include a schema that should have
/// declared a stronger format (e.g., <c>format: uuid</c>), a deliberately
/// polymorphic identifier whose typing is intentional, or hierarchy-isolation
/// string typing where the lower-layer owner cannot enumerate consumer types.
/// </para>
/// <para>
/// <b>Added per issue #720 follow-up.</b> Known occurrences at time of
/// introduction: Genesis's <c>GenesisSeedEvolutionListener.OnPhaseChangedAsync</c>
/// calls <c>Guid.TryParse(spawnResponse.ActorId, ...)</c> against Actor's
/// <c>actorId: string</c>; lib-puppetmaster and lib-leaderboard have similar
/// call sites against event model string properties. Whether the correct
/// resolution is a schema tightening, a schema split, or an acknowledged
/// polymorphism is a per-site design decision.
/// </para>
/// <para>
/// <b>Polymorphic type exemption:</b> Properties annotated with
/// <see cref="PolymorphicTypeAttribute"/> (emitted from <c>x-polymorphic-type-properties</c>
/// in the schema) are excluded from violation detection. The attribute declares that
/// the string typing is intentional — the field carries different primitive types
/// depending on a discriminator or context.
/// </para>
/// <para>
/// <b>Detection rule:</b> For every line in a non-generated, non-test plugin
/// source file that contains a call matching
/// <c>(Guid|int|long|DateTimeOffset|DateTime).(Try)?Parse(</c>, if the first
/// argument is a <b>member access on a suspicious-looking variable</b>, the
/// call is flagged — unless the accessed property has <c>[PolymorphicType]</c>.
/// A "suspicious variable" is one whose name matches conventional generated-response
/// or generated-event naming:
/// </para>
/// <list type="bullet">
///   <item>Exact name match (case-insensitive): <c>evt</c>, <c>ev</c>,
///     <c>event</c>, <c>response</c>, <c>resp</c>, <c>result</c>, <c>notification</c></item>
///   <item>Suffix match: any variable name ending in <c>Response</c>, <c>Result</c>,
///     <c>Event</c>, or <c>Notification</c></item>
/// </list>
/// <para>
/// <b>What this rule does NOT catch</b> (documented false negatives):
/// </para>
/// <list type="bullet">
///   <item>Parses from variables with non-conventional names (e.g.,
///     <c>Guid.Parse(assetMetadata.AssetId)</c> in lib-save-load — <c>assetMetadata</c>
///     is a generated response but doesn't match the suspicious pattern).
///     Can be caught by extending the suspicious-variable list later.</item>
///   <item>Parses inside chained expressions like
///     <c>Guid.Parse(GetFoo().Bar)</c> where the source is a method call.</item>
///   <item>Parses from strings NOT declared on a generated model (e.g., from
///     a cache key split, from an ABML parameter dictionary, from a RabbitMQ
///     header). These are legitimate string-to-primitive conversions that
///     must exist because the source value has no schema.</item>
/// </list>
/// <para>
/// <b>What this rule does NOT flag</b> (correctly-scoped exemptions verified
/// against the existing codebase):
/// </para>
/// <list type="bullet">
///   <item>State-store key parsing: <c>Guid.TryParse(itemIdStr, out ...)</c>,
///     <c>Guid.TryParse(parts[1], out ...)</c>, etc. where the variable is
///     a local string or array indexer.</item>
///   <item>Cursor / pagination ints: <c>int.TryParse(body.Cursor, out ...)</c>
///     — <c>body.Cursor</c> is a request field, not a response.</item>
///   <item>ABML handler parameters:
///     <c>value is string str &amp;&amp; Guid.TryParse(str, out var parsed)</c>
///     — parses a local <c>str</c> variable, not a member access.</item>
///   <item>Infrastructure protocol parsing (RabbitMQ <c>MessageId</c>,
///     RtpEngine bencode lengths, etc.) — the source variable doesn't match
///     the suspicious pattern.</item>
///   <item><c>[PolymorphicType]</c>-annotated properties — the schema explicitly
///     declares these as intentionally polymorphic via <c>x-polymorphic-type-properties</c>.</item>
/// </list>
/// </remarks>
public class TypeSafetyTests
{
    /// <summary>
    /// Regex matching any primitive parse call. Captures:
    /// <list type="bullet">
    ///   <item>Group "var": the variable name being accessed</item>
    ///   <item>Group "prop": the property name being parsed</item>
    /// </list>
    /// The regex only matches when the argument starts with
    /// <c>identifier.</c> or <c>identifier?.</c> — i.e., a member access on
    /// the named identifier. If the argument is a local string, indexer, or
    /// method call, the regex does not match and the line is not checked.
    /// </summary>
    private static readonly Regex ParseCallOnMemberAccessRegex = new(
        @"\b(Guid|int|long|DateTimeOffset|DateTime)\.(Try)?Parse\(\s*" +
        @"(?<var>[a-zA-Z_][a-zA-Z0-9_]*)\s*\??\s*\.(?<prop>[a-zA-Z_][a-zA-Z0-9_]*)",
        RegexOptions.Compiled);

    /// <summary>
    /// Property names that have <see cref="PolymorphicTypeAttribute"/> on any generated
    /// model type. Properties in this set are intentionally polymorphic and should not
    /// be flagged as type-safety violations. Built once via reflection at test startup.
    /// </summary>
    private static readonly Lazy<HashSet<string>> PolymorphicPropertyNames = new(BuildPolymorphicPropertySet);

    /// <summary>
    /// Variable names treated as suspicious regardless of casing (but case-insensitive
    /// exact match). A variable called <c>evt</c>, <c>response</c>, <c>result</c>,
    /// <c>notification</c>, etc. is almost certainly holding a generated model instance.
    /// </summary>
    private static readonly HashSet<string> SuspiciousExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "evt",
        "ev",
        "event",
        "response",
        "resp",
        "result",
        "notification",
    };

    /// <summary>
    /// Suffixes that identify a variable as holding a generated model. Matched against
    /// the variable name with <c>EndsWith</c> (case-sensitive on the capital letter:
    /// <c>spawnResponse</c> ends with <c>Response</c>, <c>someresponse</c> does not).
    /// </summary>
    private static readonly string[] SuspiciousSuffixes =
    [
        "Response",
        "Result",
        "Event",
        "Notification",
    ];

    [Fact]
    public void Services_NoParseOnGeneratedResponseProperties()
    {
        var violations = new List<string>();
        var pluginsDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins");
        if (!Directory.Exists(pluginsDir))
            return;

        foreach (var pluginDir in Directory.GetDirectories(pluginsDir, "lib-*"))
        {
            var dirName = Path.GetFileName(pluginDir);
            if (dirName.EndsWith(".tests", StringComparison.Ordinal))
                continue;

            foreach (var sourceFile in Directory.EnumerateFiles(pluginDir, "*.cs", SearchOption.AllDirectories))
            {
                var relativeToPlugin = Path.GetRelativePath(pluginDir, sourceFile);
                if (relativeToPlugin.StartsWith("Generated", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (relativeToPlugin.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || relativeToPlugin.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    continue;

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(sourceFile);
                }
                catch
                {
                    continue;
                }

                var fileRelativeToRepo = Path.GetRelativePath(TestAssemblyDiscovery.RepoRoot, sourceFile);
                ScanFileForParseViolations(lines, fileRelativeToRepo, violations);
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} primitive Parse/TryParse call(s) on properties of generated response or event models " +
            $"(per IMPLEMENTATION TENETS T25 — type safety across schema boundaries). " +
            $"Each call parses a schema-declared string field to a stronger primitive type at runtime. " +
            $"Investigate each occurrence — possible causes are schema tightening needed, deliberately " +
            $"polymorphic typing, or hierarchy-isolation string typing:\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Per-file scan
    // ═════════════════════════════════════════════════════════════════════════

    private static void ScanFileForParseViolations(string[] lines, string fileRelativePath, List<string> violations)
    {
        var polymorphicNames = PolymorphicPropertyNames.Value;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Skip lines that are entirely comments. XML doc comments and block-comment
            // content can reference Parse patterns in prose without being real call sites.
            if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("*", StringComparison.Ordinal))
                continue;

            var match = ParseCallOnMemberAccessRegex.Match(line);
            if (!match.Success)
                continue;

            var variableName = match.Groups["var"].Value;
            if (!IsSuspiciousVariableName(variableName))
                continue;

            // Skip properties marked with [PolymorphicType] in the schema.
            // These are intentionally string-typed fields that consumers legitimately parse.
            var propertyName = match.Groups["prop"].Value;
            if (polymorphicNames.Contains(propertyName))
                continue;

            var snippet = BuildSnippet(line);
            violations.Add($"{fileRelativePath}:{i + 1}: {snippet}");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // [PolymorphicType] attribute discovery
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scans all loaded assemblies for properties with <see cref="PolymorphicTypeAttribute"/>
    /// and returns a set of their PascalCase property names.
    /// </summary>
    private static HashSet<string> BuildPolymorphicPropertySet()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // Scan assemblies that contain generated models: bannou-service (shared models/events)
            // and lib-* plugin assemblies (plugin-specific models). The bannou-service assembly
            // name is "bannou-service" (project name), not "BeyondImmersion.*".
            var assemblyName = assembly.GetName().Name;
            if (assemblyName == null)
                continue;
            if (assemblyName != "bannou-service"
                && !assemblyName.StartsWith("lib-", StringComparison.Ordinal))
                continue;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetCustomAttribute<PolymorphicTypeAttribute>() != null)
                    {
                        names.Add(property.Name);
                    }
                }
            }
        }

        return names;
    }

    private static bool IsSuspiciousVariableName(string name)
    {
        if (SuspiciousExactNames.Contains(name))
            return true;

        foreach (var suffix in SuspiciousSuffixes)
        {
            // EndsWith with Ordinal — we want case-sensitive suffix matching so that
            // "spawnResponse" (capital R) matches "Response" but "someresponse" (lowercase)
            // does not. The lowercase form should be caught by the exact-name set.
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string BuildSnippet(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length > 140)
            trimmed = trimmed.Substring(0, 140) + "...";
        return trimmed;
    }
}
