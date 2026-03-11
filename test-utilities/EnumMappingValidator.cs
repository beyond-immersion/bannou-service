using Xunit;

namespace BeyondImmersion.BannouService.TestUtilities;

// ⛔ FROZEN FILE — DO NOT MODIFY WITHOUT EXPLICIT USER PERMISSION ⛔
// Structural test validator. Changes affect validation across all services.

/// <summary>
/// Validates enum boundary mappings at test time, catching value drift between
/// schema-generated enums and external SDK/library enums before it reaches production.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> Every enum boundary crossing in a plugin should have a corresponding
/// unit test that validates the mapping is complete and correct. This validator provides
/// reusable assertion methods for the three common boundary patterns:
/// </para>
/// <list type="bullet">
///   <item><b>Full coverage (identical enums):</b> Use <see cref="AssertFullCoverage{TSource, TTarget}"/>
///   when both enums should have the same values (A2 plugin SDK boundaries with <c>MapByName</c>).</item>
///   <item><b>Subset validation:</b> Use <see cref="AssertSubset{TSubset, TSuperset}"/>
///   when one enum is a strict subset of another (API safety subsets, granularity tiers).</item>
///   <item><b>Switch coverage:</b> Use <see cref="AssertSwitchCoversAllValues{TSource}"/>
///   when a lossy switch expression must handle every source value (even if mapping is not 1:1).</item>
/// </list>
/// </remarks>
public static class EnumMappingValidator
{
    /// <summary>
    /// Asserts that every value in <typeparamref name="TSource"/> has a matching name in
    /// <typeparamref name="TTarget"/>, AND every value in <typeparamref name="TTarget"/>
    /// has a matching name in <typeparamref name="TSource"/>. Use this for A2 boundaries
    /// where the enums should be identical (same names, both directions).
    /// </summary>
    /// <typeparam name="TSource">The source enum type (e.g., schema-generated).</typeparam>
    /// <typeparam name="TTarget">The target enum type (e.g., SDK enum).</typeparam>
    public static void AssertFullCoverage<TSource, TTarget>()
        where TSource : struct, Enum
        where TTarget : struct, Enum
    {
        var sourceNames = Enum.GetNames<TSource>();
        var targetNames = new HashSet<string>(Enum.GetNames<TTarget>());

        var missingInTarget = sourceNames.Where(name => !targetNames.Contains(name)).ToList();
        if (missingInTarget.Count > 0)
        {
            Assert.Fail(
                $"Enum mapping drift: {typeof(TSource).Name} has values not found in " +
                $"{typeof(TTarget).Name}: [{string.Join(", ", missingInTarget)}]. " +
                $"Update the target enum or add explicit handling for these values.");
        }

        var sourceNameSet = new HashSet<string>(sourceNames);
        var missingInSource = Enum.GetNames<TTarget>().Where(name => !sourceNameSet.Contains(name)).ToList();
        if (missingInSource.Count > 0)
        {
            Assert.Fail(
                $"Enum mapping drift: {typeof(TTarget).Name} has values not found in " +
                $"{typeof(TSource).Name}: [{string.Join(", ", missingInSource)}]. " +
                $"Update the source enum or add explicit handling for these values.");
        }
    }

    /// <summary>
    /// Asserts that every value in <typeparamref name="TSource"/> has a matching name in
    /// <typeparamref name="TTarget"/> (one direction only). Use this for A2 boundaries where
    /// the source is a subset of the target, or when only forward mapping is needed.
    /// </summary>
    /// <typeparam name="TSource">The source enum type whose values must all exist in the target.</typeparam>
    /// <typeparam name="TTarget">The target enum type (may have additional values).</typeparam>
    public static void AssertSourceCoveredByTarget<TSource, TTarget>()
        where TSource : struct, Enum
        where TTarget : struct, Enum
    {
        var targetNames = new HashSet<string>(Enum.GetNames<TTarget>());
        var missingInTarget = Enum.GetNames<TSource>().Where(name => !targetNames.Contains(name)).ToList();

        if (missingInTarget.Count > 0)
        {
            Assert.Fail(
                $"Enum subset violation: {typeof(TSource).Name} has values not found in " +
                $"{typeof(TTarget).Name}: [{string.Join(", ", missingInTarget)}]. " +
                $"Every {typeof(TSource).Name} value must exist in {typeof(TTarget).Name}.");
        }
    }

    /// <summary>
    /// Asserts that every value in <typeparamref name="TSubset"/> exists (by name) in
    /// <typeparamref name="TSuperset"/>. This is an alias for
    /// <see cref="AssertSourceCoveredByTarget{TSource, TTarget}"/> with clearer naming
    /// for subset/superset relationships.
    /// </summary>
    /// <typeparam name="TSubset">The smaller enum (all its values must exist in the superset).</typeparam>
    /// <typeparam name="TSuperset">The larger enum (may have additional values).</typeparam>
    public static void AssertSubset<TSubset, TSuperset>()
        where TSubset : struct, Enum
        where TSuperset : struct, Enum
    {
        AssertSourceCoveredByTarget<TSubset, TSuperset>();
    }

    /// <summary>
    /// Validates a superset-to-subset enum mapping that uses
    /// <see cref="EnumMapping.MapByNameOrDefault{TSource, TTarget}"/>. Asserts:
    /// <list type="number">
    ///   <item>Every value in <typeparamref name="TSubset"/> has a matching name in <typeparamref name="TSuperset"/></item>
    ///   <item>The set of extra superset values (those not in subset) matches <paramref name="expectedExtras"/> exactly</item>
    /// </list>
    /// If the SDK adds a new value not listed in <paramref name="expectedExtras"/>, the test fails —
    /// forcing a decision: add the value to the schema or add it to the expected extras list.
    /// If a previously-extra value is added to the schema, the test also fails — keeping the list clean.
    /// </summary>
    /// <typeparam name="TSuperset">The larger enum (e.g., SDK enum with extra values).</typeparam>
    /// <typeparam name="TSubset">The smaller enum (e.g., schema-generated, all values exist in superset).</typeparam>
    /// <param name="expectedExtras">
    /// The superset value names that intentionally have no match in the subset and will map to the fallback.
    /// </param>
    public static void AssertSupersetToSubsetMapping<TSuperset, TSubset>(params string[] expectedExtras)
        where TSuperset : struct, Enum
        where TSubset : struct, Enum
    {
        // 1. Verify subset relationship (every subset value exists in superset)
        AssertSubset<TSubset, TSuperset>();

        // 2. Find actual extra values in superset
        var subsetNames = new HashSet<string>(Enum.GetNames<TSubset>());
        var actualExtras = Enum.GetNames<TSuperset>()
            .Where(name => !subsetNames.Contains(name))
            .ToHashSet();

        var expectedExtrasSet = new HashSet<string>(expectedExtras);

        // 3. Check for unexpected extras (SDK added values we didn't know about)
        var unexpectedExtras = actualExtras.Except(expectedExtrasSet).OrderBy(n => n).ToList();
        if (unexpectedExtras.Count > 0)
        {
            Assert.Fail(
                $"Superset {typeof(TSuperset).Name} has new values not in " +
                $"{typeof(TSubset).Name} and not in expectedExtras: " +
                $"[{string.Join(", ", unexpectedExtras)}]. " +
                $"Either add these to {typeof(TSubset).Name}'s schema or " +
                $"include them in expectedExtras.");
        }

        // 4. Check for stale expected extras (values that now exist in subset or were removed from superset)
        var staleExtras = expectedExtrasSet.Except(actualExtras).OrderBy(n => n).ToList();
        if (staleExtras.Count > 0)
        {
            Assert.Fail(
                $"expectedExtras contains values that are no longer extras in " +
                $"{typeof(TSuperset).Name} → {typeof(TSubset).Name}: " +
                $"[{string.Join(", ", staleExtras)}]. " +
                $"Remove them from expectedExtras.");
        }
    }

    /// <summary>
    /// Asserts that a switch expression or mapping function handles every value of
    /// <typeparamref name="TSource"/> without throwing. Use this for lossy mappings
    /// where source values intentionally map to different target names (e.g.,
    /// <c>Aeolian -> Minor</c>), making name-based mapping impossible.
    /// The function under test should be the actual switch/mapping method from the service.
    /// </summary>
    /// <typeparam name="TSource">The source enum type.</typeparam>
    /// <param name="mappingFunc">The mapping function to validate (typically the service's switch expression).</param>
    public static void AssertSwitchCoversAllValues<TSource>(Func<TSource, object> mappingFunc)
        where TSource : struct, Enum
    {
        var failures = new List<string>();

        foreach (var value in Enum.GetValues<TSource>())
        {
            try
            {
                var result = mappingFunc(value);
                if (result is null)
                {
                    failures.Add($"{typeof(TSource).Name}.{value} mapped to null");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{typeof(TSource).Name}.{value} threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                $"Switch coverage gap: mapping function does not handle all " +
                $"{typeof(TSource).Name} values:\n" +
                string.Join("\n", failures.Select(f => $"  - {f}")));
        }
    }
}
