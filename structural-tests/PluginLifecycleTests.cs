using System.Text.RegularExpressions;
using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Structural enforcement of plugin lifecycle exception handling — item #16 of issue #720's follow-up.
/// </summary>
/// <remarks>
/// <para>
/// Plugin lifecycle method overrides (<c>OnStartAsync</c>, <c>OnRunningAsync</c>, <c>OnShutdownAsync</c>,
/// <c>ConfigureServices</c>) must either propagate exceptions or explicitly signal failure. A bare
/// catch-and-continue hides initialization failures behind a successful-looking return value, causing
/// the plugin to come up with stale in-memory state while the lifecycle system reports success.
/// </para>
/// <para>
/// <b>Concrete bug this catches:</b> Genesis populates its in-memory wallet map from MySQL during
/// <c>OnStartAsync</c>. If the MySQL query throws, the outer catch logs and falls through to
/// <c>return true;</c> — startup reports success even though the wallet map is empty. All subsequent
/// currency transactions for pre-existing genesis entities silently miss the growth listener, and the
/// in-memory state drifts from MySQL until the plugin is restarted.
/// </para>
/// <para>
/// <b>Detection rule:</b> For every override of a lifecycle method in a plugin class, every
/// <c>catch (Exception ...)</c> block that is NOT filtered with a <c>when</c> clause must contain at
/// least ONE of the following escape patterns within the catch body:
/// </para>
/// <list type="bullet">
///   <item><c>throw </c> — rethrow or throw a new exception type (trailing space excludes identifiers starting with "throw")</item>
///   <item><c>throw;</c> — bare rethrow (no trailing space)</item>
///   <item><c>return false;</c> — explicit startup-failure signal; legal only for <c>OnStartAsync</c> because it returns <c>Task&lt;bool&gt;</c></item>
/// </list>
/// <para>
/// <b>Exempt catches</b> (pass unconditionally):
/// </para>
/// <list type="bullet">
///   <item><c>catch (OperationCanceledException ...)</c> — cancellation is a separate concern and is handled explicitly during shutdown</item>
///   <item>Filtered catches <c>catch (Exception ex) when (...)</c> — the <c>when</c> clause scopes handling intentionally</item>
///   <item>Catches of specific exception types that are NOT <c>Exception</c> itself (e.g., <c>catch (InvalidOperationException)</c>) — narrowly scoped by design</item>
/// </list>
/// <para>
/// <b>Scope:</b> The scan applies to <c>*Plugin.cs</c> files at the top level of each <c>plugins/lib-*/</c>
/// directory (excluding <c>.tests</c> plugins and <c>Services/</c> subdirectories). Only catches appearing
/// directly within the override method's brace range are considered — catches inside private helper methods
/// called from the override are out of scope for this test. The helpers are separate methods with their
/// own <c>try</c>/<c>catch</c> semantics and are evaluated by other structural tests.
/// </para>
/// <para>
/// <b>What this test does NOT catch:</b>
/// </para>
/// <list type="bullet">
///   <item>Semantic correctness of the escape handler (e.g., is the rethrown exception the right type?)</item>
///   <item>Correctness of a <c>when</c> filter condition — the filter is trusted as the intentional mechanism</item>
///   <item>Wildcard <c>catch</c> blocks with no exception type at all (<c>catch { }</c>) — these are extremely unusual in this codebase</item>
///   <item>Catches inside helper methods called from the override — those are their own scopes</item>
///   <item>Substring false positives: a <c>catch (Exception ...)</c> inside a string literal or multi-line comment could be incorrectly detected. This is rare enough in practice that the simple lexical approach is preferred over a full C# parser.</item>
/// </list>
/// </remarks>
public class PluginLifecycleTests
{
    /// <summary>
    /// Regex for lifecycle method override signatures. Matches the single-line method header up to
    /// the opening parenthesis of the parameter list. The method name capture group is used to
    /// distinguish <c>OnStartAsync</c> (which has <c>return false;</c> available as an escape pattern
    /// because it returns <c>Task&lt;bool&gt;</c>) from the other lifecycle methods (which do not).
    /// </summary>
    private static readonly Regex LifecycleMethodRegex = new(
        @"\boverride\s+.*\b(OnStartAsync|OnRunningAsync|OnShutdownAsync|ConfigureServices)\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void PluginLifecycle_NoExceptionSwallow()
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

            // Only scan *Plugin.cs files at the plugin's top level (not Services/ subdirs).
            foreach (var sourceFile in Directory.EnumerateFiles(pluginDir, "*Plugin.cs", SearchOption.TopDirectoryOnly))
            {
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
                ScanFileForLifecycleCatchViolations(lines, fileRelativeToRepo, violations);
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} catch (Exception) block(s) in plugin lifecycle method overrides that " +
            $"neither propagate the exception nor signal explicit failure (item #16 of issue #720's follow-up). " +
            $"A bare catch-and-continue in a lifecycle method override hides initialization failures and causes " +
            $"the plugin to appear healthy while running with stale or incomplete state. Either rethrow, throw a " +
            $"different exception, or (for OnStartAsync only) return false.\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // File scan
    // ═════════════════════════════════════════════════════════════════════════

    private static void ScanFileForLifecycleCatchViolations(string[] lines, string fileRelativePath, List<string> violations)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            // Skip lines that are entirely comments.
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            var match = LifecycleMethodRegex.Match(line);
            if (!match.Success)
                continue;

            var methodName = match.Groups[1].Value;

            // Locate the opening brace of the method body. The opening brace may be on the
            // same line as the signature or on a subsequent line. If the method is abstract
            // (ends in ';' before reaching '{'), the brace search will fail within the window
            // and we silently skip.
            int methodOpenBraceLine = -1;
            int methodOpenBraceCol = -1;
            for (var j = i; j < lines.Length && j < i + 10; j++)
            {
                var searchLine = lines[j];

                // Skip comment-only continuation lines while scanning for the opening brace.
                if (j != i && searchLine.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;

                var braceIdx = FindOpenBraceIgnoringStrings(searchLine);
                if (braceIdx >= 0)
                {
                    methodOpenBraceLine = j;
                    methodOpenBraceCol = braceIdx;
                    break;
                }
            }

            if (methodOpenBraceLine < 0)
                continue; // Expression-bodied override or abstract signature — no body to scan.

            var methodCloseBraceLine = FindMatchingCloseBrace(lines, methodOpenBraceLine, methodOpenBraceCol);
            if (methodCloseBraceLine < 0)
                continue; // Brace matching failed — give up on this method.

            // Scan every line inside the method body for catch (Exception ...) blocks.
            // Nested catches (inside foreach loops, nested try blocks, local functions declared
            // inside the override) are all in scope — the override is responsible for the
            // outcome of its own body.
            ScanBodyRangeForCatchViolations(
                lines, methodOpenBraceLine + 1, methodCloseBraceLine, methodName, fileRelativePath, violations);

            // Skip past the method body so we don't double-scan if the outer loop would
            // re-enter the same range on a later iteration (defensive — the signature regex
            // is unlikely to match inside the body, but this keeps scans strictly partitioned).
            i = methodCloseBraceLine;
        }
    }

    private static void ScanBodyRangeForCatchViolations(
        string[] lines, int startLine, int endLine, string methodName,
        string fileRelativePath, List<string> violations)
    {
        for (var i = startLine; i <= endLine; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            // Match "catch (Exception<delim>" or "catch(Exception<delim>" where delim is a
            // space (followed by a variable name) or a closing paren. This deliberately
            // excludes OperationCanceledException, InvalidOperationException, ApiException,
            // and any other specific exception type — those are narrowly-scoped catches that
            // pass unconditionally per the tenet rule.
            if (!IsCatchException(line))
                continue;

            var catchStartLine = i;

            // Walk forward looking for either a `when` filter or the opening brace of the
            // catch body. Comment-only lines are skipped during the walk.
            var filterFound = false;
            int catchOpenBraceLine = -1;
            int catchOpenBraceCol = -1;

            for (var j = i; j < lines.Length && j < i + 10 && j <= endLine; j++)
            {
                var searchLine = lines[j];

                if (j != i && searchLine.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    continue;

                // A `when` filter may appear on the same line as `catch` or on any line
                // before the opening brace. Both " when (" and " when(" are valid C#.
                if (searchLine.Contains(" when (", StringComparison.Ordinal) ||
                    searchLine.Contains(" when(", StringComparison.Ordinal))
                {
                    filterFound = true;
                }

                var braceIdx = FindOpenBraceIgnoringStrings(searchLine);
                if (braceIdx >= 0)
                {
                    catchOpenBraceLine = j;
                    catchOpenBraceCol = braceIdx;
                    break;
                }
            }

            if (filterFound)
                continue; // Filtered catch — the filter is the mechanism, pass.

            if (catchOpenBraceLine < 0)
                continue; // Could not locate the opening brace — give up.

            var catchCloseBraceLine = FindMatchingCloseBrace(lines, catchOpenBraceLine, catchOpenBraceCol);
            if (catchCloseBraceLine < 0)
                continue; // Brace matching failed — give up.

            // Scan every line of the catch body for an escape pattern. Comment-only lines
            // are skipped so code comments discussing the swallow cannot be mistaken for
            // a fix.
            var hasEscape = false;
            for (var j = catchOpenBraceLine; j <= catchCloseBraceLine; j++)
            {
                var bodyLine = lines[j];
                var bodyTrimmed = bodyLine.TrimStart();
                if (bodyTrimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (ContainsEscapePattern(bodyLine, methodName))
                {
                    hasEscape = true;
                    break;
                }
            }

            if (!hasEscape)
            {
                var snippet = BuildViolationSnippet(lines, catchStartLine, catchOpenBraceLine, catchCloseBraceLine);
                violations.Add($"{fileRelativePath}:{catchStartLine + 1}: [{methodName}] {snippet}");
            }
        }
    }

    /// <summary>
    /// Returns true if the line contains <c>catch (Exception&lt;delim&gt;</c> or
    /// <c>catch(Exception&lt;delim&gt;</c> where the delimiter is either a space (before an
    /// identifier name) or a closing paren. This tightly matches the base <see cref="Exception"/>
    /// type and deliberately does NOT match specific derived types like
    /// <c>OperationCanceledException</c>, <c>InvalidOperationException</c>, or <c>ApiException</c>.
    /// </summary>
    private static bool IsCatchException(string line)
    {
        return line.Contains("catch (Exception ", StringComparison.Ordinal)
            || line.Contains("catch (Exception)", StringComparison.Ordinal)
            || line.Contains("catch(Exception ", StringComparison.Ordinal)
            || line.Contains("catch(Exception)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns true if the line contains any escape pattern that satisfies the tenet.
    /// <c>return false;</c> is only a valid escape pattern for <c>OnStartAsync</c> overrides,
    /// which return <c>Task&lt;bool&gt;</c> and interpret <c>false</c> as an explicit startup-failure
    /// signal to the plugin loader.
    /// </summary>
    private static bool ContainsEscapePattern(string line, string methodName)
    {
        // Rethrow patterns. "throw " (with trailing space) matches `throw new`, `throw ex;`,
        // and `throw ex,` — the trailing space excludes identifiers starting with "throw".
        // "throw;" catches the bare rethrow with no trailing space.
        if (line.Contains("throw ", StringComparison.Ordinal) ||
            line.Contains("throw;", StringComparison.Ordinal))
            return true;

        // Explicit startup-failure signal. Only legal for OnStartAsync because it returns
        // Task<bool>. Other lifecycle methods return Task (OnRunningAsync, OnShutdownAsync)
        // or void (ConfigureServices), so `return false;` is either a compile error or a
        // return-from-a-sub-method rather than an override exit — neither is a valid escape.
        if (string.Equals(methodName, "OnStartAsync", StringComparison.Ordinal) &&
            line.Contains("return false;", StringComparison.Ordinal))
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
        return $"{catchText} — body spans {bodyLineCount} line(s), contains no throw/return false; (catch-and-continue swallows the failure)";
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
    /// a method signature or <c>catch</c> keyword, so we do not handle them here.
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
    /// Mirrors the brace-matching implementation used in <c>CatchHandlingTests</c> and
    /// <c>DistributedLockTests</c>. The three copies are intentionally independent to keep
    /// the tests decoupled — if a future test needs the same logic, consider factoring it
    /// into a shared helper in <c>structural-tests/</c>.
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
