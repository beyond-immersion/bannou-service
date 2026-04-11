using System.Text.RegularExpressions;
using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Structural enforcement of inter-service error handling per IMPLEMENTATION TENETS T7.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>catch (ApiException</c> block in plugin source code must handle the exception
/// explicitly by either rethrowing, returning a mapped failure status, or publishing an
/// error/failure event. A bare log-and-continue silently drops failures from inter-service
/// calls without informing the caller, without publishing an observable event, and without
/// affecting the service's return status. This matches the pattern that produced item #15
/// of issue #720's follow-up — <c>EnsureActorTemplatesForRegistrationAsync</c> logging and
/// continuing on every non-409 ApiException while the rest of the system assumed the
/// template was registered.
/// </para>
/// <para>
/// <b>Detection rule:</b> For every line matching <c>catch (ApiException</c> or
/// <c>catch(ApiException</c> in a non-generated, non-test plugin source file:
/// </para>
/// <list type="bullet">
///   <item>If the catch has a <c>when</c> clause (anywhere between the <c>catch</c>
///     keyword and the opening brace), the catch is considered explicitly scoped and passes.
///     The filter is the mechanism — the developer is saying "I only catch these specific
///     statuses and intentionally handle them."</item>
///   <item>Otherwise, the catch body must contain at least ONE of the following substrings:
///     <list type="bullet">
///       <item><c>throw </c> or <c>throw;</c> — rethrow or throw a new exception</item>
///       <item><c>return (StatusCodes.</c> — return a mapped failure status</item>
///       <item><c>return ((StatusCodes)</c> — return a status cast from an integer</item>
///       <item><c>TryPublishErrorAsync(</c> — publish a structured error event on the message bus</item>
///       <item>Any call matching <c>Publish*(Failed|Error|Cancelled)*Async(</c> — publish a domain-specific failure event</item>
///     </list>
///   </item>
/// </list>
/// <para>
/// <b>What this test does NOT catch:</b>
/// </para>
/// <list type="bullet">
///   <item>Semantic correctness of the handler (e.g., is the returned status the right one?)</item>
///   <item>The correctness of a <c>when</c> filter condition</item>
///   <item>Wildcard <c>catch (Exception)</c> blocks that swallow <c>ApiException</c> as a side effect</item>
///   <item>Error handling delegated to a helper method (the helper's body is not followed)</item>
///   <item>Substring false positives: a <c>catch (ApiException ...)</c> inside a string literal
///     or multi-line comment could be incorrectly detected. This is rare enough in practice
///     that the simple lexical approach is preferred over a full C# parser.</item>
///   <item><c>return null;</c> or <c>return false;</c> — these are NOT escape patterns.
///     A <c>Try*</c> helper that returns null on ApiException without publishing an error
///     event is intentionally flagged (see item #13 of issue #720's follow-up — the
///     deferred bond materialization silent-drop bug).</item>
/// </list>
/// </remarks>
public class CatchHandlingTests
{
    /// <summary>
    /// Regex for domain-specific failure event publisher method names. Matches method names
    /// of the form <c>Publish{Entity}{Action}(Failed|Error|Cancelled){Suffix}Async(</c>. This
    /// catches both generic patterns (<c>PublishXyzFailedAsync</c>) and the Genesis-specific
    /// <c>PublishGenesisEntityTransitionFailedAsync</c> name. The regex is intentionally loose
    /// — any method name containing the three keywords plus <c>Async(</c> after <c>Publish</c>
    /// qualifies.
    /// </summary>
    private static readonly Regex PublishFailureEventRegex = new(
        @"Publish\w*(Failed|Error|Cancelled)\w*Async\(",
        RegexOptions.Compiled);

    [Fact]
    public void Services_CatchApiException_MustHandleOrPropagate()
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
                ScanFileForCatchViolations(lines, fileRelativeToRepo, violations);
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} catch (ApiException) block(s) that do not handle or propagate the exception " +
            $"(per IMPLEMENTATION TENETS T7 — inter-service error handling):\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // File scan
    // ═════════════════════════════════════════════════════════════════════════

    private static void ScanFileForCatchViolations(string[] lines, string fileRelativePath, List<string> violations)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Skip lines that are entirely comments.
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            // Look for "catch (ApiException" or "catch(ApiException" as a substring.
            // This will match variants like:
            //   catch (ApiException)
            //   catch (ApiException ex)
            //   catch (ApiException<T> ex)
            //   catch (ApiException ex) when (...)
            if (!line.Contains("catch (ApiException", StringComparison.Ordinal) &&
                !line.Contains("catch(ApiException", StringComparison.Ordinal))
                continue;

            // Determine whether this catch has a "when" filter. The filter may be on the
            // same line as the catch keyword, or on subsequent lines (up to the opening brace).
            // If we find "when" before the opening brace, the catch is scoped and we pass.
            var catchStartLine = i;
            var filterFound = false;
            var openBraceLine = -1;
            var openBraceCol = -1;

            for (var j = i; j < lines.Length && j < i + 10; j++)
            {
                var searchLine = lines[j];

                // Skip comment-only lines while searching
                if (j != i && searchLine.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;

                // Check for "when" filter — space-delimited to avoid matching "whenever"
                // or similar substrings. Both " when (" and " when(" are legal C#.
                if (searchLine.Contains(" when (", StringComparison.Ordinal) ||
                    searchLine.Contains(" when(", StringComparison.Ordinal))
                {
                    filterFound = true;
                }

                // Look for the opening brace of the catch body.
                var braceIdx = FindOpenBraceIgnoringStrings(searchLine);
                if (braceIdx >= 0)
                {
                    openBraceLine = j;
                    openBraceCol = braceIdx;
                    break;
                }
            }

            if (filterFound)
                continue; // Filtered catch — the filter is the mechanism, pass.

            if (openBraceLine < 0)
                continue; // Could not locate the opening brace within 10 lines — give up.

            // Find the matching closing brace using a brace-counting walker that respects
            // strings and comments.
            var closeBraceLine = FindMatchingCloseBrace(lines, openBraceLine, openBraceCol);
            if (closeBraceLine < 0)
                continue; // Brace matching failed — give up.

            // Scan lines from openBraceLine through closeBraceLine for any escape pattern.
            // We scan the whole line content rather than slicing out only the body portion
            // because:
            //   - The catch signature itself does not contain escape patterns, so including
            //     the signature line in the scan is safe.
            //   - Same-line catches like `catch (ApiException) { throw; }` are correctly
            //     detected because the `throw;` substring appears on the line.
            var hasEscape = false;
            for (var j = openBraceLine; j <= closeBraceLine; j++)
            {
                var bodyLine = lines[j];
                var bodyTrimmed = bodyLine.TrimStart();
                if (bodyTrimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (ContainsEscapePattern(bodyLine))
                {
                    hasEscape = true;
                    break;
                }
            }

            if (!hasEscape)
            {
                var snippet = BuildViolationSnippet(lines, catchStartLine, openBraceLine, closeBraceLine);
                violations.Add($"{fileRelativePath}:{catchStartLine + 1}: {snippet}");
            }
        }
    }

    /// <summary>
    /// Returns true if the line contains any escape pattern that satisfies the tenet.
    /// </summary>
    private static bool ContainsEscapePattern(string line)
    {
        // Rethrow patterns. "throw " (with trailing space) matches `throw new`, `throw ex;`,
        // and `throw ex,` — the trailing space excludes identifiers starting with "throw".
        // "throw;" catches the bare rethrow with no trailing space.
        if (line.Contains("throw ", StringComparison.Ordinal) ||
            line.Contains("throw;", StringComparison.Ordinal))
            return true;

        // Mapped status return. Catches both `return (StatusCodes.XXX, ...)` and
        // `return ((StatusCodes)ex.StatusCode, ...)`.
        if (line.Contains("return (StatusCodes.", StringComparison.Ordinal) ||
            line.Contains("return ((StatusCodes)", StringComparison.Ordinal))
            return true;

        // Error event publish.
        if (line.Contains("TryPublishErrorAsync(", StringComparison.Ordinal))
            return true;

        // Domain-specific failure event publish.
        if (PublishFailureEventRegex.IsMatch(line))
            return true;

        return false;
    }

    /// <summary>
    /// Builds a one-line violation description. Shows the catch keyword line and the
    /// number of lines in the catch body so the developer can locate the issue quickly.
    /// </summary>
    private static string BuildViolationSnippet(string[] lines, int catchLine, int openBraceLine, int closeBraceLine)
    {
        var catchText = lines[catchLine].Trim();
        if (catchText.Length > 100)
            catchText = catchText.Substring(0, 100) + "...";

        var bodyLineCount = closeBraceLine - openBraceLine + 1;
        return $"{catchText} — body spans {bodyLineCount} line(s), contains no throw/return/TryPublishErrorAsync/Publish*FailedAsync";
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Source parsing helpers
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the column of the first <c>{</c> on the given line that is not inside a
    /// string literal or comment. Returns -1 if no such brace exists.
    /// </summary>
    /// <remarks>
    /// This is a simplified version of the brace walker — it only processes a single
    /// line and only tracks single-line strings, char literals, and line comments.
    /// Multi-line strings and block comments are unlikely to appear on a line containing
    /// a <c>catch</c> keyword, so we do not handle them here.
    /// </remarks>
    private static int FindOpenBraceIgnoringStrings(string line)
    {
        var inString = false;
        var inChar = false;

        for (var c = 0; c < line.Length; c++)
        {
            var ch = line[c];
            var next = (c + 1 < line.Length) ? line[c + 1] : '\0';

            if (inString)
            {
                if (ch == '\\' && next != '\0') { c++; continue; }
                if (ch == '"') inString = false;
                continue;
            }

            if (inChar)
            {
                if (ch == '\\' && next != '\0') { c++; continue; }
                if (ch == '\'') inChar = false;
                continue;
            }

            if (ch == '/' && next == '/')
                return -1; // rest of line is comment
            if (ch == '"')
            {
                inString = true;
                continue;
            }
            if (ch == '\'')
            {
                inChar = true;
                continue;
            }
            if (ch == '{')
                return c;
        }

        return -1;
    }

    /// <summary>
    /// Given the line and column of an opening brace, walks forward counting braces and
    /// returns the line number of the matching closing brace. Respects string literals,
    /// character literals, single-line comments, multi-line comments, and verbatim strings.
    /// Returns -1 if the braces cannot be matched.
    /// </summary>
    /// <remarks>
    /// Matches the brace-matching implementation used in <c>DistributedLockTests</c>. The
    /// two copies are intentionally independent to keep the tests decoupled — if a future
    /// test needs the same logic, consider factoring it into a shared helper in
    /// <c>structural-tests/</c>.
    /// </remarks>
    private static int FindMatchingCloseBrace(string[] sourceLines, int openLine, int openCol)
    {
        var depth = 1; // we are already inside the opening brace
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var inVerbatimString = false;
        var inChar = false;

        for (var i = openLine; i < sourceLines.Length; i++)
        {
            var line = sourceLines[i];
            // Reset single-line comment state at the start of each physical line.
            inLineComment = false;

            // On the opening brace line, start scanning AFTER the opening brace.
            // On subsequent lines, start from column 0.
            var startCol = (i == openLine) ? openCol + 1 : 0;

            for (var c = startCol; c < line.Length; c++)
            {
                var ch = line[c];
                var next = (c + 1 < line.Length) ? line[c + 1] : '\0';

                if (inLineComment)
                    break; // rest of line is comment

                if (inBlockComment)
                {
                    if (ch == '*' && next == '/')
                    {
                        inBlockComment = false;
                        c++; // skip '/'
                    }
                    continue;
                }

                if (inVerbatimString)
                {
                    if (ch == '"')
                    {
                        if (next == '"')
                        {
                            c++; // escaped quote in verbatim string
                            continue;
                        }
                        inVerbatimString = false;
                    }
                    continue;
                }

                if (inString)
                {
                    if (ch == '\\' && next != '\0')
                    {
                        c++; // skip escaped character
                        continue;
                    }
                    if (ch == '"')
                        inString = false;
                    continue;
                }

                if (inChar)
                {
                    if (ch == '\\' && next != '\0')
                    {
                        c++;
                        continue;
                    }
                    if (ch == '\'')
                        inChar = false;
                    continue;
                }

                // Not inside any literal or comment — process structural characters.
                if (ch == '/' && next == '/')
                {
                    inLineComment = true;
                    break;
                }
                if (ch == '/' && next == '*')
                {
                    inBlockComment = true;
                    c++;
                    continue;
                }
                if (ch == '@' && next == '"')
                {
                    inVerbatimString = true;
                    c++;
                    continue;
                }
                if (ch == '$' && next == '"')
                {
                    // Interpolated string — treat as ordinary string for brace counting.
                    // Well-formed interpolations balance braces internally, so we ignore
                    // them here for simplicity.
                    inString = true;
                    c++;
                    continue;
                }
                if (ch == '"')
                {
                    inString = true;
                    continue;
                }
                if (ch == '\'')
                {
                    inChar = true;
                    continue;
                }
                if (ch == '{')
                {
                    depth++;
                    continue;
                }
                if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                    continue;
                }
            }
        }

        return -1;
    }
}
