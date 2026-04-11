using System.Text.RegularExpressions;
using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Structural enforcement of T25 type safety at inter-service boundaries.
/// </summary>
/// <remarks>
/// <para>
/// This test catches the specific anti-pattern where plugin code calls
/// <c>Guid.TryParse</c>, <c>int.TryParse</c>, or similar string-to-primitive
/// conversion methods on a property of a generated response or event model.
/// Such calls indicate a schema type mismatch: the owning service declared the
/// field as <c>string</c> in its OpenAPI schema, but a consumer expects a
/// stronger type like <c>Guid</c>. The fix is to tighten the schema so NSwag
/// generates the correct type on both sides — never paper over the mismatch
/// with runtime parsing in plugin code.
/// </para>
/// <para>
/// <b>This is item #12 of issue #720's follow-up.</b> Genesis's
/// <c>GenesisSeedEvolutionListener.OnPhaseChangedAsync</c> calls
/// <c>Guid.TryParse(spawnResponse.ActorId, ...)</c> because Actor's schema
/// declares <c>actorId</c> as <c>string</c> while Genesis stores <c>ActorId</c>
/// as <c>Guid?</c>. Grepping the codebase for the same pattern reveals two
/// additional instances: lib-puppetmaster and lib-leaderboard both call
/// <c>Guid.(Try)?Parse(evt.XyzId, ...)</c> on event model properties that
/// should have been declared as <c>uuid</c> in the source schema.
/// </para>
/// <para>
/// <b>Detection rule:</b> For every line in a non-generated, non-test plugin
/// source file that contains a call matching
/// <c>(Guid|int|long|DateTimeOffset|DateTime).(Try)?Parse(</c>, if the first
/// argument is a <b>member access on a suspicious-looking variable</b>, the
/// call is flagged. A "suspicious variable" is one whose name matches
/// conventional generated-response or generated-event naming:
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
/// </list>
/// </remarks>
public class TypeSafetyTests
{
    /// <summary>
    /// Regex matching any primitive parse call. Captures:
    /// <list type="bullet">
    ///   <item>Group 1: the primitive type name (<c>Guid</c>, <c>int</c>, etc.)</item>
    ///   <item>Group 2: <c>Try</c> if present, empty otherwise</item>
    ///   <item>Group 3: the first-identifier portion of the argument</item>
    /// </list>
    /// The regex only matches when the argument starts with
    /// <c>identifier.</c> or <c>identifier?.</c> — i.e., a member access on
    /// the named identifier. If the argument is a local string, indexer, or
    /// method call, the regex does not match and the line is not checked.
    /// </summary>
    private static readonly Regex ParseCallOnMemberAccessRegex = new(
        @"\b(Guid|int|long|DateTimeOffset|DateTime)\.(Try)?Parse\(\s*" +
        @"(?<var>[a-zA-Z_][a-zA-Z0-9_]*)\s*\??\s*\.",
        RegexOptions.Compiled);

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
            $"These indicate a schema type mismatch: the owning service declared the field as 'string' but " +
            $"the consumer expects a stronger type. Fix the schema, not the consumer:\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Per-file scan
    // ═════════════════════════════════════════════════════════════════════════

    private static void ScanFileForParseViolations(string[] lines, string fileRelativePath, List<string> violations)
    {
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

            var snippet = BuildSnippet(line);
            violations.Add($"{fileRelativePath}:{i + 1}: {snippet}");
        }
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
