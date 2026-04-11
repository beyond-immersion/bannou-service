using BeyondImmersion.BannouService.Attributes;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Structural enforcement of <see cref="RequiresDistributedLockAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// For every method annotated with <see cref="RequiresDistributedLockAttribute"/>
/// (directly on a concrete class method, or on an interface method that the class
/// implements), this test verifies that the method's source body acquires a
/// distributed lock BEFORE any state store write on every code path.
/// </para>
/// <para>
/// <b>Detection rule:</b>
/// </para>
/// <list type="bullet">
///   <item>The scanner extracts the method body from the plugin source file by
///     locating the method signature and walking forward counting braces.</item>
///   <item>A "direct state write" is any line containing
///     <c>.SaveAsync(</c>, <c>.DeleteAsync(</c>, <c>.TrySaveAsync(</c>, or
///     <c>.UpdateWithRetryAsync(</c>.</item>
///   <item>A "writing helper" is any non-public instance method declared in the
///     same source file whose body contains a direct state write.</item>
///   <item>A "helper-write call" is any line in the annotated method body that
///     invokes a writing helper by name (e.g., <c>UpdateEntityPhaseOnlyAsync(</c>).</item>
///   <item>The first <c>LockAsync(</c> call in the method body establishes the
///     lock boundary. Any direct write or helper-write call appearing at a lower
///     line number than the lock boundary is a violation. If no <c>LockAsync(</c>
///     call exists at all, every write and helper-write call is a violation.</item>
/// </list>
/// <para>
/// <b>What this test does NOT catch:</b> writes performed by methods in other
/// classes, writes performed via inter-service client calls, incorrect lock keys,
/// or lock release semantics. The scanner is lexical-only and does not trace
/// control flow through if/else branches — it assumes that any helper call on a
/// path before the lock is reachable.
/// </para>
/// </remarks>
public class DistributedLockTests
{
    /// <summary>
    /// State store write operations that count as "the lock boundary must already be acquired."
    /// </summary>
    private static readonly string[] StateWriteSubstrings =
    [
        ".SaveAsync(",
        ".DeleteAsync(",
        ".TrySaveAsync(",
        ".UpdateWithRetryAsync(",
        ".AddToStringListAsync(",
        ".RemoveFromStringListAsync(",
    ];

    /// <summary>
    /// Regex that matches a C# method signature line (public/private/internal/protected,
    /// optional modifiers, optional return type with generics, method name, and opening paren).
    /// Captures the method name.
    /// </summary>
    private static readonly Regex MethodSignatureRegex = new(
        @"^\s*(public|private|protected|internal)\s+" +
        @"(?:(?:static|async|virtual|override|sealed|partial|unsafe)\s+)*" +
        @"(?:[\w<>,\?\s\[\]\.]+?\s+)" + // return type (non-greedy; tolerates Task<T>, T?, etc.)
        @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void LockRequired_ImplementationsAcquireLockBeforeWrites()
    {
        EnsureAssembliesLoaded();

        // Step 1: Discover every concrete class method that must be checked.
        // A method is checked if either:
        //   (a) the concrete method itself has [RequiresDistributedLock], OR
        //   (b) the method implements an interface method that has [RequiresDistributedLock].
        var checkTargets = DiscoverAnnotatedMethods();
        if (checkTargets.Count == 0)
            return; // No annotations anywhere — nothing to enforce (valid state).

        // Step 2: Group by source file so we parse each file at most once.
        var byFile = new Dictionary<string, List<CheckTarget>>(StringComparer.Ordinal);
        var missingFiles = new List<string>();

        foreach (var target in checkTargets)
        {
            var sourceFile = FindSourceFileForType(target.DeclaringType);
            if (sourceFile == null)
            {
                missingFiles.Add(
                    $"{target.DeclaringType.FullName}: source file not found in plugins/lib-*/ " +
                    $"for method {target.Method.Name}. The structural test cannot verify lock " +
                    $"acquisition without the source file.");
                continue;
            }

            if (!byFile.TryGetValue(sourceFile, out var list))
            {
                list = new List<CheckTarget>();
                byFile[sourceFile] = list;
            }
            list.Add(target);
        }

        // Step 3: For each file, parse writing-helper methods (once), then validate each target.
        var failures = new List<string>();

        foreach (var (sourceFile, targets) in byFile)
        {
            string[] sourceLines;
            try
            {
                sourceLines = File.ReadAllLines(sourceFile);
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(sourceFile)}: failed to read source file — {ex.Message}");
                continue;
            }

            // Build the set of writing helper method names in this source file.
            var writingHelpers = FindWritingHelperMethodNames(sourceLines);

            foreach (var target in targets)
            {
                ValidateMethod(target, sourceFile, sourceLines, writingHelpers, failures);
            }
        }

        // Combine errors. Missing-file errors are surfaced separately so they can't
        // be confused with "the test passed" — they indicate the scanner couldn't do its job.
        var allErrors = missingFiles.Concat(failures).ToList();

        Assert.True(
            allErrors.Count == 0,
            $"Found {allErrors.Count} [RequiresDistributedLock] violation(s):\n" +
            string.Join("\n", allErrors.Select(e => $"  - {e}")));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Discovery
    // ═════════════════════════════════════════════════════════════════════════

    private sealed record CheckTarget(
        Type DeclaringType,
        MethodInfo Method,
        string LockScope,
        string Origin /* "direct" or "interface: ISomething" */);

    private static List<CheckTarget> DiscoverAnnotatedMethods()
    {
        var results = new List<CheckTarget>();

        // Iterate every loaded assembly. We look for both:
        //   (a) annotated interface methods — then find every class that implements them
        //   (b) annotated concrete class methods directly
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        // First pass: collect interface methods with [RequiresDistributedLock].
        var annotatedInterfaceMethods = new List<MethodInfo>();

        foreach (var assembly in assemblies)
        {
            if (!IsProjectAssembly(assembly))
                continue;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException) { continue; }

            foreach (var type in types.Where(t => t.IsInterface))
            {
                foreach (var method in type.GetMethods())
                {
                    if (method.GetCustomAttribute<RequiresDistributedLockAttribute>() != null)
                        annotatedInterfaceMethods.Add(method);
                }
            }
        }

        // Second pass: walk every concrete plugin class.
        //   - If it implements an annotated interface method, add a CheckTarget for its implementation
        //   - If one of its own methods has the attribute directly, add a CheckTarget for that method
        foreach (var assembly in assemblies)
        {
            if (!IsPluginAssembly(assembly))
                continue;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException) { continue; }

            foreach (var type in types)
            {
                if (type.IsInterface || type.IsAbstract || !type.IsClass)
                    continue;

                // (a) Interface-propagated annotations
                foreach (var iface in type.GetInterfaces())
                {
                    // Only interfaces from annotated set
                    var ifaceMethods = annotatedInterfaceMethods
                        .Where(m => m.DeclaringType == iface)
                        .ToList();
                    if (ifaceMethods.Count == 0)
                        continue;

                    var map = type.GetInterfaceMap(iface);
                    for (var i = 0; i < map.InterfaceMethods.Length; i++)
                    {
                        var ifaceMethod = map.InterfaceMethods[i];
                        var annotated = ifaceMethods.FirstOrDefault(m => m == ifaceMethod);
                        if (annotated == null)
                            continue;

                        var concreteMethod = map.TargetMethods[i];
                        var attr = annotated.GetCustomAttribute<RequiresDistributedLockAttribute>()!;
                        results.Add(new CheckTarget(
                            type,
                            concreteMethod,
                            attr.LockScope,
                            $"interface: {iface.Name}"));
                    }
                }

                // (b) Direct annotations on concrete methods
                foreach (var method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var attr = method.GetCustomAttribute<RequiresDistributedLockAttribute>();
                    if (attr == null)
                        continue;

                    // Avoid double-counting if the concrete method override also has the attribute
                    // AND the interface method has it (unlikely but possible). We dedupe by (type, method name).
                    if (results.Any(r => r.DeclaringType == type && r.Method.Name == method.Name))
                        continue;

                    results.Add(new CheckTarget(type, method, attr.LockScope, "direct"));
                }
            }
        }

        return results;
    }

    private static bool IsProjectAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        if (name == null) return false;
        if (name.StartsWith("System", StringComparison.Ordinal)) return false;
        if (name.StartsWith("Microsoft", StringComparison.Ordinal)) return false;
        if (name.StartsWith("netstandard", StringComparison.Ordinal)) return false;
        if (name.StartsWith("mscorlib", StringComparison.Ordinal)) return false;
        if (name.StartsWith("xunit", StringComparison.Ordinal)) return false;
        if (name.StartsWith("Moq", StringComparison.Ordinal)) return false;
        if (name.StartsWith("Castle", StringComparison.Ordinal)) return false;
        return true;
    }

    private static bool IsPluginAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        return name != null
            && name.StartsWith("lib-", StringComparison.Ordinal)
            && !name.EndsWith(".tests", StringComparison.Ordinal);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Source file lookup
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds the source file containing the given type's declaration. The type name may
    /// not match the file name (e.g., a partial class split across dot-separated files,
    /// or a helper class in a subdirectory). This method walks the plugin directory
    /// looking for a file that declares the type.
    /// </summary>
    private static string? FindSourceFileForType(Type type)
    {
        // Determine which plugin directory to search. The assembly name is "lib-{service}";
        // the plugin directory is "plugins/lib-{service}".
        var assemblyName = type.Assembly.GetName().Name;
        if (assemblyName == null || !assemblyName.StartsWith("lib-", StringComparison.Ordinal))
            return null;

        var pluginDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins", assemblyName);
        if (!Directory.Exists(pluginDir))
            return null;

        // Search all .cs files in the plugin (excluding Generated/, bin/, obj/).
        // For each, check whether the file declares the target type by looking for either
        //   "class TypeName"
        // or
        //   "partial class TypeName"
        // as a substring on some line. This is sufficient because plugin code does not
        // use source-generators that emit class declarations at runtime.
        var declPattern = $"class {type.Name}";

        foreach (var file in Directory.EnumerateFiles(pluginDir, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(pluginDir, file);
            if (relative.StartsWith("Generated", StringComparison.OrdinalIgnoreCase))
                continue;
            if (relative.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                continue;

            string text;
            try { text = File.ReadAllText(file); }
            catch { continue; }

            if (!text.Contains(declPattern, StringComparison.Ordinal))
                continue;

            // The declPattern might appear in a comment or string. For the test's purposes,
            // if it appears in a C# source file and is followed (possibly whitespace-separated)
            // by an opening brace or ":" (for inheritance) or "<" (for generics), that's a
            // reasonable signal. We accept any match for simplicity — the downstream body
            // extraction will fail cleanly if this is wrong, which will show up as a
            // missing-body error rather than a false pass.
            return file;
        }

        return null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Writing-helper detection (file-level, called once per file)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scans the source file for non-public instance methods whose bodies contain
    /// at least one direct state write. Returns the set of such method names.
    /// Private/internal/protected methods are considered "helpers"; public methods
    /// are skipped because they are independently scannable entry points.
    /// </summary>
    private static HashSet<string> FindWritingHelperMethodNames(string[] sourceLines)
    {
        var writers = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < sourceLines.Length; i++)
        {
            var line = sourceLines[i];
            var match = MethodSignatureRegex.Match(line);
            if (!match.Success)
                continue;

            // Only consider private/internal/protected methods as "helpers". Public methods
            // are independently checked as entry points (or have their own annotations).
            var visibility = match.Groups[1].Value;
            if (visibility == "public")
                continue;

            var methodName = match.Groups["name"].Value;

            // Constructors have no return type and will not match our regex (which requires
            // a return type). Still, skip "lambda-like" false positives by sanity-checking
            // the method name is PascalCase and not a language keyword.
            if (methodName.Length == 0 || !char.IsUpper(methodName[0]))
                continue;

            // Extract the method body
            var (startBodyLine, endBodyLine) = FindMethodBody(sourceLines, i);
            if (startBodyLine < 0)
                continue;

            // Scan the body for any direct state write
            for (var j = startBodyLine; j <= endBodyLine; j++)
            {
                var bodyLine = sourceLines[j];
                var trimmed = bodyLine.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;
                if (StateWriteSubstrings.Any(w => bodyLine.Contains(w, StringComparison.Ordinal)))
                {
                    writers.Add(methodName);
                    break;
                }
            }
        }

        return writers;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Per-method validation
    // ═════════════════════════════════════════════════════════════════════════

    private static void ValidateMethod(
        CheckTarget target,
        string sourceFile,
        string[] sourceLines,
        HashSet<string> writingHelpers,
        List<string> failures)
    {
        var fileName = Path.GetRelativePath(TestAssemblyDiscovery.RepoRoot, sourceFile);

        // Find the method's signature line in the source file.
        var methodName = target.Method.Name;
        var signatureLine = FindMethodSignatureLine(sourceLines, methodName);
        if (signatureLine < 0)
        {
            failures.Add(
                $"{fileName}: cannot locate method {target.DeclaringType.Name}.{methodName} " +
                $"in source file (expected for [RequiresDistributedLock({target.LockScope})])");
            return;
        }

        // Extract the method body.
        var (bodyStart, bodyEnd) = FindMethodBody(sourceLines, signatureLine);
        if (bodyStart < 0)
        {
            failures.Add(
                $"{fileName}:{signatureLine + 1}: cannot determine method body for " +
                $"{target.DeclaringType.Name}.{methodName}");
            return;
        }

        // Find the first LockAsync( line within the body.
        var lockLine = -1;
        for (var j = bodyStart; j <= bodyEnd; j++)
        {
            var line = sourceLines[j];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (line.Contains(".LockAsync(", StringComparison.Ordinal)
                || line.Contains(" LockAsync(", StringComparison.Ordinal))
            {
                lockLine = j;
                break;
            }
        }

        // Scan body for pre-lock direct writes and pre-lock helper-write calls.
        var preLockViolations = new List<string>();

        for (var j = bodyStart; j <= bodyEnd; j++)
        {
            var line = sourceLines[j];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            // If we're past the lock boundary, stop scanning.
            if (lockLine >= 0 && j >= lockLine)
                break;

            // Direct state write on this line?
            if (StateWriteSubstrings.Any(w => line.Contains(w, StringComparison.Ordinal)))
            {
                preLockViolations.Add(
                    $"line {j + 1}: direct state-store write before LockAsync — {trimmed.Trim()}");
                continue;
            }

            // Call to a writing helper on this line? Match "<HelperName>(" substring.
            foreach (var helper in writingHelpers)
            {
                if (line.Contains(helper + "(", StringComparison.Ordinal))
                {
                    preLockViolations.Add(
                        $"line {j + 1}: call to writing helper {helper} before LockAsync — {trimmed.Trim()}");
                    break;
                }
            }
        }

        // Compose failure messages.
        if (lockLine < 0 && preLockViolations.Count > 0)
        {
            // No lock at all AND there are writes — report as "no lock acquired."
            var summary = string.Join("; ", preLockViolations.Take(3));
            var more = preLockViolations.Count > 3 ? $" (+{preLockViolations.Count - 3} more)" : "";
            failures.Add(
                $"{fileName}:{signatureLine + 1}: {target.DeclaringType.Name}.{methodName} " +
                $"has [RequiresDistributedLock(\"{target.LockScope}\")] " +
                $"({target.Origin}) but does not call LockAsync anywhere in the method body. " +
                $"Writes found: {summary}{more}");
            return;
        }

        foreach (var violation in preLockViolations)
        {
            failures.Add(
                $"{fileName}: {target.DeclaringType.Name}.{methodName} " +
                $"[RequiresDistributedLock(\"{target.LockScope}\")] ({target.Origin}) — {violation}. " +
                $"LockAsync is at line {lockLine + 1}.");
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Source parsing helpers
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Finds the line number of the first method signature matching the given name.
    /// Returns -1 if not found.
    /// </summary>
    private static int FindMethodSignatureLine(string[] sourceLines, string methodName)
    {
        for (var i = 0; i < sourceLines.Length; i++)
        {
            var match = MethodSignatureRegex.Match(sourceLines[i]);
            if (match.Success && match.Groups["name"].Value == methodName)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Given the line number of a method signature, finds the body's opening brace line
    /// (startBody) and matching closing brace line (endBody). Returns (-1, -1) if the
    /// braces cannot be matched (e.g., expression-bodied method or parsing failure).
    /// </summary>
    /// <remarks>
    /// The brace matcher is a simple character-by-character walker that respects:
    ///   - Single-line <c>//</c> comments
    ///   - Multi-line <c>/* ... */</c> comments
    ///   - Character literals <c>'{'</c>, <c>'}'</c>
    ///   - Ordinary string literals <c>"{"</c>, <c>"}"</c> (with backslash escapes)
    ///   - Verbatim string literals <c>@"..."</c>
    ///   - Interpolated string literals <c>$"..."</c> (no interpolation bracket tracking;
    ///     naive pass assumes well-formed interpolations do not contain unbalanced braces)
    /// This is not a full C# tokenizer. For well-formed plugin source, it is sufficient.
    /// </remarks>
    private static (int startBody, int endBody) FindMethodBody(string[] sourceLines, int signatureLine)
    {
        // Find the opening brace. It may be on the signature line or a later line.
        var openLine = -1;
        var openCol = -1;
        for (var i = signatureLine; i < sourceLines.Length && i < signatureLine + 10; i++)
        {
            var line = sourceLines[i];
            // Skip XML doc continuation lines
            if (line.TrimStart().StartsWith("///", StringComparison.Ordinal))
                continue;

            for (var c = (i == signatureLine ? 0 : 0); c < line.Length; c++)
            {
                var ch = line[c];
                if (ch == '{')
                {
                    // Distinguish method body brace from property accessor or object initializer:
                    // a method body opening brace is always preceded (on the same line or a prior
                    // line) by the method signature's closing paren ")". We accept any "{" found
                    // after the signature line as the method body — this is adequate because the
                    // regex guarantees we're at a method signature, not a property.
                    openLine = i;
                    openCol = c;
                    break;
                }
                if (ch == ';')
                {
                    // Expression-bodied method (or abstract method) — no body to scan.
                    return (-1, -1);
                }
            }

            if (openLine >= 0)
                break;
        }

        if (openLine < 0)
            return (-1, -1);

        // Walk forward counting braces, respecting strings and comments.
        var depth = 0;
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var inVerbatimString = false;
        var inChar = false;

        for (var i = openLine; i < sourceLines.Length; i++)
        {
            var line = sourceLines[i];
            var startCol = (i == openLine) ? openCol : 0;

            // Reset line-comment state at start of each line
            inLineComment = false;

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
                    {
                        inString = false;
                    }
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
                    {
                        inChar = false;
                    }
                    continue;
                }

                // Not inside any literal or comment — process structural characters
                if (ch == '/' && next == '/')
                {
                    inLineComment = true;
                    break;
                }
                if (ch == '/' && next == '*')
                {
                    inBlockComment = true;
                    c++; // skip '*'
                    continue;
                }
                if (ch == '@' && next == '"')
                {
                    inVerbatimString = true;
                    c++; // skip '"'
                    continue;
                }
                if (ch == '$' && next == '"')
                {
                    // Interpolated string (non-verbatim). Treat as a regular string; this
                    // naive approach does not track interpolation-bracket balance. It is
                    // sufficient for well-formed plugin source.
                    inString = true;
                    c++; // skip '"'
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
                    {
                        // The body ends at this line. Return (openLine + 1, i) so the caller
                        // skips the opening brace line and includes the closing brace line.
                        var startBody = openLine + 1;
                        var endBody = i;
                        return (startBody, endBody);
                    }
                    continue;
                }
            }
        }

        return (-1, -1); // unbalanced — parser gave up
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Assembly loading
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ensures all plugin assemblies are loaded into the AppDomain.
    /// Mirrors the pattern from StructuralTests.EnsureAssembliesLoaded().
    /// </summary>
    private static void EnsureAssembliesLoaded()
    {
        var outputDir = Path.GetDirectoryName(typeof(DistributedLockTests).Assembly.Location);
        if (outputDir == null)
            return;

        foreach (var dllPath in Directory.GetFiles(outputDir, "lib-*.dll"))
        {
            var fileName = Path.GetFileNameWithoutExtension(dllPath);
            if (fileName.EndsWith(".tests", StringComparison.Ordinal))
                continue;

            try
            {
                var assemblyName = AssemblyName.GetAssemblyName(dllPath);
                var existing = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == assemblyName.Name);

                if (existing == null)
                    Assembly.LoadFrom(dllPath);
            }
            catch
            {
                // Skip assemblies that fail to load (safe failure mode).
            }
        }
    }
}
