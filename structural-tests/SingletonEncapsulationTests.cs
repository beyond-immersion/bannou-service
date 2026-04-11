using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using BeyondImmersion.BannouService.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BeyondImmersion.BannouService.StructuralTests;

/// <summary>
/// Structural enforcement of singleton encapsulation — item #27 of issue #720's follow-up.
/// </summary>
/// <remarks>
/// <para>
/// Classes registered as <see cref="ServiceLifetime.Singleton"/> in a Bannou plugin's DI container
/// must NOT expose mutable collections as public instance members (fields or properties). A
/// singleton lives for the life of the process and is accessible to every consumer that resolves
/// it from DI. Exposing an unencapsulated <c>ConcurrentDictionary</c>, <c>List</c>, or
/// <c>HashSet</c> directly surrenders invariant enforcement: any caller can mutate the collection
/// in any way, bypassing validation, logging, ordering guarantees, or atomic multi-key operations
/// that the owning class might otherwise enforce through methods.
/// </para>
/// <para>
/// <b>Concrete bug this catches:</b> <c>GenesisGrowthState</c> is registered as a Singleton and
/// exposes <c>public ConcurrentDictionary&lt;Guid, GenesisWalletMapping&gt; WalletMap { get; }</c>
/// plus <c>public ConcurrentDictionary&lt;string, Guid&gt; ActorTemplateMap { get; }</c>. The
/// currency transaction listener, the growth flush worker, the seed evolution listener, and three
/// sets of event handlers all read from and write to these dictionaries directly. There is no
/// method-level encapsulation, no atomic "register wallet" operation, no invariant check on entries
/// before they land in the map, and no way to refactor the contained invariants without finding
/// every raw dictionary call site across the plugin. The fix is to replace the exposed collections
/// with explicit methods (<c>RegisterWallet</c>, <c>TryGetWalletMapping</c>, <c>RemoveWallet</c>,
/// etc.) on the singleton and make the backing collections private.
/// </para>
/// <para>
/// <b>Discovery — hybrid attribute reflection + source scan:</b>
/// </para>
/// <list type="number">
///   <item>
///     <b>Path A (attribute reflection)</b>: Every type in a loaded <c>lib-*</c> plugin assembly
///     with <c>[BannouService(..., lifetime: Singleton)]</c> or
///     <c>[BannouHelperService(..., Singleton)]</c>.
///   </item>
///   <item>
///     <b>Path B (source scan)</b>: Every type name extracted from
///     <c>services.AddSingleton&lt;T&gt;()</c> or <c>services.AddSingleton&lt;IFace, T&gt;()</c>
///     in <c>plugins/lib-*/*Plugin.cs</c>, looked up by name in any loaded plugin assembly.
///   </item>
/// </list>
/// <para>
/// Path B is essential because some plugin classes are registered directly via
/// <c>services.AddSingleton&lt;T&gt;()</c> without an attribute — <c>GenesisGrowthState</c> is the
/// canonical example. Path A alone would miss them. The pre-existing structural test
/// <c>PluginTypes_RegisteredInDI_ShouldHaveBannouHelperServiceAttribute</c> flags this as a
/// separate violation, but that test is currently failing, so we cannot rely on its eventual fix
/// to route <c>GenesisGrowthState</c> through Path A.
/// </para>
/// <para>
/// <b>Violation test:</b> For each candidate singleton type, reflect over all public instance
/// fields and properties. A member is a violation if its type (or the generic type definition, if
/// it is a generic type) is one of the forbidden mutable collection types listed in
/// <see cref="ForbiddenGenericDefinitions"/>. The forbidden list includes the concrete concurrent
/// dictionary/bag/queue/stack types, the standard generic <c>List</c>/<c>Dictionary</c>/<c>HashSet</c>
/// family, and the mutable interfaces (<c>IDictionary</c>, <c>IList</c>, <c>ICollection</c>,
/// <c>ISet</c>).
/// </para>
/// <para>
/// <b>Exempt types</b> (pass unconditionally because they do not expose mutation):
/// </para>
/// <list type="bullet">
///   <item><c>IReadOnlyDictionary</c>, <c>IReadOnlyList</c>, <c>IReadOnlyCollection</c>, <c>IReadOnlySet</c></item>
///   <item>Any type whose namespace begins with <c>System.Collections.Immutable</c> (immutable collection hierarchy)</item>
/// </list>
/// <para>
/// <b>What this test does NOT catch:</b>
/// </para>
/// <list type="bullet">
///   <item>Private fields — only public instance members are scanned. A private
///     <c>ConcurrentDictionary</c> backing a public method surface is the correct pattern.</item>
///   <item>Static members — these are process-wide globals, not singleton instance state, and
///     belong to a different class of problem.</item>
///   <item>Exposed mutable <b>reference-typed</b> POCOs that aren't collection types (e.g., a
///     public mutable DTO class). The rule specifically targets collection types where the
///     encapsulation failure is most egregious.</item>
///   <item>Mutable collections wrapped in a read-only interface property type but backed by a
///     field that is still externally accessible (no such case exists in this codebase).</item>
///   <item>Non-Singleton services (Scoped, Transient). Short-lived services have a different risk
///     profile and this test does not cover them.</item>
/// </list>
/// </remarks>
public class SingletonEncapsulationTests
{
    /// <summary>
    /// Generic type definitions that are forbidden as public instance member types on singleton
    /// classes. The check compares <see cref="Type.GetGenericTypeDefinition"/> against these.
    /// </summary>
    private static readonly HashSet<Type> ForbiddenGenericDefinitions = new()
    {
        // Concurrent collections
        typeof(ConcurrentDictionary<,>),
        typeof(ConcurrentBag<>),
        typeof(ConcurrentQueue<>),
        typeof(ConcurrentStack<>),

        // Standard generic collections
        typeof(Dictionary<,>),
        typeof(SortedDictionary<,>),
        typeof(List<>),
        typeof(HashSet<>),
        typeof(SortedSet<>),

        // Mutable collection interfaces
        typeof(IDictionary<,>),
        typeof(IList<>),
        typeof(ICollection<>),
        typeof(ISet<>),
    };

    /// <summary>
    /// Read-only interface generic type definitions that pass the encapsulation check even when
    /// exposed on a singleton. Members with these types expose a view, not mutation rights.
    /// </summary>
    private static readonly HashSet<Type> ReadOnlyExemptDefinitions = new()
    {
        typeof(IReadOnlyDictionary<,>),
        typeof(IReadOnlyList<>),
        typeof(IReadOnlyCollection<>),
        typeof(IReadOnlySet<>),
    };

    /// <summary>
    /// Namespace prefix that marks a type as part of the immutable collections hierarchy
    /// (<c>System.Collections.Immutable.ImmutableDictionary&lt;,&gt;</c>, etc.). Any member whose
    /// type's namespace begins with this string passes unconditionally.
    /// </summary>
    private const string ImmutableCollectionsNamespacePrefix = "System.Collections.Immutable";

    [Fact]
    public void Singletons_MustNotExposeMutableCollections()
    {
        EnsureAssembliesLoaded();

        // Combine attribute reflection (Path A) and source scan (Path B) into a single
        // deduplicated set of candidate singleton types.
        var candidates = new HashSet<Type>();

        var attributeCandidates = DiscoverAttributedSingletonTypes().ToList();
        foreach (var type in attributeCandidates)
            candidates.Add(type);

        var sourceScanCandidates = DiscoverSourceScanSingletonTypes().ToList();
        foreach (var type in sourceScanCandidates)
            candidates.Add(type);

        var violations = new List<string>();

        foreach (var type in candidates)
        {
            ScanTypeForMutableCollectionViolations(type, violations);
        }

        Assert.True(
            violations.Count == 0,
            $"Found {violations.Count} public mutable collection member(s) on singleton class(es) " +
            $"(item #27 of issue #720's follow-up). Singleton state is shared by every consumer — " +
            $"exposing a mutable collection directly surrenders invariant enforcement. Replace the " +
            $"public collection with private backing storage and method-level accessors, or expose " +
            $"it as a read-only interface (IReadOnlyDictionary, IReadOnlyList, IReadOnlyCollection, " +
            $"IReadOnlySet) or an immutable collection type.\n" +
            string.Join("\n", violations.Select(v => $"  - {v}")));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Discovery — Path A: attribute reflection
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Discovers all types with <see cref="BannouServiceAttribute"/> or
    /// <see cref="BannouHelperServiceAttribute"/> and a <c>Lifetime</c> of
    /// <see cref="ServiceLifetime.Singleton"/> across loaded plugin assemblies.
    /// </summary>
    private static IEnumerable<Type> DiscoverAttributedSingletonTypes()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            var assemblyName = assembly.GetName().Name;
            if (assemblyName == null ||
                assemblyName.StartsWith("System", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Microsoft", StringComparison.Ordinal) ||
                assemblyName.StartsWith("netstandard", StringComparison.Ordinal) ||
                assemblyName.StartsWith("mscorlib", StringComparison.Ordinal) ||
                assemblyName.StartsWith("xunit", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Moq", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Castle", StringComparison.Ordinal))
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException)
            {
                continue;
            }

            foreach (var type in types)
            {
                var serviceAttr = type.GetCustomAttribute<BannouServiceAttribute>(inherit: true);
                if (serviceAttr != null && serviceAttr.Lifetime == ServiceLifetime.Singleton)
                {
                    yield return type;
                    continue;
                }

                var helperAttr = type.GetCustomAttribute<BannouHelperServiceAttribute>(inherit: true);
                if (helperAttr != null && helperAttr.Lifetime == ServiceLifetime.Singleton)
                {
                    yield return type;
                }
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Discovery — Path B: source scan of Plugin.cs files
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Discovers types registered as Singleton via direct
    /// <c>services.AddSingleton&lt;T&gt;()</c> or
    /// <c>services.AddSingleton&lt;IFace, T&gt;()</c> calls in plugin registration files.
    /// Resolves the type name by looking it up in any loaded plugin assembly.
    /// </summary>
    private static IEnumerable<Type> DiscoverSourceScanSingletonTypes()
    {
        var pluginsDir = Path.Combine(TestAssemblyDiscovery.RepoRoot, "plugins");
        if (!Directory.Exists(pluginsDir))
            yield break;

        // Build a lookup: type name -> Type for all types in lib-* assemblies. A single type name
        // may map to multiple Types if the same short name exists in multiple plugins; all matches
        // are yielded so the member scan can find the real one.
        var pluginTypesByName = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var assemblyName = assembly.GetName().Name;
            if (assemblyName == null || !assemblyName.StartsWith("lib-", StringComparison.Ordinal))
                continue;
            if (assemblyName.EndsWith(".tests", StringComparison.Ordinal))
                continue;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException) { continue; }

            foreach (var type in types)
            {
                if (type.IsInterface || type.IsAbstract)
                    continue;

                if (!pluginTypesByName.TryGetValue(type.Name, out var list))
                {
                    list = new List<Type>();
                    pluginTypesByName[type.Name] = list;
                }
                list.Add(type);
            }
        }

        foreach (var pluginFile in Directory.GetFiles(pluginsDir, "*Plugin.cs", SearchOption.AllDirectories))
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(pluginFile);
            }
            catch
            {
                continue;
            }

            foreach (var rawLine in lines)
            {
                var trimmed = rawLine.TrimStart();

                // Focus on AddSingleton registrations. Broader AddScoped/AddTransient calls are
                // out of scope for this test.
                if (!trimmed.Contains("services.AddSingleton", StringComparison.Ordinal))
                    continue;

                // Skip factory lambda registrations — those resolve the actual instance at
                // runtime via a delegate, so there's no closed-form "this is the Singleton type"
                // extractable by source scanning. Attribute reflection (Path A) still catches
                // anything relevant if the class itself is attributed.
                if (trimmed.Contains("sp =>", StringComparison.Ordinal) ||
                    trimmed.Contains("(sp", StringComparison.Ordinal))
                    continue;

                // Skip pure comments.
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                    continue;

                // Extract generic arguments. The line may contain additional tokens (arguments,
                // semicolons, comments) — we only care about the first <...> region.
                var angleBracketStart = trimmed.IndexOf('<');
                var angleBracketEnd = trimmed.IndexOf('>', angleBracketStart + 1);
                if (angleBracketStart < 0 || angleBracketEnd < 0)
                    continue;

                var generics = trimmed[(angleBracketStart + 1)..angleBracketEnd];
                var parts = generics.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                // Implementation type is the LAST generic argument:
                //   AddSingleton<T>()             → parts[0]
                //   AddSingleton<IFace, T>()      → parts[1]
                var implTypeName = parts[^1];

                // Skip namespace-qualified external types and nested generics. The source
                // scan is a best-effort heuristic — types with '.' or '<' in the generic
                // argument are harder to match and typically live outside plugin assemblies.
                if (implTypeName.Contains('.', StringComparison.Ordinal) ||
                    implTypeName.Contains('<', StringComparison.Ordinal))
                    continue;

                if (!pluginTypesByName.TryGetValue(implTypeName, out var matchingTypes))
                    continue; // not a plugin type — framework/SDK types are out of scope

                foreach (var type in matchingTypes)
                {
                    yield return type;
                }
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Member scan
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scans all public instance fields and properties of <paramref name="type"/> for members
    /// whose type is a forbidden mutable collection. Appends one violation entry per offending
    /// member to <paramref name="violations"/>.
    /// </summary>
    private static void ScanTypeForMutableCollectionViolations(Type type, List<string> violations)
    {
        // Public instance fields — the most direct form of state exposure. Bannou's convention
        // is to use properties, but a public field is still structurally possible.
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (IsForbiddenCollectionType(field.FieldType))
            {
                violations.Add(FormatViolation(type, "field", field.Name, field.FieldType));
            }
        }

        // Public instance properties — the typical form for collection exposure. Both
        // get-only and get/set properties are scanned; an init-only getter still exposes
        // the collection's mutating methods.
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Skip indexers (properties with parameters) — these are unusual on singletons
            // and the violation model doesn't apply cleanly to them.
            if (property.GetIndexParameters().Length > 0)
                continue;

            if (IsForbiddenCollectionType(property.PropertyType))
            {
                violations.Add(FormatViolation(type, "property", property.Name, property.PropertyType));
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="memberType"/> is a forbidden mutable collection
    /// type per <see cref="ForbiddenGenericDefinitions"/>. Immutable and read-only-interface
    /// collection types return <c>false</c> via the exemption checks.
    /// </summary>
    private static bool IsForbiddenCollectionType(Type memberType)
    {
        // Arrays are mutable but commonly used for return-by-copy snapshots; they are NOT on
        // the forbidden list. If a future audit wants to include them, add `memberType.IsArray`
        // here and document the rationale.
        if (!memberType.IsGenericType)
            return false;

        var definition = memberType.GetGenericTypeDefinition();

        // Exemption 1: the type is a read-only collection interface.
        if (ReadOnlyExemptDefinitions.Contains(definition))
            return false;

        // Exemption 2: the type lives in the System.Collections.Immutable namespace. This covers
        // ImmutableDictionary<,>, ImmutableList<>, ImmutableHashSet<>, and any future additions
        // to that namespace without maintaining an explicit list.
        var ns = definition.Namespace;
        if (ns != null && ns.StartsWith(ImmutableCollectionsNamespacePrefix, StringComparison.Ordinal))
            return false;

        // Forbidden match — the member is a mutable collection of a type on the block list.
        return ForbiddenGenericDefinitions.Contains(definition);
    }

    /// <summary>
    /// Formats a violation entry: class name, member kind, member name, and resolved member
    /// type name including generic arguments.
    /// </summary>
    private static string FormatViolation(Type ownerType, string memberKind, string memberName, Type memberType)
    {
        var ownerLabel = ownerType.FullName ?? ownerType.Name;
        var typeLabel = FormatTypeName(memberType);
        return $"{ownerLabel}.{memberName} ({memberKind}) — type {typeLabel} is a mutable collection";
    }

    /// <summary>
    /// Produces a short, human-readable form of a generic type like
    /// <c>ConcurrentDictionary&lt;Guid, GenesisWalletMapping&gt;</c> instead of the CLR's
    /// <c>ConcurrentDictionary`2[System.Guid,...]</c> form.
    /// </summary>
    private static string FormatTypeName(Type type)
    {
        if (!type.IsGenericType)
            return type.Name;

        var definition = type.GetGenericTypeDefinition();
        var baseName = definition.Name;
        var tickIdx = baseName.IndexOf('`', StringComparison.Ordinal);
        if (tickIdx >= 0)
            baseName = baseName[..tickIdx];

        var args = type.GetGenericArguments().Select(FormatTypeName);
        return $"{baseName}<{string.Join(", ", args)}>";
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Assembly loading (inline copy — each structural test file has its own)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ensures every <c>lib-*.dll</c> in the structural-tests output directory is loaded into
    /// the current <see cref="AppDomain"/> so that reflection over plugin types works. Mirrors
    /// the inline copies used in <c>StructuralTests.cs</c> and <c>ConfigurationDefaultTests.cs</c>
    /// — the three copies are intentionally independent to keep the files decoupled.
    /// </summary>
    private static void EnsureAssembliesLoaded()
    {
        var outputDir = Path.GetDirectoryName(typeof(SingletonEncapsulationTests).Assembly.Location);
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
                // Skip assemblies that fail to load (safe failure mode — those plugins simply
                // won't be validated by this test).
            }
        }
    }
}
