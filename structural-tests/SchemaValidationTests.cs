using System.Text.RegularExpressions;
using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Validates OpenAPI schema files in the schemas/ directory for compliance with
/// SCHEMA-RULES.md and development tenets. Uses line-based YAML scanning (no
/// YAML parser dependency) to catch common schema authoring mistakes.
/// </summary>
public class SchemaValidationTests
{
    private static readonly string SchemasDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "schemas");

    /// <summary>
    /// Gets all non-generated schema YAML files.
    /// </summary>
    private static IEnumerable<string> GetSchemaFiles(string pattern = "*.yaml")
    {
        if (!Directory.Exists(SchemasDir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(SchemasDir, pattern))
        {
            // Skip Generated/ subdirectory files
            var relativePath = Path.GetRelativePath(SchemasDir, file);
            if (relativePath.StartsWith("Generated", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return file;
        }
    }

    /// <summary>
    /// Strips inline YAML comments (e.g., "value  # comment" -> "value").
    /// </summary>
    private static string StripYamlComment(string value)
    {
        // Don't strip # inside quoted strings; for unquoted values, # preceded by whitespace is a comment
        var commentIndex = value.IndexOf('#');
        if (commentIndex > 0 && value[commentIndex - 1] == ' ')
        {
            return value[..commentIndex].TrimEnd();
        }
        return value;
    }

    /// <summary>
    /// Validates that all enum values in schema files use PascalCase.
    /// Wrong casing breaks NSwag C# code generation and causes serialization mismatches.
    /// Per SCHEMA-RULES.md and QUALITY TENETS naming conventions.
    /// </summary>
    [Fact]
    public void SchemaEnumValues_ArePascalCase()
    {
        var pascalCasePattern = new Regex(@"^[A-Z][a-zA-Z0-9]*$", RegexOptions.Compiled);
        var violations = new List<string>();

        foreach (var file in GetSchemaFiles())
        {
            var fileName = Path.GetFileName(file);
            var lines = File.ReadAllLines(file);
            var inEnum = false;
            var isIntegerEnum = false;
            var inComponentSchemas = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                var indent = line.Length - trimmed.Length;

                // Track whether we're inside components/schemas (where domain enums live).
                // Enums in paths/ (e.g., header parameter constraints like "websocket") are
                // protocol-level constants, not domain enums subject to PascalCase rules.
                if (indent == 0 && trimmed.StartsWith("components:", StringComparison.Ordinal))
                {
                    // Could be components section; schemas: follows at indent 2
                    continue;
                }
                if (indent == 2 && trimmed == "schemas:")
                {
                    inComponentSchemas = true;
                    continue;
                }
                // Any other top-level key exits components/schemas
                if (indent == 0 && trimmed.Length > 0 && !trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    inComponentSchemas = false;
                }

                // Only validate enums defined in components/schemas
                if (!inComponentSchemas && !inEnum)
                    continue;

                // Track type: integer/number to skip integer enums
                if (trimmed.StartsWith("type:", StringComparison.Ordinal))
                {
                    var typeValue = trimmed["type:".Length..].Trim();
                    isIntegerEnum = typeValue is "integer" or "number";
                }

                // Detect enum: array start
                if (trimmed.StartsWith("enum:", StringComparison.Ordinal))
                {
                    // Integer enums use x-enumNames for naming; values are numeric
                    if (isIntegerEnum)
                    {
                        continue;
                    }

                    inEnum = true;
                    // Check for inline enum: [Value1, Value2] format
                    var inlineMatch = Regex.Match(trimmed, @"enum:\s*\[(.+)\]");
                    if (inlineMatch.Success)
                    {
                        var values = inlineMatch.Groups[1].Value.Split(',')
                            .Select(v => StripYamlComment(v.Trim().Trim('\'', '"')));
                        foreach (var val in values)
                        {
                            if (!string.IsNullOrEmpty(val) && !pascalCasePattern.IsMatch(val))
                            {
                                violations.Add($"{fileName}:{i + 1}: enum value '{val}'");
                            }
                        }
                        inEnum = false;
                    }
                    continue;
                }

                if (!inEnum)
                    continue;

                // Enum items are "  - Value" lines
                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    var value = StripYamlComment(trimmed[2..].Trim().Trim('\'', '"'));
                    if (!string.IsNullOrEmpty(value) && !pascalCasePattern.IsMatch(value))
                    {
                        violations.Add($"{fileName}:{i + 1}: enum value '{value}'");
                    }
                }
                else if (trimmed.Length > 0 && !trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    // Non-item, non-comment line = end of enum block
                    inEnum = false;
                    isIntegerEnum = false;
                }
            }
        }

        var grouped = violations
            .GroupBy(v => v.Split(':')[0])
            .OrderBy(g => g.Key);

        var report = string.Join("\n", grouped.Select(g =>
            $"\n  [{g.Key}] ({g.Count()} violation(s)):\n" +
            string.Join("\n", g.Select(v => $"    - {v}"))));

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} non-PascalCase enum value(s) across " +
            $"{grouped.Count()} schema file(s):{report}");
    }

    /// <summary>
    /// Validates that every endpoint in API schema files has an x-permissions declaration.
    /// Missing x-permissions means the endpoint is either unprotected or will fail
    /// permission registration. Per FOUNDATION TENETS.
    /// </summary>
    [Fact]
    public void SchemaEndpoints_HaveXPermissions()
    {
        var violations = new List<string>();

        foreach (var file in GetSchemaFiles("*-api.yaml"))
        {
            var fileName = Path.GetFileName(file);
            var lines = File.ReadAllLines(file);
            var inPaths = false;
            var currentPath = string.Empty;
            var currentMethod = string.Empty;
            var methodIndent = -1;
            var foundPermissions = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                var indent = line.Length - trimmed.Length;

                // Detect paths: section
                if (trimmed == "paths:")
                {
                    inPaths = true;
                    continue;
                }

                if (!inPaths)
                    continue;

                // Top-level key after paths (e.g., components:) exits paths section
                if (indent == 0 && trimmed.Length > 0 && !trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    // Check last method before exiting
                    if (!string.IsNullOrEmpty(currentMethod) && !foundPermissions)
                    {
                        violations.Add($"{fileName}: {currentMethod} {currentPath}");
                    }
                    inPaths = false;
                    continue;
                }

                // Path entry (e.g., "  /account/list:")
                if (indent == 2 && trimmed.StartsWith("/", StringComparison.Ordinal) && trimmed.EndsWith(":", StringComparison.Ordinal))
                {
                    // Check previous method
                    if (!string.IsNullOrEmpty(currentMethod) && !foundPermissions)
                    {
                        violations.Add($"{fileName}: {currentMethod} {currentPath}");
                    }
                    currentPath = trimmed.TrimEnd(':');
                    currentMethod = string.Empty;
                    foundPermissions = false;
                    continue;
                }

                // HTTP method (e.g., "    post:")
                if (indent == 4 && (trimmed == "post:" || trimmed == "get:" ||
                    trimmed == "put:" || trimmed == "delete:"))
                {
                    // Check previous method
                    if (!string.IsNullOrEmpty(currentMethod) && !foundPermissions)
                    {
                        violations.Add($"{fileName}: {currentMethod} {currentPath}");
                    }
                    currentMethod = trimmed.TrimEnd(':');
                    methodIndent = indent;
                    foundPermissions = false;
                    continue;
                }

                // x-permissions within method block
                if (!string.IsNullOrEmpty(currentMethod) && indent > methodIndent &&
                    trimmed.StartsWith("x-permissions:", StringComparison.Ordinal))
                {
                    foundPermissions = true;
                }
            }

            // Check last method in file
            if (!string.IsNullOrEmpty(currentMethod) && !foundPermissions)
            {
                violations.Add($"{fileName}: {currentMethod} {currentPath}");
            }
        }

        var grouped = violations
            .GroupBy(v => v.Split(':')[0])
            .OrderBy(g => g.Key);

        var report = string.Join("\n", grouped.Select(g =>
            $"\n  [{g.Key}] ({g.Count()} endpoint(s)):\n" +
            string.Join("\n", g.Select(v => $"    - {v}"))));

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} endpoint(s) without x-permissions across " +
            $"{grouped.Count()} schema file(s) (FOUNDATION TENETS: X-Permissions):{report}");
    }

    /// <summary>
    /// Validates that configuration schema env var names do not contain hyphens.
    /// Hyphens in env var names cause shell issues. Service names with hyphens
    /// must use underscores in env var prefixes (e.g., game-session -> GAME_SESSION_).
    /// </summary>
    [Fact]
    public void SchemaConfigEnvVars_HaveNoHyphens()
    {
        var violations = new List<string>();

        foreach (var file in GetSchemaFiles("*-configuration.yaml"))
        {
            var fileName = Path.GetFileName(file);
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();

                // Match env: "SOME-VAR" or env: SOME-VAR patterns
                if (!trimmed.StartsWith("env:", StringComparison.Ordinal))
                    continue;

                var envValue = trimmed["env:".Length..].Trim().Trim('"', '\'');
                if (envValue.Contains('-'))
                {
                    violations.Add($"{fileName}:{i + 1}: env: {envValue}");
                }
            }
        }

        var grouped = violations
            .GroupBy(v => v.Split(':')[0])
            .OrderBy(g => g.Key);

        var report = string.Join("\n", grouped.Select(g =>
            $"\n  [{g.Key}] ({g.Count()} violation(s)):\n" +
            string.Join("\n", g.Select(v => $"    - {v}"))));

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} config env var(s) with hyphens across " +
            $"{grouped.Count()} schema file(s):{report}");
    }

    /// <summary>
    /// Validates that client event schemas use allOf with BaseClientEvent.
    /// Client events must inherit from the base event schema for proper
    /// WebSocket event routing (eventName, timestamp fields).
    /// </summary>
    [Fact]
    public void SchemaClientEvents_UseAllOfWithBaseClientEvent()
    {
        var violations = new List<string>();

        foreach (var file in GetSchemaFiles("*-client-events.yaml"))
        {
            var fileName = Path.GetFileName(file);
            var lines = File.ReadAllLines(file);
            var inSchemas = false;
            var currentSchema = string.Empty;
            var schemaIndent = -1;
            var foundAllOf = false;
            var foundBaseRef = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                var indent = line.Length - trimmed.Length;

                // Detect schemas section
                if (trimmed == "schemas:" || trimmed == "components:")
                {
                    // schemas is usually under components, but could be top-level in client-events
                    continue;
                }

                // Schema definition (indented type name ending with :)
                // Client event schemas are typically at indent 4 (under components/schemas)
                if (indent >= 4 && indent <= 6 && trimmed.EndsWith(":", StringComparison.Ordinal) &&
                    !trimmed.StartsWith("-", StringComparison.Ordinal) &&
                    !trimmed.StartsWith("#", StringComparison.Ordinal) &&
                    trimmed.Contains("ClientEvent", StringComparison.Ordinal) &&
                    !trimmed.StartsWith("BaseClientEvent", StringComparison.Ordinal))
                {
                    // Check previous schema
                    if (!string.IsNullOrEmpty(currentSchema) && !foundBaseRef)
                    {
                        violations.Add($"{fileName}: {currentSchema}");
                    }

                    currentSchema = trimmed.TrimEnd(':').Trim();
                    schemaIndent = indent;
                    foundAllOf = false;
                    foundBaseRef = false;
                    inSchemas = true;
                    continue;
                }

                if (!inSchemas || string.IsNullOrEmpty(currentSchema))
                    continue;

                // Check if we've left the schema definition
                if (indent <= schemaIndent && trimmed.Length > 0 &&
                    !trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    // Check and reset
                    if (!foundBaseRef)
                    {
                        violations.Add($"{fileName}: {currentSchema}");
                    }
                    currentSchema = string.Empty;
                    inSchemas = false;
                    i--; // Re-process this line
                    continue;
                }

                if (trimmed.StartsWith("allOf:", StringComparison.Ordinal))
                    foundAllOf = true;

                if (foundAllOf && trimmed.Contains("BaseClientEvent", StringComparison.Ordinal))
                    foundBaseRef = true;
            }

            // Check last schema
            if (!string.IsNullOrEmpty(currentSchema) && !foundBaseRef)
            {
                violations.Add($"{fileName}: {currentSchema}");
            }
        }

        var grouped = violations
            .GroupBy(v => v.Split(':')[0])
            .OrderBy(g => g.Key);

        var report = string.Join("\n", grouped.Select(g =>
            $"\n  [{g.Key}] ({g.Count()} event(s)):\n" +
            string.Join("\n", g.Select(v => $"    - {v}"))));

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} client event schema(s) not using allOf with " +
            $"BaseClientEvent across {grouped.Count()} file(s):{report}");
    }
}
